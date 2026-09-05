using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>How a clash came out, from the attacker's side of it.</summary>
    public enum ClashOutcome
    {
        DefenderWins = -1,
        Tie = 0,
        AttackerWins = 1
    }

    /// <summary>
    /// Which element answers which.
    ///
    /// A seam, not a table. What beats what is a tuning decision that will be argued over for the
    /// life of the game, and burying it in the rules would mean a recompile every time somebody
    /// wanted to try Nyx a notch weaker. <c>Dragoneye.Data</c> authors it; the rules only ask.
    ///
    /// Sized for the whole element set even where a build enables fewer of them, so enabling one
    /// later is content rather than code.
    /// </summary>
    public interface IElementMatchup
    {
        /// <summary>How <paramref name="attacker"/> fares against <paramref name="defender"/>.</summary>
        ClashOutcome Compare(Element attacker, Element defender);
    }

    /// <summary>
    /// What a side does with more than one committed element.
    ///
    /// A side commits two because something gave it advantage or something gave it disadvantage --
    /// never merely because it felt like it -- so the reason it has two is also the rule for
    /// reading them.
    /// </summary>
    public enum ClashBias
    {
        /// <summary>One element. Nothing to choose between.</summary>
        None = 0,

        /// <summary>Advantage: the better of the two results stands.</summary>
        Best = 1,

        /// <summary>Disadvantage: the worse of the two results stands.</summary>
        Worst = 2
    }

    /// <summary>
    /// One side of a clash: what it put in, and how its commitment is read.
    /// </summary>
    public readonly struct ClashCommitment
    {
        /// <summary>Nothing committed. The side is not contesting.</summary>
        public static ClashCommitment None => new ClashCommitment(null, ClashBias.None);

        public readonly IReadOnlyList<Element> Elements;
        public readonly ClashBias Bias;

        public ClashCommitment(IReadOnlyList<Element> elements, ClashBias bias = ClashBias.None)
        {
            Elements = elements ?? System.Array.Empty<Element>();
            Bias = bias;
        }

        public static ClashCommitment Of(Element element) =>
            new ClashCommitment(new[] { element });

        public bool IsEmpty => Elements.Count == 0;
    }

    /// <summary>
    /// How an attack is contested.
    ///
    /// DE-005 makes an attack a contest rather than a damage calculation: the attacker's skill puts
    /// an element in, the defender answers with one of their own, and what the skill does depends on
    /// which of the two answered better. All of it is here and none of it touches a scene, because
    /// both machines have to reach the same answer from the same commitments.
    ///
    /// Nothing in this file knows about concealment. Which side sees what and when is a property of
    /// the order things are emitted in, and that belongs to whatever is running the fight -- these
    /// are only the sums it does once both commitments are in.
    /// </summary>
    public static class ClashRules
    {
        /// <summary>What an even side puts in.</summary>
        public const int SingleCommitment = 1;

        /// <summary>What a side at advantage or disadvantage puts in.</summary>
        public const int DoubleCommitment = 2;

        /// <summary>
        /// How many elements a side has to commit.
        ///
        /// Two for advantage and two for disadvantage alike -- that symmetry is the point, and
        /// DE-006 is explicit about it. A side that is better placed does not get its edge for free:
        /// it spends twice as fast for it, so a shield is not free defence and flanking punishes
        /// reserves as well as odds.
        ///
        /// Advantage and disadvantage on the same side cancel, leaving an ordinary single
        /// commitment, because a creature that is both better and worse placed is neither.
        /// </summary>
        public static int CommitmentFor(bool advantage, bool disadvantage) =>
            advantage == disadvantage ? SingleCommitment : DoubleCommitment;

        /// <summary>Which way a doubled commitment is read. See <see cref="CommitmentFor"/>.</summary>
        public static ClashBias BiasFor(bool advantage, bool disadvantage)
        {
            if (advantage == disadvantage)
            {
                return ClashBias.None;
            }

            return advantage ? ClashBias.Best : ClashBias.Worst;
        }

        /// <summary>
        /// Who came out on top.
        ///
        /// An attacker that committed nothing is not contesting anything, so nothing is contested:
        /// a skill costing no element resolves as though unanswered. So does an attack the defender
        /// did not answer -- whether because they held nothing they could spend, or because they
        /// chose to take it rather than pay.
        ///
        /// **The reduction order is stated, not discovered.** With a bias on both sides the answer
        /// depends on which commitment is collapsed first, and two machines picking differently
        /// would resolve the same clash two ways and be very hard to trace. The defender's
        /// commitment is reduced first, against each of the attacker's elements in turn; the
        /// attacker's is reduced after. It only matters when both sides are biased at once -- a
        /// shielded attacker striking a flanked defender -- and in that case it is the attacker,
        /// who spent the action, who gets the benefit of the doubt.
        /// </summary>
        public static ClashOutcome Resolve(ClashCommitment attacker, ClashCommitment defender,
            IElementMatchup matchup)
        {
            if (matchup == null || attacker.IsEmpty || defender.IsEmpty)
            {
                return ClashOutcome.AttackerWins;
            }

            var best = 0;
            var first = true;

            foreach (var mine in attacker.Elements)
            {
                var against = Against(mine, defender, matchup);

                if (first || Prefer(against, best, attacker.Bias))
                {
                    best = against;
                    first = false;
                }
            }

            return (ClashOutcome)best;
        }

        /// <summary>One attacking element against the whole of what the defender put up.</summary>
        static int Against(Element attacking, ClashCommitment defender, IElementMatchup matchup)
        {
            var result = 0;
            var first = true;

            foreach (var theirs in defender.Elements)
            {
                // Scored from the attacker's side throughout, so "better" means the same thing at
                // every step. The defender's own preference is therefore the reverse of the bias
                // they carry.
                var score = (int)matchup.Compare(attacking, theirs);

                if (first || Prefer(score, result, Reversed(defender.Bias)))
                {
                    result = score;
                    first = false;
                }
            }

            return result;
        }

        static ClashBias Reversed(ClashBias bias)
        {
            switch (bias)
            {
                case ClashBias.Best: return ClashBias.Worst;
                case ClashBias.Worst: return ClashBias.Best;
                default: return ClashBias.None;
            }
        }

        /// <summary>Whether a candidate is the one this bias would keep.</summary>
        static bool Prefer(int candidate, int held, ClashBias bias)
        {
            switch (bias)
            {
                case ClashBias.Best: return candidate > held;
                case ClashBias.Worst: return candidate < held;

                // Nothing to choose between: an unbiased side committed one element, so the first
                // is the only one.
                default: return false;
            }
        }

        /// <summary>
        /// What the outcome leaves of the effect that was aimed.
        ///
        /// An attack gets through only by winning. A tie and a loss both stop it dead, and what
        /// separates them is on the defender's side of the ledger rather than the attacker's --
        /// see <see cref="Refunds"/>.
        ///
        /// Three outcomes, then, and each says something different to the person it happened to:
        ///
        ///   * win  -- no damage, and the element comes back
        ///   * tie  -- no damage, and the element is gone
        ///   * lose -- damage, and the element is gone
        ///
        /// That makes attacking a war of attrition rather than a coin flip. You throw elements to
        /// drain somebody's hand, and the hits land once they have nothing left to answer with --
        /// which is why a level-one creature holds four of them and not one.
        /// </summary>
        public static SkillEffect Scale(SkillEffect effect, ClashOutcome outcome) =>
            outcome == ClashOutcome.AttackerWins
                ? effect
                : new SkillEffect(effect.Kind, 0);

        /// <summary>
        /// Whether the defender keeps what they put up.
        ///
        /// Only on a clean win. Answering correctly enough to stop the blow is worth something on
        /// its own; answering well enough to stop it *and* keep the element is what makes reading
        /// an opponent worth doing rather than merely worth guessing at.
        ///
        /// The attacker never gets theirs back. Theirs paid for the action.
        /// </summary>
        public static bool Refunds(ClashOutcome outcome) => outcome == ClashOutcome.DefenderWins;

        /// <summary>
        /// What a defender may answer with: everything they still hold, one entry per element.
        ///
        /// Built from the pool rather than from the skill, so the prompt cannot offer something the
        /// spend would then refuse -- and, more to the point, so that nothing about the attack is
        /// needed to build it. The list a defender is shown is a fact about the defender.
        /// </summary>
        public static List<Element> AnswersFor(ElementLedger ledger)
        {
            var answers = new List<Element>();

            foreach (var element in ElementInfo.All)
            {
                if (ledger.Pool[element] > 0)
                {
                    answers.Add(element);
                }
            }

            return answers;
        }
    }
}

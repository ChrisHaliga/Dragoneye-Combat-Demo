using System;
using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>
    /// What somebody watching believes a creature could still put up.
    ///
    /// Built from what is public: the elements they have been proven to hold and are not currently
    /// spent, and a count of the ones nobody has identified. It is deliberately not the truth --
    /// it is the best guess the information allows, which is what both a player and a computer
    /// creature have to decide from.
    ///
    /// The unidentified ones are spread evenly across <see cref="Candidates"/>. That is a
    /// confession of ignorance rather than a model: knowing nothing about an element means every
    /// element it could be is equally likely. Narrowing the candidates is how knowledge gets folded
    /// in -- an attacker who has only ever been seen to throw Pyro and Aero has a candidate list of
    /// two, and the guess sharpens accordingly.
    /// </summary>
    public readonly struct PossibleElements
    {
        /// <summary>Proven to be held, and not currently spent.</summary>
        public readonly ElementCounts Known;

        /// <summary>Held and available, but nobody has put a name to them.</summary>
        public readonly int Unknown;

        /// <summary>What an unidentified one might turn out to be.</summary>
        public readonly IReadOnlyList<Element> Candidates;

        /// <summary>
        /// Whether this hand is known to be empty, as opposed to merely unknown.
        ///
        /// The distinction is the whole difference between "they cannot answer" and "we have no
        /// idea what they will answer with", and conflating the two is how every option on a
        /// prompt came to read as a certain loss. Ignorance is not information.
        /// </summary>
        public readonly bool Empty;

        public PossibleElements(ElementCounts known, int unknown,
            IReadOnlyList<Element> candidates = null, bool empty = false)
        {
            Known = known;
            Unknown = unknown < 0 ? 0 : unknown;
            Candidates = candidates != null && candidates.Count > 0 ? candidates : ElementInfo.All;
            Empty = empty;
        }

        /// <summary>Known to hold nothing. An attack against this is unopposed.</summary>
        public static PossibleElements None =>
            new PossibleElements(ElementCounts.Empty, 0, null, empty: true);

        /// <summary>Nothing is known at all. Every element is as likely as every other.</summary>
        public static PossibleElements Unknowable =>
            new PossibleElements(ElementCounts.Empty, 0);

        /// <summary>
        /// What an observer can work out from a creature's public record.
        ///
        /// Two numbers matter and they are both exactly knowable without seeing the hand. How many
        /// elements are in it: the total the creature owns, less however many are currently spent,
        /// which is public because everybody watched them go. And which of those are identified:
        /// the ones proven to exist and since taken back.
        ///
        /// Everything left over is unidentified and available, and that is the number the forecast
        /// actually turns on. Working it out as "proven, less spent" -- which is what this used to
        /// do -- gave nearly always zero, because an element is proven *by* being spent. The guess
        /// then had nothing in it, which was read as certainty rather than as ignorance.
        /// </summary>
        public static PossibleElements Seen(ElementLedger ledger,
            IReadOnlyList<Element> candidates = null)
        {
            // How many are in the hand. Exact: spending moves an element out and returning moves it
            // back, so the count is the total less what is outstanding, and both are public.
            var inHand = ledger.Total - ledger.Outstanding.Count;

            if (ledger.Total <= 0)
            {
                // Nothing has told us how big the hand is. That is ignorance, not an empty hand,
                // and saying otherwise would forecast a certainty out of thin air.
                return Unknowable;
            }

            if (inHand <= 0)
            {
                return None;
            }

            var identified = ElementCounts.Empty;

            foreach (var element in ElementInfo.All)
            {
                var spent = 0;

                foreach (var outstanding in ledger.Outstanding)
                {
                    if (outstanding == element)
                    {
                        spent++;
                    }
                }

                // Proven to exist and not currently spent, so it is in the hand and has a name.
                var left = ledger.Identified[element] - spent;

                if (left > 0)
                {
                    identified = identified.With(element, left);
                }
            }

            var nameless = inHand - identified.Total;

            return new PossibleElements(identified, nameless, candidates);
        }

        /// <summary>How much of the guess sits on one element.</summary>
        public float WeightOf(Element element)
        {
            var weight = (float)Known[element];

            if (Unknown <= 0)
            {
                return weight;
            }

            for (var i = 0; i < Candidates.Count; i++)
            {
                if (Candidates[i] == element)
                {
                    return weight + ((float)Unknown / Candidates.Count);
                }
            }

            return weight;
        }

        /// <summary>Whether there is anything at all to put up.</summary>
        public bool Any => Known.Total > 0 || Unknown > 0;
    }

    /// <summary>How a clash is expected to go, as three shares that sum to one.</summary>
    public readonly struct ClashOdds
    {
        /// <summary>Everything lands, from the asking side's point of view.</summary>
        public readonly float Win;

        public readonly float Tie;
        public readonly float Loss;

        public ClashOdds(float win, float tie, float loss)
        {
            Win = win;
            Tie = tie;
            Loss = loss;
        }

        /// <summary>Nothing to contest it. Certain, and the only certainty in here.</summary>
        public static ClashOdds Unopposed => new ClashOdds(1f, 0f, 0f);

        /// <summary>How much better than even this is, from -1 to 1.</summary>
        public float Edge => Win - Loss;
    }

    /// <summary>
    /// What a clash is likely to come to, given what each side can work out about the other.
    ///
    /// Here rather than in a view because three different things ask it and must agree: the label
    /// under an attacker's cursor, the numbers on a defender's prompt, and the computer deciding
    /// what to put up. A player who is shown one set of odds and then beaten by a creature working
    /// from another has been lied to, however slightly.
    ///
    /// None of it is hidden information. Everything it reads -- what has been proven, what is
    /// currently spent, which skills have been seen -- is public by design, and the point is to
    /// spare a player from doing the arithmetic rather than to tell them anything they could not
    /// have counted themselves.
    /// </summary>
    public static class ClashForecast
    {
        /// <summary>How an attack fares against whatever the defender might answer with.</summary>
        public static ClashOdds Attacking(Element attacking, PossibleElements defence,
            IElementMatchup matchup) =>
            Fold(attacking, defence, matchup, mineAttacks: true);

        /// <summary>How an answer fares against whatever the attack might turn out to be.</summary>
        public static ClashOdds Defending(Element answering, PossibleElements attack,
            IElementMatchup matchup) =>
            Fold(answering, attack, matchup, mineAttacks: false);

        static ClashOdds Fold(Element mine, PossibleElements theirs, IElementMatchup matchup,
            bool mineAttacks)
        {
            if (matchup == null)
            {
                return ClashOdds.Unopposed;
            }

            if (theirs.Empty)
            {
                // Known to hold nothing. For an attacker that is a certainty; for a defender
                // reading it the other way round, it is the same certainty against them.
                return mineAttacks ? ClashOdds.Unopposed : new ClashOdds(0f, 0f, 1f);
            }

            // Knowing nothing means every element is as likely as any other, which is the honest
            // answer and emphatically not the same as knowing they have nothing. Saying "certain
            // loss" because the guess was empty was the worst kind of wrong: confident.
            var blind = !theirs.Any;

            float win = 0f, tie = 0f, loss = 0f, total = 0f;

            foreach (var element in ElementInfo.All)
            {
                var weight = blind ? Uniform(theirs, element) : theirs.WeightOf(element);

                if (weight <= 0f)
                {
                    continue;
                }

                total += weight;

                // The table always reads attacker against defender, so the roles go in the right
                // way round and the result is turned over for the side asking.
                var outcome = mineAttacks
                    ? matchup.Compare(mine, element)
                    : matchup.Compare(element, mine);

                var forMe = mineAttacks ? (int)outcome : -(int)outcome;

                if (forMe > 0)
                {
                    win += weight;
                }
                else if (forMe < 0)
                {
                    loss += weight;
                }
                else
                {
                    tie += weight;
                }
            }

            return total <= 0f
                ? ClashOdds.Unopposed
                : new ClashOdds(win / total, tie / total, loss / total);
        }

        /// <summary>Every candidate equally, for a hand nothing is known about.</summary>
        static float Uniform(PossibleElements theirs, Element element)
        {
            for (var i = 0; i < theirs.Candidates.Count; i++)
            {
                if (theirs.Candidates[i] == element)
                {
                    return 1f;
                }
            }

            return 0f;
        }
    }

    /// <summary>
    /// What a computer creature puts up, and how sure it is about it.
    ///
    /// Weighted rather than best-first. A defender that always answered optimally would be a
    /// defender a player could hard-counter every single time once they had worked out the table,
    /// and a fight whose right answer never changes is a fight with one turn in it. Weighting keeps
    /// the good answer usual and the bad answer possible.
    ///
    /// It reads exactly what a player reads. The forecast is built from public information and
    /// nothing else, so a computer creature is not guessing better than a person could -- it is
    /// guessing the same and then rolling.
    /// </summary>
    public static class ClashDefenceOdds
    {
        /// <summary>
        /// How sharply the better answer is favoured.
        ///
        /// Cubed, so an answer that beats everything is about eight times likelier than an even one.
        /// Sharper than that and the randomness stops mattering; flatter and the creature stops
        /// looking like it is trying.
        /// </summary>
        public const float Decisiveness = 3f;

        /// <summary>
        /// The weight a hopeless answer still carries.
        ///
        /// Small, but never zero. Zero is what makes a creature predictable enough to be beaten by
        /// rote rather than by play, and the whole reason for rolling at all is to stop that.
        /// </summary>
        public const float Floor = 0.15f;

        /// <summary>What one option is worth putting up.</summary>
        public static float WeightOf(Element answering, PossibleElements attack,
            IElementMatchup matchup)
        {
            var odds = ClashForecast.Defending(answering, attack, matchup);
            var scaled = (float)Math.Pow(1f + odds.Edge, Decisiveness);

            return scaled < Floor ? Floor : scaled;
        }

        /// <summary>
        /// Picks one, favouring the better answer without ever being certain to take it.
        /// </summary>
        /// <param name="roll">Anywhere in [0, 1). The caller owns the randomness.</param>
        public static bool TryChoose(IReadOnlyList<Element> options, PossibleElements attack,
            IElementMatchup matchup, float roll, out Element chosen)
        {
            chosen = default;

            if (options == null || options.Count == 0)
            {
                return false;
            }

            var total = 0f;

            for (var i = 0; i < options.Count; i++)
            {
                total += WeightOf(options[i], attack, matchup);
            }

            if (total <= 0f)
            {
                chosen = options[0];
                return true;
            }

            var target = (roll < 0f ? 0f : roll >= 1f ? 0.999999f : roll) * total;
            var running = 0f;

            for (var i = 0; i < options.Count; i++)
            {
                running += WeightOf(options[i], attack, matchup);

                if (target < running)
                {
                    chosen = options[i];
                    return true;
                }
            }

            // Only reachable on a rounding edge, and the last option is as good an answer as any.
            chosen = options[options.Count - 1];
            return true;
        }
    }
}

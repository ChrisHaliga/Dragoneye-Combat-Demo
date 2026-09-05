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

        /// <summary>Held, but nobody has put a name to them.</summary>
        public readonly int Unknown;

        /// <summary>What an unidentified one might turn out to be.</summary>
        public readonly IReadOnlyList<Element> Candidates;

        public PossibleElements(ElementCounts known, int unknown,
            IReadOnlyList<Element> candidates = null)
        {
            Known = known;
            Unknown = unknown < 0 ? 0 : unknown;
            Candidates = candidates != null && candidates.Count > 0 ? candidates : ElementInfo.All;
        }

        /// <summary>Nothing known and nothing left. An attack against this is unopposed.</summary>
        public static PossibleElements None => new PossibleElements(ElementCounts.Empty, 0);

        /// <summary>
        /// What an observer can work out from a creature's public record.
        ///
        /// Proven, less whatever of it is currently spent. An element that has been seen and then
        /// spent again is still known to exist, but it is not something the creature can put up
        /// right now, and a forecast that counted it would be forecasting a hand nobody holds.
        /// </summary>
        public static PossibleElements Seen(ElementLedger ledger,
            IReadOnlyList<Element> candidates = null)
        {
            var available = ElementCounts.Empty;

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

                var left = ledger.Identified[element] - spent;

                if (left > 0)
                {
                    available = available.With(element, left);
                }
            }

            return new PossibleElements(available, ledger.Unidentified, candidates);
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
            if (matchup == null || !theirs.Any)
            {
                // Nothing they can put up. For an attacker that is a certainty; for a defender
                // reading it the other way round, it is the same certainty against them.
                return mineAttacks ? ClashOdds.Unopposed : new ClashOdds(0f, 0f, 1f);
            }

            float win = 0f, tie = 0f, loss = 0f, total = 0f;

            foreach (var element in ElementInfo.All)
            {
                var weight = theirs.WeightOf(element);

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

using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Sim
{
    /// <summary>
    /// A creature's elements: what it still holds, and what everyone has been told it spent.
    ///
    /// Two ledgers, on purpose. <see cref="Ledger"/> is the published one -- the reveal record,
    /// what is outstanding, what has been proven -- and it is what every forecast, prompt and
    /// card reads. <see cref="Private"/> is the truth, including anything committed to a clash
    /// and not yet announced, and it is for the fight's own arithmetic only: whether a second
    /// commitment can be afforded, what is left to spend.
    ///
    /// The gap between them is the whole of DE-005's concealment. An attacker's element leaves
    /// the hand the moment they swing, but the record does not move until the defender has
    /// answered -- because the record is public, and a defender who could read it would know
    /// exactly what was coming. Reading <see cref="Private"/> for a forecast is not a small
    /// error: it forecasts against every element except the one about to arrive.
    /// </summary>
    public sealed class ElementPool
    {
        ElementLedger m_Ledger;

        // A spend that has happened but has not been announced. Only while a clash is in flight.
        ElementLedger? m_Pending;

        // What was put up for the clash in flight, so it can be handed back if it is kept.
        readonly List<Element> m_Committed = new List<Element>();

        public ElementPool(ElementCounts starting) => m_Ledger = ElementLedger.Starting(starting);

        /// <summary>What everybody has been told. What views, prompts and forecasts read.</summary>
        public ElementLedger Ledger => m_Ledger;

        /// <summary>The truth, commitments included. For the fight's own arithmetic and nothing else.</summary>
        public ElementLedger Private => m_Pending ?? m_Ledger;

        /// <summary>
        /// What is actually in the hand right now, commitments already gone from it.
        ///
        /// For the creature's own player, who may watch their hand shrink as they act, and for the
        /// brain. Nobody else is entitled to it, and the record they read has not moved yet.
        /// </summary>
        public ElementCounts Hand => Private.Pool;

        /// <summary>Whether an answer has been put up that nobody has been told about yet.</summary>
        public bool HasAnswerInFlight => m_Pending.HasValue;

        /// <summary>
        /// Spends an element, lowering the pool and raising the reveal record together.
        ///
        /// Refused outright while a commitment is unannounced. What was put up is remembered as a
        /// list beside the record so it can be handed back if it is kept, and that list is matched
        /// against the record by element -- so a spend that lands underneath it takes the entry
        /// the answer was going to reclaim, and the hand ends up with an element nobody has.
        ///
        /// The fight cannot reach this: a clash makes it busy, and a busy fight takes no orders.
        /// The refusal is here so that if that ever stops being true, it stops being true loudly,
        /// rather than by quietly inventing an element.
        /// </summary>
        /// <returns>False if the creature does not hold it, in which case nothing changed.</returns>
        public bool Spend(Element element, int amount, out SpendRefusal refusal)
        {
            if (m_Pending.HasValue)
            {
                refusal = SpendRefusal.AnswerInFlight;
                return false;
            }

            if (!m_Ledger.TrySpend(element, amount, out var next, out refusal))
            {
                return false;
            }

            m_Ledger = next;
            return true;
        }

        /// <summary>
        /// Spends an element without saying what it was.
        ///
        /// The pool moves at once, so the creature's own player sees their hand shrink and the
        /// brain prices its next action correctly. The record does not move until
        /// <see cref="AnnounceCommitted"/>, which DE-005 is specific about: each side's expenditure
        /// is emitted after that side's own reveal.
        /// </summary>
        public bool Commit(Element element, int amount, out SpendRefusal refusal)
        {
            if (!Private.TrySpend(element, amount, out var next, out refusal))
            {
                return false;
            }

            m_Pending = next;

            for (var i = 0; i < amount; i++)
            {
                m_Committed.Add(element);
            }

            return true;
        }

        /// <summary>
        /// Says what was committed, now that saying so is allowed.
        ///
        /// Safe to call with nothing pending, because both sides of a clash are announced together
        /// and only one of them may have committed anything.
        /// </summary>
        /// <param name="keep">
        /// Whether the commitment goes back into the hand. A defender who answered well enough to
        /// stop the blow outright keeps what they put up; everybody else has spent it.
        /// </param>
        public void AnnounceCommitted(bool keep = false)
        {
            if (!m_Pending.HasValue)
            {
                return;
            }

            var ledger = m_Pending.Value;

            if (keep && m_Committed.Count > 0)
            {
                // Shown, but not spent. The identification stands -- putting an element up is how
                // you prove you have it, and nobody unsees that -- while the element itself goes
                // back in the hand.
                var pool = ledger.Pool;
                var outstanding = new List<Element>(ledger.Outstanding);

                foreach (var element in m_Committed)
                {
                    pool = pool.Plus(element, 1);

                    // From the end: this commitment is the most recent thing to leave the hand.
                    var last = outstanding.LastIndexOf(element);

                    if (last >= 0)
                    {
                        outstanding.RemoveAt(last);
                    }
                }

                ledger = new ElementLedger(pool, ledger.Revealed, outstanding,
                    ledger.Total, ledger.Identified);
            }

            m_Committed.Clear();
            m_Ledger = ledger;
            m_Pending = null;
        }

        /// <summary>
        /// Brings back the oldest outstanding spend, which is what Take a Breath does.
        ///
        /// Refused while a commitment is unannounced, for the reason <see cref="Spend"/> is, and
        /// for one more: the oldest spend the truth knows about may be the commitment itself, and
        /// handing that back would take an answer out of the air mid-clash and tell everybody
        /// about it on the way.
        /// </summary>
        public bool Return(out Element returned, out SpendRefusal refusal)
        {
            returned = default;

            if (m_Pending.HasValue)
            {
                refusal = SpendRefusal.AnswerInFlight;
                return false;
            }

            if (!m_Ledger.TryReturn(out var next, out returned, out refusal))
            {
                return false;
            }

            m_Ledger = next;
            return true;
        }
    }
}

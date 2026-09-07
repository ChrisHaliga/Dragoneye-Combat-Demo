using System;
using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Sim
{
    /// <summary>
    /// Whose turn it is, what round it is, and who is left.
    ///
    /// The order is built once by <see cref="TurnOrder"/> and only ever shrinks: a creature that
    /// falls is taken out, and the index corrected in the same breath, because entries before the
    /// current one shift everything down and without the correction the turn silently passes to
    /// whoever moved into the slot.
    /// </summary>
    public sealed class TurnQueue
    {
        /// <summary>The fight is still going.</summary>
        public const int Running = -1;

        /// <summary>The fight is finished and nobody won it.</summary>
        public const int NoWinner = -2;

        readonly List<uint> m_Order = new List<uint>();

        /// <summary>Initiative order as creature ids. Fastest first.</summary>
        public IReadOnlyList<uint> Order => m_Order;

        /// <summary>Where in the order the turn is, or -1 between fights.</summary>
        public int Index { get; private set; } = -1;

        /// <summary>Rounds completed plus one. Zero before the fight starts.</summary>
        public int Round { get; private set; }

        /// <summary>
        /// <see cref="Running"/>, <see cref="NoWinner"/>, or the winning party as an int.
        ///
        /// An int rather than a nullable party, because the replicated copy has to be one and the
        /// two should agree on what the number means.
        /// </summary>
        public int Outcome { get; private set; } = Running;

        public bool IsOver => Outcome != Running;

        /// <summary>
        /// Whether a side actually won it.
        ///
        /// Separate from <see cref="IsOver"/> because a fight can end with nobody standing, and
        /// because a test scenario stops the fight the moment its script runs out -- neither is
        /// a victory, and awarding one would put a lie on the screen.
        /// </summary>
        public bool HasWinner => Outcome >= 0;

        /// <summary>The winning party. Only meaningful when <see cref="HasWinner"/>.</summary>
        public Party Winner => (Party)(Outcome < 0 ? 0 : Outcome);

        /// <summary>The creature whose turn it is, or 0 if there is none.</summary>
        public uint ActiveId => Index >= 0 && Index < m_Order.Count ? m_Order[Index] : 0u;

        /// <summary>Builds the initiative order and starts the first turn.</summary>
        public void Begin(IReadOnlyList<Combatant> combatants, Func<uint, bool> isActive)
        {
            m_Order.Clear();
            m_Order.AddRange(TurnOrder.Build(combatants));

            Outcome = Running;
            Round = 1;
            Index = TurnOrder.TryFirst(m_Order, isActive, out var first) ? first : -1;
        }

        /// <summary>
        /// Hands the turn to the next creature that can still act, rolling the round over when
        /// the order wraps.
        /// </summary>
        /// <returns>False when nobody can act, which means the fight is over.</returns>
        public bool Advance(Func<uint, bool> isActive, out bool wrapped)
        {
            wrapped = false;

            if (IsOver)
            {
                return false;
            }

            if (!TurnOrder.TryAdvance(m_Order, Index, isActive, out var next, out wrapped))
            {
                Index = -1;
                return false;
            }

            if (wrapped)
            {
                Round++;
            }

            Index = next;
            return true;
        }

        /// <summary>Records the winner, which ends the fight.</summary>
        public void DeclareWinner(Party party) => Finish((int)party);

        /// <summary>Ends the fight with nobody winning it.</summary>
        public void End() => Finish(NoWinner);

        void Finish(int outcome)
        {
            // The first ending stands. A scenario that stops a fight somebody has already won
            // must not take the win back.
            if (IsOver)
            {
                return;
            }

            Outcome = outcome;
            Index = -1;
        }

        /// <summary>Removes a dead creature from the order, keeping the turn where it was.</summary>
        public void Remove(uint id)
        {
            var at = m_Order.IndexOf(id);

            if (at < 0)
            {
                return;
            }

            m_Order.RemoveAt(at);

            if (at < Index)
            {
                Index--;
            }
        }
    }
}

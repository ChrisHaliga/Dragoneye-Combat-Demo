using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>
    /// One creature's place in the initiative queue, reduced to what ordering needs.
    ///
    /// A flat struct rather than a reference to the creature, so the ordering can be built and
    /// tested without a scene, a network or a prefab -- and so the server can replicate the order
    /// as ids rather than object references.
    /// </summary>
    public readonly struct Combatant
    {
        public readonly uint Id;
        public readonly int Speed;

        public Combatant(uint id, int speed)
        {
            Id = id;
            Speed = speed;
        }
    }

    /// <summary>
    /// Who acts, in what order.
    ///
    /// Fastest first. Ties break on id, ascending -- and the tiebreak is the point of this class
    /// existing rather than a bare <c>Sort</c> call. Every peer builds this list independently from
    /// replicated state, so any ordering that depends on insertion order, dictionary iteration or a
    /// random seed would put two clients in different turns and be nearly impossible to reproduce.
    /// Speed and id are both replicated and both stable, so this is the same list everywhere.
    /// </summary>
    public static class TurnOrder
    {
        /// <summary>
        /// Orders combatants for a round. The input is not mutated.
        /// </summary>
        public static List<uint> Build(IReadOnlyList<Combatant> combatants)
        {
            var order = new List<uint>();
            if (combatants == null)
            {
                return order;
            }

            var sorted = new List<Combatant>(combatants);

            // A comparison, not a key selector: List.Sort is unstable, so equal speeds would come
            // out in whatever order the partitioning happened to leave them. Falling through to the
            // id makes the result total and therefore identical on every machine.
            sorted.Sort((a, b) =>
                a.Speed != b.Speed ? b.Speed.CompareTo(a.Speed) : a.Id.CompareTo(b.Id));

            foreach (var combatant in sorted)
            {
                order.Add(combatant.Id);
            }

            return order;
        }

        /// <summary>
        /// The next actor after <paramref name="currentIndex"/>, skipping anyone no longer able to
        /// act, and whether the round rolled over.
        ///
        /// Skipping is done here rather than by removing the dead from the order, so the turn bar
        /// can keep showing who was in the fight and in what position.
        /// </summary>
        /// <param name="isActive">Whether the combatant with this id can still take a turn.</param>
        /// <returns>False when nobody at all can act, which ends the match rather than the round.</returns>
        public static bool TryAdvance(IReadOnlyList<uint> order, int currentIndex,
            System.Func<uint, bool> isActive, out int nextIndex, out bool roundEnded)
        {
            nextIndex = currentIndex;
            roundEnded = false;

            if (order == null || order.Count == 0 || isActive == null)
            {
                return false;
            }

            for (var step = 1; step <= order.Count; step++)
            {
                var candidate = (currentIndex + step) % order.Count;

                // Wrapping past the end is what a round is. Detected on the step that crosses, so a
                // round still ends when the last few combatants in the order are all dead.
                if (currentIndex + step >= order.Count)
                {
                    roundEnded = true;
                }

                if (isActive(order[candidate]))
                {
                    nextIndex = candidate;
                    return true;
                }
            }

            roundEnded = false;
            return false;
        }

        /// <summary>
        /// The first combatant that can act, for the opening turn of a match.
        /// </summary>
        public static bool TryFirst(IReadOnlyList<uint> order, System.Func<uint, bool> isActive,
            out int index)
        {
            index = 0;

            if (order == null || isActive == null)
            {
                return false;
            }

            for (var i = 0; i < order.Count; i++)
            {
                if (isActive(order[i]))
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }
    }
}

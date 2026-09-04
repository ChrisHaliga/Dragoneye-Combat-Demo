using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// The arena as combat needs to see it: what a route costs, what is standing where, and what is
    /// next to a given hex.
    ///
    /// One implementation, used by the server when it validates an action, by the brain when it
    /// decides one, and by the client when it prices the hover. That is the point. Each of those had
    /// its own copy of "living creatures block a route, except the one moving", and three copies of a
    /// rule is three chances for the label to promise a move the server refuses.
    ///
    /// Occupancy comes from <see cref="UnitIndex"/> rather than a scan of the creature registry,
    /// because the index is already the authority on which hex holds what and answers in constant
    /// time.
    /// </summary>
    public sealed class ArenaBoard : IBoardQuery
    {
        readonly ArenaMap m_Map;
        readonly UnitIndex m_Units;

        // Reused across calls. Every method that fills them consumes them before returning, and
        // PathTo hands back a copy, so no caller is left holding a buffer that later changes.
        readonly List<Hex> m_Path = new List<Hex>();
        readonly HashSet<Hex> m_Blocked = new HashSet<Hex>();

        public ArenaBoard(ArenaMap map, UnitIndex units)
        {
            m_Map = map;
            m_Units = units;
        }

        /// <summary>True when there is a map to path across.</summary>
        public bool IsReady => m_Map != null && m_Map.Map != null && m_Units != null;

        /// <summary>Steps along the cheapest route, or -1 if there is none.</summary>
        public int CostTo(Hex from, Hex to) => TryPath(from, to) ? m_Path.Count : -1;

        /// <summary>
        /// The cheapest route, destination last, empty if unreachable.
        ///
        /// Returns a copy. The caller may hold it across further queries -- the brain compares
        /// several routes before choosing -- and handing out the shared buffer would let the second
        /// query rewrite the first answer.
        /// </summary>
        public IReadOnlyList<Hex> PathTo(Hex from, Hex to) =>
            TryPath(from, to) ? new List<Hex>(m_Path) : System.Array.Empty<Hex>();

        public bool IsOccupied(Hex hex) => m_Units != null && m_Units.IsOccupied(hex);

        /// <summary>
        /// Whether at least one neighbouring hex could be stepped into.
        ///
        /// Asked rather than assumed from AP: a creature walled in by its own allies has points it
        /// cannot spend, and the End Turn prompt would otherwise never fire for it.
        /// </summary>
        public bool HasOpenNeighbour(Hex from)
        {
            if (!IsReady)
            {
                return false;
            }

            foreach (var neighbour in from.Neighbors())
            {
                if (!m_Units.IsOccupied(neighbour)
                    && m_Map.Map.TryGetTile(neighbour, out var tile) && tile.IsWalkable)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether a living enemy stands within attack reach of this hex.</summary>
        public bool HasEnemyInReach(Hex from, Party party)
        {
            if (m_Units == null)
            {
                return false;
            }

            foreach (var candidate in Hex.Range(from, CombatRules.AttackRange))
            {
                if (candidate == from || !m_Units.TryGet(candidate, out var occupant))
                {
                    continue;
                }

                var creature = occupant.GetComponent<CreatureState>();
                if (creature != null && creature.IsAlive && creature.Party != party)
                {
                    return true;
                }
            }

            return false;
        }

        bool TryPath(Hex from, Hex to)
        {
            m_Path.Clear();

            if (!IsReady)
            {
                return false;
            }

            m_Blocked.Clear();
            m_Units.CopyOccupiedTo(m_Blocked, from);

            return HexPathfinder.TryFindPath(m_Map.Map, from, to, m_Blocked, m_Path);
        }
    }
}

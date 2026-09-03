using System.Collections.Generic;
using Dragoneye.Hex;
using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Which unit is standing on which hex.
    ///
    /// This is what makes "click a unit" and "click a tile" the same operation: a click resolves to
    /// a hex, and the hex is looked up here. No second raycast, no colliders on units, and the same
    /// structure answers the occupancy question that movement validation and, later, targeting both
    /// need anyway.
    ///
    /// A plain component rather than a static so it dies with the arena scene. A static registry
    /// would carry stale entries into the next match.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitIndex : MonoBehaviour
    {
        readonly Dictionary<Hex, UnitState> m_Occupants = new Dictionary<Hex, UnitState>();

        public bool TryGet(Hex hex, out UnitState unit) => m_Occupants.TryGetValue(hex, out unit);

        public bool IsOccupied(Hex hex) => m_Occupants.ContainsKey(hex);

        /// <summary>Occupancy ignoring one unit -- the mover should not block its own move.</summary>
        public bool IsOccupiedByOther(Hex hex, UnitState mover) =>
            m_Occupants.TryGetValue(hex, out var occupant) && occupant != mover;

        public void Register(UnitState unit) => m_Occupants[unit.Cell] = unit;

        public void Unregister(UnitState unit)
        {
            if (m_Occupants.TryGetValue(unit.Cell, out var occupant) && occupant == unit)
            {
                m_Occupants.Remove(unit.Cell);
            }
        }

        public void Move(UnitState unit, Hex from, Hex to)
        {
            // Guarded because the entry at `from` may already belong to someone else if two units
            // swapped in the same tick.
            if (m_Occupants.TryGetValue(from, out var occupant) && occupant == unit)
            {
                m_Occupants.Remove(from);
            }

            m_Occupants[to] = unit;
        }
    }
}

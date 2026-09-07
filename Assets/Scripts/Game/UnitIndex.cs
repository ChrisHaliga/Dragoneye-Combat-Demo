using System;
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
    /// Who is standing where. One occupant per cell; a split tile can hold one per area.</summary>
    [DisallowMultipleComponent]
    public sealed class UnitIndex : MonoBehaviour
    {
        readonly Dictionary<Cell, UnitState> m_Occupants = new Dictionary<Cell, UnitState>();

        /// <summary>Somebody arrived, left or moved. Anything that drew the board's occupancy redraws.</summary>
        public event Action Changed;

        public bool TryGet(Cell cell, out UnitState unit) => m_Occupants.TryGetValue(cell, out unit);

        public bool IsOccupied(Cell cell) => m_Occupants.ContainsKey(cell);

        public bool IsOccupiedByOther(Cell cell, UnitState mover) =>
            m_Occupants.TryGetValue(cell, out var occupant) && occupant != mover;

        /// <summary>Everybody on any area of a tile. A line passing over the tile passes them all.</summary>
        public void OccupantsOf(Hex tile, List<UnitState> into)
        {
            foreach (var pair in m_Occupants)
            {
                if (pair.Key.Tile == tile)
                {
                    into.Add(pair.Value);
                }
            }
        }

        public void CopyOccupiedTo(ICollection<Cell> into, Cell except)
        {
            if (into == null)
            {
                return;
            }

            foreach (var pair in m_Occupants)
            {
                if (pair.Key != except)
                {
                    into.Add(pair.Key);
                }
            }
        }

        public void Register(UnitState unit)
        {
            m_Occupants[unit.Cell] = unit;
            Notify.Raise(Changed, this);
        }

        public void Unregister(UnitState unit)
        {
            if (m_Occupants.TryGetValue(unit.Cell, out var occupant) && occupant == unit)
            {
                m_Occupants.Remove(unit.Cell);
                Notify.Raise(Changed, this);
            }
        }

        public void Move(UnitState unit, Cell from, Cell to)
        {
            // Guarded because the entry at `from` may already belong to someone else if two units
            // swapped in the same tick.
            if (m_Occupants.TryGetValue(from, out var occupant) && occupant == unit)
            {
                m_Occupants.Remove(from);
            }

            m_Occupants[to] = unit;
            Notify.Raise(Changed, this);
        }
    }
}

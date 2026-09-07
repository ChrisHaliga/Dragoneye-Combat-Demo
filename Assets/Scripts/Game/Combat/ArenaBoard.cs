using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;
using Dragoneye.Sim;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// The board as a client sees it: the fight's own <see cref="FightBoard"/>, read over
    /// replicated occupancy.
    ///
    /// The server's board is built over the fight's table and this one over the unit index, and
    /// they answer every question with the same code -- which is what lets a cursor promise a
    /// price the server will honour. The grid is read live rather than captured, because the
    /// arena rebuilds it when a different map is picked, and a board holding the old one would
    /// price routes through walls that are no longer there.
    /// </summary>
    public sealed class ArenaBoard : FightBoard
    {
        readonly ArenaMap m_Map;

        public ArenaBoard(ArenaMap map, UnitIndex units, CreatureRegistry creatures)
            : base(null, new ClientOccupancy(units, creatures))
        {
            m_Map = map;
        }

        public override IGridRules Grid => m_Map != null ? m_Map.Grid : null;
    }

    /// <summary>
    /// Who is standing where, read from the replicated unit index and the creature registry.
    ///
    /// The unit index answers by cell and the registry by id; a board question needs both, and
    /// this is the one place the two are read together for it.
    /// </summary>
    public sealed class ClientOccupancy : IOccupancy
    {
        readonly UnitIndex m_Units;
        readonly CreatureRegistry m_Creatures;
        readonly List<UnitState> m_Occupants = new List<UnitState>();

        public ClientOccupancy(UnitIndex units, CreatureRegistry creatures)
        {
            m_Units = units;
            m_Creatures = creatures;
        }

        public bool IsOccupied(Cell cell) => m_Units != null && m_Units.IsOccupied(cell);

        public bool TryGet(Cell cell, out uint id)
        {
            if (m_Units != null && m_Units.TryGet(cell, out var unit))
            {
                id = (uint)unit.NetworkObjectId;
                return true;
            }

            id = 0;
            return false;
        }

        public void OccupantsOf(Hex tile, List<uint> into)
        {
            if (m_Units == null)
            {
                return;
            }

            m_Occupants.Clear();
            m_Units.OccupantsOf(tile, m_Occupants);

            foreach (var unit in m_Occupants)
            {
                into.Add((uint)unit.NetworkObjectId);
            }
        }

        public void CopyOccupiedTo(ICollection<Cell> into, Cell except) =>
            m_Units?.CopyOccupiedTo(into, except);

        public bool IsAlive(uint id)
        {
            var creature = m_Creatures != null ? m_Creatures.ByTurnId(id) : null;
            return creature != null && creature.IsAlive;
        }

        public Party PartyOf(uint id)
        {
            var creature = m_Creatures != null ? m_Creatures.ByTurnId(id) : null;
            return creature != null ? creature.Party : default;
        }
    }
}

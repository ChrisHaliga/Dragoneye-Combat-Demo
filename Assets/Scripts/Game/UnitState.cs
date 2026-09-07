using System;
using Dragoneye.Hex;
using Unity.Netcode;
using UnityEngine;
using Dragoneye.Game.Combat;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// A unit's replicated position: which hex it stands on, and whether it still stands at all.
    ///
    /// A mirror of the fight's own record of where the creature is, written by
    /// <see cref="CombatDirector"/> after every order. The cell is the *only* replicated
    /// position. There is deliberately no NetworkTransform on the unit prefab: two ints per move
    /// replace a continuous transform stream, every client derives the world position locally,
    /// and the height never travels at all -- so "constant Y across the map" is structural rather
    /// than something to remember to enforce.
    ///
    /// It also means animation cannot leak into the data. There is no mechanism by which the
    /// authoritative position could wait for a view to finish sliding.
    ///
    /// Server-write, unlike the focus point's owner-authoritative transform. A unit's position
    /// decides outcomes, so clients send intent through <see cref="UnitCommands"/> and the fight
    /// decides. The payload is a destination rather than a stream, so authority costs nothing here.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class UnitState : NetworkBehaviour
    {
        readonly NetworkVariable<NetCell> m_Cell = new NetworkVariable<NetCell>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // Whether this unit holds a cell at all. A dead creature gives its cell up and stays as
        // an object, so a watcher behind the fight can still be shown it fall.
        readonly NetworkVariable<bool> m_OnBoard = new NetworkVariable<bool>(true);

        // Where the spawner wants this unit, held until there is a NetworkVariable to put it in.
        NetCell m_StartCell;

        UnitIndex m_Index;

        /// <summary>The hex this unit occupies. Authoritative the instant the server writes it.</summary>
        public Cell Cell => m_Cell.Value.ToCell();

        /// <summary>Raised on every client when the unit's cell changes.</summary>
        public event Action<Cell> CellChanged;

        /// <summary>Whether this unit still holds a cell. False once it has fallen.</summary>
        public bool OnBoard => m_OnBoard.Value;

        /// <summary>
        /// Server only, and only before <c>Spawn()</c>. Sets where the unit comes into existence.
        ///
        /// Separate from <see cref="ServerMirror"/> because a NetworkVariable written before the
        /// object is spawned is dropped. Writing the cell after the spawn call instead would work,
        /// but only by ordering: the object is live and filed at (0,0) in between, so every unit
        /// momentarily claims the same hex and anything reading occupancy in that window is wrong.
        /// Set here, published in <see cref="OnNetworkSpawn"/>, the unit is never anywhere else.
        /// </summary>
        public void ServerPlaceAt(Cell cell) => m_StartCell = new NetCell(cell);

        public override void OnNetworkSpawn()
        {
            // Before subscribing and before registering: this is the unit's first position, not a
            // move, and the index must never see the default cell.
            if (IsServer)
            {
                m_Cell.Value = m_StartCell;
            }

            m_Cell.OnValueChanged += OnCellChanged;
            m_OnBoard.OnValueChanged += OnBoardChanged;

            var context = ArenaContext.Current;
            m_Index = context != null ? context.Units : null;

            if (m_Index != null && m_OnBoard.Value)
            {
                m_Index.Register(this);
            }
            else
            {
                // Without the index this unit is invisible to occupancy and to clicks, which used
                // to happen with nothing logged at all.
                Debug.LogError("UnitState found no unit index; it will be unclickable.", this);
            }

            Notify.Raise(CellChanged, Cell, this);
        }

        public override void OnNetworkDespawn()
        {
            m_Cell.OnValueChanged -= OnCellChanged;
            m_OnBoard.OnValueChanged -= OnBoardChanged;

            if (m_Index != null)
            {
                m_Index.Unregister(this);
                m_Index = null;
            }
        }

        /// <summary>
        /// Server only. Copies where the fight says this unit is.
        ///
        /// Leaving the board is written before the cell: a fallen creature's cell is meaningless,
        /// and the index must unregister it rather than move it.
        /// </summary>
        public void ServerMirror(Cell cell, bool onBoard)
        {
            if (!IsServer)
            {
                return;
            }

            if (m_OnBoard.Value != onBoard)
            {
                m_OnBoard.Value = onBoard;
            }

            var next = new NetCell(cell);

            if (onBoard && !m_Cell.Value.Equals(next))
            {
                m_Cell.Value = next;
            }
        }

        void OnBoardChanged(bool previous, bool current)
        {
            if (!current)
            {
                m_Index?.Unregister(this);
            }
        }

        void OnCellChanged(NetCell previous, NetCell current)
        {
            if (m_OnBoard.Value)
            {
                m_Index?.Move(this, previous.ToCell(), current.ToCell());
            }

            Notify.Raise(CellChanged, current.ToCell(), this);
        }
    }
}

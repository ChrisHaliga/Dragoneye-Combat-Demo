using System;
using Dragoneye.Hex;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// A unit's replicated state: which hex it stands on, and whose it is.
    ///
    /// The cell is the *only* replicated position. There is deliberately no NetworkTransform on the
    /// unit prefab: two ints per move replace a continuous transform stream, every client derives
    /// the world position locally, and the height never travels at all -- so "constant Y across the
    /// map" is structural rather than something to remember to enforce.
    ///
    /// It also means animation cannot leak into the data. There is no mechanism by which the
    /// authoritative position could wait for a view to finish sliding.
    ///
    /// Server-write, unlike the focus point's owner-authoritative transform. A unit's position
    /// decides outcomes, so clients send intent through <see cref="UnitCommands"/> and the server
    /// decides. The payload is a destination rather than a stream, so authority costs nothing here.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class UnitState : NetworkBehaviour
    {
        readonly NetworkVariable<NetCell> m_Cell = new NetworkVariable<NetCell>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        readonly NetworkVariable<int> m_OwnerSlot = new NetworkVariable<int>(-1);

        UnitIndex m_Index;

        /// <summary>The hex this unit occupies. Authoritative the instant the server writes it.</summary>
        public Hex Cell => m_Cell.Value.ToHex();

        /// <summary>Stable player slot, or -1 before the server has assigned one. Drives colour.</summary>
        public int OwnerSlot => m_OwnerSlot.Value;

        /// <summary>Raised on every client when the unit's cell changes.</summary>
        public event Action<Hex> CellChanged;

        /// <summary>Raised when the owning slot arrives or changes.</summary>
        public event Action SlotChanged;

        public override void OnNetworkSpawn()
        {
            m_Cell.OnValueChanged += OnCellChanged;
            m_OwnerSlot.OnValueChanged += OnSlotChanged;

            var context = ArenaContext.Current;
            m_Index = context != null ? context.Units : null;

            if (m_Index != null)
            {
                m_Index.Register(this);
            }

            CellChanged?.Invoke(Cell);
            SlotChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            m_Cell.OnValueChanged -= OnCellChanged;
            m_OwnerSlot.OnValueChanged -= OnSlotChanged;

            if (m_Index != null)
            {
                m_Index.Unregister(this);
                m_Index = null;
            }
        }

        /// <summary>Server only. Places the unit without any client being able to ask for it.</summary>
        public void ServerSetCell(Hex hex)
        {
            if (IsServer)
            {
                m_Cell.Value = new NetCell(hex);
            }
        }

        /// <summary>Server only.</summary>
        public void ServerSetSlot(int slot)
        {
            if (IsServer)
            {
                m_OwnerSlot.Value = slot;
            }
        }

        void OnCellChanged(NetCell previous, NetCell current)
        {
            m_Index?.Move(this, previous.ToHex(), current.ToHex());
            CellChanged?.Invoke(current.ToHex());
        }

        void OnSlotChanged(int previous, int current) => SlotChanged?.Invoke();
    }
}

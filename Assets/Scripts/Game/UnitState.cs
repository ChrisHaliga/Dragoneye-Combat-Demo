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

        UnitIndex m_Index;

        /// <summary>The hex this unit occupies. Authoritative the instant the server writes it.</summary>
        public Hex Cell => m_Cell.Value.ToHex();

        /// <summary>Raised on every client when the unit's cell changes.</summary>
        public event Action<Hex> CellChanged;

        public override void OnNetworkSpawn()
        {
            m_Cell.OnValueChanged += OnCellChanged;

            var context = ArenaContext.Current;
            m_Index = context != null ? context.Units : null;

            if (m_Index != null)
            {
                m_Index.Register(this);
            }
            else
            {
                // Without the index this unit is invisible to occupancy and to clicks, which used
                // to happen with nothing logged at all.
                Debug.LogError("UnitState found no unit index; it will be unclickable.", this);
            }

            CellChanged?.Invoke(Cell);
        }

        public override void OnNetworkDespawn()
        {
            m_Cell.OnValueChanged -= OnCellChanged;

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

        void OnCellChanged(NetCell previous, NetCell current)
        {
            m_Index?.Move(this, previous.ToHex(), current.ToHex());
            CellChanged?.Invoke(current.ToHex());
        }

    }
}

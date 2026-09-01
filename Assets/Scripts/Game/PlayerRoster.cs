using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>One player's entry in the match roster.</summary>
    public struct PlayerEntry : INetworkSerializable, IEquatable<PlayerEntry>
    {
        /// <summary>Stable 0-based index for this match. Drives colour.</summary>
        public int Slot;

        public ulong ClientId;

        public FixedString64Bytes Name;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
        }

        public bool Equals(PlayerEntry other) =>
            Slot == other.Slot && ClientId == other.ClientId && Name.Equals(other.Name);
    }

    /// <summary>
    /// Who is in this match, and which slot each player holds.
    ///
    /// Exists so presentation never keys off an NGO client id. Client ids are assigned by the
    /// transport and reused after a disconnect, so using one as a colour index means a player can
    /// change colour mid-session, and two players can collide on the same colour after a reconnect.
    /// A slot is assigned once, by the server, and never moves.
    ///
    /// The roster is also where a name belongs: <see cref="FocusView"/> reads from here rather than
    /// from the session, which keeps the game assembly out of the lobby's business and avoids the
    /// one-round-trip "Player 0" flash while a name replicates.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRoster : NetworkBehaviour
    {
        readonly NetworkList<PlayerEntry> m_Entries = new NetworkList<PlayerEntry>();

        public static PlayerRoster Current { get; private set; }

        /// <summary>Raised whenever an entry is added, removed or changed.</summary>
        public event Action Changed;

        public override void OnNetworkSpawn()
        {
            Current = this;
            m_Entries.OnListChanged += OnListChanged;
        }

        public override void OnNetworkDespawn()
        {
            m_Entries.OnListChanged -= OnListChanged;

            if (Current == this)
            {
                Current = null;
            }
        }

        void OnListChanged(NetworkListEvent<PlayerEntry> _) => Changed?.Invoke();

        /// <summary>Server only. Assigns the next free slot, or returns the existing one.</summary>
        public int Register(ulong clientId, string name)
        {
            if (!IsServer)
            {
                return 0;
            }

            for (var i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].ClientId == clientId)
                {
                    return m_Entries[i].Slot;
                }
            }

            var slot = m_Entries.Count;
            m_Entries.Add(new PlayerEntry
            {
                Slot = slot,
                ClientId = clientId,
                Name = new FixedString64Bytes(FixedStringText.Clamp(name))
            });

            return slot;
        }

        public bool TryGet(ulong clientId, out PlayerEntry entry)
        {
            for (var i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].ClientId == clientId)
                {
                    entry = m_Entries[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }
    }
}

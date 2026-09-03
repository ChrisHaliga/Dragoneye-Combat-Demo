using System;
using System.Collections.Generic;
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

        // Names reported by clients before their entry exists. The server cannot know a remote
        // player's lobby name any other way: it lives on that player's UGS account.
        readonly Dictionary<ulong, string> m_ReportedNames = new Dictionary<ulong, string>();

        public static PlayerRoster Current { get; private set; }

        /// <summary>Raised whenever an entry is added, removed or changed.</summary>
        public event Action Changed;

        public override void OnNetworkSpawn()
        {
            Current = this;
            m_Entries.OnListChanged += OnListChanged;

            if (IsServer)
            {
                // Slots are handed out on connect rather than at match start, so the draft can
                // address players while they are still choosing.
                foreach (var clientId in NetworkManager.ConnectedClientsIds)
                {
                    Register(clientId, string.Empty);
                }

                NetworkManager.OnClientConnectedCallback += OnClientConnected;
            }

            // Every peer reports its own name, including the host. Previously only the host's name
            // was ever recorded, because the server filled entries from its own SessionRunner --
            // so everyone else showed as "Player N".
            var runner = Dragoneye.Multiplayer.SessionRunner.Instance;
            var name = runner != null ? runner.PlayerName : null;
            if (!string.IsNullOrEmpty(name))
            {
                ReportNameRpc(new FixedString64Bytes(FixedStringText.Clamp(name)));
            }
        }

        /// <summary>
        /// A player telling the server what it is called. Sender-derived, so a client can only ever
        /// name itself.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        void ReportNameRpc(FixedString64Bytes name, RpcParams rpc = default)
        {
            var clientId = rpc.Receive.SenderClientId;
            m_ReportedNames[clientId] = name.ToString();

            for (var i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].ClientId == clientId)
                {
                    var entry = m_Entries[i];
                    entry.Name = name;
                    m_Entries[i] = entry;
                    return;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            m_Entries.OnListChanged -= OnListChanged;

            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            }

            if (Current == this)
            {
                Current = null;
            }
        }

        void OnListChanged(NetworkListEvent<PlayerEntry> _) => Changed?.Invoke();

        void OnClientConnected(ulong clientId) => Register(clientId, string.Empty);

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

            // Prefer what the client reported over whatever the caller guessed.
            if (m_ReportedNames.TryGetValue(clientId, out var reported) && !string.IsNullOrEmpty(reported))
            {
                name = reported;
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

        /// <summary>Reverse lookup: which client holds a slot.</summary>
        public bool TryGetBySlot(int slot, out PlayerEntry entry)
        {
            for (var i = 0; i < m_Entries.Count; i++)
            {
                if (m_Entries[i].Slot == slot)
                {
                    entry = m_Entries[i];
                    return true;
                }
            }

            entry = default;
            return false;
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

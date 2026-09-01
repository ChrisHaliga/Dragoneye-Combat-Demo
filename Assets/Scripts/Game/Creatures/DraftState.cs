using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// The party draft: which creatures are in play, on which side, claimed by whom.
    ///
    /// Spawned from a prefab by the host rather than placed in a scene. The draft has to outlive the
    /// menu-to-arena transition, and a dynamically spawned NetworkObject does that for free -- NGO
    /// moves every root-level spawned object with <c>DestroyWithScene == false</c> into
    /// DontDestroyOnLoad before a single-mode load. An in-scene object in Bootstrap could not work:
    /// that scene is unloaded during boot and netcode never loaded it, so nothing there is ever
    /// spawned at all.
    ///
    /// Replicated over netcode rather than UGS lobby properties. Netcode is already running during
    /// the lobby, and draft edits are frequent, structured and order-sensitive; lobby properties are
    /// rate-limited, string-typed and eventually consistent. The lobby keeps what it is good at --
    /// discovery, join codes, ready state.
    ///
    /// Every mutation is a server RPC. Client-side buttons decide what to *offer*; this decides what
    /// is allowed.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class DraftState : NetworkBehaviour
    {
        [SerializeField, Tooltip("Resolves creature ids. Must be the same asset on every peer.")]
        CreatureCatalog m_Catalog;

        readonly NetworkList<RosterEntry> m_Roster = new NetworkList<RosterEntry>();
        readonly NetworkList<PartyChoice> m_PartyChoices = new NetworkList<PartyChoice>();

        readonly NetworkVariable<uint> m_ClaimSequence = new NetworkVariable<uint>();

        public static DraftState Current { get; private set; }

        public CreatureCatalog Catalog => m_Catalog;

        public int EntryCount => m_Roster.Count;

        public RosterEntry this[int index] => m_Roster[index];

        /// <summary>Raised whenever the roster or party choices change.</summary>
        public event Action Changed;

        public override void OnNetworkSpawn()
        {
            Current = this;
            m_Roster.OnListChanged += OnRosterChanged;
            m_PartyChoices.OnListChanged += OnChoicesChanged;
            Changed?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            m_Roster.OnListChanged -= OnRosterChanged;
            m_PartyChoices.OnListChanged -= OnChoicesChanged;

            if (Current == this)
            {
                Current = null;
            }
        }

        void OnRosterChanged(NetworkListEvent<RosterEntry> _) => Changed?.Invoke();

        void OnChoicesChanged(NetworkListEvent<PartyChoice> _) => Changed?.Invoke();

        // ---------------------------------------------------------------- queries

        /// <summary>A snapshot of the roster. Used at match start, when it stops changing.</summary>
        public List<RosterEntry> Snapshot()
        {
            var copy = new List<RosterEntry>(m_Roster.Count);
            for (var i = 0; i < m_Roster.Count; i++)
            {
                copy.Add(m_Roster[i]);
            }

            return copy;
        }

        public Party PartyOf(byte slot)
        {
            for (var i = 0; i < m_PartyChoices.Count; i++)
            {
                if (m_PartyChoices[i].Slot == slot)
                {
                    return m_PartyChoices[i].Party;
                }
            }

            return Party.Heroes;
        }

        public bool HasChosenParty(byte slot)
        {
            for (var i = 0; i < m_PartyChoices.Count; i++)
            {
                if (m_PartyChoices[i].Slot == slot)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Slots in a party, ascending. The order that makes claim caps deterministic.</summary>
        public List<byte> SlotsIn(Party party)
        {
            var slots = new List<byte>();
            for (var i = 0; i < m_PartyChoices.Count; i++)
            {
                if (m_PartyChoices[i].Party == party)
                {
                    slots.Add(m_PartyChoices[i].Slot);
                }
            }

            slots.Sort();
            return slots;
        }

        public int CreatureCountIn(Party party)
        {
            var count = 0;
            for (var i = 0; i < m_Roster.Count; i++)
            {
                if (m_Roster[i].Party == party)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>The claim cap for one player, given who else is currently in their party.</summary>
        public int CapFor(byte slot)
        {
            var party = PartyOf(slot);
            var slots = SlotsIn(party);
            var ordinal = slots.IndexOf(slot);

            return ordinal < 0 ? 0 : ClaimRules.CapFor(CreatureCountIn(party), slots.Count, ordinal);
        }

        public int ClaimCountFor(byte slot)
        {
            var count = 0;
            for (var i = 0; i < m_Roster.Count; i++)
            {
                if (m_Roster[i].ClaimedBySlot == slot)
                {
                    count++;
                }
            }

            return count;
        }

        // ---------------------------------------------------------------- commands

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AddCreatureRpc(ushort creatureId, byte partyId, RpcParams rpc = default)
        {
            if (!IsHost(rpc) || m_Catalog == null || m_Catalog.Resolve(creatureId) == null)
            {
                return;
            }

            m_Roster.Add(new RosterEntry(creatureId, (Party)partyId));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RemoveCreatureRpc(int entryIndex, RpcParams rpc = default)
        {
            if (!IsHost(rpc) || !InRange(entryIndex))
            {
                return;
            }

            m_Roster.RemoveAt(entryIndex);
        }

        /// <summary>
        /// Joins a party. Any claims held in the old party are released, and everyone already in the
        /// new party has their cap recomputed -- which may push them over and release their newest
        /// claims.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ChoosePartyRpc(byte partyId, RpcParams rpc = default)
        {
            var slot = SlotOf(rpc.Receive.SenderClientId);
            if (slot == PartyInfo.Unclaimed)
            {
                return;
            }

            ReleaseAllFor(slot);
            SetChoice(slot, (Party)partyId);
            EnforceCaps((Party)partyId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ClaimRpc(int entryIndex, RpcParams rpc = default)
        {
            var slot = SlotOf(rpc.Receive.SenderClientId);
            if (slot == PartyInfo.Unclaimed || !InRange(entryIndex))
            {
                return;
            }

            var entry = m_Roster[entryIndex];

            // Three separate questions, and all three have to hold: nobody else has it, it is on
            // this player's side, and they have room.
            if (entry.IsClaimed
                || entry.Party != PartyOf(slot)
                || ClaimCountFor(slot) >= CapFor(slot))
            {
                return;
            }

            m_ClaimSequence.Value++;
            entry.ClaimedBySlot = slot;
            entry.ClaimSequence = m_ClaimSequence.Value;
            m_Roster[entryIndex] = entry;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReleaseRpc(int entryIndex, RpcParams rpc = default)
        {
            var slot = SlotOf(rpc.Receive.SenderClientId);
            if (!InRange(entryIndex) || m_Roster[entryIndex].ClaimedBySlot != slot)
            {
                return;
            }

            Release(entryIndex);
        }

        // ---------------------------------------------------------------- server helpers

        /// <summary>
        /// Host check done server-side against the sender's client id. A client-side button state
        /// is a suggestion; this is the decision.
        /// </summary>
        static bool IsHost(RpcParams rpc) => rpc.Receive.SenderClientId == NetworkManager.ServerClientId;

        bool InRange(int index) => index >= 0 && index < m_Roster.Count;

        byte SlotOf(ulong clientId)
        {
            var roster = PlayerRoster.Current;
            return roster != null && roster.TryGet(clientId, out var entry) && entry.Slot >= 0
                ? (byte)entry.Slot
                : PartyInfo.Unclaimed;
        }

        void SetChoice(byte slot, Party party)
        {
            for (var i = 0; i < m_PartyChoices.Count; i++)
            {
                if (m_PartyChoices[i].Slot == slot)
                {
                    m_PartyChoices[i] = new PartyChoice(slot, party);
                    return;
                }
            }

            m_PartyChoices.Add(new PartyChoice(slot, party));
        }

        void Release(int entryIndex)
        {
            var entry = m_Roster[entryIndex];
            entry.ClaimedBySlot = PartyInfo.Unclaimed;
            entry.ClaimSequence = 0;
            m_Roster[entryIndex] = entry;
        }

        void ReleaseAllFor(byte slot)
        {
            for (var i = 0; i < m_Roster.Count; i++)
            {
                if (m_Roster[i].ClaimedBySlot == slot)
                {
                    Release(i);
                }
            }
        }

        /// <summary>
        /// Recomputes caps for a party and releases anything over. Runs whenever membership changes,
        /// because one player joining shrinks everyone else's share.
        /// </summary>
        void EnforceCaps(Party party)
        {
            var slots = SlotsIn(party);
            var creatures = CreatureCountIn(party);

            for (var ordinal = 0; ordinal < slots.Count; ordinal++)
            {
                var slot = slots[ordinal];
                var cap = ClaimRules.CapFor(creatures, slots.Count, ordinal);

                var claims = new List<(int, uint)>();
                for (var i = 0; i < m_Roster.Count; i++)
                {
                    if (m_Roster[i].ClaimedBySlot == slot)
                    {
                        claims.Add((i, m_Roster[i].ClaimSequence));
                    }
                }

                foreach (var index in ClaimRules.ClaimsToRelease(claims, cap))
                {
                    Release(index);
                }
            }
        }

        /// <summary>
        /// Server only. Fills an empty draft so a match can be played before the draft UI exists.
        /// Creatures are dealt round-robin across the parties that have players, or across Heroes
        /// and Monsters when nobody has chosen.
        /// </summary>
        public void ServerSeedIfEmpty(int perParty)
        {
            if (!IsServer || m_Roster.Count > 0 || m_Catalog == null || m_Catalog.Count == 0)
            {
                return;
            }

            var parties = new List<Party>();
            for (var i = 0; i < m_PartyChoices.Count; i++)
            {
                if (!parties.Contains(m_PartyChoices[i].Party))
                {
                    parties.Add(m_PartyChoices[i].Party);
                }
            }

            if (parties.Count == 0)
            {
                parties.Add(Party.Heroes);
                parties.Add(Party.Monsters);
            }

            var next = 0;
            foreach (var party in parties)
            {
                for (var i = 0; i < perParty; i++)
                {
                    var definition = m_Catalog.Creatures[next % m_Catalog.Count];
                    next++;
                    m_Roster.Add(new RosterEntry(m_Catalog.IdOf(definition), party));
                }
            }

            // Give each player the creatures on their own side, up to their cap.
            foreach (var party in parties)
            {
                foreach (var slot in SlotsIn(party))
                {
                    var cap = CapFor(slot);
                    for (var i = 0; i < m_Roster.Count && ClaimCountFor(slot) < cap; i++)
                    {
                        var entry = m_Roster[i];
                        if (entry.IsClaimed || entry.Party != party)
                        {
                            continue;
                        }

                        m_ClaimSequence.Value++;
                        entry.ClaimedBySlot = slot;
                        entry.ClaimSequence = m_ClaimSequence.Value;
                        m_Roster[i] = entry;
                    }
                }
            }
        }
    }
}

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
    /// rate-limited, string-typed and eventually consistent.
    ///
    /// Every mutation is a server RPC. Client-side buttons decide what to *offer*; this decides what
    /// is allowed.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public sealed class DraftState : NetworkBehaviour
    {
        [SerializeField, Tooltip("Fallback catalog. ArenaContext owns the live one once an arena loads.")]
        CreatureCatalog m_Catalog;

        readonly NetworkList<RosterEntry> m_Roster = new NetworkList<RosterEntry>();
        readonly NetworkList<PartyChoice> m_PartyChoices = new NetworkList<PartyChoice>();

        readonly List<RosterEntry> m_RosterView = new List<RosterEntry>();
        readonly List<PartyChoice> m_ChoiceView = new List<PartyChoice>();

        // Plain fields, not NetworkVariables: both are written and read only on the server, so
        // replicating them would cost a spawn payload slot and a delta channel for nothing.
        uint m_NextEntryId = 1;
        uint m_ClaimSequence;

        public static DraftState Current { get; private set; }

        public CreatureCatalog Catalog => m_Catalog;

        /// <summary>
        /// A readable mirror of the replicated roster, rebuilt whenever it changes.
        ///
        /// NetworkList is not an IReadOnlyList and cannot be constructed outside a live session, so
        /// anything reading it directly was untestable and every consumer needed this type. Mirroring
        /// once per change lets all the questions live in <see cref="DraftQueries"/> as pure
        /// functions. The lists hold tens of entries; the copy is not worth optimising.
        /// </summary>
        public IReadOnlyList<RosterEntry> Roster => m_RosterView;

        public IReadOnlyList<PartyChoice> Choices => m_ChoiceView;

        /// <summary>Raised whenever the roster or party choices change.</summary>
        public event Action Changed;

        public override void OnNetworkSpawn()
        {
            if (Current != null && Current != this)
            {
                Debug.LogError("A second DraftState spawned; the match expects one.", this);
            }
            else
            {
                Current = this;
            }

            m_Roster.OnListChanged += OnRosterChanged;
            m_PartyChoices.OnListChanged += OnChoicesChanged;
            RebuildViews();
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

        void OnRosterChanged(NetworkListEvent<RosterEntry> _) => RebuildViews();

        void OnChoicesChanged(NetworkListEvent<PartyChoice> _) => RebuildViews();

        void RebuildViews()
        {
            m_RosterView.Clear();
            for (var i = 0; i < m_Roster.Count; i++)
            {
                m_RosterView.Add(m_Roster[i]);
            }

            m_ChoiceView.Clear();
            for (var i = 0; i < m_PartyChoices.Count; i++)
            {
                m_ChoiceView.Add(m_PartyChoices[i]);
            }

            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- queries

        /// <summary>A copy of the roster, safe to hold while spawning mutates nothing.</summary>
        public List<RosterEntry> Snapshot() => new List<RosterEntry>(m_RosterView);

        // Every question below is answered by DraftQueries over the mirrored lists. These are thin
        // on purpose: the server and every client must reach identical answers, and shared pure
        // functions are the cheapest way to guarantee that.

        public bool TryGetParty(byte slot, out Party party) =>
            DraftQueries.TryGetParty(m_ChoiceView, slot, out party);

        public bool HasChosenParty(byte slot) => TryGetParty(slot, out _);

        public List<byte> SlotsIn(Party party) => DraftQueries.SlotsIn(m_ChoiceView, party);

        public int CreatureCountIn(Party party) => DraftQueries.CreatureCountIn(m_RosterView, party);

        public int CapFor(byte slot) => DraftQueries.CapFor(m_RosterView, m_ChoiceView, slot);

        public int ClaimCountFor(byte slot) => DraftQueries.ClaimCountFor(m_RosterView, slot);

        /// <summary>Whether a claim would succeed. The UI uses this to decide what to offer.</summary>
        public bool CanClaim(byte slot, uint entryId) =>
            DraftQueries.CanClaim(m_RosterView, m_ChoiceView, slot, entryId);

        // ---------------------------------------------------------------- commands

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void AddCreatureRpc(ushort creatureId, byte partyId, RpcParams rpc = default)
        {
            if (!IsFromHost(rpc) || m_Catalog == null || m_Catalog.Resolve(creatureId) == null)
            {
                return;
            }

            m_Roster.Add(new RosterEntry(m_NextEntryId++, creatureId, (Party)partyId));

            // Adding only ever raises caps, so no claim can become invalid. Removing is the
            // asymmetric case and does need EnforceCaps.
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RemoveCreatureRpc(uint entryId, RpcParams rpc = default)
        {
            if (!IsFromHost(rpc))
            {
                return;
            }

            var index = IndexOf(entryId);
            if (index < 0)
            {
                return;
            }

            var party = m_Roster[index].Party;
            m_Roster.RemoveAt(index);

            // Removing lowers everyone's share of that party, which can put existing claims over
            // cap. Without this they stay over until somebody happens to change teams.
            EnforceCaps(party);
        }

        /// <summary>
        /// Joins a party. Claims held in the old party are released, and everyone already in the new
        /// party has their cap recomputed -- which may push them over and release their newest claims.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ChoosePartyRpc(byte partyId, RpcParams rpc = default)
        {
            var slot = SlotOf(rpc.Receive.SenderClientId);
            if (slot == PartyInfo.Unclaimed)
            {
                return;
            }

            var hadParty = TryGetParty(slot, out var previous);

            ReleaseAllFor(slot);
            SetChoice(slot, (Party)partyId);

            EnforceCaps((Party)partyId);
            if (hadParty && previous != (Party)partyId)
            {
                EnforceCaps(previous);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ClaimRpc(uint entryId, RpcParams rpc = default)
        {
            var slot = SlotOf(rpc.Receive.SenderClientId);
            var index = IndexOf(entryId);

            if (slot == PartyInfo.Unclaimed || index < 0)
            {
                return;
            }

            // The same predicate the client used to enable the button, so the two can never
            // disagree about what is allowed.
            if (!CanClaim(slot, entryId))
            {
                return;
            }

            var entry = m_Roster[index];

            entry.ClaimedBySlot = slot;
            entry.ClaimSequence = ++m_ClaimSequence;
            m_Roster[index] = entry;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void ReleaseRpc(uint entryId, RpcParams rpc = default)
        {
            var slot = SlotOf(rpc.Receive.SenderClientId);
            var index = IndexOf(entryId);

            if (index < 0 || m_Roster[index].ClaimedBySlot != slot)
            {
                return;
            }

            Release(index);
        }

        // ---------------------------------------------------------------- server helpers

        /// <summary>
        /// Host check done server-side against the sender's client id. A client-side button state is
        /// a suggestion; this is the decision.
        /// </summary>
        static bool IsFromHost(RpcParams rpc) =>
            rpc.Receive.SenderClientId == NetworkManager.ServerClientId;

        int IndexOf(uint entryId) => DraftQueries.IndexOf(m_RosterView, entryId);

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

        void Release(int index)
        {
            var entry = m_Roster[index];
            entry.ClaimedBySlot = PartyInfo.Unclaimed;
            entry.ClaimSequence = 0;
            m_Roster[index] = entry;
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
        /// Recomputes caps for a party and releases anything over. Runs whenever the membership or
        /// the creature count changes, because either one changes everyone's share.
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
        /// Server only. Fills an empty draft so a match is playable without anyone using the draft
        /// UI, and -- critically -- puts every player on a side and hands them their share.
        ///
        /// Seeding creatures without also seeding party choices left every creature unclaimed, so
        /// they all spawned server-owned: the host silently owned everything and no client owned
        /// anything.
        /// </summary>
        public void ServerSeedIfEmpty(int perParty, IReadOnlyList<byte> playerSlots)
        {
            if (!IsServer || m_Roster.Count > 0 || m_Catalog == null || m_Catalog.Count == 0)
            {
                return;
            }

            // Spread players who have not chosen across the first two parties, so a solo host still
            // gets an opponent and a pair start on opposite sides.
            if (playerSlots != null)
            {
                for (var i = 0; i < playerSlots.Count; i++)
                {
                    if (!HasChosenParty(playerSlots[i]))
                    {
                        SetChoice(playerSlots[i], i % 2 == 0 ? Party.Heroes : Party.Monsters);
                    }
                }
            }

            var parties = new List<Party>();
            for (var i = 0; i < m_PartyChoices.Count; i++)
            {
                if (!parties.Contains(m_PartyChoices[i].Party))
                {
                    parties.Add(m_PartyChoices[i].Party);
                }
            }

            // Always at least two sides. A solo host all on Heroes would otherwise face nobody.
            if (!parties.Contains(Party.Heroes))
            {
                parties.Insert(0, Party.Heroes);
            }

            if (parties.Count < 2)
            {
                parties.Add(Party.Monsters);
            }

            var next = 0;
            foreach (var party in parties)
            {
                for (var i = 0; i < perParty; i++)
                {
                    var definition = m_Catalog.Creatures[next % m_Catalog.Count];
                    next++;
                    m_Roster.Add(new RosterEntry(m_NextEntryId++, m_Catalog.IdOf(definition), party));
                }
            }

            AutoClaim(parties);
        }

        /// <summary>Gives each player the creatures on their own side, up to their cap.</summary>
        void AutoClaim(List<Party> parties)
        {
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

                        entry.ClaimedBySlot = slot;
                        entry.ClaimSequence = ++m_ClaimSequence;
                        m_Roster[i] = entry;
                    }
                }
            }
        }
    }
}

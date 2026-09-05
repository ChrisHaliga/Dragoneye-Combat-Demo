using System;
using System.Collections.Generic;
using Dragoneye.Combat;
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

        // How many players had been defaulted onto a side, so the check below is a comparison
        // rather than a walk of the roster every frame.
        int m_DefaultedFor = -1;

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

        /// <summary>
        /// Puts every player who has a slot and no side on the default one.
        ///
        /// Players arrive expecting to be on the same team as each other; being scattered across
        /// parties by default is the surprising outcome, and picking a side is a decision the board
        /// offers rather than one it demands before anything else can happen.
        ///
        /// Polled on the server rather than driven off a roster event, because the draft is spawned
        /// and the roster is not, so either can exist first. It walks a handful of entries.
        /// </summary>
        void Update()
        {
            if (!IsServer)
            {
                return;
            }

            var roster = PlayerRoster.Current;

            if (roster == null || roster.Count == m_DefaultedFor)
            {
                return;
            }

            m_DefaultedFor = roster.Count;

            for (var i = 0; i < roster.Count; i++)
            {
                var slot = roster.At(i).Slot;

                if (slot >= 0 && slot < PartyInfo.Unclaimed && !HasChosenParty((byte)slot))
                {
                    SetChoice((byte)slot, PartyInfo.Default);
                }
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

            // Authored level by default: what the designer decided this creature is, which the
            // host may then raise or lower for this match.
            var definition = m_Catalog.Resolve(creatureId);

            m_Roster.Add(new RosterEntry(m_NextEntryId++, creatureId, (Party)partyId,
                definition != null ? definition.Level : Progression.FirstLevel));

            // Adding only ever raises caps, so no claim can become invalid. Removing is the
            // asymmetric case and does need EnforceCaps.
        }

        /// <summary>
        /// The host fielding a creature above or below what it was authored at.
        ///
        /// Host only, and clamped here rather than trusted from the message: the stepper on the
        /// board decides what to offer, and this decides what is allowed.
        ///
        /// Only unclaimed creatures. A character a player brought carries its own level, earned,
        /// and is not the host's to adjust.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetCreatureLevelRpc(uint entryId, int level, RpcParams rpc = default)
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

            var entry = m_Roster[index];
            entry.Level = RosterEntry.Clamp(level);
            m_Roster[index] = entry;
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
        /// The host putting another player on a side.
        ///
        /// Host-only, and the one thing the host controls about a player character: whose it is was
        /// settled when its owner submitted it, and cannot be changed by anybody.
        /// </summary>
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void SetPartyForRpc(byte slot, byte partyId, RpcParams rpc = default)
        {
            if (!IsFromHost(rpc) || slot == PartyInfo.Unclaimed)
            {
                return;
            }

            var hadParty = TryGetParty(slot, out var previous);
            var party = (Party)partyId;

            ReleaseAllFor(slot);
            SetChoice(slot, party);

            EnforceCaps(party);

            if (hadParty && previous != party)
            {
                EnforceCaps(previous);
            }
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

        /// <summary>
        /// Server only. Undoes a departed player's part in the draft: what they claimed goes back
        /// into the pool, and the side they picked is forgotten.
        ///
        /// Safe to do the moment they go, match or no match. A claim only decides who commands a
        /// creature when one is spawned, and the creatures in a running fight were spawned long
        /// before this.
        /// </summary>
        public void ServerReleaseSlot(byte slot)
        {
            if (!IsServer || slot == PartyInfo.Unclaimed)
            {
                return;
            }

            for (var i = 0; i < m_Roster.Count; i++)
            {
                if (m_Roster[i].ClaimedBySlot == slot)
                {
                    Release(i);
                }
            }

            for (var i = m_PartyChoices.Count - 1; i >= 0; i--)
            {
                if (m_PartyChoices[i].Slot == slot)
                {
                    m_PartyChoices.RemoveAt(i);
                }
            }
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
        /// Server only. Brings the draft to a state a match can actually be played from, whatever
        /// the players did or did not do in the lobby.
        ///
        /// Three steps, and they are separate because they have different conditions. Seeding only
        /// makes sense for an untouched draft; assigning a side and handing out shares have to
        /// happen either way. They used to all sit behind the "is the roster empty" guard, so a host
        /// who added a single creature by hand got no party assigned and nothing claimed -- every
        /// creature stayed unclaimed, which means server-owned, which means nobody could move
        /// anything. It looked like the opposite bug on a solo host, where owning the server also
        /// meant owning every unclaimed creature on the board.
        /// </summary>
        public void ServerPrepareForMatch(int perParty, IReadOnlyList<byte> playerSlots)
        {
            if (!IsServer)
            {
                return;
            }

            AssignMissingParties(playerSlots);
            SeedIfEmpty(perParty);
            ClaimUpToCaps();
        }

        /// <summary>
        /// Puts anyone who somehow still has no side on the heroes.
        ///
        /// A backstop rather than the normal path: everyone is defaulted to the heroes the moment
        /// they get a slot, so by the time a match starts this should find nobody. It used to
        /// alternate sides, which meant the second player to join was silently made the opposition.
        /// </summary>
        void AssignMissingParties(IReadOnlyList<byte> playerSlots)
        {
            if (playerSlots == null)
            {
                return;
            }

            for (var i = 0; i < playerSlots.Count; i++)
            {
                if (!HasChosenParty(playerSlots[i]))
                {
                    SetChoice(playerSlots[i], PartyInfo.Default);
                }
            }
        }

        /// <summary>
        /// Deals a starting roster, but only into a draft nobody has touched. A hand-built roster is
        /// the host's decision and must not be added to behind their back.
        /// </summary>
        void SeedIfEmpty(int perParty)
        {
            if (m_Roster.Count > 0 || m_Catalog == null || m_Catalog.Count == 0 || perParty <= 0)
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
        }

        /// <summary>
        /// Hands every player the unclaimed creatures on their own side, up to their cap.
        ///
        /// Runs at match start regardless of what the lobby did, so forgetting to press Claim costs
        /// a player nothing. Caps still apply, so whatever is left over stays unclaimed and is run
        /// by the computer -- including every creature in a party nobody joined.
        /// </summary>
        void ClaimUpToCaps()
        {
            foreach (var party in PartyInfo.All)
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

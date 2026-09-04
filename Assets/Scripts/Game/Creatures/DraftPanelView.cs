using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The draft board: the host fills each party with creatures, and every player picks a side and
    /// claims their share of it.
    ///
    /// A separate document from the session menu rather than an addition to it. The menu lives in
    /// the multiplayer assembly and has no business knowing what a creature is; putting the draft
    /// here keeps that boundary and means the lobby UI never has to change when creature rules do.
    ///
    /// One column per party and one card per combatant, so the state of the match is the layout. The
    /// previous arrangement showed a single party at a time behind a "Viewing" toggle and kept the
    /// characters players brought in a second list underneath, which meant working out who was on
    /// which side involved clicking through four views and reading two lists. Brought characters are
    /// now cards in their party like anything else -- what makes them different is what the card
    /// offers, not which list it lives in.
    ///
    /// Every button here only *offers* an action. <see cref="DraftState"/> re-checks all of it
    /// server-side, so a disabled button is a courtesy and never a security measure.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class DraftPanelView : MonoBehaviour
    {
        /// <summary>The elements of one party column, so a refresh can find them without a query.</summary>
        sealed class Column
        {
            public VisualElement Root;
            public VisualElement List;
            public Label Count;
            public VisualElement AddRow;
            public DropdownField Creatures;
        }

        VisualElement m_Root;
        VisualElement m_Panel;
        VisualElement m_TeamButtons;
        VisualElement m_PartyColumns;
        Label m_CapLabel;

        readonly Dictionary<Party, Column> m_Columns = new Dictionary<Party, Column>();

        DraftState m_Draft;
        PlayerRoster m_Roster;
        PlayerCharacters m_Characters;

        void Start()
        {
            m_Root = GetComponent<UIDocument>().rootVisualElement;

            // Both this document and the session menu share one panel, and this one sorts above it.
            // A full-screen element with the default picking mode therefore eats every click meant
            // for the menu underneath -- which is exactly what happened to the Host button. The
            // document root AND the template root both have to opt out; only the panel opts back in.
            CreatureDisplay.MakeClickThrough(m_Root);

            m_Panel = m_Root.Q<VisualElement>("draft-panel");
            m_TeamButtons = m_Root.Q<VisualElement>("team-buttons");
            m_PartyColumns = m_Root.Q<VisualElement>("party-columns");
            m_CapLabel = m_Root.Q<Label>("cap-label");

            if (m_Panel == null || m_TeamButtons == null || m_PartyColumns == null
                || m_CapLabel == null)
            {
                // A missing element used to throw here, which left the root at its default picking
                // mode and blocked the menu below with no visible cause.
                Debug.LogError("DraftPanelView could not find its elements; check DraftPanel.uxml.", this);
                enabled = false;
                return;
            }

            BuildPartyButtons();
            BuildPartyColumns();
            Refresh();
        }

        void Update()
        {
            // The draft is a spawned network object, so it appears some frames after this component
            // does. Polling for it once beats an ordering assumption that would silently leave the
            // panel dead.
            if (m_Draft != DraftState.Current || m_Roster != PlayerRoster.Current
                || m_Characters != PlayerCharacters.Current)
            {
                Rebind();
            }
        }

        void OnDestroy() => Unbind();

        void Rebind()
        {
            Unbind();

            m_Draft = DraftState.Current;
            m_Roster = PlayerRoster.Current;
            m_Characters = PlayerCharacters.Current;

            if (m_Characters != null)
            {
                m_Characters.Changed += Refresh;
            }

            if (m_Draft != null)
            {
                m_Draft.Changed += Refresh;
            }

            if (m_Roster != null)
            {
                m_Roster.Changed += Refresh;
            }

            PopulateCreatureDropdowns();
            Refresh();
        }

        void Unbind()
        {
            if (m_Draft != null)
            {
                m_Draft.Changed -= Refresh;
            }

            if (m_Roster != null)
            {
                m_Roster.Changed -= Refresh;
            }

            if (m_Characters != null)
            {
                m_Characters.Changed -= Refresh;
            }
        }

        // ---------- building ----------

        void BuildPartyButtons()
        {
            foreach (var party in PartyInfo.All)
            {
                var chosen = party;

                var join = new Button(() => m_Draft?.ChoosePartyRpc((byte)chosen))
                {
                    text = PartyPalette.NameOf(chosen)
                };
                join.AddToClassList("button");
                m_TeamButtons.Add(join);
            }
        }

        /// <summary>
        /// One column per party, built once.
        ///
        /// The add control lives in the column rather than in a shared host panel, so the party a
        /// creature joins is the column the host clicked in. That removes the "Viewing" mode the
        /// old panel needed only to answer "add to which side".
        /// </summary>
        void BuildPartyColumns()
        {
            foreach (var party in PartyInfo.All)
            {
                var chosen = party;

                var root = new VisualElement();
                root.AddToClassList("party");

                var head = new VisualElement();
                head.AddToClassList("party__head");

                var flag = new VisualElement();
                flag.AddToClassList("party__flag");
                flag.style.backgroundColor = PartyPalette.ForParty(party);
                head.Add(flag);

                var name = new Label(PartyPalette.NameOf(party).ToUpperInvariant());
                name.AddToClassList("party__name");
                head.Add(name);

                var count = new Label();
                count.AddToClassList("party__count");
                head.Add(count);

                root.Add(head);

                var list = new VisualElement();
                list.AddToClassList("party__list");
                root.Add(list);

                var addRow = new VisualElement();
                addRow.AddToClassList("party__add");

                var creatures = new DropdownField();
                creatures.AddToClassList("dropdown");
                addRow.Add(creatures);

                var add = new Button(() => OnAddClicked(chosen)) { text = "Add" };
                add.AddToClassList("button");
                add.AddToClassList("button--compact");
                addRow.Add(add);

                root.Add(addRow);
                m_PartyColumns.Add(root);

                m_Columns[party] = new Column
                {
                    Root = root,
                    List = list,
                    Count = count,
                    AddRow = addRow,
                    Creatures = creatures
                };
            }
        }

        void PopulateCreatureDropdowns()
        {
            var catalog = m_Draft != null ? m_Draft.Catalog : null;
            var names = new List<string>();

            if (catalog != null)
            {
                foreach (var creature in catalog.Creatures)
                {
                    names.Add(creature != null ? creature.DisplayName : "(missing)");
                }
            }

            foreach (var column in m_Columns.Values)
            {
                column.Creatures.choices = names;
                column.Creatures.index = names.Count > 0 ? 0 : -1;
            }
        }

        void OnAddClicked(Party party)
        {
            var catalog = m_Draft != null ? m_Draft.Catalog : null;

            if (catalog == null || !m_Columns.TryGetValue(party, out var column))
            {
                return;
            }

            var index = column.Creatures.index;

            if (index < 0 || index >= catalog.Count)
            {
                return;
            }

            m_Draft.AddCreatureRpc(catalog.IdOf(catalog.Creatures[index]), (byte)party);
        }

        // ---------- refresh ----------

        void Refresh()
        {
            if (m_Root == null)
            {
                return;
            }

            var ready = m_Draft != null;
            m_Panel.EnableInClassList("is-hidden", !ready);
            m_Panel.pickingMode = ready ? PickingMode.Position : PickingMode.Ignore;

            if (!ready)
            {
                return;
            }

            var isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            var hasSlot = TryGetLocalSlot(out var slot);
            var myParty = default(Party);
            var hasParty = hasSlot && m_Draft.TryGetParty(slot, out myParty);

            for (var i = 0; i < PartyInfo.All.Length; i++)
            {
                m_TeamButtons[i].EnableInClassList("button--chosen",
                    hasParty && PartyInfo.All[i] == myParty);
            }

            m_CapLabel.text = hasParty
                ? $"{PartyPalette.NameOf(myParty).ToUpperInvariant()}  ·  "
                    + $"{m_Draft.ClaimCountFor(slot)} OF {m_Draft.CapFor(slot)} CLAIMED"
                : "PICK A SIDE TO CLAIM CREATURES";

            foreach (var party in PartyInfo.All)
            {
                RefreshColumn(party, hasSlot, slot, isHost, hasParty && party == myParty);
            }
        }

        void RefreshColumn(Party party, bool hasSlot, byte slot, bool isHost, bool mine)
        {
            var column = m_Columns[party];

            column.Root.EnableInClassList("party--mine", mine);
            column.AddRow.EnableInClassList("is-hidden", !isHost);
            column.List.Clear();

            var members = 0;

            // Brought characters first: they were spoken for before the draft began, and putting
            // them at the top means a column reads owner-first rather than pool-first.
            var characters = m_Characters;

            if (characters != null)
            {
                foreach (var build in characters.All)
                {
                    if (!m_Draft.TryGetParty(build.Slot, out var on) || on != party)
                    {
                        continue;
                    }

                    column.List.Add(BroughtCard(characters, build,
                        hasSlot && build.Slot == slot, isHost));
                    members++;
                }
            }

            var catalog = m_Draft.Catalog;

            foreach (var entry in m_Draft.Roster)
            {
                if (entry.Party != party)
                {
                    continue;
                }

                column.List.Add(RosterCard(entry, catalog, hasSlot, slot, isHost));
                members++;
            }

            column.Count.text = members.ToString();

            if (members == 0)
            {
                var empty = new Label(isHost
                    ? "Nobody yet. Add a creature below."
                    : "Nobody yet.");
                empty.AddToClassList("party__empty");
                column.List.Add(empty);
            }
        }

        /// <summary>
        /// A character somebody brought.
        ///
        /// No claim and no remove: it is permanently its owner's, and the only decision left is the
        /// side it fights on, which is the host's. That is the whole difference between this card
        /// and a pool one.
        /// </summary>
        VisualElement BroughtCard(PlayerCharacters characters, NetBuild build, bool mine,
            bool isHost)
        {
            var card = new VisualElement();
            card.AddToClassList("fighter");
            card.AddToClassList("fighter--brought");
            card.EnableInClassList("fighter--mine", mine);

            var body = new VisualElement();
            body.AddToClassList("fighter__body");

            var name = new Label(build.Name.ToString());
            name.AddToClassList("fighter__name");
            body.Add(name);

            var loadout = characters.LoadoutFor(build.Slot);
            var owner = mine ? "You" : OwnerName(build.Slot);

            var detail = new Label(loadout != null
                ? $"{owner} · {loadout.Vitals.MaxHealth} HP · {loadout.Vitals.MaxAp} AP"
                : owner);
            detail.AddToClassList("fighter__owner");
            detail.EnableInClassList("fighter__owner--mine", mine);
            body.Add(detail);

            card.Add(body);

            // Only the host reassigns sides, and only sides. Whose character it is was settled when
            // its owner submitted it. Colour chips rather than four named buttons: the column
            // already says which party this is, so all that is needed is somewhere else to send it.
            if (isHost && m_Draft != null)
            {
                card.Add(PartyChips(build.Slot));
            }

            return card;
        }

        /// <summary>A chip per party, the current one lit. Moves a brought character to another side.</summary>
        VisualElement PartyChips(byte slot)
        {
            var chips = new VisualElement();
            chips.AddToClassList("fighter__actions");

            var onParty = m_Draft.TryGetParty(slot, out var current);

            foreach (var party in PartyInfo.All)
            {
                var chosen = party;

                var chip = new Button(() => m_Draft.SetPartyForRpc(slot, (byte)chosen));
                chip.AddToClassList("party-chip");
                chip.EnableInClassList("party-chip--current", onParty && current == party);
                chip.style.backgroundColor = PartyPalette.ForParty(party);
                chip.tooltip = PartyPalette.NameOf(party);
                chips.Add(chip);
            }

            return chips;
        }

        VisualElement RosterCard(RosterEntry entry, CreatureCatalog catalog, bool hasSlot,
            byte slot, bool isHost)
        {
            var definition = catalog != null ? catalog.Resolve(entry.CreatureId) : null;
            var mine = hasSlot && entry.ClaimedBySlot == slot;

            var card = new VisualElement();
            card.AddToClassList("fighter");
            card.EnableInClassList("fighter--claimed", entry.IsClaimed);
            card.EnableInClassList("fighter--mine", mine);

            var body = new VisualElement();
            body.AddToClassList("fighter__body");

            var name = new Label(definition != null ? definition.DisplayName : "Unknown");
            name.AddToClassList("fighter__name");
            body.Add(name);

            var owner = new Label(OwnerText(entry, mine));
            owner.AddToClassList("fighter__owner");
            owner.EnableInClassList("fighter__owner--mine", mine);
            body.Add(owner);

            card.Add(body);

            var actions = new VisualElement();
            actions.AddToClassList("fighter__actions");

            var entryId = entry.EntryId;

            if (mine)
            {
                actions.Add(SmallButton("Release", () => m_Draft.ReleaseRpc(entryId), true));
            }
            else
            {
                // The same predicate the server uses to decide, so the button is never enabled for
                // something that will be refused.
                var canClaim = hasSlot && m_Draft.CanClaim(slot, entryId);
                actions.Add(SmallButton("Claim", () => m_Draft.ClaimRpc(entryId), canClaim));
            }

            if (isHost)
            {
                actions.Add(SmallButton("×", () => m_Draft.RemoveCreatureRpc(entryId), true));
            }

            card.Add(actions);
            return card;
        }

        // ---------- naming ----------

        bool TryGetLocalSlot(out byte slot)
        {
            var manager = NetworkManager.Singleton;

            if (m_Roster != null && manager != null
                && m_Roster.TryGet(manager.LocalClientId, out var entry) && entry.Slot >= 0)
            {
                slot = (byte)entry.Slot;
                return true;
            }

            slot = PartyInfo.Unclaimed;
            return false;
        }

        string OwnerName(byte slot)
        {
            if (m_Roster != null && m_Roster.TryGetBySlot(slot, out var entry))
            {
                var name = entry.Name.ToString();
                return string.IsNullOrEmpty(name) ? $"Player {slot + 1}" : name;
            }

            return $"Player {slot + 1}";
        }

        string OwnerText(RosterEntry entry, bool mine)
        {
            if (!entry.IsClaimed)
            {
                return "Computer";
            }

            return mine ? "Yours" : OwnerName(entry.ClaimedBySlot);
        }

        static Button SmallButton(string text, System.Action action, bool enabled)
        {
            var button = new Button(action) { text = text };
            button.AddToClassList("button");
            button.AddToClassList("button--compact");
            button.SetEnabled(enabled);
            return button;
        }
    }
}

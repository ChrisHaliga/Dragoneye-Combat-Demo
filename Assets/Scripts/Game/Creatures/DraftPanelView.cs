using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Multiplayer;
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
    /// This is the whole pre-match screen. It fills the window and sorts above the session menu,
    /// so anything the menu still drew underneath -- the join code, the ready toggle, Start --
    /// could be neither seen nor clicked. Those controls now run along the bottom of this board,
    /// bound by <see cref="MatchSetupBar"/> so that what a session is stays in the multiplayer
    /// assembly and only the document is shared.
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
        MatchSetupBar m_Setup;
        bool m_Live;

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

            m_Setup = new MatchSetupBar(m_Root);

            if (m_Panel == null || m_TeamButtons == null || m_PartyColumns == null
                || m_CapLabel == null || !m_Setup.IsBound)
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

            // Netcode starting or stopping is what shows and hides this screen, and neither raises
            // anything worth subscribing to from here. Compared against the same question Refresh
            // answers, or the board would rebuild every frame it was standing aside -- which is how
            // a click gets destroyed between the press and the release.
            if (ShouldShow() != m_Live)
            {
                Refresh();
            }
        }

        /// <summary>Whether there is a match to prepare for at all.</summary>
        static bool IsLive()
        {
            var manager = NetworkManager.Singleton;
            return manager != null && (manager.IsListening || manager.IsConnectedClient);
        }

        /// <summary>
        /// Whether the board should be on screen.
        ///
        /// The level-up screen is drawn on the document underneath this one, so the board stands
        /// aside while it is open rather than covering the panel the player was just sent to. Asked
        /// of the screen rather than of the character: a player who put a level-up off and then
        /// opened it deliberately is looking at it either way.
        /// </summary>
        static bool ShouldShow() => IsLive() && !LevelUpScreen.IsShowing;

        void OnDestroy()
        {
            Unbind();
            m_Setup?.Dispose();
        }

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

            // The screen is up whenever netcode is, not only once the draft object has replicated.
            // A client that has connected but has not yet received the draft still needs the join
            // code, the player list and a way out, and a host still needs to see that it worked.
            var live = ShouldShow();
            m_Live = live;

            m_Panel.EnableInClassList("is-hidden", !live);
            m_Panel.pickingMode = live ? PickingMode.Position : PickingMode.Ignore;

            if (!live)
            {
                return;
            }

            m_Setup.Refresh();

            if (m_Draft == null)
            {
                ShowWaiting();
                return;
            }

            var isHost = NetworkManager.Singleton.IsServer;
            var hasSlot = TryGetLocalSlot(out var slot);
            var myParty = default(Party);
            var hasParty = hasSlot && m_Draft.TryGetParty(slot, out myParty);

            m_TeamButtons.SetEnabled(true);

            for (var i = 0; i < PartyInfo.All.Length; i++)
            {
                m_TeamButtons[i].EnableInClassList("button--chosen",
                    hasParty && PartyInfo.All[i] == myParty);
            }

            // A character somebody brought is claimed by definition: they cannot claim it twice and
            // cannot give it up. Counting it on both sides of the tally is what stops a player who
            // brought a hero and took nothing from the pool reading "0 OF 0 CLAIMED".
            var brought = BroughtCountFor(slot);

            m_CapLabel.text = hasParty
                ? $"{PartyPalette.NameOf(myParty).ToUpperInvariant()}  ·  "
                    + $"{m_Draft.ClaimCountFor(slot) + brought} OF "
                    + $"{m_Draft.CapFor(slot) + brought} CLAIMED"
                : "PICK A SIDE TO CLAIM CREATURES";

            foreach (var party in PartyInfo.All)
            {
                RefreshColumn(party, hasSlot, slot, isHost, hasParty && party == myParty);
            }
        }

        /// <summary>Connected, but the draft has not arrived yet. The frame, and nothing to do in it.</summary>
        void ShowWaiting()
        {
            m_CapLabel.text = "WAITING FOR THE HOST";
            m_TeamButtons.SetEnabled(false);

            foreach (var column in m_Columns.Values)
            {
                column.Root.EnableInClassList("party--mine", false);
                column.AddRow.EnableInClassList("is-hidden", true);
                column.Count.text = "0";
                column.List.Clear();
            }
        }

        /// <summary>How many characters this player brought. Always theirs, always in play.</summary>
        int BroughtCountFor(byte slot)
        {
            if (m_Characters == null || slot == PartyInfo.Unclaimed)
            {
                return 0;
            }

            var count = 0;

            foreach (var build in m_Characters.All)
            {
                if (build.Slot == slot)
                {
                    count++;
                }
            }

            return count;
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
                        hasSlot && build.Slot == slot));
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
        /// side it fights on, which its owner makes for themselves in "Your side". That is the whole
        /// difference between this card and a pool one -- the owner's name sits where a pool card
        /// keeps its buttons, because a card never needs both.
        /// </summary>
        VisualElement BroughtCard(PlayerCharacters characters, NetBuild build, bool mine)
        {
            var loadout = characters.LoadoutFor(build.Slot);

            var card = Card(build.Name.ToString(),
                loadout != null
                    ? CharacterSheet.Describe(loadout.Vitals.Level,
                        loadout.Species != null ? loadout.Species.Name : "Unknown",
                        loadout.Class != null ? loadout.Class.Name : "Unknown", compact: true)
                    : string.Empty,
                loadout?.Vitals,
                out var actions);

            card.AddToClassList("fighter--brought");
            card.EnableInClassList("fighter--mine", mine);

            actions.Add(Owner(mine ? "You" : OwnerName(build.Slot), mine));
            return card;
        }

        VisualElement RosterCard(RosterEntry entry, CreatureCatalog catalog, bool hasSlot,
            byte slot, bool isHost)
        {
            var definition = catalog != null ? catalog.Resolve(entry.CreatureId) : null;
            var mine = hasSlot && entry.ClaimedBySlot == slot;
            var profile = CreatureProfile.FromDefinition(definition, entry.Level);

            var card = Card(definition != null ? definition.DisplayName : "Unknown",
                definition != null
                    ? CharacterSheet.Describe(entry.Level, definition.SpeciesName,
                        definition.ClassName, compact: true)
                    : string.Empty,
                new Vitals(entry.Level, profile.MaxHealth, profile.MaxAp, profile.Initiative),
                out var actions);

            card.EnableInClassList("fighter--claimed", entry.IsClaimed);
            card.EnableInClassList("fighter--mine", mine);

            var entryId = entry.EntryId;

            // Whoever has it, or nothing. A creature nobody has claimed is run by the computer, and
            // saying so on every unclaimed card in four columns is four columns of the same word.
            if (entry.IsClaimed && !mine)
            {
                actions.Add(Owner(OwnerName(entry.ClaimedBySlot), false));
            }

            // The host decides how much of a creature this is. The same goblin is a nuisance in one
            // match and a problem in the next, and the alternative -- authoring a second goblin --
            // is a second asset to keep in step with the first.
            if (isHost)
            {
                actions.Add(LevelStep("−", entryId, entry.Level - 1,
                    entry.Level > Progression.FirstLevel));
                actions.Add(LevelStep("+", entryId, entry.Level + 1,
                    entry.Level < Progression.MaxLevel));
            }

            if (mine)
            {
                actions.Add(SmallButton("Release", () => m_Draft.ReleaseRpc(entryId), true));
            }
            else if (!entry.IsClaimed)
            {
                // The same predicate the server uses to decide, so the button is never enabled for
                // something that will be refused. Somebody else's creature gets their name instead:
                // a Claim button that can never be pressed says less than the reason it cannot.
                actions.Add(SmallButton("Claim", () => m_Draft.ClaimRpc(entryId),
                    hasSlot && m_Draft.CanClaim(slot, entryId)));
            }

            if (isHost)
            {
                actions.Add(SmallButton("×", () => m_Draft.RemoveCreatureRpc(entryId), true));
            }

            return card;
        }

        /// <summary>
        /// The shape every combatant card shares: who it is and what it is on the left, what it can
        /// do and who is running it on the right.
        ///
        /// Two rows against two rows. The stats line up with the name and the controls line up with
        /// the description, so four columns of these read as a table rather than as a pile of
        /// differently-shaped cards -- and a premade and a brought character differ only in what
        /// goes in the bottom-right corner.
        /// </summary>
        static VisualElement Card(string name, string meta, Vitals? vitals, out VisualElement actions)
        {
            var card = new VisualElement();
            card.AddToClassList("fighter");

            var body = new VisualElement();
            body.AddToClassList("fighter__body");

            var title = new Label(name);
            title.AddToClassList("fighter__name");
            body.Add(title);

            var subtitle = new Label(meta);
            subtitle.AddToClassList("fighter__meta");
            body.Add(subtitle);

            card.Add(body);

            var side = new VisualElement();
            side.AddToClassList("fighter__side");

            var stats = new VisualElement();
            stats.AddToClassList("fighter__stats");

            if (vitals.HasValue)
            {
                stats.Add(MiniStat("HP", vitals.Value.MaxHealth.ToString()));
                stats.Add(MiniStat("AP", vitals.Value.MaxAp.ToString()));
                stats.Add(MiniStat("SPD", vitals.Value.Speed.ToString()));
            }

            side.Add(stats);

            actions = new VisualElement();
            actions.AddToClassList("fighter__actions");
            side.Add(actions);

            card.Add(side);
            return card;
        }

        /// <summary>One stat, small enough that three of them fit beside a name.</summary>
        static VisualElement MiniStat(string label, string value)
        {
            var stat = new VisualElement();
            stat.AddToClassList("ministat");

            var caption = new Label(label);
            caption.AddToClassList("ministat__label");
            stat.Add(caption);

            var number = new Label(value);
            number.AddToClassList("ministat__value");
            stat.Add(number);

            return stat;
        }

        /// <summary>Who is running this one, in the corner the buttons would otherwise be in.</summary>
        static Label Owner(string name, bool mine)
        {
            var label = new Label(name);
            label.AddToClassList("fighter__owner");
            label.EnableInClassList("fighter__owner--mine", mine);
            return label;
        }

        /// <summary>
        /// One nudge to a creature's level.
        ///
        /// Offered rather than applied: <see cref="DraftState"/> clamps whatever arrives, so a
        /// disabled button here is a courtesy and never the thing keeping a level in range.
        /// </summary>
        Button LevelStep(string text, uint entryId, int level, bool enabled)
        {
            var button = new Button(() => m_Draft.SetCreatureLevelRpc(entryId, level))
            {
                text = text,
                tooltip = $"Field this creature at level {level}"
            };

            button.AddToClassList("level-step");
            button.SetEnabled(enabled);
            return button;
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

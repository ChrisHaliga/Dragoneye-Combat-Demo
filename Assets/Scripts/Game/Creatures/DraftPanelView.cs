using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The lobby draft: the host fills each party with creatures, and every player picks a side and
    /// claims their share of it.
    ///
    /// A separate document from the session menu rather than an addition to it. The menu lives in
    /// the multiplayer assembly and has no business knowing what a creature is; putting the draft
    /// here keeps that boundary and means the lobby UI never has to change when creature rules do.
    ///
    /// Every button here only *offers* an action. <see cref="DraftState"/> re-checks all of it
    /// server-side, so a disabled button is a courtesy and never a security measure.
    ///
    /// "Your team" and "Viewing" are separate on purpose: the host has to manage four parties while
    /// belonging to one of them.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class DraftPanelView : MonoBehaviour
    {
        VisualElement m_Root;
        VisualElement m_Panel;
        VisualElement m_TeamButtons;
        VisualElement m_ViewButtons;
        VisualElement m_HostTools;
        DropdownField m_CreatureDropdown;
        Button m_AddButton;
        Label m_CapLabel;
        ScrollView m_RosterList;
        ScrollView m_CharacterRoster;

        Party m_Viewing = Party.Heroes;
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
            m_ViewButtons = m_Root.Q<VisualElement>("view-buttons");
            m_HostTools = m_Root.Q<VisualElement>("host-tools");
            m_CreatureDropdown = m_Root.Q<DropdownField>("creature-dropdown");
            m_AddButton = m_Root.Q<Button>("add-button");
            m_CapLabel = m_Root.Q<Label>("cap-label");
            m_RosterList = m_Root.Q<ScrollView>("roster-list");
            m_CharacterRoster = m_Root.Q<ScrollView>("character-roster");

            if (m_Panel == null || m_TeamButtons == null || m_ViewButtons == null
                || m_HostTools == null || m_CreatureDropdown == null || m_AddButton == null
                || m_CapLabel == null || m_RosterList == null)
            {
                // A missing element used to throw here, which left the root at its default picking
                // mode and blocked the menu below with no visible cause.
                Debug.LogError("DraftPanelView could not find its elements; check DraftPanel.uxml.", this);
                enabled = false;
                return;
            }

            m_AddButton.clicked += OnAddClicked;

            BuildPartyButtons();
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

            PopulateCreatureDropdown();
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

                var view = new Button(() =>
                {
                    m_Viewing = chosen;
                    Refresh();
                })
                {
                    text = PartyPalette.NameOf(chosen)
                };
                view.AddToClassList("button");
                m_ViewButtons.Add(view);
            }
        }

        void PopulateCreatureDropdown()
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

            m_CreatureDropdown.choices = names;
            m_CreatureDropdown.index = names.Count > 0 ? 0 : -1;
        }

        void OnAddClicked()
        {
            var catalog = m_Draft != null ? m_Draft.Catalog : null;
            if (catalog == null || m_CreatureDropdown.index < 0
                || m_CreatureDropdown.index >= catalog.Count)
            {
                return;
            }

            var definition = catalog.Creatures[m_CreatureDropdown.index];
            m_Draft.AddCreatureRpc(catalog.IdOf(definition), (byte)m_Viewing);
        }

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
            m_HostTools.EnableInClassList("is-hidden", !isHost);

            var hasSlot = TryGetLocalSlot(out var slot);
            var myParty = default(Party);
            var hasParty = hasSlot && m_Draft.TryGetParty(slot, out myParty);

            for (var i = 0; i < PartyInfo.All.Length; i++)
            {
                var party = PartyInfo.All[i];
                m_TeamButtons[i].EnableInClassList("button--chosen", hasParty && party == myParty);
                m_ViewButtons[i].EnableInClassList("button--viewing", party == m_Viewing);
            }

            m_CapLabel.text = hasParty
                ? $"{PartyPalette.NameOf(myParty)}: claimed {m_Draft.ClaimCountFor(slot)} of {m_Draft.CapFor(slot)}"
                : "Pick a team to claim creatures.";

            RebuildRoster(hasSlot, slot, isHost);
            RebuildCharacters(isHost);
        }

        /// <summary>
        /// The characters players brought, and -- for the host -- which side each fights on.
        ///
        /// Listed apart from the draft pool because they behave differently: a brought character is
        /// permanently its owner's and has no Claim button, so mixing the two lists would mean
        /// explaining on every row why half of them cannot be taken.
        /// </summary>
        void RebuildCharacters(bool isHost)
        {
            if (m_CharacterRoster == null)
            {
                return;
            }

            m_CharacterRoster.Clear();

            var characters = PlayerCharacters.Current;

            if (characters == null || characters.All.Count == 0)
            {
                var none = new Label("Nobody has brought a character yet.");
                none.AddToClassList("brought-row__none");
                m_CharacterRoster.Add(none);
                return;
            }

            foreach (var build in characters.All)
            {
                m_CharacterRoster.Add(BuildBroughtRow(characters, build, isHost));
            }
        }

        VisualElement BuildBroughtRow(PlayerCharacters characters, NetBuild build, bool isHost)
        {
            var row = new VisualElement();
            row.AddToClassList("brought-row");

            var body = new VisualElement();
            body.AddToClassList("brought-row__body");

            var name = new Label(build.Name.ToString());
            name.AddToClassList("brought-row__name");
            body.Add(name);

            var loadout = characters.LoadoutFor(build.Slot);
            var owner = OwnerName(build.Slot);
            var className = loadout != null && loadout.Class != null ? loadout.Class.Name : "No class";

            var detail = new Label(loadout != null
                ? $"{owner} · {className} · {loadout.Vitals.MaxHealth} HP · {loadout.Vitals.MaxAp} AP"
                : owner);
            detail.AddToClassList("brought-row__detail");
            body.Add(detail);

            row.Add(body);

            // Only the host reassigns sides, and only sides. Whose character it is was settled when
            // its owner submitted it.
            if (isHost && m_Draft != null)
            {
                var buttons = new VisualElement();
                buttons.AddToClassList("brought-row__party");

                var onParty = m_Draft.TryGetParty(build.Slot, out var current);

                foreach (var party in PartyInfo.All)
                {
                    var captured = party;
                    var button = SmallButton(PartyPalette.NameOf(party),
                        () => m_Draft.SetPartyForRpc(build.Slot, (byte)captured), true);

                    button.EnableInClassList("button--chosen", onParty && current == party);
                    buttons.Add(button);
                }

                row.Add(buttons);
            }

            return row;
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

        void RebuildRoster(bool hasSlot, byte slot, bool isHost)
        {
            m_RosterList.Clear();

            var catalog = m_Draft.Catalog;

            foreach (var entry in m_Draft.Roster)
            {
                if (entry.Party != m_Viewing)
                {
                    continue;
                }

                var definition = catalog != null ? catalog.Resolve(entry.CreatureId) : null;

                var row = new VisualElement();
                row.AddToClassList("roster-row");

                var body = new VisualElement();
                body.AddToClassList("roster-row__body");

                var name = new Label(definition != null ? definition.DisplayName : "Unknown");
                name.AddToClassList("roster-row__name");

                var mine = hasSlot && entry.ClaimedBySlot == slot;
                var owner = new Label(OwnerText(entry, mine));
                owner.AddToClassList("roster-row__owner");
                if (mine)
                {
                    owner.AddToClassList("roster-row__owner--mine");
                }

                body.Add(name);
                body.Add(owner);
                row.Add(body);

                var entryId = entry.EntryId;

                if (mine)
                {
                    row.Add(SmallButton("Release", () => m_Draft.ReleaseRpc(entryId), true));
                }
                else
                {
                    // The same predicate the server uses to decide, so the button is never enabled
                    // for something that will be refused.
                    var canClaim = hasSlot && m_Draft.CanClaim(slot, entryId);
                    row.Add(SmallButton("Claim", () => m_Draft.ClaimRpc(entryId), canClaim));
                }

                if (isHost)
                {
                    row.Add(SmallButton("Remove", () => m_Draft.RemoveCreatureRpc(entryId), true));
                }

                m_RosterList.Add(row);
            }
        }

        string OwnerText(RosterEntry entry, bool mine)
        {
            if (!entry.IsClaimed)
            {
                return "Unclaimed - computer";
            }

            if (mine)
            {
                return "Yours";
            }

            if (m_Roster != null && m_Roster.TryGetBySlot(entry.ClaimedBySlot, out var player))
            {
                var name = player.Name.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            return $"Player {entry.ClaimedBySlot + 1}";
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

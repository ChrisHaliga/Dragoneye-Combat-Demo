using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The arena HUD: the local player's party down the left, a summary card on the right.
    ///
    /// One component drives both because they are two views of one selection, and splitting them
    /// would mean duplicating the subscription and the redraw. Neither writes anything back --
    /// clicking a portrait sets <see cref="CreatureSelection"/>, and everything else follows from
    /// that, exactly as a board click does.
    ///
    /// Rebuilt wholesale on change rather than diffed. A party is a handful of rows and changes
    /// only when a creature spawns, dies or takes damage; a pooling layer here would be machinery
    /// with nothing to do.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class ArenaHudView : MonoBehaviour
    {
        [SerializeField]
        CreatureSelection m_Selection;

        VisualElement m_Root;
        Label m_PartyTitle;
        ScrollView m_PortraitList;

        VisualElement m_Card;
        Label m_CardName;
        Label m_CardSubtitle;
        Label m_CardController;
        Label m_CardHp;
        Label m_CardAp;
        Label m_CardSpeed;
        Label m_CardDescription;
        VisualElement m_CardApPips;

        readonly List<CreatureState> m_Observed = new List<CreatureState>();

        void Start()
        {
            if (m_Selection == null)
            {
                Debug.LogError($"{nameof(ArenaHudView)} has no {nameof(CreatureSelection)}.", this);
                enabled = false;
                return;
            }

            m_Root = GetComponent<UIDocument>().rootVisualElement;

            // The HUD floats over the board, so its empty regions must not swallow clicks meant for
            // a tile. Only the controls below opt back in.
            m_Root.pickingMode = PickingMode.Ignore;

            m_PartyTitle = m_Root.Q<Label>("party-title");
            m_PortraitList = m_Root.Q<ScrollView>("portrait-list");

            m_Card = m_Root.Q<VisualElement>("summary-card");
            m_CardName = m_Root.Q<Label>("card-name");
            m_CardSubtitle = m_Root.Q<Label>("card-subtitle");
            m_CardController = m_Root.Q<Label>("card-controller");
            m_CardHp = m_Root.Q<Label>("card-hp");
            m_CardAp = m_Root.Q<Label>("card-ap");
            m_CardSpeed = m_Root.Q<Label>("card-speed");
            m_CardDescription = m_Root.Q<Label>("card-description");
            m_CardApPips = m_Root.Q<VisualElement>("card-ap-pips");

            m_Root.Q<VisualElement>("party-column").pickingMode = PickingMode.Ignore;

            CreatureRegistry.Changed += Rebuild;
            m_Selection.SelectionChanged += OnSelectionChanged;

            Rebuild();
        }

        void OnDestroy()
        {
            CreatureRegistry.Changed -= Rebuild;

            if (m_Selection != null)
            {
                m_Selection.SelectionChanged -= OnSelectionChanged;
            }

            Unobserve();
        }

        void OnSelectionChanged(CreatureState _) => Rebuild();

        void Unobserve()
        {
            foreach (var creature in m_Observed)
            {
                if (creature != null)
                {
                    creature.Changed -= Rebuild;
                }
            }

            m_Observed.Clear();
        }

        void Rebuild()
        {
            if (m_Root == null)
            {
                return;
            }

            Unobserve();
            m_PortraitList.Clear();

            var party = LocalParty();
            m_PartyTitle.text = party.HasValue ? PartyPalette.NameOf(party.Value) : "Party";

            if (party.HasValue)
            {
                // Everyone on the player's side, not just their own claims: you need to see what
                // your teammates are fielding.
                foreach (var creature in CreatureRegistry.InParty(party.Value))
                {
                    creature.Changed += Rebuild;
                    m_Observed.Add(creature);
                    m_PortraitList.Add(BuildPortrait(creature));
                }
            }

            RebuildCard();
        }

        /// <summary>
        /// The party the local player is on, inferred from a creature they control. Falls back to
        /// the first party present so a spectator still sees something.
        /// </summary>
        Party? LocalParty()
        {
            var roster = PlayerRoster.Current;
            var manager = Unity.Netcode.NetworkManager.Singleton;

            if (roster != null && manager != null
                && roster.TryGet(manager.LocalClientId, out var entry))
            {
                foreach (var creature in CreatureRegistry.All)
                {
                    if (creature != null && creature.ControllerSlot == entry.Slot)
                    {
                        return creature.Party;
                    }
                }
            }

            foreach (var creature in CreatureRegistry.All)
            {
                if (creature != null)
                {
                    return creature.Party;
                }
            }

            return null;
        }

        VisualElement BuildPortrait(CreatureState creature)
        {
            var row = new VisualElement();
            row.AddToClassList("portrait");

            if (m_Selection.Selected == creature)
            {
                row.AddToClassList("portrait--selected");
            }

            // The border is the ownership channel -- which of a shared party is mine.
            row.style.borderTopColor = row.style.borderBottomColor =
                row.style.borderLeftColor = row.style.borderRightColor =
                    creature.IsComputerControlled
                        ? new Color(0.35f, 0.37f, 0.44f)
                        : PlayerPalette.ForSlot(creature.ControllerSlot);

            var image = new VisualElement();
            image.AddToClassList("portrait__image");

            var definition = creature.Definition;
            if (definition != null && definition.Portrait != null)
            {
                image.style.backgroundImage = new StyleBackground(definition.Portrait);
            }
            else
            {
                // No sprite authored: a lettered tile beats an empty box.
                var initial = new Label(InitialOf(creature.DisplayName));
                initial.AddToClassList("portrait__initial");
                image.Add(initial);
            }

            var body = new VisualElement();
            body.AddToClassList("portrait__body");

            var name = new Label(creature.DisplayName);
            name.AddToClassList("portrait__name");

            var track = new VisualElement();
            track.AddToClassList("hp-track");

            var fill = new VisualElement();
            fill.AddToClassList("hp-fill");
            fill.style.width = Length.Percent(Fraction(creature.CurrentHp, creature.MaxHp) * 100f);
            track.Add(fill);

            body.Add(name);
            body.Add(track);

            row.Add(image);
            row.Add(body);

            row.pickingMode = PickingMode.Position;
            row.RegisterCallback<ClickEvent>(_ => m_Selection.Select(creature));

            return row;
        }

        void RebuildCard()
        {
            var creature = m_Selection.Selected;
            var visible = creature != null;

            m_Card.EnableInClassList("is-hidden", !visible);
            m_Card.pickingMode = visible ? PickingMode.Position : PickingMode.Ignore;

            if (!visible)
            {
                return;
            }

            m_CardName.text = creature.DisplayName;

            var definition = creature.Definition;
            m_CardSubtitle.text = definition != null
                ? $"{definition.SpeciesName} · {definition.ClassName} · {PartyPalette.NameOf(creature.Party)}"
                : PartyPalette.NameOf(creature.Party);

            m_CardController.text = $"Controlled by {ControllerName(creature)}";
            m_CardHp.text = $"{creature.CurrentHp} / {creature.MaxHp}";
            m_CardAp.text = $"{creature.CurrentAp} / {creature.MaxAp}";
            m_CardSpeed.text = creature.Speed.ToString();
            m_CardDescription.text = definition != null ? definition.Description : string.Empty;

            BuildPips(creature.CurrentAp, creature.MaxAp);
        }

        void BuildPips(int filled, int total)
        {
            m_CardApPips.Clear();

            for (var i = 0; i < total; i++)
            {
                var pip = new VisualElement();
                pip.AddToClassList("pip");
                if (i < filled)
                {
                    pip.AddToClassList("pip--filled");
                }

                m_CardApPips.Add(pip);
            }
        }

        static string ControllerName(CreatureState creature)
        {
            if (creature.IsComputerControlled)
            {
                return "Computer";
            }

            var roster = PlayerRoster.Current;
            if (roster != null && roster.TryGetBySlot(creature.ControllerSlot, out var entry))
            {
                var name = entry.Name.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    return name;
                }
            }

            return $"Player {creature.ControllerSlot + 1}";
        }

        static float Fraction(int current, int max) =>
            max <= 0 ? 0f : Mathf.Clamp01((float)current / max);

        static string InitialOf(string name) =>
            string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
    }
}

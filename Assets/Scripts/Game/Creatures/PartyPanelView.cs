using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The party column: one portrait per creature on the local player's side.
    ///
    /// Shows the whole party, not just the player's own claims -- you need to see what your
    /// teammates are fielding. Which of them are yours is carried by the border colour, because that
    /// is the question a shared party raises and colour answers it without costing a row of text.
    ///
    /// Split from the summary card: they are two views with different data and different redraw
    /// triggers, and one class doing both was doing two jobs.
    ///
    /// Rebuilt wholesale on change rather than diffed. A party is a handful of rows that change only
    /// when a creature spawns, dies or takes damage; pooling here would be machinery with nothing
    /// to do.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class PartyPanelView : MonoBehaviour
    {
        [SerializeField]
        CreatureSelection m_Selection;

        [SerializeField]
        CreatureRegistry m_Creatures;

        Label m_Title;
        ScrollView m_List;

        readonly List<CreatureState> m_Observed = new List<CreatureState>();

        void Start()
        {
            if (m_Selection == null || m_Creatures == null)
            {
                Debug.LogError("PartyPanelView is missing its selection or registry.", this);
                enabled = false;
                return;
            }

            var root = GetComponent<UIDocument>().rootVisualElement;
            CreatureDisplay.MakeClickThrough(root);

            m_Title = root.Q<Label>("party-title");
            m_List = root.Q<ScrollView>("portrait-list");

            var column = root.Q<VisualElement>("party-column");
            if (column != null)
            {
                column.pickingMode = PickingMode.Ignore;
            }

            if (m_Title == null || m_List == null)
            {
                Debug.LogError("PartyPanelView could not find its elements; check ArenaHud.uxml.", this);
                enabled = false;
                return;
            }

            m_Creatures.Changed += Rebuild;
            m_Selection.SelectionChanged += OnSelectionChanged;

            Rebuild();
        }

        void OnDestroy()
        {
            if (m_Creatures != null)
            {
                m_Creatures.Changed -= Rebuild;
            }

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
            if (m_List == null)
            {
                return;
            }

            Unobserve();
            m_List.Clear();

            var party = LocalParty();
            m_Title.text = party.HasValue ? PartyPalette.NameOf(party.Value) : "Spectating";

            if (!party.HasValue)
            {
                return;
            }

            foreach (var creature in m_Creatures.InParty(party.Value))
            {
                creature.Changed += Rebuild;
                m_Observed.Add(creature);
                m_List.Add(BuildPortrait(creature));
            }
        }

        /// <summary>
        /// The side the local player chose, read from the draft.
        ///
        /// This used to be inferred from a creature the player happened to control, which was a view
        /// reconstructing a fact the draft already owns -- and it was wrong exactly when it mattered:
        /// a player whose teammates claimed everything, or whose claims were released by
        /// <c>EnforceCaps</c>, controls nothing, so the inference fell through to "the first party
        /// present" and showed them the enemy column.
        ///
        /// Null means no party, which is a real state -- a spectator, or a player who has not picked
        /// yet -- and the caller shows an empty column for it rather than guessing.
        /// </summary>
        Party? LocalParty()
        {
            var roster = PlayerRoster.Current;
            var manager = NetworkManager.Singleton;
            var draft = DraftState.Current;

            if (roster == null || manager == null || draft == null
                || !roster.TryGet(manager.LocalClientId, out var entry)
                || entry.Slot < 0 || entry.Slot > byte.MaxValue)
            {
                return null;
            }

            return DraftQueries.TryGetParty(draft.Choices, (byte)entry.Slot, out var party)
                ? party
                : (Party?)null;
        }

        VisualElement BuildPortrait(CreatureState creature)
        {
            var row = new VisualElement();
            row.AddToClassList("portrait");

            if (m_Selection.Selected == creature)
            {
                row.AddToClassList("portrait--selected");
            }

            var border = CreatureDisplay.OwnerColor(creature);
            row.style.borderTopColor = row.style.borderBottomColor =
                row.style.borderLeftColor = row.style.borderRightColor = border;

            row.Add(BuildImage(creature));
            row.Add(BuildBody(creature));

            row.pickingMode = PickingMode.Position;
            row.RegisterCallback<ClickEvent>(_ => m_Selection.Select(creature));

            return row;
        }

        static VisualElement BuildImage(CreatureState creature)
        {
            var image = new VisualElement();
            image.AddToClassList("portrait__image");

            var definition = creature.Definition;
            if (definition != null && definition.Portrait != null)
            {
                image.style.backgroundImage = new StyleBackground(definition.Portrait);
                return image;
            }

            var initial = new Label(CreatureDisplay.Initial(creature.DisplayName));
            initial.AddToClassList("portrait__initial");
            image.Add(initial);
            return image;
        }

        static VisualElement BuildBody(CreatureState creature)
        {
            var body = new VisualElement();
            body.AddToClassList("portrait__body");

            var name = new Label(creature.DisplayName);
            name.AddToClassList("portrait__name");

            // The track stays visible so a nearly-empty bar reads as "hurt" rather than "missing".
            var track = new VisualElement();
            track.AddToClassList("hp-track");

            var fill = new VisualElement();
            fill.AddToClassList("hp-fill");
            fill.style.width = Length.Percent(CreatureDisplay.HealthFraction(creature) * 100f);
            track.Add(fill);

            body.Add(name);
            body.Add(track);
            return body;
        }
    }
}

using Dragoneye.Combat;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.Game;
using Dragoneye.Game.Combat;

namespace Dragoneye.Game.Creatures
{
    /// <summary>
    /// The party column: one portrait per creature on the local player's side.
    ///
    /// Shows the whole party, not just the player's own claims -- you need to see what your
    /// teammates are fielding. Which of them are yours is carried by the border colour, because that
    /// is the question a shared party raises and colour answers it without costing a row of text.
    ///
    /// Health is read from the shown fight, so a bar falls when the blow is shown to land. A
    /// creature that has fallen stays in the column, dimmed: the party is still the party, and a
    /// row that vanished said less than one that is plainly gone.
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

        VisualElement m_Column;
        CombatPlayback m_Playback;

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

            m_Column = root.Q<VisualElement>("party-column");

            if (m_Column != null)
            {
                m_Column.pickingMode = PickingMode.Ignore;
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

        /// <summary>
        /// Stands aside while a test scenario is running, and follows the playback.
        ///
        /// A scenario is watched rather than played -- nobody's party is anybody's -- and the
        /// report of what it proved wants the column this would otherwise be holding.
        /// </summary>
        void Update()
        {
            if (m_Playback != CombatPlayback.Current)
            {
                if (m_Playback != null)
                {
                    m_Playback.Changed -= Rebuild;
                }

                m_Playback = CombatPlayback.Current;

                if (m_Playback != null)
                {
                    m_Playback.Changed += Rebuild;
                }
            }

            if (m_Column == null)
            {
                return;
            }

            var scenario = ScenarioRunner.Current != null && ScenarioRunner.Current.Scenario != null;
            m_Column.EnableInClassList("is-hidden", scenario);
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

            if (m_Playback != null)
            {
                m_Playback.Changed -= Rebuild;
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

            // Named and coloured, matching the band on the inspect card. Two panels talking about
            // the same side should say so the same way.
            m_Title.text = party.HasValue
                ? "TEAM " + PartyPalette.NameOf(party.Value).ToUpperInvariant()
                : "SPECTATING";

            m_Title.style.color = party.HasValue
                ? new StyleColor(PartyPalette.ForParty(party.Value))
                : new StyleColor(StyleKeyword.Null);

            if (!party.HasValue)
            {
                return;
            }

            foreach (var creature in m_Creatures.InParty(party.Value))
            {
                // Identity still comes from the creature: a name filled in as a client connects.
                creature.Changed += Rebuild;
                m_Observed.Add(creature);
                m_List.Add(BuildPortrait(creature));
            }
        }

        /// <summary>
        /// The side the local player chose, read from the draft.
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
            row.EnableInClassList("portrait--fallen", !Shown.IsAlive(creature));

            if (m_Selection.Selected == creature)
            {
                row.AddToClassList("portrait--selected");
            }

            // Only the left edge. The rest of the border is what the stylesheet uses to mark the
            // selected card, and setting all four here would have painted over it.
            row.style.borderLeftColor = CreatureDisplay.OwnerColor(creature);

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

            CreatureDisplay.DrawPortrait(image, creature);
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

            // The silver bar, above the health it protects, and only where there is any.
            if (creature.MaxArmour > 0)
            {
                var guard = new VisualElement();
                guard.AddToClassList("armour-track");

                var plate = new VisualElement();
                plate.AddToClassList("armour-fill");
                plate.style.width = Length.Percent(CreatureDisplay.ArmourFraction(creature) * 100f);
                guard.Add(plate);

                body.Add(guard);
            }

            body.Add(track);
            return body;
        }
    }
}

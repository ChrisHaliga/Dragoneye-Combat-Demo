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
    /// The party column: one card per creature on the local player's side.
    ///
    /// A face, what it is holding down one edge, and what is left of it along the bottom. No frame
    /// around the column and no heading over it: the cards are the panel, and a box drawn round
    /// them cost a strip of board to say only that a box had been drawn.
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

            m_List = root.Q<ScrollView>("portrait-list");

            m_Column = root.Q<VisualElement>("party-column");

            if (m_Column != null)
            {
                m_Column.pickingMode = PickingMode.Ignore;
            }

            if (m_List == null)
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

        /// <summary>
        /// One creature: its face, its hand and its bars, with the numbers on because this is the
        /// side the player is answerable for.
        ///
        /// Right-click reads it. A left click used to open the inspector, which meant the card
        /// appeared for a glance at a health bar and stayed until something else was clicked --
        /// reading a creature is now something you ask for.
        /// </summary>
        VisualElement BuildPortrait(CreatureState creature)
        {
            var card = new VisualElement();
            card.AddToClassList("portrait");
            card.EnableInClassList("portrait--fallen", !Shown.IsAlive(creature));

            // The whole edge in the controlling player's colour: with the click gone there is no
            // second fact competing for the border.
            card.style.borderTopColor = card.style.borderBottomColor =
                card.style.borderLeftColor = card.style.borderRightColor =
                    CreatureDisplay.OwnerColor(creature);

            CreatureDisplay.DrawPortrait(card, creature);
            CreatureDisplay.DrawElements(card, creature);
            CreatureDisplay.DrawVitals(card, creature, numbers: true);

            card.tooltip = $"{creature.DisplayName}\n{CreatureDisplay.ControllerName(creature)}"
                + "\n\nRight-click to inspect.";

            card.pickingMode = PickingMode.Position;
            card.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1)
                {
                    m_Selection.Select(creature);
                    evt.StopPropagation();
                }
            });

            return card;
        }
    }
}

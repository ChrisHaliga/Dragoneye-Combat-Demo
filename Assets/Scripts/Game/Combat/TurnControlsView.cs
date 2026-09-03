using Dragoneye.Hex.Systems;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The bottom-centre End Turn button, the AP readout above it, the action label that follows the
    /// cursor, and the banner that closes the match.
    ///
    /// Four small things in one component because they are the same concern from the player's side:
    /// what can I do right now, and what happens when I stop. Splitting them would mean four
    /// documents observing the same turn state.
    ///
    /// The button highlights when the active creature can no longer afford anything, and that is all
    /// it does. The turn always ends on a click -- never on running out of AP -- so a player can
    /// stop early or hold what is left.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class TurnControlsView : MonoBehaviour
    {
        [SerializeField]
        BoardActionInput m_Input;

        [SerializeField]
        ArenaMap m_Map;

        [SerializeField]
        UnitIndex m_Units;

        [SerializeField, Min(0f), Tooltip("How long the outcome banner is shown before the match "
             + "closes and everyone returns to the menu.")]
        float m_OutcomeDwell = 4f;

        VisualElement m_Footer;
        VisualElement m_Banner;
        Label m_Ap;
        Label m_Cursor;
        Label m_OutcomeTitle;
        Button m_EndTurn;

        bool m_Closing;

        void Start()
        {
            if (m_Input == null || m_Map == null || m_Units == null)
            {
                Debug.LogError($"{nameof(TurnControlsView)} is missing references.", this);
                enabled = false;
                return;
            }

            var root = GetComponent<UIDocument>().rootVisualElement;

            m_Footer = root.Q<VisualElement>("turn-footer");
            m_Banner = root.Q<VisualElement>("outcome-banner");
            m_Ap = root.Q<Label>("ap-label");
            m_Cursor = root.Q<Label>("cursor-action");
            m_OutcomeTitle = root.Q<Label>("outcome-title");
            m_EndTurn = root.Q<Button>("end-turn-button");

            if (m_Footer == null || m_Banner == null || m_Ap == null || m_Cursor == null
                || m_OutcomeTitle == null || m_EndTurn == null)
            {
                Debug.LogError($"{nameof(TurnControlsView)} could not find its elements; "
                    + "check ArenaHud.uxml.", this);
                enabled = false;
                return;
            }

            // The button is the one thing on this document that must take clicks; everything else is
            // an overlay the board has to be reachable through.
            CreatureDisplay.MakeClickThrough(root);

            m_EndTurn.clicked += OnEndTurnClicked;
            m_Cursor.pickingMode = PickingMode.Ignore;
        }

        void OnDestroy()
        {
            if (m_EndTurn != null)
            {
                m_EndTurn.clicked -= OnEndTurnClicked;
            }
        }

        void Update()
        {
            if (m_Footer == null)
            {
                return;
            }

            RefreshFooter();
            RefreshCursor();
            RefreshOutcome();
        }

        void RefreshFooter()
        {
            var actor = m_Input.Actor;
            var mine = actor != null;

            m_Footer.EnableInClassList("is-hidden", !mine);

            if (!mine)
            {
                return;
            }

            m_Ap.text = $"{actor.DisplayName} -- {actor.CurrentAp} / {actor.MaxAp} AP";

            // Cheapest possible action, asked of the board rather than assumed: a creature boxed in
            // by its own allies cannot move even with AP to spare.
            var spent = !CombatRules.CanAffordAnything(
                actor.CurrentAp, AnyStepAvailable(actor), AnyEnemyInReach(actor));

            m_EndTurn.EnableInClassList("end-turn--spent", spent);
            m_EndTurn.text = spent ? "End Turn (no AP)" : "End Turn";
        }

        /// <summary>Whether at least one neighbouring hex can be stepped into.</summary>
        bool AnyStepAvailable(CreatureState actor)
        {
            if (m_Map == null || m_Map.Map == null)
            {
                return false;
            }

            foreach (var neighbour in actor.Cell.Neighbors())
            {
                if (!m_Units.IsOccupied(neighbour)
                    && m_Map.Map.TryGetTile(neighbour, out var tile) && tile.IsWalkable)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether an enemy stands close enough to be hit without moving.</summary>
        bool AnyEnemyInReach(CreatureState actor)
        {
            foreach (var neighbour in actor.Cell.Neighbors())
            {
                if (m_Units.TryGet(neighbour, out var occupant))
                {
                    var creature = occupant.GetComponent<CreatureState>();
                    if (creature != null && creature.IsAlive && creature.Party != actor.Party)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Pins the action label beside the cursor.
        ///
        /// Positioned in panel coordinates, which are y-down from the top-left, while Unity's input
        /// gives y-up from the bottom-left. Flipping it is the whole reason this is not a one-liner.
        /// </summary>
        void RefreshCursor()
        {
            var plan = m_Input.Hovered;
            var text = ActionResolver.Describe(plan);

            m_Cursor.text = text;
            m_Cursor.EnableInClassList("is-hidden", string.IsNullOrEmpty(text));

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
            {
                return;
            }

            var position = mouse.position.ReadValue();
            var panel = m_Cursor.panel != null
                ? RuntimePanelUtils.ScreenToPanel(m_Cursor.panel,
                    new Vector2(position.x, Screen.height - position.y))
                : Vector2.zero;

            m_Cursor.style.left = panel.x + 18f;
            m_Cursor.style.top = panel.y + 12f;
        }

        void RefreshOutcome()
        {
            var turns = TurnState.Current;
            var over = turns != null && turns.IsOver;

            m_Banner.EnableInClassList("is-hidden", !over);

            if (!over || m_Closing)
            {
                return;
            }

            m_Closing = true;
            m_OutcomeTitle.text = $"{PartyPalette.NameOf(turns.Winner)} win";

            Invoke(nameof(CloseMatch), m_OutcomeDwell);
        }

        /// <summary>
        /// Ends the match for everyone.
        ///
        /// Routed through <see cref="MatchFlow"/> rather than shutting netcode down here, so a solo
        /// match and a hosted one close the same way and the menu is reached by the one path that
        /// already knows how to get there.
        /// </summary>
        void CloseMatch() => MatchFlow.Instance?.LeaveMatch();

        void OnEndTurnClicked()
        {
            var turns = TurnState.Current;
            if (turns != null)
            {
                turns.GetComponent<TurnCommands>()?.RequestEndTurn();
            }
        }
    }
}

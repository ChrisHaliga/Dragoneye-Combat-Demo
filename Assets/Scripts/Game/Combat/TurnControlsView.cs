using Dragoneye.Combat;
using Dragoneye.Hex.Systems;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The bottom-centre End Turn button, the AP readout above it, the action label that follows the
    /// cursor, and the banner announcing the winner.
    ///
    /// Draws and nothing else. Every question it asks -- can this creature still do anything, what
    /// would this click cost, who won -- is answered elsewhere; closing the match once it is over
    /// belongs to <see cref="MatchConclusion"/>. A view that could end a turn, price an action or
    /// shut down a session would be a second authority on all three.
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

        VisualElement m_Footer;
        VisualElement m_Banner;
        Label m_Ap;
        Label m_Cursor;
        Label m_OutcomeTitle;
        Button m_EndTurn;

        ArenaBoard m_Board;

        void Start()
        {
            if (m_Input == null || m_Map == null || m_Units == null)
            {
                Debug.LogError($"{nameof(TurnControlsView)} is missing references.", this);
                enabled = false;
                return;
            }

            m_Board = new ArenaBoard(m_Map, m_Units);

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
            // an overlay the board has to stay reachable through.
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

            // What "nothing left to do" means now depends on what the creature knows: a bow can
            // still act at four tiles where a dagger cannot act at two.
            var spent = !CombatRules.CanAffordAnything(
                actor.CurrentAp,
                m_Board.HasOpenNeighbour(actor.Cell),
                AnySkillUsable(actor));

            m_EndTurn.EnableInClassList("end-turn--spent", spent);
            m_EndTurn.text = spent ? "End Turn (no AP)" : "End Turn";
        }

        /// <summary>
        /// Pins the action label beside the cursor.
        ///
        /// Positioned in panel coordinates, which are y-down from the top-left, while Unity's input
        /// gives y-up from the bottom-left. Flipping it is the whole reason this is not a one-liner.
        /// </summary>
        /// <summary>
        /// Whether anything on the bar could still be used on somebody.
        ///
        /// Asked of the same rules the bar and the server ask, so the End Turn prompt cannot say
        /// "no AP" while a usable skill is still lit. Reach is per skill now: a bow can still act at
        /// four tiles where a dagger cannot act at two.
        /// </summary>
        bool AnySkillUsable(CreatureState actor)
        {
            var skills = actor.GetComponent<SkillCommands>();
            var pool = actor.GetComponent<CreaturePool>();

            if (skills == null || pool == null)
            {
                return false;
            }

            foreach (var skill in skills.Skills)
            {
                if (SkillRules.CheckAffordable(skill, true, actor.CurrentAp, pool.Ledger)
                    != SkillRefusal.None)
                {
                    continue;
                }

                if (skill.Target != SkillTarget.Creature
                    || m_Board.HasEnemyInReach(actor.Cell, actor.Party, skill.Range))
                {
                    return true;
                }
            }

            return false;
        }

        void RefreshCursor()
        {
            var text = ActionLabels.Describe(m_Input.Hovered);

            // Beside the price rather than instead of it. A player deciding whether to walk round
            // the back is weighing what the walk costs against what the position buys, and hiding
            // one half of that to make room for the other would be answering the question for them.
            if (m_Input.HoveredIsFlank && !string.IsNullOrEmpty(text))
            {
                text += "  ·  " + ClashLabels.Advantage;
            }

            // The odds on their own line. A player weighing an attack wants the price and the
            // chances together, and running them into one line makes both harder to read than
            // either would be alone.
            if (m_Input.HoveredOdds.HasValue && !string.IsNullOrEmpty(text))
            {
                text += "\n" + ClashLabels.Forecast(m_Input.HoveredOdds.Value);
            }

            m_Cursor.text = text;
            m_Cursor.EnableInClassList("is-flank", m_Input.HoveredIsFlank);
            m_Cursor.EnableInClassList("is-hidden", string.IsNullOrEmpty(text));

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || m_Cursor.panel == null)
            {
                return;
            }

            var screen = mouse.position.ReadValue();
            var panel = RuntimePanelUtils.ScreenToPanel(m_Cursor.panel,
                new Vector2(screen.x, Screen.height - screen.y));

            m_Cursor.style.left = panel.x + 18f;
            m_Cursor.style.top = panel.y + 12f;
        }

        void RefreshOutcome()
        {
            var turns = TurnState.Current;
            var over = turns != null && turns.IsOver;

            m_Banner.EnableInClassList("is-hidden", !over);

            if (over)
            {
                m_OutcomeTitle.text = $"{PartyPalette.NameOf(turns.Winner)} win";
            }
        }

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

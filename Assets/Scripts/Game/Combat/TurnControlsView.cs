using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex.Systems;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
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
        VisualElement m_ApPips;
        Label m_ApText;
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
            m_ApPips = root.Q<VisualElement>("ap-pips");
            m_ApText = root.Q<Label>("ap-text");
            m_Cursor = root.Q<Label>("cursor-action");
            m_OutcomeTitle = root.Q<Label>("outcome-title");
            m_EndTurn = root.Q<Button>("end-turn-button");

            if (m_Footer == null || m_Banner == null || m_ApPips == null || m_ApText == null
                || m_Cursor == null
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

            // The outline on End Turn blinks while there is nothing left to spend. A class flipped
            // on a schedule with an eased border is the closest USS comes to a pulse, and it is
            // only visible while the spent class is on, so the schedule can simply run.
            var endTurn = m_EndTurn;
            endTurn.schedule.Execute(() => endTurn.ToggleInClassList("end-turn--pulse")).Every(650);
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

            ApPips.Fill(m_ApPips, actor.CurrentAp, actor.MaxAp);
            m_ApText.text = $"{actor.CurrentAp} / {actor.MaxAp} AP";

            // What "nothing left to do" means now depends on what the creature knows: a bow can
            // still act at four tiles where a dagger cannot act at two.
            var spent = !CombatRules.CanAffordAnything(
                actor.CurrentAp,
                m_Board.HasOpenNeighbour(actor.Cell),
                AnySkillUsable(actor),
                actor.StepCost);

            // The words never change; the outline does. A button whose label rewrites itself
            // reads as two buttons, and the player already knows the number -- it is right above.
            m_EndTurn.EnableInClassList("end-turn--spent", spent);
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
            var skills = actor.SkillCommands;
            var pool = actor.Pool;

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

        static string Names(IReadOnlyList<CreatureState> creatures)
        {
            var text = string.Empty;

            for (var i = 0; i < creatures.Count; i++)
            {
                text += (i > 0 ? ", " : string.Empty) + creatures[i].DisplayName;
            }

            return text;
        }

        void RefreshCursor()
        {
            // A move waiting on a bearing has already chosen its tile, so pricing the one under the
            // cursor would advertise something the next click is not going to do.
            if (m_Input.PendingMove.HasValue)
            {
                m_Cursor.text = "Click to face this way";
                m_Cursor.EnableInClassList("is-flank", false);
                m_Cursor.EnableInClassList("is-hidden", false);
                PlaceCursor();
                return;
            }

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
                text += "\n" + ClashLabels.Forecast(m_Input.HoveredOdds.Value)
                    + "\n" + ClashLabels.AttackerStakes;
            }

            // A shot says its chance, and names whoever it would fly over. Both before the
            // click, because both are decided by where the shooter stands, and standing
            // somewhere else first is the whole decision.
            if (m_Input.HoveredShot.HasValue && !string.IsNullOrEmpty(text))
            {
                var shot = m_Input.HoveredShot.Value;
                if (!shot.IsBlocked)
                {
                    text += "\n" + ClashLabels.Chance(shot.Chance);
                }

                if (shot.IsCovered)
                {
                    var over = Names(shot.Bodies);

                    if (shot.Walls == Dragoneye.Hex.Systems.LineVerdict.Obstructed)
                    {
                        over += (over.Length > 0 ? ", " : string.Empty) + "a low wall";
                    }

                    text += "\n" + ClashLabels.Cover(over, SkillRules.CoverPenalty * shot.Cover);
                }
            }

            // Only where there is already something to say. It is a price on leaving, so it
            // belongs beside what leaving costs -- and it is on a skill as much as on a move,
            // because a skill that walks into range walks.
            if (m_Input.HoveredProvokes && !string.IsNullOrEmpty(text))
            {
                text += "\n" + ClashLabels.Provokes;
            }

            m_Cursor.text = text;
            m_Cursor.EnableInClassList("is-flank", m_Input.HoveredIsFlank);
            m_Cursor.EnableInClassList("is-hidden", string.IsNullOrEmpty(text));

            if (!string.IsNullOrEmpty(text))
            {
                PlaceCursor();
            }
        }

        /// <summary>Puts the label beside the pointer, wherever the pointer is.</summary>
        void PlaceCursor()
        {
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
            // A scenario's outcome is its report, and this banner would cover it.
            var scenario = ScenarioRunner.Current != null && ScenarioRunner.Current.Scenario != null;
            var over = Shown.IsOver && !scenario;

            m_Banner.EnableInClassList("is-hidden", !over);

            if (over)
            {
                m_OutcomeTitle.text = Shown.HasWinner
                    ? $"{PartyPalette.NameOf(Shown.Winner)} win"
                    : "Nobody is left standing";
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

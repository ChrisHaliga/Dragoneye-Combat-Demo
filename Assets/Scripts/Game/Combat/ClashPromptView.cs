using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.UI;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// The question put to a defender, and the only place they can answer it.
    ///
    /// Eight things to choose between: the seven elements, held or not, and taking the blow. All
    /// seven are always shown, because a hand is a fixed set and a row that changed length as it
    /// drained would teach the player nothing about either the set or the hand. What they do not
    /// hold is dim and dead. Taking the blow is the eighth option rather than a button off to the
    /// side, because it is an answer -- it costs nothing and it is sometimes the right one -- and
    /// it should sit beside the others with its odds under it like theirs.
    ///
    /// It shows what they hold and, of what is coming, only what is public: which skills the
    /// attacker has been watched using, and -- for a swing at somebody walking past, which is
    /// always their weapon -- which element that is, once the weapon has been seen. A
    /// <see cref="DefenceRequest"/> has no room for the attacker's actual commitment, so there is
    /// nothing here to be careful about withholding.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class ClashPromptView : MonoBehaviour
    {
        [SerializeField, Tooltip("The board input, which owns the creature registry.")]
        BoardActionInput m_Input;

        VisualElement m_Root;
        VisualElement m_Panel;
        VisualElement m_Options;
        VisualElement m_Help;
        Label m_Tally;

        // What the player had open before the question interrupted them, and whether it did.
        CreatureState m_Restore;
        bool m_Interrupted;

        DefenceRequest m_Request;
        bool m_Open;
        bool m_Declined;

        // A question that has arrived and is waiting for the fight to be shown up to it. The
        // simulation asked the moment the swing was decided; the player is asked once they have
        // seen it thrown.
        bool m_Waiting;

        readonly List<Element> m_Staged = new List<Element>();

        void Start()
        {
            if (m_Input == null)
            {
                Debug.LogError($"{nameof(ClashPromptView)} has no board input.", this);
                enabled = false;
                return;
            }

            var document = GetComponent<UIDocument>().rootVisualElement;

            // The template root, not the document root: the stylesheet is attached inside it, so a
            // sibling of it would be styled by nothing at all.
            m_Root = document.Q<VisualElement>("root") ?? document;

            ClashCommands.Asked += OnAsked;
            ClashCommands.Closed += Close;
        }

        void OnDestroy()
        {
            ClashCommands.Asked -= OnAsked;
            ClashCommands.Closed -= Close;
        }

        /// <summary>Whether a decision is on screen. The board stands aside while it is.</summary>
        public bool IsOpen => m_Open;

        // The press that sent the move is the press that opened this. It must not also answer it.
        readonly PromptGuard m_Guard = new PromptGuard();

        void OnAsked(DefenceRequest request)
        {
            m_Request = request;
            m_Staged.Clear();
            m_Declined = false;

            Close();
            m_Waiting = true;
            m_WaitingSince = Time.unscaledTime;
        }

        /// <summary>
        /// Opens the question once everything before it has been shown.
        ///
        /// The simulation stops on this question, so by the time the playback has caught up the
        /// shown fight and the real one are the same fight -- and the attack the player is being
        /// asked about is the one they just watched begin.
        /// </summary>
        void Update()
        {
            if (!m_Waiting)
            {
                return;
            }

            if (!Shown.IsCaughtUp)
            {
                if (Time.unscaledTime - m_WaitingSince < ShowAnyway)
                {
                    return;
                }

                Debug.LogWarning("The playback has not caught up, so the defence is being asked "
                    + "for anyway. The fight cannot go on until it is answered.", this);
            }

            m_Waiting = false;
            Build();
            m_Guard.Open(m_Panel);
            m_Open = true;
        }

        /// <summary>
        /// How long a question waits for the screen to catch up before it is asked anyway.
        ///
        /// The fight stops on a question, and nothing but an answer restarts it. So a question
        /// that is never asked stops the match: no prompt, no error, no turn, nothing to press.
        /// Waiting for the playback is a courtesy -- being asked about a blow you have watched
        /// land reads better than being asked about one you have not -- and a courtesy is not
        /// worth a fight that cannot continue.
        /// </summary>
        const float ShowAnyway = 5f;

        float m_WaitingSince;

        void Close()
        {
            m_Panel?.RemoveFromHierarchy();
            m_Panel = null;
            m_Help = null;
            m_Open = false;
            m_Waiting = false;

            PutTheCardBack();
        }

        CreatureState Defender => Creature((uint)m_Request.DefenderId);

        CreatureState Attacker => Creature((uint)m_Request.AttackerId);

        CreatureState Creature(uint turnId) =>
            m_Input.Creatures != null ? m_Input.Creatures.ByTurnId(turnId) : null;

        /// <summary>
        /// How much of an element this creature still has to put up, less what is already staged.
        ///
        /// Read from the defender's own pool rather than carried on the request, because the count
        /// is theirs and the request goes over a wire. Falls back to the request's option list if
        /// the creature cannot be found, so a prompt is never dead -- the sequence refuses anything
        /// unpayable anyway, which is the check that counts.
        /// </summary>
        int Held(Element element)
        {
            var defender = Defender;
            var pool = defender != null ? defender.Pool : null;

            var held = pool != null && LocalPlayer.Controls(defender)
                ? pool.Pool[element]
                : Contains(m_Request.Options, element) ? 1 : 0;

            var staged = 0;

            foreach (var chosen in m_Staged)
            {
                if (chosen == element)
                {
                    staged++;
                }
            }

            return held - staged;
        }

        static bool Contains(IReadOnlyList<Element> elements, Element element)
        {
            foreach (var candidate in elements)
            {
                if (candidate == element)
                {
                    return true;
                }
            }

            return false;
        }

        void Build()
        {
            m_Panel = new VisualElement();
            m_Panel.AddToClassList("clash-prompt");

            // The reason and the help button on one line. No title: eight runes with a percentage
            // under each is not mistakable for anything other than the question it is.
            var head = new VisualElement();
            head.AddToClassList("clash-prompt__head");

            var reason = new Label(ClashLabels.Describe(m_Request));
            reason.AddToClassList("clash-prompt__reason");
            head.Add(reason);

            head.Add(HelpButton());
            m_Panel.Add(head);

            m_Options = new VisualElement();
            m_Options.AddToClassList("clash-prompt__options");
            m_Panel.Add(m_Options);

            // Only a pair needs a second line: it has to say that one is chosen and one is
            // still wanted. A single answer needs nothing under the row, because the click is it.
            m_Tally = new Label();
            m_Tally.AddToClassList("clash-prompt__tally");
            m_Tally.EnableInClassList("is-hidden", m_Request.Required <= 1);
            m_Panel.Add(m_Tally);

            m_Help = HelpWindow();
            m_Panel.Add(m_Help);

            m_Root.Add(m_Panel);

            ShowTheAttacker();

            // The frame after it exists, so it can ease in from the state the stylesheet starts it in.
            var panel = m_Panel;
            panel.schedule.Execute(() => panel.AddToClassList("clash-prompt--in"));

            Refresh();
        }

        /// <summary>
        /// Opens the inspector on whoever is attacking, for as long as the question is up.
        ///
        /// This used to be a sentence listing the skills the attacker had been watched using. The
        /// card says that and everything else about them, in the place a player already knows to
        /// look, and it does not have to be kept in step with what the card decides is public.
        ///
        /// Whatever was being looked at before comes back when the question goes, so a player who
        /// had a card open for their own reasons does not lose it to an interruption.
        /// </summary>
        void ShowTheAttacker()
        {
            var selection = m_Input.Selection;
            var attacker = Attacker;

            if (selection == null || attacker == null)
            {
                return;
            }

            m_Restore = selection.Selected;
            m_Interrupted = true;
            selection.Select(attacker);
        }

        void PutTheCardBack()
        {
            if (!m_Interrupted)
            {
                return;
            }

            m_Interrupted = false;
            m_Input.Selection?.Select(m_Restore);
            m_Restore = null;
        }

        /// <summary>The corner button that opens the rules beside the panel.</summary>
        Button HelpButton()
        {
            var help = new Button(() => m_Help?.ToggleInClassList("is-hidden"))
            {
                text = "?"
            };

            help.AddToClassList("clash-prompt__help");
            help.tooltip = "What a win, a tie and a loss cost, and what beats what.";

            return help;
        }

        /// <summary>
        /// The rules, beside the panel rather than inside it.
        ///
        /// Off to the left, because the inspector is on the right and the board is behind. Shut
        /// unless asked for: a player who knows the table does not need three sentences and a
        /// seven-column chart between them and the eight buttons, every single clash.
        /// </summary>
        VisualElement HelpWindow()
        {
            var window = new VisualElement();
            window.AddToClassList("clash-help");
            window.AddToClassList("is-hidden");

            var stakes = new Label(ClashLabels.Stakes);
            stakes.AddToClassList("clash-help__stakes");
            window.Add(stakes);

            var chart = new VisualElement();
            chart.AddToClassList("clash-help__chart");
            ElementChart.Build(chart);
            window.Add(chart);

            return window;
        }

        void Refresh()
        {
            m_Options.Clear();

            foreach (var element in ElementInfo.All)
            {
                m_Options.Add(ElementOption(element));
            }

            m_Options.Add(DeclineOption());

            m_Tally.text = m_Staged.Count == 0
                ? $"Pick {m_Request.Required}."
                : $"{m_Staged.Count} of {m_Request.Required}. Click one again to put it back.";
        }

        /// <summary>How answering with this element is expected to go, against what is known.</summary>
        ClashOdds OddsFor(Element element)
        {
            if (m_Request.HasTelegraph)
            {
                return CreatureKnowledge.ForecastDefenceAgainst(element, m_Request.Telegraphed);
            }

            var attacker = Attacker;
            return attacker != null
                ? CreatureKnowledge.ForecastDefence(element, attacker)
                : ClashOdds.Even;
        }

        VisualElement ElementOption(Element element)
        {
            var left = Held(element);
            var staged = 0;

            foreach (var chosen in m_Staged)
            {
                if (chosen == element)
                {
                    staged++;
                }
            }

            var button = new Button(() => Toggle(element));
            button.AddToClassList("clash-option");
            button.EnableInClassList("clash-option--staged", staged > 0);
            button.EnableInClassList("clash-option--empty", left + staged <= 0);
            button.text = string.Empty;
            button.tooltip = ElementLore.Describe(element);

            var mark = new VisualElement();
            mark.AddToClassList("clash-option__mark");
            CharacterSheet.PaintElement(mark, element);
            button.Add(mark);

            // What you hold and what putting one up would leave you holding. No name: the rune is
            // the name, and the number is the thing actually being weighed.
            button.Add(Count(left, left > 0 ? left - 1 : 0));

            // Only an element the defender holds none of is off the table.
            button.SetEnabled(left + staged > 0);

            return Answer(button, ClashLabels.Chances(OddsFor(element)),
                "Win: the attack misses and this element comes back. Tie: it misses, but the "
                + "element is spent. Lose: you take the hit and it is spent.");
        }

        /// <summary>
        /// The eighth answer: nothing. It always loses, and it costs nothing, and those two facts
        /// are on it in the same place the other seven carry theirs.
        /// </summary>
        VisualElement DeclineOption()
        {
            var button = new Button(ToggleDecline);
            button.AddToClassList("clash-option");
            button.AddToClassList("clash-option--decline");
            button.EnableInClassList("clash-option--staged", m_Declined);
            button.text = string.Empty;
            button.tooltip = "Put nothing up. The attack lands as it is, and you spend nothing.";

            var mark = new VisualElement();
            mark.AddToClassList("clash-option__mark");
            mark.AddToClassList("clash-option__mark--none");

            var bar = new VisualElement();
            bar.AddToClassList("clash-option__mark-bar");
            mark.Add(bar);

            button.Add(mark);

            // Nothing goes up and nothing comes off the hand, which is the whole of its appeal.
            button.Add(Count(0, 0));

            return Answer(button, ClashLabels.Chances(ClashOdds.CertainLoss),
                "You take the hit. Nothing is spent.");
        }

        /// <summary>What you hold, and what you would hold having put one up.</summary>
        static Label Count(int now, int after)
        {
            var count = new Label($"{now} ({after})");
            count.AddToClassList("clash-option__count");
            return count;
        }

        /// <summary>
        /// One answer: the button, and its odds under it.
        ///
        /// Under rather than inside, so the button is the rune and the number it costs -- the two
        /// things a click decides -- and the forecast sits below where it can be read across the
        /// row without the eye stepping in and out of eight boxes.
        /// </summary>
        static VisualElement Answer(VisualElement button, string chances, string explain)
        {
            var cell = new VisualElement();
            cell.AddToClassList("clash-answer");
            cell.Add(button);

            var odds = new Label(chances);
            odds.AddToClassList("clash-option__odds");
            odds.tooltip = explain;
            cell.Add(odds);

            return cell;
        }

        /// <summary>
        /// Picks an element. The click is the answer.
        ///
        /// One element asked for: it goes the moment it is clicked. A confirm button after a
        /// single choice is a second click that decides nothing, and the panel is up in the
        /// middle of somebody else's turn -- the less of it the better. Two asked for: the first
        /// is staged and the second sends both; clicking the staged one again puts it back.
        /// </summary>
        void Toggle(Element element)
        {
            if (!m_Guard.Accepts)
            {
                return;
            }

            m_Declined = false;

            if (Held(element) <= 0 && !m_Staged.Contains(element))
            {
                return;
            }

            if (m_Staged.Contains(element) && m_Staged.Count < m_Request.Required)
            {
                // A second click on a staged element takes it back, unless it is a second copy
                // being staged of something held twice -- which is what the held count says.
                if (Held(element) <= 0)
                {
                    m_Staged.Remove(element);
                    Refresh();
                    return;
                }
            }

            m_Staged.Add(element);

            if (m_Staged.Count >= m_Request.Required)
            {
                Send(m_Staged);
                return;
            }

            Refresh();
        }

        /// <summary>The eighth answer goes the moment it is clicked, like the other seven.</summary>
        void ToggleDecline()
        {
            if (m_Guard.Accepts)
            {
                Send(System.Array.Empty<Element>());
            }
        }

        /// <summary>
        /// Sends the answer, and takes the prompt down without waiting to be told.
        ///
        /// The server decides whether it stands, and will say so by closing the clash -- but a
        /// panel that lingers after a click reads as a click that did not land, and a second click
        /// on it would be a second answer.
        /// </summary>
        void Send(IReadOnlyList<Element> answer)
        {
            ClashCommands.Current?.Answer(answer);
            Close();
        }
    }
}

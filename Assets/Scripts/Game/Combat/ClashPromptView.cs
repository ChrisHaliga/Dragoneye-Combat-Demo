using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
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
        Label m_Tally;

        DefenceRequest m_Request;
        bool m_Open;
        bool m_Declined;

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
            Build();
            m_Guard.Open(m_Panel);
            m_Open = true;
        }

        void Close()
        {
            m_Panel?.RemoveFromHierarchy();
            m_Panel = null;
            m_Open = false;
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
            var pool = defender != null ? defender.GetComponent<CreaturePool>() : null;

            var held = pool != null && pool.CanSee
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

            var title = new Label("Answer the attack");
            title.AddToClassList("clash-prompt__title");
            m_Panel.Add(title);

            var reason = new Label(ClashLabels.Describe(m_Request));
            reason.AddToClassList("clash-prompt__reason");
            m_Panel.Add(reason);

            m_Panel.Add(Intelligence());

            var key = new Label(ClashLabels.OddsKey);
            key.AddToClassList("clash-prompt__key");
            m_Panel.Add(key);

            m_Options = new VisualElement();
            m_Options.AddToClassList("clash-prompt__options");
            m_Panel.Add(m_Options);

            // Only a pair needs a second line: it has to say that one is chosen and one is
            // still wanted. A single answer needs nothing under the row, because the click is it.
            m_Tally = new Label();
            m_Tally.AddToClassList("clash-prompt__tally");
            m_Tally.EnableInClassList("is-hidden", m_Request.Required <= 1);
            m_Panel.Add(m_Tally);

            m_Root.Add(m_Panel);

            // The frame after it exists, so it can ease in from the state the stylesheet starts it in.
            var panel = m_Panel;
            panel.schedule.Execute(() => panel.AddToClassList("clash-prompt--in"));

            Refresh();
        }

        /// <summary>
        /// What is known about the attack, in one line.
        ///
        /// A swing at somebody walking past is always the swinger's weapon, and once the weapon
        /// has been seen its element is known -- so the line says so, and the odds below are
        /// worked out against that one element rather than the whole hand. Otherwise it lists
        /// the skills this creature has been watched using, which is what a player would be
        /// counting on their fingers anyway.
        /// </summary>
        VisualElement Intelligence()
        {
            var line = new Label();
            line.AddToClassList("clash-prompt__intel");

            var attacker = Attacker;

            if (m_Request.HasTelegraph)
            {
                var rune = CombatLogLines.Rune(m_Request.Telegraphed);
                line.text = $"They are swinging with their weapon, which you have seen: it "
                    + $"arrives as {rune}.";
                line.AddToClassList("clash-prompt__intel--known");
                return line;
            }

            var seen = new List<string>();
            var commands = attacker != null ? attacker.GetComponent<SkillCommands>() : null;
            var catalog = SkillCatalog.Current;

            if (commands != null && catalog != null)
            {
                foreach (var id in commands.SeenSkillIds)
                {
                    if (catalog.TryGetSkill(id, out var skill) && skill.IsContested
                        && skill.ElementCost > 0)
                    {
                        seen.Add($"{skill.Name} ({CombatLogLines.Rune(skill.Element)})");
                    }
                }
            }

            line.text = seen.Count > 0
                ? "Seen attacking with: " + string.Join(", ", seen) + "."
                : "You have not seen this creature attack yet.";

            return line;
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
                ? $"Choose {m_Request.Required} elements. The first is kept until the second is picked."
                : $"{m_Staged.Count} of {m_Request.Required} chosen. Pick the other, or click "
                    + "the first again to put it back.";
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

            var name = new Label(ElementInfo.ShortNameOf(element));
            name.AddToClassList("clash-option__name");
            button.Add(name);

            var count = new Label(staged > 0
                ? $"{staged} of {left + staged} chosen"
                : left > 0 ? $"{left} held" : "none held");
            count.AddToClassList("clash-option__count");
            button.Add(count);

            var chances = new Label(ClashLabels.Chances(OddsFor(element)));
            chances.AddToClassList("clash-option__odds");
            chances.tooltip = "Win: the attack misses and this element comes back. Tie: it "
                + "misses, but the element is spent. Lose: you take the hit and it is spent.";
            button.Add(chances);

            // Only an element the defender holds none of is off the table.
            button.SetEnabled(left + staged > 0);

            return button;
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

            var name = new Label("NONE");
            name.AddToClassList("clash-option__name");
            button.Add(name);

            var count = new Label("costs nothing");
            count.AddToClassList("clash-option__count");
            button.Add(count);

            var chances = new Label(ClashLabels.Chances(ClashOdds.CertainLoss));
            chances.AddToClassList("clash-option__odds");
            chances.tooltip = "You take the hit. Nothing is spent.";
            button.Add(chances);

            return button;
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

using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The question put to a defender, and the only place they can answer it.
    ///
    /// It shows what they hold and nothing about what is coming, because that is all it is given:
    /// a <see cref="DefenceRequest"/> has no room for the attacker's skill or element, so there is
    /// nothing here to be careful about withholding.
    ///
    /// The counts beside each rune come from the defender's own pool, which they are entitled to
    /// see and nobody else is. That is what lets them spend the same element twice when they hold
    /// two of it -- the request lists what they may answer with, once each, and how much of it
    /// there is is their own business.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class ClashPromptView : MonoBehaviour
    {
        [SerializeField, Tooltip("The board input, which owns the creature registry.")]
        BoardActionInput m_Input;

        VisualElement m_Root;
        VisualElement m_Panel;
        Label m_Title;
        Label m_Reason;
        VisualElement m_Options;
        Label m_Tally;
        Button m_Answer;
        Button m_Decline;

        DefenceRequest m_Request;
        bool m_Open;

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

        void OnAsked(DefenceRequest request)
        {
            m_Request = request;
            m_Staged.Clear();

            Close();
            Build();
            m_Open = true;
        }

        void Close()
        {
            m_Panel?.RemoveFromHierarchy();
            m_Panel = null;
            m_Open = false;
        }

        /// <summary>
        /// How much of an element this creature still has to put up.
        ///
        /// Read from the defender's own pool rather than carried on the request, because the count
        /// is theirs and the request goes over a wire. Falls back to one apiece if the creature
        /// cannot be found, so a prompt is never dead -- the sequence refuses anything unpayable
        /// anyway, which is the check that counts.
        /// </summary>
        int Held(Element element)
        {
            var creature = m_Input.Creatures != null
                ? m_Input.Creatures.ByTurnId((uint)m_Request.DefenderId)
                : null;

            var pool = creature != null ? creature.GetComponent<CreaturePool>() : null;
            var held = pool != null && pool.CanSee ? pool.Pool[element] : 1;

            var staged = 0;

            foreach (var element_ in m_Staged)
            {
                if (element_ == element)
                {
                    staged++;
                }
            }

            return held - staged;
        }

        void Build()
        {
            m_Panel = new VisualElement();
            m_Panel.AddToClassList("clash-prompt");

            m_Title = new Label("Answer the attack");
            m_Title.AddToClassList("clash-prompt__title");
            m_Panel.Add(m_Title);

            m_Reason = new Label(ClashLabels.Describe(m_Request));
            m_Reason.AddToClassList("clash-prompt__reason");
            m_Panel.Add(m_Reason);

            var key = new Label("Stopped / halved / through");
            key.AddToClassList("clash-prompt__key");
            m_Panel.Add(key);

            m_Options = new VisualElement();
            m_Options.AddToClassList("clash-prompt__options");
            m_Panel.Add(m_Options);

            m_Tally = new Label();
            m_Tally.AddToClassList("clash-prompt__tally");
            m_Panel.Add(m_Tally);

            var actions = new VisualElement();
            actions.AddToClassList("clash-prompt__actions");

            m_Answer = new Button(OnAnswerClicked) { text = "Answer" };
            m_Answer.AddToClassList("button");
            m_Answer.AddToClassList("button--primary");
            actions.Add(m_Answer);

            m_Decline = new Button(OnDeclineClicked) { text = "Take it" };
            m_Decline.AddToClassList("button");
            m_Decline.tooltip = "Spend nothing, and let the attack land as it is.";
            actions.Add(m_Decline);

            m_Panel.Add(actions);
            m_Root.Add(m_Panel);

            Refresh();
        }

        void Refresh()
        {
            m_Options.Clear();

            foreach (var element in m_Request.Options)
            {
                m_Options.Add(BuildOption(element));
            }

            m_Tally.text = m_Request.Required > 1
                ? $"{m_Staged.Count} of {m_Request.Required} committed"
                : m_Staged.Count > 0 ? "Committed" : "Choose one";

            m_Answer.SetEnabled(m_Staged.Count > 0);
        }

        VisualElement BuildOption(Element element)
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

            var button = new Button();
            button.AddToClassList("clash-option");
            button.EnableInClassList("clash-option--staged", staged > 0);
            button.text = string.Empty;

            var mark = new VisualElement();
            mark.AddToClassList("clash-option__mark");
            CharacterSheet.PaintElement(mark, element);
            button.Add(mark);

            var name = new Label(ElementInfo.ShortNameOf(element));
            name.AddToClassList("clash-option__name");
            button.Add(name);

            var count = new Label(staged > 0 ? $"{staged} of {left + staged}" : $"{left} held");
            count.AddToClassList("clash-option__count");
            button.Add(count);

            // What this answer is worth, so nobody has to hold the matchup table in their head to
            // play well. Built from what the attacker has been proven to hold and which elements
            // their seen skills could arrive as -- all of it public, none of it their commitment.
            var attacker = m_Input.Creatures != null
                ? m_Input.Creatures.ByTurnId((uint)m_Request.AttackerId)
                : null;

            if (attacker != null)
            {
                var odds = CreatureKnowledge.ForecastDefence(element, attacker);

                var chances = new Label(ClashLabels.Chances(odds));
                chances.AddToClassList("clash-option__odds");
                chances.tooltip = "Chance this answer stops the attack, halves it, or lets it "
                    + "through -- worked out from what this attacker has been seen holding.";
                button.Add(chances);
            }

            // Full, or none of this left. Either way there is nothing to add.
            button.SetEnabled(m_Staged.Count < m_Request.Required && left > 0);
            button.clicked += () =>
            {
                m_Staged.Add(element);
                Refresh();
            };

            return button;
        }

        /// <summary>
        /// Sends the answer, and takes the prompt down without waiting to be told.
        ///
        /// The server decides whether it stands, and will say so by closing the clash -- but a
        /// panel that lingers after a click reads as a click that did not land, and a second click
        /// on it would be a second answer.
        /// </summary>
        void OnAnswerClicked()
        {
            ClashCommands.Current?.Answer(m_Staged);
            Close();
        }

        void OnDeclineClicked()
        {
            ClashCommands.Current?.Answer(System.Array.Empty<Element>());
            Close();
        }

        /// <summary>Whether a decision is on screen. The board stands aside while it is.</summary>
        public bool IsOpen => m_Open;
    }
}

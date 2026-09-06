using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The offer of a swing at somebody walking out from under your nose.
    ///
    /// The mirror of <see cref="ClashPromptView"/> and deliberately built to look like it: the same
    /// panel, the same row of runes, the same three numbers under each. What differs is which end
    /// of the exchange you are on. A defence prompt asks what you will put up against something
    /// already committed; this asks whether to commit at all, and every option costs an element you
    /// will not get back.
    ///
    /// So declining is a first-class answer and not a way out of the dialog. It is spelled out on
    /// its own button, because a swing that is worse than even hands the mover a free look at your
    /// hand -- and a prompt that only offered ways to say yes would be selling one.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class OpportunityPromptView : MonoBehaviour
    {
        [SerializeField, Tooltip("The board input, which owns the creature registry.")]
        BoardActionInput m_Input;

        VisualElement m_Root;
        VisualElement m_Panel;

        OpportunityOffer m_Offer;

        void Start()
        {
            if (m_Input == null)
            {
                Debug.LogError($"{nameof(OpportunityPromptView)} has no board input.", this);
                enabled = false;
                return;
            }

            var document = GetComponent<UIDocument>().rootVisualElement;

            // The template root, not the document root: the stylesheet is attached inside it.
            m_Root = document.Q<VisualElement>("root") ?? document;

            OpportunityCommands.Offered += OnOffered;
            OpportunityCommands.Closed += Close;
        }

        void OnDestroy()
        {
            OpportunityCommands.Offered -= OnOffered;
            OpportunityCommands.Closed -= Close;
        }

        void OnOffered(OpportunityOffer offer)
        {
            m_Offer = offer;
            Close();
            Build();
        }

        void Close()
        {
            m_Panel?.RemoveFromHierarchy();
            m_Panel = null;
        }

        CreatureState CreatureFor(uint turnId) =>
            m_Input.Creatures != null ? m_Input.Creatures.ByTurnId(turnId) : null;

        void Build()
        {
            var watcher = CreatureFor(m_Offer.WatcherId);
            var mover = CreatureFor(m_Offer.MoverId);

            m_Panel = new VisualElement();
            m_Panel.AddToClassList("clash-prompt");
            m_Panel.AddToClassList("clash-prompt--opportunity");

            var title = new Label(watcher != null
                ? $"{watcher.DisplayName} can swing"
                : "Opportunity attack");

            title.AddToClassList("clash-prompt__title");
            m_Panel.Add(title);

            var reason = new Label(mover != null
                ? $"{mover.DisplayName} is moving out from under your nose. Spend an element to "
                    + $"swing for {Opportunity.Damage}, or let them go."
                : "Spend an element to swing, or let them go.");

            reason.AddToClassList("clash-prompt__reason");
            m_Panel.Add(reason);

            var options = new VisualElement();
            options.AddToClassList("clash-prompt__options");

            foreach (var element in m_Offer.Options)
            {
                options.Add(Option(element, mover));
            }

            m_Panel.Add(options);

            var key = new Label(ClashLabels.OddsKey);
            key.AddToClassList("clash-prompt__key");
            m_Panel.Add(key);

            var actions = new VisualElement();
            actions.AddToClassList("clash-prompt__actions");

            var decline = new Button(() => Answer(null)) { text = "Let them go" };
            decline.AddToClassList("button");
            actions.Add(decline);

            m_Panel.Add(actions);

            m_Root.Add(m_Panel);
        }

        /// <summary>
        /// One element, with what swinging with it is expected to come to.
        ///
        /// The odds are the attacker's this time, not the defender's, and they are worked out from
        /// exactly what a player could count for themselves: what the mover has been proven to hold
        /// and how much of it is spent.
        /// </summary>
        VisualElement Option(Element element, CreatureState mover)
        {
            var button = new Button(() => Answer(element));
            button.AddToClassList("clash-option");
            button.text = string.Empty;
            button.tooltip = ElementLore.Describe(element);

            var mark = new VisualElement();
            mark.AddToClassList("clash-option__mark");
            CharacterSheet.PaintElement(mark, element);
            button.Add(mark);

            var name = new Label(ElementInfo.ShortNameOf(element));
            name.AddToClassList("clash-option__name");
            button.Add(name);

            if (mover != null)
            {
                var chances = new Label(ClashLabels.Chances(
                    CreatureKnowledge.Forecast(element, mover)));

                chances.AddToClassList("clash-option__odds");
                chances.tooltip = "Win: the swing lands. Tie or lose: it does not, and the element "
                    + "is spent either way.";

                button.Add(chances);
            }

            return button;
        }

        void Answer(Element? element)
        {
            OpportunityCommands.Current?.Answer(element);
            Close();
        }
    }
}

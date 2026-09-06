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
    /// One attack and two answers. The swing is whatever the creature is already carrying, so there
    /// is nothing to choose and nothing to weigh but the odds -- which are shown, because the whole
    /// decision is whether an element is worth a chance at that.
    ///
    /// It interrupts somebody else's turn, which is the reason it says so much in so few words: a
    /// panel that appears when a player is not expecting one has to answer "why am I looking at
    /// this" before it asks anything.
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

        // The press that sent the mover's order can be the press this opens under, on a host
        // that runs both sides. It must not also be the answer.
        readonly PromptGuard m_Guard = new PromptGuard();

        void OnOffered(OpportunityOffer offer)
        {
            m_Offer = offer;
            Close();
            Build();

            if (m_Panel != null)
            {
                m_Guard.Open(m_Panel);
            }
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
            var swing = m_Offer.Swing;

            if (swing == null)
            {
                return;
            }

            var watcher = CreatureFor(m_Offer.WatcherId);
            var mover = CreatureFor(m_Offer.MoverId);

            m_Panel = new VisualElement();
            m_Panel.AddToClassList("clash-prompt");
            m_Panel.AddToClassList("clash-prompt--opportunity");

            var title = new Label(mover != null
                ? $"{mover.DisplayName} is getting past you"
                : "Opportunity attack");

            title.AddToClassList("clash-prompt__title");
            m_Panel.Add(title);

            var reason = new Label(watcher != null
                ? $"{watcher.DisplayName} can swing for free as they go. It costs the element and "
                    + "nothing else, and they answer it as they would any attack."
                : "A free swing as they go. It costs the element and nothing else.");

            reason.AddToClassList("clash-prompt__reason");
            m_Panel.Add(reason);

            var options = new VisualElement();
            options.AddToClassList("clash-prompt__options");
            options.Add(Attack(swing, mover));
            m_Panel.Add(options);

            var key = new Label(ClashLabels.OddsKey);
            key.AddToClassList("clash-prompt__key");
            m_Panel.Add(key);

            var actions = new VisualElement();
            actions.AddToClassList("clash-prompt__actions");

            var take = new Button(() => Answer(true)) { text = "Swing" };
            take.AddToClassList("button");
            take.AddToClassList("button--primary");
            actions.Add(take);

            var decline = new Button(() => Answer(false)) { text = "Let them go" };
            decline.AddToClassList("button");
            actions.Add(decline);

            m_Panel.Add(actions);
            m_Root.Add(m_Panel);

            // The frame after it exists, so it can ease in from the state the stylesheet starts it in.
            var panel = m_Panel;
            panel.schedule.Execute(() => panel.AddToClassList("clash-prompt--in"));
        }

        /// <summary>
        /// The one attack on offer: what it is made of, what it costs, and how it is likely to go.
        ///
        /// Drawn as an option rather than a sentence so it reads like the defence prompt beside it,
        /// but it is not a button -- there is nothing here to pick. The two answers are below.
        /// </summary>
        VisualElement Attack(SkillSpec swing, CreatureState mover)
        {
            var row = new VisualElement();
            row.AddToClassList("clash-option");
            row.AddToClassList("clash-option--fixed");
            row.tooltip = ElementLore.Describe(swing.Element);

            var mark = new VisualElement();
            mark.AddToClassList("clash-option__mark");
            CharacterSheet.PaintElement(mark, swing.Element);
            row.Add(mark);

            var name = new Label(ElementInfo.ShortNameOf(swing.Element));
            name.AddToClassList("clash-option__name");
            row.Add(name);

            var cost = new Label($"{swing.ElementCost} for {swing.Effect.Amount}");
            cost.AddToClassList("clash-option__count");
            cost.tooltip = $"Costs {swing.ElementCost} of this element. Does "
                + $"{swing.Effect.Amount} damage if it gets through.";

            row.Add(cost);

            if (mover != null)
            {
                var chances = new Label(ClashLabels.Chances(
                    CreatureKnowledge.Forecast(swing.Element, mover)));

                chances.AddToClassList("clash-option__odds");
                chances.tooltip = "Win: the swing lands. Tie or lose: it does not, and the element "
                    + "is spent either way.";

                row.Add(chances);
            }

            return row;
        }

        void Answer(bool swings)
        {
            if (!m_Guard.Accepts)
            {
                return;
            }

            OpportunityCommands.Current?.Answer(swings);
            Close();
        }
    }
}

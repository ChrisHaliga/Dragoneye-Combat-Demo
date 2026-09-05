using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>What happened in a clash, once everybody is allowed to know.</summary>
    public readonly struct ClashReport
    {
        public readonly uint AttackerId;
        public readonly uint DefenderId;
        public readonly int SkillId;
        public readonly IReadOnlyList<Element> Attacker;
        public readonly IReadOnlyList<Element> Defender;
        public readonly ClashOutcome Outcome;

        public ClashReport(uint attackerId, uint defenderId, int skillId,
            IReadOnlyList<Element> attacker, IReadOnlyList<Element> defender, ClashOutcome outcome)
        {
            AttackerId = attackerId;
            DefenderId = defenderId;
            SkillId = skillId;
            Attacker = attacker ?? System.Array.Empty<Element>();
            Defender = defender ?? System.Array.Empty<Element>();
            Outcome = outcome;
        }
    }

    /// <summary>
    /// The exchange, in one line, where everybody can read it.
    ///
    /// This replaced three separate numbers floating off two different heads at the same moment.
    /// A clash is one event with two halves and an answer, and splitting it across the board meant
    /// reading three things in three places in the second before they faded -- which came to
    /// "something happened and I do not know what", especially against a computer creature whose
    /// answer was the thing you most wanted to see.
    ///
    /// The damage itself still rises off the defender, where it belongs: that is a fact about them,
    /// and it is announced separately for its own reasons.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class ClashResultView : MonoBehaviour
    {
        [SerializeField, Min(0.5f), Tooltip("Seconds the exchange stays up before it fades.")]
        float m_Dwell = 3.5f;

        [SerializeField, Min(0.1f), Tooltip("Seconds it takes to fade once the dwell is over.")]
        float m_Fade = 0.6f;

        VisualElement m_Root;
        VisualElement m_Strip;
        float m_Shown;

        void Start()
        {
            var document = GetComponent<UIDocument>().rootVisualElement;

            // The template root, not the document root: the stylesheet is attached inside it.
            m_Root = document.Q<VisualElement>("root") ?? document;

            ClashCommands.Resolved += OnResolved;
        }

        void OnDestroy() => ClashCommands.Resolved -= OnResolved;

        void Update()
        {
            if (m_Strip == null)
            {
                return;
            }

            m_Shown += Time.unscaledDeltaTime;

            if (m_Shown < m_Dwell)
            {
                return;
            }

            var gone = (m_Shown - m_Dwell) / m_Fade;

            if (gone >= 1f)
            {
                Clear();
                return;
            }

            m_Strip.style.opacity = 1f - gone;
        }

        void OnResolved(ClashReport report)
        {
            Clear();

            m_Strip = new VisualElement();
            m_Strip.AddToClassList("clash-result");
            m_Strip.pickingMode = PickingMode.Ignore;

            var skill = SkillCatalog.Current != null
                && SkillCatalog.Current.TryGetSkill(report.SkillId, out var spec)
                ? spec.Name
                : "Attack";

            var title = new Label(skill.ToUpperInvariant());
            title.AddToClassList("clash-result__skill");
            m_Strip.Add(title);

            var exchange = new VisualElement();
            exchange.AddToClassList("clash-result__exchange");

            exchange.Add(Side(report.Attacker, "clash-result__attacker"));

            var versus = new Label("vs");
            versus.AddToClassList("clash-result__versus");
            exchange.Add(versus);

            exchange.Add(Side(report.Defender, "clash-result__defender"));

            m_Strip.Add(exchange);

            var outcome = new Label(ClashLabels.Describe(report.Outcome));
            outcome.AddToClassList("clash-result__outcome");
            outcome.EnableInClassList("clash-result__outcome--held",
                report.Outcome != ClashOutcome.AttackerWins);
            m_Strip.Add(outcome);

            m_Root.Add(m_Strip);
            m_Shown = 0f;
        }

        /// <summary>One side's commitment: its runes, or the fact that there were none.</summary>
        static VisualElement Side(IReadOnlyList<Element> elements, string className)
        {
            var side = new VisualElement();
            side.AddToClassList("clash-result__side");
            side.AddToClassList(className);

            if (elements.Count == 0)
            {
                var none = new Label("no answer");
                none.AddToClassList("clash-result__none");
                side.Add(none);
                return side;
            }

            foreach (var element in elements)
            {
                var mark = new VisualElement();
                mark.AddToClassList("clash-result__mark");
                CharacterSheet.PaintElement(mark, element);
                mark.tooltip = ElementInfo.NameOf(element);
                side.Add(mark);
            }

            return side;
        }

        void Clear()
        {
            m_Strip?.RemoveFromHierarchy();
            m_Strip = null;
            m_Shown = 0f;
        }
    }
}

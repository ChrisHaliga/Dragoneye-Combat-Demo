using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Multiplayer;
using Dragoneye.Sim;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.UI;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// The exchange, in one line, where everybody can read it.
    ///
    /// This replaced three separate numbers floating off two different heads at the same moment.
    /// A clash is one event with two halves and an answer, and splitting it across the board meant
    /// reading three things in three places in the second before they faded.
    ///
    /// Shown when the clash is shown to resolve -- after the swing has been seen and before the
    /// blow lands -- which is the beat the playback leaves for it. The damage itself still rises
    /// off the defender, where it belongs.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class ClashResultView : MonoBehaviour
    {
        [SerializeField, Tooltip("The board input, which owns the creature registry.")]
        BoardActionInput m_Input;

        [SerializeField, Min(0.5f), Tooltip("Seconds the exchange stays up before it fades.")]
        float m_Dwell = 3.5f;

        [SerializeField, Min(0.1f), Tooltip("Seconds it takes to fade once the dwell is over.")]
        float m_Fade = 0.6f;

        VisualElement m_Root;
        VisualElement m_Strip;
        float m_Shown;
        CombatPlayback m_Playback;

        void Start()
        {
            var document = GetComponent<UIDocument>().rootVisualElement;

            // The template root, not the document root: the stylesheet is attached inside it.
            m_Root = document.Q<VisualElement>("root") ?? document;
        }

        void OnDestroy()
        {
            if (m_Playback != null)
            {
                m_Playback.Presenting -= OnPresenting;
            }
        }

        void Update()
        {
            if (m_Playback != CombatPlayback.Current)
            {
                if (m_Playback != null)
                {
                    m_Playback.Presenting -= OnPresenting;
                }

                m_Playback = CombatPlayback.Current;

                if (m_Playback != null)
                {
                    m_Playback.Presenting += OnPresenting;
                }
            }

            if (m_Strip == null)
            {
                return;
            }

            m_Shown += Time.unscaledDeltaTime * (m_Playback != null ? m_Playback.Speed : 1f);

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

        void OnPresenting(CombatEvent e)
        {
            if (e.Kind != CombatEventKind.ClashResolved)
            {
                return;
            }

            Clear();

            m_Strip = new VisualElement();
            m_Strip.AddToClassList("clash-result");
            m_Strip.pickingMode = PickingMode.Ignore;

            // A swing belongs to no catalogue, so it names itself.
            var skill = e.Skill == Opportunity.SkillId
                ? Opportunity.Name
                : SkillCatalog.Current != null
                    && SkillCatalog.Current.TryGetSkill(e.Skill, out var spec)
                    ? spec.Name
                    : "Attack";

            var title = new Label(skill.ToUpperInvariant());
            title.AddToClassList("clash-result__skill");
            m_Strip.Add(title);

            var exchange = new VisualElement();
            exchange.AddToClassList("clash-result__exchange");

            exchange.Add(Side(e.Elements, "clash-result__attacker"));

            var versus = new Label("vs");
            versus.AddToClassList("clash-result__versus");
            exchange.Add(versus);

            exchange.Add(Side(e.Answer, "clash-result__defender"));

            m_Strip.Add(exchange);

            // The words say what happened to the attack; the colour says whose news that is.
            var outcome = new Label(ClashLabels.Describe(e.Outcome));
            outcome.AddToClassList("clash-result__outcome");
            outcome.style.color = Tint(ClashLabels.ColourOf(e.Outcome, SideOf(e)));
            m_Strip.Add(outcome);

            m_Root.Add(m_Strip);
            m_Shown = 0f;

            var strip = m_Strip;
            strip.schedule.Execute(() => strip.AddToClassList("clash-result--in"));
        }

        /// <summary>
        /// Which end of this exchange the local player's side is on.
        ///
        /// The defender first, for the one case where both are on it: an attack landing on your
        /// own side is bad news whoever threw it.
        /// </summary>
        LogSide SideOf(CombatEvent e)
        {
            var side = LocalPlayer.Side();
            var creatures = m_Input != null ? m_Input.Creatures : null;

            if (!side.HasValue || creatures == null)
            {
                return LogSide.Neither;
            }

            var defender = creatures.ByTurnId(e.Target);

            if (defender != null && defender.Party == side.Value)
            {
                return LogSide.Defender;
            }

            var attacker = creatures.ByTurnId(e.Actor);

            return attacker != null && attacker.Party == side.Value
                ? LogSide.Attacker
                : LogSide.Neither;
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
                mark.tooltip = ElementLore.Describe(element);
                side.Add(mark);
            }

            return side;
        }

        /// <summary>A hex colour from the label palette, as a style colour.</summary>
        static Color Tint(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out var colour) ? colour : Color.white;

        void Clear()
        {
            m_Strip?.RemoveFromHierarchy();
            m_Strip = null;
            m_Shown = 0f;
        }
    }
}

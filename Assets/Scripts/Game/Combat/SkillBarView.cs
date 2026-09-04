using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The skills the active creature can use, and why the others cannot be.
    ///
    /// Availability comes from <see cref="SkillRules"/> -- the same check the server runs when the
    /// skill arrives, so a button that is enabled here is a button the host will honour. DE-002 asks
    /// for exactly that, and for the reason to be available rather than implied by a grey box.
    ///
    /// Selecting a skill arms it; the next board click aims it. That is why the selection lives
    /// here rather than in the input component: the bar is what the player pressed, and the input
    /// asks it what is armed.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class SkillBarView : MonoBehaviour
    {
        [SerializeField]
        BoardActionInput m_Input;

        VisualElement m_Bar;
        Label m_Reason;

        int m_Selected = NoSkill;

        /// <summary>No skill armed; a board click means move or attack.</summary>
        public const int NoSkill = 0;

        /// <summary>The skill the next board click will use, or <see cref="NoSkill"/>.</summary>
        public int SelectedSkill => m_Selected;

        /// <summary>Disarms, after a skill has been used or the turn has passed.</summary>
        public void ClearSelection() => m_Selected = NoSkill;

        void Start()
        {
            if (m_Input == null)
            {
                Debug.LogError($"{nameof(SkillBarView)} has no board input.", this);
                enabled = false;
                return;
            }

            var root = GetComponent<UIDocument>().rootVisualElement;

            m_Bar = root.Q<VisualElement>("skill-bar");
            m_Reason = root.Q<Label>("skill-reason");

            if (m_Bar == null || m_Reason == null)
            {
                Debug.LogError($"{nameof(SkillBarView)} could not find its elements; "
                    + "check ArenaHud.uxml.", this);
                enabled = false;
            }
        }

        // Rebuilt every frame the actor or its resources change. A skill becomes unusable the moment
        // AP is spent, and a bar that only repainted on turn change would keep offering it.
        void Update()
        {
            if (m_Bar == null)
            {
                return;
            }

            var actor = m_Input.Actor;

            if (actor == null)
            {
                m_Bar.Clear();
                m_Reason.text = string.Empty;
                m_Selected = NoSkill;
                return;
            }

            Rebuild(actor);
        }

        void Rebuild(CreatureState actor)
        {
            var commands = actor.GetComponent<SkillCommands>();
            var pool = actor.GetComponent<CreaturePool>();

            m_Bar.Clear();

            if (commands == null || pool == null)
            {
                return;
            }

            var ledger = pool.Ledger;
            var worstReason = SkillRefusal.None;

            foreach (var skill in commands.Skills)
            {
                var refusal = SkillRules.CheckAffordable(skill, true, actor.CurrentAp, ledger);

                if (skill.Id == m_Selected && refusal != SkillRefusal.None)
                {
                    // Something changed under the player -- AP spent, an element gone. Disarm rather
                    // than leave a skill armed that the next click would have refused.
                    m_Selected = NoSkill;
                }

                if (refusal != SkillRefusal.None && worstReason == SkillRefusal.None)
                {
                    worstReason = refusal;
                }

                m_Bar.Add(BuildButton(skill, refusal));
            }

            m_Reason.text = m_Selected == NoSkill && worstReason != SkillRefusal.None
                ? SkillLabels.Describe(worstReason)
                : string.Empty;
        }

        VisualElement BuildButton(SkillSpec skill, SkillRefusal refusal)
        {
            var usable = refusal == SkillRefusal.None;

            var button = new VisualElement();
            button.AddToClassList("skill-button");
            button.EnableInClassList("skill-button--unusable", !usable);
            button.EnableInClassList("skill-button--selected", skill.Id == m_Selected);

            var name = new Label(skill.Name);
            name.AddToClassList("skill-button__name");
            button.Add(name);

            var costs = new VisualElement();
            costs.AddToClassList("skill-button__cost");

            var ap = new Label($"{skill.ApCost} AP");
            ap.AddToClassList("skill-button__ap");
            costs.Add(ap);

            if (skill.ElementCost > 0)
            {
                var element = new Label(
                    $"{skill.ElementCost} {ElementInfo.NameOf(skill.Element).Substring(0, 2).ToUpperInvariant()}");
                element.AddToClassList("skill-button__element");
                element.style.color = ElementPalette.ForElement(skill.Element);
                costs.Add(element);
            }

            button.Add(costs);

            // The reason is on the button as well as the line, so hovering an unusable skill
            // explains itself without the player having to look elsewhere.
            button.tooltip = usable ? skill.Description : SkillLabels.Describe(refusal);

            if (usable)
            {
                button.RegisterCallback<ClickEvent>(_ =>
                    m_Selected = m_Selected == skill.Id ? NoSkill : skill.Id);
            }

            return button;
        }
    }
}

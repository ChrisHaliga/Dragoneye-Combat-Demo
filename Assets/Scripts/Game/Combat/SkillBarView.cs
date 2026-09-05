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

        // What the bar was last drawn from. A click is a press and a release on the same element,
        // so rebuilding every frame destroyed the button between the two and nothing was ever
        // clicked -- the bar looked alive and did nothing at all.
        uint m_DrawnFor;
        Ap m_DrawnAp;
        int m_DrawnPool;
        // Not -1: that is a real selection now, and a sentinel that collides with one means the
        // first draw of a turn is skipped.
        int m_DrawnSelected = int.MinValue;
        int m_DrawnCount = -1;

        /// <summary>
        /// Nothing armed. A board click inspects and does nothing else.
        ///
        /// Zero because no skill may be authored with that id, so it cannot be mistaken for one.
        /// </summary>
        public const int NoSkill = 0;

        /// <summary>
        /// Walking, armed the same way a skill is.
        ///
        /// Negative for the same reason <see cref="NoSkill"/> is zero: authored ids start at one,
        /// so neither can collide with a real skill.
        /// </summary>
        public const int MoveSkill = -1;

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

        /// <summary>
        /// Redrawn when what it is drawing has changed, and not otherwise.
        ///
        /// A skill becomes unusable the moment AP is spent or an element leaves the pool, so this
        /// cannot wait for the turn to change -- but it must not run every frame either. A button is
        /// clicked by pressing and releasing on the same element, and a bar rebuilt between those
        /// two events has already thrown away the thing that was pressed.
        /// </summary>
        void Update()
        {
            if (m_Bar == null)
            {
                return;
            }

            var actor = m_Input.Actor;

            if (actor == null)
            {
                if (m_DrawnFor != 0 || m_DrawnCount != 0)
                {
                    m_Bar.Clear();
                    m_Reason.text = string.Empty;
                    m_Selected = NoSkill;
                    m_DrawnFor = 0;
                    m_DrawnCount = 0;
                }

                return;
            }

            // A turn starts ready to walk. Moving is what a player does most of, and making the
            // common case the one that needs a click first is backwards. Clicking Move again puts
            // it away, and with nothing armed a stray click on the board costs nothing.
            if (m_DrawnFor != actor.TurnId)
            {
                m_Selected = MoveSkill;
            }

            var pool = actor.GetComponent<CreaturePool>();
            var poolHash = pool != null ? Hash(pool.Ledger.Pool) : 0;
            var count = SkillCount(actor);

            if (m_DrawnFor == actor.TurnId && m_DrawnAp == actor.CurrentAp
                && m_DrawnPool == poolHash && m_DrawnSelected == m_Selected
                && m_DrawnCount == count)
            {
                return;
            }

            m_DrawnFor = actor.TurnId;
            m_DrawnAp = actor.CurrentAp;
            m_DrawnPool = poolHash;
            m_DrawnSelected = m_Selected;
            m_DrawnCount = count;

            Rebuild(actor);
        }

        static int SkillCount(CreatureState actor)
        {
            var commands = actor.GetComponent<SkillCommands>();
            return commands != null ? commands.Skills.Count : 0;
        }

        /// <summary>
        /// A cheap stand-in for "the pool changed".
        ///
        /// Order-dependent on purpose, so two different spreads holding the same number of elements
        /// do not collide and leave the bar showing a skill that can no longer be paid for.
        /// </summary>
        static int Hash(ElementCounts pool)
        {
            var hash = 17;

            foreach (var element in ElementInfo.All)
            {
                hash = (hash * 31) + pool[element];
            }

            return hash;
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

            m_Bar.Add(BuildMoveButton());

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

        /// <summary>
        /// Arms a skill, or uses it outright when there is nothing to aim it at.
        ///
        /// Something you do to yourself has one possible target, and making the player then click
        /// their own piece to confirm it is a step that answers no question. Everything aimed at
        /// somebody else is armed and takes the next board click, and clicking an armed skill again
        /// puts it away.
        /// </summary>
        void OnSkillClicked(SkillSpec skill)
        {
            if (skill.Target != SkillTarget.Self)
            {
                m_Selected = m_Selected == skill.Id ? NoSkill : skill.Id;
                return;
            }

            var actor = m_Input.Actor;
            var commands = actor != null ? actor.GetComponent<SkillCommands>() : null;

            if (commands != null)
            {
                commands.RequestUse(skill.Id, actor.Cell);
            }

            m_Selected = NoSkill;
        }

        /// <summary>
        /// Walking, as something you arm rather than something a click means by default.
        ///
        /// It sits in the bar with everything else because that is where a player looks to see what
        /// this turn can do, and because "what does a click do right now" then has one answer they
        /// can see rather than a rule they have to remember.
        /// </summary>
        VisualElement BuildMoveButton()
        {
            var button = new Button();
            button.AddToClassList("skill-button");
            button.AddToClassList("skill-button--move");
            button.EnableInClassList("skill-button--selected", m_Selected == MoveSkill);

            var name = new Label("Move");
            name.AddToClassList("skill-button__name");
            button.Add(name);
            button.text = string.Empty;

            var costs = new VisualElement();
            costs.AddToClassList("skill-button__cost");

            var ap = new Label("½ AP / TILE");
            ap.AddToClassList("skill-button__ap");
            costs.Add(ap);
            button.Add(costs);

            button.tooltip = "Walk. Half an action point for every tile of the route. "
                + "Click again to put it away.";

            button.clicked += () => m_Selected = m_Selected == MoveSkill ? NoSkill : MoveSkill;

            return button;
        }

        VisualElement BuildButton(SkillSpec skill, SkillRefusal refusal)
        {
            var usable = refusal == SkillRefusal.None;

            // A real Button, not a VisualElement with a click handler: the HUD root is made
            // click-through so the board underneath stays reachable, and that pass leaves the
            // framework's own controls alone by type.
            var button = new Button();
            button.AddToClassList("skill-button");
            button.EnableInClassList("skill-button--unusable", !usable);
            button.EnableInClassList("skill-button--selected", skill.Id == m_Selected);

            var name = new Label(skill.Name);
            name.AddToClassList("skill-button__name");
            button.Add(name);
            button.text = string.Empty;

            var costs = new VisualElement();
            costs.AddToClassList("skill-button__cost");

            var ap = new Label($"{skill.ApCost} AP");
            ap.AddToClassList("skill-button__ap");
            costs.Add(ap);

            if (skill.ElementCost > 0)
            {
                var element = new Label(
                    $"{skill.ElementCost} {ElementInfo.ShortNameOf(skill.Element)}");
                element.AddToClassList("skill-button__element");
                element.style.color = ElementPalette.ForElement(skill.Element);
                costs.Add(element);
            }

            button.Add(costs);

            // The reason is on the button as well as the line, so hovering an unusable skill
            // explains itself without the player having to look elsewhere.
            button.tooltip = usable ? skill.Description : SkillLabels.Describe(refusal);

            button.SetEnabled(usable);
            button.clicked += () => OnSkillClicked(skill);

            return button;
        }
    }
}

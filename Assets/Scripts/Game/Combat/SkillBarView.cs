using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Dragoneye.UI;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// The action bar: a row of fixed slots along the bottom of the screen, one for walking, eight
    /// for skills and three held for items, each with its key, its icon and its price.
    ///
    /// Fixed slots rather than a row that grows: a player learns where Strike is and presses 2
    /// without looking, and a bar whose buttons moved as skills came and went would make that
    /// impossible. Empty slots are drawn empty. Availability comes from <see cref="SkillRules"/> --
    /// the same check the server runs when the skill arrives, so a slot that is lit here is a slot
    /// the host will honour.
    ///
    /// Selecting a slot arms it; the next board click aims it. That is why the selection lives
    /// here rather than in the input component: the bar is what the player pressed, and the input
    /// asks it what is armed.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class SkillBarView : MonoBehaviour
    {
        [SerializeField]
        BoardActionInput m_Input;

        /// <summary>Skill slots on the bar. Keys 2 to 9.</summary>
        public const int SkillSlots = 8;

        /// <summary>Item slots on the bar. Nothing is carried yet; the slots say where it will go.</summary>
        public const int ItemSlots = 3;

        VisualElement m_Bar;
        VisualElement m_Hand;
        Label m_Reason;

        // What the skill under the cursor, or the one armed, actually does. Above the bar, because
        // a row of icons is not a description and a tooltip is not one either until it is hovered.
        VisualElement m_Detail;
        Label m_DetailHead;
        Label m_DetailText;
        int m_Hovered = NoSkill;

        int m_Selected = NoSkill;

        // Which element the armed skill will arrive as, for the few that offer a choice. Null for
        // everything else, and for a skill still waiting to be told.
        Element? m_SelectedElement;

        // The skill whose element the player is picking right now, if any. The bar shows the
        // options in place of the slots while this is set.
        int m_Choosing = NoSkill;

        // What the bar was last drawn from. A click is a press and a release on the same element,
        // so rebuilding every frame destroyed the button between the two and nothing was ever
        // clicked -- the bar looked alive and did nothing at all.
        uint m_DrawnFor;
        Ap m_DrawnAp;
        int m_DrawnPool;
        int m_DrawnSelected = int.MinValue;
        int m_DrawnChoosing = int.MinValue;
        int m_DrawnCount = -1;

        // The skills in slot order, as last drawn, so a key press finds its slot.
        readonly List<SkillSpec> m_Slotted = new List<SkillSpec>();

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

        /// <summary>
        /// Which element the armed skill will arrive as, for the few that offer a choice.
        ///
        /// Null for everything else, which is what the server reads as "the skill decides".
        /// </summary>
        public Element? SelectedElement => m_SelectedElement;

        /// <summary>Disarms, after a skill has been used or the turn has passed.</summary>
        public void ClearSelection()
        {
            m_Selected = NoSkill;
            m_SelectedElement = null;
            m_Choosing = NoSkill;
        }

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
            m_Hand = root.Q<VisualElement>("own-hand");
            m_Reason = root.Q<Label>("skill-reason");

            if (m_Bar == null)
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
                    m_Hand?.Clear();
                    m_Slotted.Clear();
                    m_Detail?.AddToClassList("is-hidden");

                    if (m_Reason != null)
                    {
                        m_Reason.text = string.Empty;
                    }

                    m_Selected = NoSkill;
                    m_DrawnFor = 0;
                    m_DrawnCount = 0;
                }

                return;
            }

            // A turn starts ready to walk. Moving is what a player does most of, and making the
            // common case the one that needs a click first is backwards. Pressing 1 again puts it
            // away, and with nothing armed a stray click on the board costs nothing.
            if (m_DrawnFor != actor.TurnId)
            {
                m_Selected = MoveSkill;
            }

            ReadKeys(actor);

            var pool = actor.Pool;
            var poolHash = pool != null ? Hash(pool.Ledger.Pool) : 0;
            var count = SkillCount(actor);

            if (m_DrawnFor == actor.TurnId && m_DrawnAp == actor.CurrentAp
                && m_DrawnPool == poolHash && m_DrawnSelected == m_Selected
                && m_DrawnChoosing == m_Choosing && m_DrawnCount == count)
            {
                return;
            }

            m_DrawnFor = actor.TurnId;
            m_DrawnAp = actor.CurrentAp;
            m_DrawnPool = poolHash;
            m_DrawnSelected = m_Selected;
            m_DrawnChoosing = m_Choosing;
            m_DrawnCount = count;

            Rebuild(actor);
        }

        /// <summary>
        /// The number keys: 1 walks, 2 to 9 are the skill slots in order.
        ///
        /// The same thing a click on the slot does, so the two cannot drift. Ignored while a
        /// question is open on screen, because the answer to that is not on this bar.
        /// </summary>
        void ReadKeys(CreatureState actor)
        {
            var keyboard = Keyboard.current;

            if (keyboard == null || FightPause.IsPaused || m_Choosing != NoSkill)
            {
                return;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                ToggleMove();
                return;
            }

            for (var slot = 0; slot < SkillSlots; slot++)
            {
                if (!Digit(keyboard, slot + 2).wasPressedThisFrame)
                {
                    continue;
                }

                if (slot < m_Slotted.Count && m_Slotted[slot] != null && IsUsable(actor, m_Slotted[slot]))
                {
                    OnSkillClicked(m_Slotted[slot]);
                }

                return;
            }
        }

        static UnityEngine.InputSystem.Controls.KeyControl Digit(Keyboard keyboard, int digit)
        {
            switch (digit)
            {
                case 2: return keyboard.digit2Key;
                case 3: return keyboard.digit3Key;
                case 4: return keyboard.digit4Key;
                case 5: return keyboard.digit5Key;
                case 6: return keyboard.digit6Key;
                case 7: return keyboard.digit7Key;
                case 8: return keyboard.digit8Key;
                default: return keyboard.digit9Key;
            }
        }

        bool IsUsable(CreatureState actor, SkillSpec skill)
        {
            var pool = actor.Pool;
            return pool != null
                && SkillRules.CheckAffordable(skill, true, actor.CurrentAp, pool.Ledger) == SkillRefusal.None;
        }

        static int SkillCount(CreatureState actor)
        {
            var commands = actor.SkillCommands;
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
            var commands = actor.SkillCommands;
            var pool = actor.Pool;

            m_Bar.Clear();
            m_Slotted.Clear();

            if (commands == null || pool == null)
            {
                return;
            }

            DrawHand(pool);

            // Picking what a fist is made of takes the bar over entirely. It is one question with
            // a few answers and a way out, and leaving the rest of the bar live beside it would
            // offer a second decision on top of the one already being asked.
            if (m_Choosing != NoSkill && commands.TryGetSkill(m_Choosing, out var choosing))
            {
                DrawElementChoice(choosing, actor, pool.Ledger);
                return;
            }

            m_Choosing = NoSkill;

            m_Bar.Add(BuildMoveSlot(actor.StepCost));

            var ledger = pool.Ledger;
            var worstReason = SkillRefusal.None;
            var slot = 0;

            foreach (var skill in commands.Skills)
            {
                if (slot >= SkillSlots)
                {
                    Debug.LogWarning($"{actor.DisplayName} knows more skills than the bar has slots; "
                        + $"{skill.Name} is not shown.", this);
                    break;
                }

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

                m_Bar.Add(BuildSkillSlot(skill, refusal, slot + 2));
                m_Slotted.Add(skill);
                slot++;
            }

            for (; slot < SkillSlots; slot++)
            {
                m_Bar.Add(EmptySlot(slot + 2, "An empty slot. A skill learned at a later level sits here."));
            }

            m_Bar.Add(Divider());

            for (var item = 0; item < ItemSlots; item++)
            {
                m_Bar.Add(EmptySlot(0, "Items come later. Nothing is carried yet."));
            }

            ShowDetail(actor);

            // The reason line is gone from the HUD -- every slot explains itself on hover and the
            // points are drawn above the bar -- but a document that still has one gets it filled.
            if (m_Reason != null)
            {
                m_Reason.text = m_Selected == NoSkill && worstReason != SkillRefusal.None
                    ? SkillLabels.Describe(worstReason)
                    : string.Empty;
            }
        }

        /// <summary>
        /// Arms a skill, or uses it outright when there is nothing to aim it at.
        ///
        /// Something you do to yourself has one possible target, and making the player then click
        /// their own piece to confirm it is a step that answers no question. Everything aimed at
        /// somebody else is armed and takes the next board click, and pressing an armed slot again
        /// puts it away.
        /// </summary>
        void OnSkillClicked(SkillSpec skill)
        {
            // A skill made of one thing goes straight to being armed. One that offers a choice asks
            // first, because the answer changes what it beats -- and a fist thrown as the wrong
            // element is a fist thrown away.
            if (skill.ChoosesElement)
            {
                BeginChoosing(skill.Id);
                return;
            }

            Use(skill, null);
        }

        void Use(SkillSpec skill, Element? element)
        {
            m_Choosing = NoSkill;

            if (skill.Target != SkillTarget.Self)
            {
                var same = m_Selected == skill.Id;
                m_Selected = same ? NoSkill : skill.Id;
                m_SelectedElement = same ? null : element;
                return;
            }

            var actor = m_Input.Actor;
            var commands = actor != null ? actor.SkillCommands : null;

            if (commands != null)
            {
                commands.RequestUse(skill.Id, actor.Cell, element);
            }

            m_Selected = NoSkill;
            m_SelectedElement = null;
        }

        void ToggleMove() => m_Selected = m_Selected == MoveSkill ? NoSkill : MoveSkill;

        /// <summary>
        /// Opens the element picker for a skill that offers one.
        ///
        /// Public because the context menu offers the same skills and must not use one without
        /// asking. A menu item that quietly picked an element would be a second answer to a
        /// question the bar asks out loud.
        /// </summary>
        public void BeginChoosing(int skillId)
        {
            m_Choosing = skillId;
            m_Selected = NoSkill;
            m_SelectedElement = null;
        }

        /// <summary>
        /// The one question, with the elements that could answer it, in the slots' place.
        ///
        /// Options the creature cannot pay for are shown and disabled rather than hidden: which
        /// elements a fist could be made of is a fact about the skill, and a row that changed
        /// length as the pool drained would teach the player nothing about either.
        /// </summary>
        void DrawElementChoice(SkillSpec skill, CreatureState actor, ElementLedger ledger)
        {
            var title = new Label($"{skill.Name} as");
            title.AddToClassList("skill-choice__title");
            m_Bar.Add(title);

            foreach (var element in skill.ElementOptions)
            {
                var option = skill.WithElement(element);
                var refusal = SkillRules.CheckAffordable(option, true, actor.CurrentAp, ledger);

                var button = new Button(() => Use(option, element));
                button.AddToClassList("action-slot");
                button.AddToClassList("action-slot--choice");
                button.SetEnabled(refusal == SkillRefusal.None);
                button.tooltip = ElementLore.Describe(element);
                button.text = string.Empty;

                var mark = new VisualElement();
                mark.AddToClassList("action-slot__rune-large");
                CharacterSheet.PaintElement(mark, element);
                button.Add(mark);

                var name = new Label(ElementInfo.ShortNameOf(element));
                name.AddToClassList("action-slot__caption");
                button.Add(name);

                m_Bar.Add(button);
            }

            var cancel = new Button(() => m_Choosing = NoSkill) { text = "Cancel" };
            cancel.AddToClassList("action-slot");
            cancel.AddToClassList("action-slot--cancel");
            m_Bar.Add(cancel);

            if (m_Reason != null)
            {
                m_Reason.text = string.Empty;
            }
        }

        /// <summary>
        /// What the creature you are playing is holding, on the line above the slots.
        ///
        /// The inspect card shows whatever was last clicked, which is usually somebody else -- so
        /// the one hand a player needs constantly was the one hand they had to give up looking at
        /// an enemy to see. This follows the player rather than the cursor.
        /// </summary>
        void DrawHand(CreaturePool pool)
        {
            if (m_Hand == null)
            {
                return;
            }

            m_Hand.Clear();

            var ledger = pool.Ledger;

            m_Hand.Add(BuildHeld(ledger.Pool));

            if (ledger.Outstanding.Count > 0)
            {
                m_Hand.Add(BuildSpent(ledger.Outstanding));
            }
        }

        /// <summary>What is still in the hand, counted under its rune.</summary>
        static VisualElement BuildHeld(ElementCounts held)
        {
            var group = new VisualElement();
            group.AddToClassList("own-hand__group");

            var title = new Label("HAND");
            title.AddToClassList("own-hand__label");
            group.Add(title);

            foreach (var element in ElementInfo.All)
            {
                var count = held[element];

                if (count > 0)
                {
                    group.Add(CharacterSheet.ElementChip(element, count));
                }
            }

            if (held.Total == 0)
            {
                var empty = new Label("nothing left to spend");
                empty.AddToClassList("own-hand__empty");
                group.Add(empty);
            }

            return group;
        }

        /// <summary>
        /// What has been spent, in the order it went -- oldest on the left.
        ///
        /// The order is the point, and it is not decoration: elements come back oldest first, so
        /// the leftmost rune here is precisely the one the next Take a Breath returns.
        /// </summary>
        static VisualElement BuildSpent(IReadOnlyList<Element> outstanding)
        {
            var group = new VisualElement();
            group.AddToClassList("own-hand__group");
            group.AddToClassList("own-spent");

            var title = new Label("SPENT");
            title.AddToClassList("own-hand__label");
            title.tooltip = "In the order they were spent. They come back oldest first.";
            group.Add(title);

            for (var i = 0; i < outstanding.Count; i++)
            {
                var mark = new VisualElement();
                mark.AddToClassList("own-spent__mark");
                mark.EnableInClassList("own-spent__mark--next", i == 0);
                CharacterSheet.PaintElement(mark, outstanding[i]);

                mark.tooltip = (i == 0 ? "Next one back." : $"{Ordinal(i + 1)} one back.")
                    + "\n\n" + ElementLore.Describe(outstanding[i]);

                group.Add(mark);
            }

            return group;
        }

        /// <summary>Small ordinals, spelled out. Nothing here ever reaches a number worth a rule.</summary>
        static string Ordinal(int position)
        {
            switch (position)
            {
                case 1: return "1st";
                case 2: return "2nd";
                case 3: return "3rd";
                default: return position + "th";
            }
        }

        // ---------- the slots ----------

        /// <summary>
        /// Walking, as something you arm rather than something a click means by default.
        ///
        /// The first slot, on key 1, because that is where a player looks to see what this turn
        /// can do -- and "what does a click do right now" then has one answer they can see rather
        /// than a rule they have to remember.
        /// </summary>
        VisualElement BuildMoveSlot(Ap stepCost)
        {
            var slot = Slot(1, SkillIcons.Move);
            slot.AddToClassList("action-slot--move");
            slot.EnableInClassList("action-slot--selected", m_Selected == MoveSkill);

            // This creature's price, not a constant. The bar is rebuilt whenever the actor
            // changes, so a knight in plate and a wolf read different numbers here.
            var ap = new Label($"{stepCost}");
            ap.AddToClassList("action-slot__ap");
            ap.tooltip = "Action points per tile.";
            slot.Add(ap);

            slot.tooltip = $"Move. {stepCost} action points for every tile of the route -- your "
                + "speed decides. Press again to put it away.";

            slot.clicked += ToggleMove;
            slot.RegisterCallback<PointerEnterEvent>(_ => Hover(MoveSkill));
            slot.RegisterCallback<PointerLeaveEvent>(_ => Hover(NoSkill));

            return slot;
        }

        VisualElement BuildSkillSlot(SkillSpec skill, SkillRefusal refusal, int key)
        {
            var usable = refusal == SkillRefusal.None;

            var slot = Slot(key, SkillIcons.For(skill));
            slot.EnableInClassList("action-slot--unusable", !usable);
            slot.EnableInClassList("action-slot--selected", skill.Id == m_Selected);

            var ap = new Label($"{skill.ApCost}");
            ap.AddToClassList("action-slot__ap");
            slot.Add(ap);

            if (skill.ElementCost > 0)
            {
                var cost = new VisualElement();
                cost.AddToClassList("action-slot__cost");

                var rune = new VisualElement();
                rune.AddToClassList("action-slot__rune");
                CharacterSheet.PaintElement(rune, skill.Element);
                cost.Add(rune);

                if (skill.ElementCost > 1)
                {
                    var count = new Label(skill.ElementCost.ToString());
                    count.AddToClassList("action-slot__rune-count");
                    count.style.color = ElementPalette.ForElement(skill.Element);
                    cost.Add(count);
                }

                slot.Add(cost);
            }

            // The reason is on the slot as well as the line, so hovering an unusable skill
            // explains itself without the player having to look elsewhere.
            slot.tooltip = usable
                ? $"{skill.Name}\n{PlainCost(skill)}\n{CharacterSheet.Describe(skill)}"
                : $"{skill.Name}\n{SkillLabels.Describe(refusal)}";

            slot.SetEnabled(usable);
            slot.clicked += () => OnSkillClicked(skill);

            // Hover names the skill the detail line is about. A skill that cannot be afforded is
            // disabled and never sees a pointer, which is why the armed one is the fallback.
            slot.RegisterCallback<PointerEnterEvent>(_ => Hover(skill.Id));
            slot.RegisterCallback<PointerLeaveEvent>(_ => Hover(NoSkill));

            return slot;
        }

        /// <summary>A slot with nothing in it: the frame, the key, and what will go there.</summary>
        static VisualElement EmptySlot(int key, string why)
        {
            var slot = new VisualElement();
            slot.AddToClassList("action-slot");
            slot.AddToClassList("action-slot--empty");
            slot.tooltip = why;

            if (key > 0)
            {
                var label = new Label(key.ToString());
                label.AddToClassList("action-slot__key");
                slot.Add(label);
            }

            return slot;
        }

        static VisualElement Divider()
        {
            var divider = new VisualElement();
            divider.AddToClassList("action-bar__divider");
            divider.pickingMode = PickingMode.Ignore;
            return divider;
        }

        /// <summary>
        /// The frame every filled slot shares: a real Button, because the HUD root is made
        /// click-through so the board underneath stays reachable, and that pass leaves the
        /// framework's own controls alone by type.
        /// </summary>
        static Button Slot(int key, Sprite icon)
        {
            var slot = new Button { text = string.Empty };
            slot.AddToClassList("action-slot");

            var image = new VisualElement();
            image.AddToClassList("action-slot__icon");
            image.pickingMode = PickingMode.Ignore;

            if (icon != null)
            {
                image.style.backgroundImage = new StyleBackground(icon);
            }

            slot.Add(image);

            var label = new Label(key.ToString());
            label.AddToClassList("action-slot__key");
            label.pickingMode = PickingMode.Ignore;
            slot.Add(label);

            return slot;
        }

        /// <summary>The cost as a tooltip says it: no colour, since tooltips carry none.</summary>
        static string PlainCost(SkillSpec skill) =>
            skill.ElementCost > 0
                ? $"{skill.ApCost} AP, {skill.ElementCost} {ElementInfo.ShortNameOf(skill.Element)}"
                : $"{skill.ApCost} AP";

        void Hover(int skillId)
        {
            if (m_Hovered == skillId)
            {
                return;
            }

            m_Hovered = skillId;
            ShowDetail(m_Input != null ? m_Input.Actor : null);
        }

        /// <summary>
        /// What the skill under the cursor does, or the armed one when nothing is hovered.
        ///
        /// Built above the bar rather than inside it: the bar is rebuilt whenever the creature's
        /// state changes, and a click is a press and a release on the same element -- anything
        /// that redraws on hover has to live where a hover cannot destroy the button under it.
        /// </summary>
        void ShowDetail(CreatureState actor)
        {
            if (m_Bar == null || m_Bar.parent == null)
            {
                return;
            }

            if (m_Detail == null)
            {
                m_Detail = new VisualElement();
                m_Detail.AddToClassList("skill-detail");
                m_Detail.pickingMode = PickingMode.Ignore;

                m_DetailHead = new Label();
                m_DetailHead.AddToClassList("skill-detail__head");
                m_Detail.Add(m_DetailHead);

                m_DetailText = new Label();
                m_DetailText.AddToClassList("skill-detail__text");
                m_Detail.Add(m_DetailText);

                // Above everything else in the footer: the line and the bar both sit under it.
                m_Bar.parent.Insert(0, m_Detail);
            }

            var wanted = m_Hovered != NoSkill ? m_Hovered : m_Selected;

            if (actor != null && wanted == MoveSkill)
            {
                m_Detail.RemoveFromClassList("is-hidden");
                m_DetailHead.text = $"Move   <color={CharacterSheet.PointsColour}>{actor.StepCost} AP per tile</color>";
                m_DetailText.text = "Walk the route the board shows. Leaving a tile an enemy is watching "
                    + "gives them a swing at you.";
                return;
            }

            var commands = actor != null ? actor.SkillCommands : null;

            if (commands == null || wanted == NoSkill || !commands.TryGetSkill(wanted, out var skill))
            {
                m_Detail.AddToClassList("is-hidden");
                return;
            }

            m_Detail.RemoveFromClassList("is-hidden");
            m_DetailHead.text = $"{skill.Name}   {CharacterSheet.Cost(skill)}";
            m_DetailText.text = CharacterSheet.Describe(skill);
        }
    }
}

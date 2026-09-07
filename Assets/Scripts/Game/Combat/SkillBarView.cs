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
    /// The action bar: a row of fixed slots along the bottom of the screen, one for walking, nine
    /// for skills and four held for items, each carrying its icon and its key and nothing else.
    ///
    /// A slot says what it is and where to press. What it costs is shown against the cursor once it
    /// is armed, which is where the player is already looking when they are deciding where to aim
    /// it -- printing the price on forty pixels of icon made every slot a small table.
    ///
    /// Fixed slots rather than a row that grows: a player learns where Strike is and presses 2
    /// without looking, and a bar whose buttons moved as skills came and went would make that
    /// impossible. Empty slots are drawn empty. Availability comes from <see cref="SkillRules"/> --
    /// the same check the server runs when the skill arrives, so a slot that is lit here is a slot
    /// the host will honour. A slot that cannot be afforded is greyed rather than disabled, because
    /// a disabled button never sees the pointer and so could never say what it was.
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

        /// <summary>Skill slots on the bar. Keys 2 to 9, then 0 for the ninth.</summary>
        public const int SkillSlots = 9;

        /// <summary>Item slots on the bar. Nothing is carried yet; the slots say where it will go.</summary>
        public const int ItemSlots = 4;

        VisualElement m_Root;
        VisualElement m_Bar;

        // One skill, in full, for when an icon is not enough. Opened from a slot's own right-click.
        VisualElement m_Window;
        Label m_WindowName;
        Label m_WindowCost;
        Label m_WindowText;
        VisualElement m_WindowStats;

        // The one-item menu a right-click opens, and the sheet behind it that closes it again.
        VisualElement m_SlotMenu;

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
        bool m_DrawnYours;

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

            var document = GetComponent<UIDocument>().rootVisualElement;

            m_Root = document.Q<VisualElement>("root") ?? document;
            m_Bar = m_Root.Q<VisualElement>("skill-bar");

            m_Window = m_Root.Q<VisualElement>("skill-window");
            m_WindowName = m_Root.Q<Label>("skill-window-name");
            m_WindowCost = m_Root.Q<Label>("skill-window-cost");
            m_WindowText = m_Root.Q<Label>("skill-window-text");
            m_WindowStats = m_Root.Q<VisualElement>("skill-window-stats");

            if (m_Bar == null || m_Window == null)
            {
                Debug.LogError($"{nameof(SkillBarView)} could not find its elements; "
                    + "check ArenaHud.uxml.", this);
                enabled = false;
                return;
            }

            BindWindow();
            BindMenu();
        }

        /// <summary>
        /// The three buttons on the left of the bar: what a creature wears, knows and carries.
        ///
        /// Only one of them has anywhere to go yet. The other two are drawn and disabled with the
        /// reason on them rather than left out, because a bar that grows a button later moves every
        /// slot along it -- and where the keys are is the one thing this bar promises not to change.
        /// </summary>
        void BindMenu()
        {
            Menu("menu-equipment", "E", false,
                "Equipment is chosen in the character creator, before the match.");
            Menu("menu-skills", "S", true, "Everything this creature can do.");
            Menu("menu-items", "I", false, "Items come later. Nothing is carried yet.");
        }

        void Menu(string name, string letter, bool works, string why)
        {
            var button = m_Root.Q<Button>(name);

            if (button == null)
            {
                return;
            }

            button.text = letter;
            button.tooltip = why;
            button.SetEnabled(works);

            if (works)
            {
                button.clicked += ShowOwnCard;
            }
        }

        /// <summary>
        /// Opens the inspector on the creature being played.
        ///
        /// The card is the only sheet the arena has, and it lists the skills and the pool. Asked
        /// for by a button, so it is a request like any other rather than something that appears.
        /// </summary>
        void ShowOwnCard()
        {
            var mine = m_Input != null && m_Input.Actor != null
                ? m_Input.Actor
                : LocalPlayer.Mine(ArenaContext.Current != null ? ArenaContext.Current.Creatures : null);

            if (mine != null)
            {
                m_Input?.Selection?.Select(mine);
            }
        }

        void BindWindow()
        {
            var close = m_Root.Q<Button>("skill-window-close");

            if (close != null)
            {
                HudIcons.DrawClose(close);
                close.clicked += CloseWindow;
            }

            CloseWindow();
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

            // Between turns the bar shows this player's own creature, greyed. It does not go
            // away: where a skill sits is something a player learns once, and a row that empties
            // itself twice a round is a row they have to find again every time.
            var actor = m_Input.Actor;
            var yours = actor != null;
            var owner = actor ?? LocalPlayer.Mine(
                ArenaContext.Current != null ? ArenaContext.Current.Creatures : null);

            if (owner == null)
            {
                if (m_DrawnFor != 0 || m_DrawnCount != 0)
                {
                    m_Bar.Clear();
                    m_Slotted.Clear();
                    m_Selected = NoSkill;
                    m_DrawnFor = 0;
                    m_DrawnCount = 0;
                }

                return;
            }

            actor = owner;

            // A turn starts ready to walk. Moving is what a player does most of, and making the
            // common case the one that needs a click first is backwards. Pressing 1 again puts it
            // away, and with nothing armed a stray click on the board costs nothing.
            if (m_DrawnFor != actor.TurnId && yours)
            {
                m_Selected = MoveSkill;
            }

            if (yours)
            {
                ReadKeys(actor);
            }

            var pool = actor.Pool;
            var poolHash = pool != null ? Hash(pool.Ledger.Pool) : 0;
            var count = SkillCount(actor);

            if (m_DrawnFor == actor.TurnId && m_DrawnAp == actor.CurrentAp
                && m_DrawnPool == poolHash && m_DrawnSelected == m_Selected
                && m_DrawnChoosing == m_Choosing && m_DrawnCount == count
                && m_DrawnYours == yours)
            {
                return;
            }

            m_DrawnYours = yours;

            m_DrawnFor = actor.TurnId;
            m_DrawnAp = actor.CurrentAp;
            m_DrawnPool = poolHash;
            m_DrawnSelected = m_Selected;
            m_DrawnChoosing = m_Choosing;
            m_DrawnCount = count;

            Rebuild(actor, yours);
        }

        /// <summary>
        /// The number keys: 1 walks, then 2 to 9 and 0 are the skill slots in order.
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

        /// <summary>
        /// The key for a slot: 1 walks, 2 through 9 are the first eight skills, and the ninth is 0.
        ///
        /// Zero last because that is where it is on the keyboard -- the row reads 1 to 0 left to
        /// right, and the bar reads the same way.
        /// </summary>
        static string KeyFor(int slot) => slot == 10 ? "0" : slot.ToString();

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
                case 9: return keyboard.digit9Key;
                default: return keyboard.digit0Key;
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

        void Rebuild(CreatureState actor, bool yours)
        {
            var commands = actor.SkillCommands;
            var pool = actor.Pool;

            m_Bar.Clear();
            m_Slotted.Clear();

            if (commands == null || pool == null)
            {
                return;
            }

            // Picking what a fist is made of takes the bar over entirely. It is one question with
            // a few answers and a way out, and leaving the rest of the bar live beside it would
            // offer a second decision on top of the one already being asked.
            if (m_Choosing != NoSkill && commands.TryGetSkill(m_Choosing, out var choosing))
            {
                DrawElementChoice(choosing, actor, pool.Ledger);
                return;
            }

            m_Choosing = NoSkill;

            m_Bar.Add(BuildMoveSlot(actor.StepCost, yours));

            var ledger = pool.Ledger;
            var slot = 0;

            foreach (var skill in commands.Skills)
            {
                if (slot >= SkillSlots)
                {
                    Debug.LogWarning($"{actor.DisplayName} knows more skills than the bar has slots; "
                        + $"{skill.Name} is not shown.", this);
                    break;
                }

                // The turn is part of the price. Between turns every slot is refused for the one
                // reason, which greys the row without a second rule about when a bar is live.
                var refusal = SkillRules.CheckAffordable(skill, yours, actor.CurrentAp, ledger);

                if (skill.Id == m_Selected && refusal != SkillRefusal.None)
                {
                    // Something changed under the player -- AP spent, an element gone. Disarm rather
                    // than leave a skill armed that the next click would have refused.
                    m_Selected = NoSkill;
                }

                m_Bar.Add(BuildSkillSlot(skill, refusal, slot + 2));
                m_Slotted.Add(skill);
                slot++;
            }

            for (; slot < SkillSlots; slot++)
            {
                m_Bar.Add(EmptySlot(KeyFor(slot + 2),
                    "An empty slot. A skill learned at a later level sits here."));
            }

            m_Bar.Add(Divider());

            for (var item = 0; item < ItemSlots; item++)
            {
                m_Bar.Add(EmptySlot(string.Empty, "Items come later. Nothing is carried yet."));
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
        }

        // ---------- the slots ----------

        /// <summary>
        /// Walking, as something you arm rather than something a click means by default.
        ///
        /// The first slot, on key 1, because that is where a player looks to see what this turn
        /// can do -- and "what does a click do right now" then has one answer they can see rather
        /// than a rule they have to remember.
        /// </summary>
        VisualElement BuildMoveSlot(Ap stepCost, bool yours)
        {
            var slot = Slot("1", SkillIcons.Move, "Move");
            slot.AddToClassList("action-slot--move");
            slot.EnableInClassList("action-slot--selected", m_Selected == MoveSkill);
            slot.EnableInClassList("action-slot--unusable", !yours);

            slot.tooltip = $"Move. {stepCost} action points for every tile of the route -- your "
                + "speed decides. Press again to put it away.";

            if (yours)
            {
                slot.clicked += ToggleMove;
            }

            return slot;
        }

        VisualElement BuildSkillSlot(SkillSpec skill, SkillRefusal refusal, int slotNumber)
        {
            var usable = refusal == SkillRefusal.None;

            var slot = Slot(KeyFor(slotNumber), SkillIcons.For(skill), skill.Name);
            slot.EnableInClassList("action-slot--unusable", !usable);
            slot.EnableInClassList("action-slot--selected", skill.Id == m_Selected);

            slot.tooltip = usable
                ? $"{skill.Name}\n{PlainCost(skill)}\n{CharacterSheet.Describe(skill)}"
                + "\n\nRight-click to inspect."
                : $"{skill.Name}\n{SkillLabels.Describe(refusal)}\n\nRight-click to inspect.";

            // Greyed rather than disabled. A disabled button never sees the pointer, so an
            // unusable slot could neither name itself nor be read -- which is exactly when a
            // player most wants to know what it is and why they cannot have it.
            if (usable)
            {
                slot.clicked += () => OnSkillClicked(skill);
            }

            slot.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1)
                {
                    OpenSlotMenu(skill, evt.position);
                    evt.StopPropagation();
                }
            });

            return slot;
        }

        /// <summary>A slot with nothing in it: the frame, the key, and what will go there.</summary>
        static VisualElement EmptySlot(string key, string why)
        {
            var slot = new VisualElement();
            slot.AddToClassList("action-slot");
            slot.AddToClassList("action-slot--empty");
            slot.tooltip = why;

            if (key.Length > 0)
            {
                var label = new Label(key);
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
        static Button Slot(string key, Sprite icon, string name)
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

            var label = new Label(key);
            label.AddToClassList("action-slot__key");
            label.pickingMode = PickingMode.Ignore;
            slot.Add(label);

            // The name, over the slot, while the pointer is on it. A child of the slot rather than
            // a panel above the bar, so being centred on the slot costs no measurement and only
            // the one being pointed at is ever up.
            var title = new Label(name);
            title.AddToClassList("action-slot__name");
            title.pickingMode = PickingMode.Ignore;
            slot.Add(title);

            slot.RegisterCallback<PointerEnterEvent>(_ => slot.AddToClassList("action-slot--named"));
            slot.RegisterCallback<PointerLeaveEvent>(_ => slot.RemoveFromClassList("action-slot--named"));

            return slot;
        }

        /// <summary>The cost as a tooltip says it: no colour, since tooltips carry none.</summary>
        static string PlainCost(SkillSpec skill) =>
            skill.ElementCost > 0
                ? $"{skill.ApCost} AP, {skill.ElementCost} {ElementInfo.ShortNameOf(skill.Element)}"
                : $"{skill.ApCost} AP";

        // ---------- reading one slot ----------

        /// <summary>
        /// The right-click menu on a slot, which offers the one thing a slot can be asked.
        ///
        /// A menu of one rather than a right-click that inspects outright: the gesture is the same
        /// one the board answers with a list, and a right-click that did something different
        /// depending on what was under it would be two gestures wearing one button.
        /// </summary>
        void OpenSlotMenu(SkillSpec skill, Vector2 position)
        {
            CloseSlotMenu();

            m_SlotMenu = new VisualElement();
            m_SlotMenu.AddToClassList("context-backdrop");
            m_SlotMenu.RegisterCallback<PointerDownEvent>(_ => CloseSlotMenu());

            var menu = new VisualElement();
            menu.AddToClassList("context-menu");

            var inspect = new Button();
            inspect.AddToClassList("context-item");

            var label = new Label("Inspect");
            label.AddToClassList("context-item__label");
            inspect.Add(label);

            inspect.clicked += () =>
            {
                ShowWindow(skill);
                CloseSlotMenu();
            };

            menu.Add(inspect);
            m_SlotMenu.Add(menu);
            m_Root.Add(m_SlotMenu);

            var point = m_SlotMenu.WorldToLocal(position);
            menu.style.left = point.x;
            menu.style.top = point.y;

            menu.schedule.Execute(() => menu.AddToClassList("context-menu--in"));
        }

        void CloseSlotMenu()
        {
            m_SlotMenu?.RemoveFromHierarchy();
            m_SlotMenu = null;
        }

        /// <summary>
        /// One skill in full: what it costs, what it does, and every number behind that.
        ///
        /// The slot is an icon and a key, which is the right amount to glance at and not enough to
        /// decide with. This is where the deciding is done, and it is asked for rather than hovered
        /// into -- a panel this size appearing under the pointer would cover the board.
        /// </summary>
        void ShowWindow(SkillSpec skill)
        {
            if (m_Window == null)
            {
                return;
            }

            m_WindowName.text = skill.Name;
            m_WindowCost.text = CharacterSheet.Cost(skill);
            m_WindowText.text = string.IsNullOrWhiteSpace(skill.Description)
                ? SkillEffectInfo.Describe(skill.Effect)
                : skill.Description;

            m_WindowStats.Clear();
            m_WindowStats.Add(WindowRow("ELEMENT", skill.ChoosesElement
                ? "Your choice"
                : ElementInfo.NameOf(skill.Element)));
            m_WindowStats.Add(WindowRow("ACTION POINTS", skill.ApCost.ToString()));
            m_WindowStats.Add(WindowRow("FROM THE POOL", skill.ElementCost == 0
                ? "Nothing"
                : $"{skill.ElementCost} {ElementInfo.ShortNameOf(skill.Element)}"));
            m_WindowStats.Add(WindowRow("REACH", Reach(skill)));
            m_WindowStats.Add(WindowRow("AIMED AT", Aimed(skill)));
            m_WindowStats.Add(WindowRow("DOES", SkillEffectInfo.Describe(skill.Effect)));

            if (skill.RollsToHit)
            {
                m_WindowStats.Add(WindowRow("TO HIT",
                    $"{SkillRules.HitChance(skill, 1)}% adjacent, "
                    + $"{SkillRules.HitChance(skill, skill.Range)}% at {skill.Range}"));
            }

            if (skill.IsContested)
            {
                m_WindowStats.Add(WindowRow("ANSWERED", "The defender may put up an element"));
            }

            m_Window.RemoveFromClassList("is-hidden");
        }

        void CloseWindow() => m_Window?.AddToClassList("is-hidden");

        static string Reach(SkillSpec skill) =>
            skill.Target == SkillTarget.Self
                ? "Yourself"
                : skill.Range <= 1 ? "Adjacent" : $"{skill.Range} tiles";

        static string Aimed(SkillSpec skill)
        {
            switch (skill.Target)
            {
                case SkillTarget.Self: return "Yourself";
                case SkillTarget.Tile: return "A place on the board";
                default: return "Another creature";
            }
        }

        static VisualElement WindowRow(string label, string value)
        {
            var row = new VisualElement();
            row.AddToClassList("skill-window__row");

            var name = new Label(label);
            name.AddToClassList("skill-window__label");
            row.Add(name);

            var text = new Label(value);
            text.AddToClassList("skill-window__value");
            row.Add(text);

            return row;
        }

    }
}

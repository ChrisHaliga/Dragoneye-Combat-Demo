using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// What you can do to whatever you right-clicked, listed where you clicked it.
    ///
    /// The board only ever offered one action at a time -- whatever the bar had armed -- so finding
    /// out what else was possible meant arming each thing in turn and reading the hover label. This
    /// asks the same questions all at once and shows the answers together.
    ///
    /// Every entry is priced by the same <see cref="ActionResolver"/> the hover label and the click
    /// use, so a line offered here is a line the server will honour, and a line refused here says
    /// why rather than being absent.
    ///
    /// Built in code rather than declared in the markup: it is a popup that exists for a few
    /// seconds at a position nothing else knows in advance, not a part of the HUD's layout.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class ContextMenuView : MonoBehaviour
    {
        [SerializeField, Tooltip("The board input, which owns the map, the units and the board.")]
        BoardActionInput m_Input;

        VisualElement m_Root;

        // A pickable sheet behind the menu. It is what makes clicking anywhere else close this
        // rather than reaching the board through it, and it is why the menu needs no other
        // dismissal handling.
        VisualElement m_Backdrop;
        VisualElement m_Menu;

        void Start()
        {
            if (m_Input == null)
            {
                Debug.LogError($"{nameof(ContextMenuView)} has no board input.", this);
                enabled = false;
                return;
            }

            // The template root, not the document root. ArenaHud.uxml attaches its stylesheet
            // *inside* that element, so a sibling of it gets no styles at all -- which is what
            // happened: the backdrop was an unstyled box in normal flow, taking layout space and
            // shoving the skill bar up the screen, with an unstyled menu invisible inside it.
            var document = GetComponent<UIDocument>().rootVisualElement;
            m_Root = document.Q<VisualElement>("root") ?? document;

            if (m_Input.Pointer != null)
            {
                m_Input.Pointer.ContextRequested += OnContextRequested;
                m_Input.Pointer.Clicked += OnBoardClicked;
            }
        }

        void OnDestroy()
        {
            if (m_Input != null && m_Input.Pointer != null)
            {
                m_Input.Pointer.ContextRequested -= OnContextRequested;
                m_Input.Pointer.Clicked -= OnBoardClicked;
            }
        }

        void OnBoardClicked(Hex _) => Close();

        void OnContextRequested(Hex hex, Vector2 screenPosition)
        {
            Close();

            var entries = Build(hex);

            if (entries.Count == 0)
            {
                return;
            }

            Open(entries, screenPosition);
        }

        /// <summary>One line of the menu: what it says, what it costs, and what it does.</summary>
        readonly struct Entry
        {
            public readonly string Label;
            public readonly string Cost;
            public readonly string Refusal;
            public readonly System.Action Act;

            /// <summary>False for the one entry that answers in place rather than acting.</summary>
            public readonly bool Closes;

            public Entry(string label, string cost, string refusal, System.Action act,
                bool closes = true)
            {
                Label = label;
                Cost = cost;
                Refusal = refusal;
                Act = act;
                Closes = closes;
            }

            public bool IsAllowed => string.IsNullOrEmpty(Refusal);
        }

        /// <summary>
        /// Everything this hex offers.
        ///
        /// A creature gets the skills that can be brought to bear on it and a way to read its card;
        /// bare ground gets a walk and what the ground is. Both get whichever of those apply, and
        /// nothing gets a line it could never use -- a skill that cannot target this creature at
        /// all is left out rather than listed and refused, because "Strike cannot target you" is a
        /// sentence nobody needs to read every time they look at themselves.
        /// </summary>
        List<Entry> Build(Hex hex)
        {
            var entries = new List<Entry>();
            var actor = m_Input.Actor;
            var units = m_Input.Units;
            var target = units != null && units.TryGet(hex, out var occupant)
                ? occupant.GetComponent<CreatureState>()
                : null;

            if (target != null)
            {
                AddSkills(entries, actor, target, hex);
                AddApproach(entries, actor, target, hex);

                var inspected = target;
                entries.Add(new Entry("Creature info", string.Empty, null,
                    () => m_Input.Selection?.Select(inspected)));

                return entries;
            }

            AddMove(entries, actor, hex);
            entries.Add(new Entry("Tile details", string.Empty, null, () => ShowDetails(hex),
                closes: false));

            return entries;
        }

        void AddMove(List<Entry> entries, CreatureState actor, Hex hex)
        {
            if (actor == null)
            {
                return;
            }

            var steps = m_Input.Board.CostTo(actor.Cell, hex);

            var plan = ActionResolver.Resolve(
                isActorsTurn: true, controlsActor: true, currentAp: actor.CurrentAp,
                targetOccupied: false, moveSteps: steps);

            if (plan.Action != BoardAction.Move)
            {
                return;
            }

            entries.Add(new Entry("Move here", $"{plan.Cost} AP",
                plan.IsAllowed ? null : ActionLabels.DescribeRefusal(plan.Refusal),
                () => actor.GetComponent<UnitCommands>()?.RequestMove(hex)));
        }

        /// <summary>
        /// Walking up to somebody, as far as their own tile allows.
        ///
        /// The tile they are standing on is not somewhere anybody can go, so this offers the
        /// nearest one that is -- which is what a player means when they right-click an enemy and
        /// ask to move.
        /// </summary>
        void AddApproach(List<Entry> entries, CreatureState actor, CreatureState target, Hex hex)
        {
            if (actor == null || target == actor
                || !m_Input.Board.TryTileInReach(actor.Cell, hex, 1, out var tile, out var steps)
                || steps <= 0)
            {
                return;
            }

            var plan = ActionResolver.Resolve(
                isActorsTurn: true, controlsActor: true, currentAp: actor.CurrentAp,
                targetOccupied: false, moveSteps: steps);

            entries.Add(new Entry("Move next to", $"{plan.Cost} AP",
                plan.IsAllowed ? null : ActionLabels.DescribeRefusal(plan.Refusal),
                () => actor.GetComponent<UnitCommands>()?.RequestMove(tile)));
        }

        /// <summary>
        /// Every skill that could be aimed at this creature, priced with the walk it would need.
        ///
        /// Filtered on whether the skill can target this creature at all rather than on whether it
        /// is affordable right now: what a player cannot pay for this turn is worth seeing with the
        /// reason attached, and what could never apply is noise.
        /// </summary>
        void AddSkills(List<Entry> entries, CreatureState actor, CreatureState target, Hex hex)
        {
            var commands = actor != null ? actor.GetComponent<SkillCommands>() : null;

            if (commands == null)
            {
                return;
            }

            var isSelf = target == actor;
            var isEnemy = target.Party != actor.Party;

            foreach (var skill in commands.Skills)
            {
                if (skill.Target == SkillTarget.Self != isSelf)
                {
                    continue;
                }

                var plan = ActionResolver.ResolveSkill(
                    isActorsTurn: true, controlsActor: true, currentAp: actor.CurrentAp,
                    skill: skill, targetIsCreature: true, targetIsEnemy: isEnemy,
                    stepsToReach: isSelf
                        ? 0
                        : m_Input.Board.StepsToReach(actor.Cell, hex, skill.Range));

                if (plan.Action != BoardAction.UseSkill)
                {
                    continue;
                }

                var id = skill.Id;
                var cost = plan.MoveCost > Ap.Zero
                    ? $"{plan.Cost} AP  (incl. {plan.MoveCost} to close)"
                    : $"{plan.Cost} AP";

                entries.Add(new Entry(skill.Name, cost,
                    plan.IsAllowed ? null : ActionLabels.DescribeRefusal(plan.Refusal),
                    () => commands.RequestUse(id, hex)));
            }
        }

        /// <summary>
        /// What the ground is, in place of the menu that asked.
        ///
        /// Shown here rather than somewhere else on screen: the player pointed at this tile, and
        /// the answer belongs where they pointed. There is one fact worth saying about a tile
        /// today; terrain that does something -- water, an acid pool -- says it in the same place.
        /// </summary>
        void ShowDetails(Hex hex)
        {
            var map = m_Input.Map != null ? m_Input.Map.Map : null;

            if (m_Menu == null || map == null || !map.TryGetTile(hex, out var tile)
                || tile.Terrain == null)
            {
                return;
            }

            var terrain = tile.Terrain;

            m_Menu.Clear();

            var name = new Label(terrain.DisplayName);
            name.AddToClassList("context-title");
            m_Menu.Add(name);

            var note = new Label(terrain.IsWalkable
                ? $"Costs {terrain.MoveCost:0.#} of a step to cross."
                : "Nothing can cross this.");
            note.AddToClassList("context-note");
            m_Menu.Add(note);
        }

        void Open(List<Entry> entries, Vector2 screenPosition)
        {
            m_Backdrop = new VisualElement();
            m_Backdrop.AddToClassList("context-backdrop");
            m_Backdrop.RegisterCallback<PointerDownEvent>(_ => Close());

            m_Menu = new VisualElement();
            m_Menu.AddToClassList("context-menu");

            foreach (var entry in entries)
            {
                m_Menu.Add(BuildRow(entry));
            }

            m_Backdrop.Add(m_Menu);
            m_Root.Add(m_Backdrop);

            Place(screenPosition);
        }

        /// <summary>
        /// Puts the menu where it was asked for, and keeps it on screen.
        ///
        /// Placed inside the backdrop rather than the HUD root, and converted into the backdrop's
        /// own space: the root carries padding, and an absolutely positioned child is measured from
        /// the padding edge rather than from where the panel thinks zero is. The backdrop has none,
        /// so the two agree.
        ///
        /// Measured after a layout pass rather than guessed at, because how tall it is depends on
        /// how many things this hex turned out to offer.
        /// </summary>
        void Place(Vector2 screenPosition)
        {
            var panel = m_Root.panel;

            if (panel == null)
            {
                return;
            }

            var point = m_Backdrop.WorldToLocal(RuntimePanelUtils.ScreenToPanel(panel,
                new Vector2(screenPosition.x, Screen.height - screenPosition.y)));

            m_Menu.style.left = point.x;
            m_Menu.style.top = point.y;

            m_Menu.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var bounds = m_Backdrop.layout;
                var size = m_Menu.layout;

                if (float.IsNaN(size.width) || bounds.width <= 0f)
                {
                    return;
                }

                m_Menu.style.left = Mathf.Max(0f, Mathf.Min(point.x, bounds.width - size.width - 4f));
                m_Menu.style.top = Mathf.Max(0f, Mathf.Min(point.y, bounds.height - size.height - 4f));
            });
        }

        VisualElement BuildRow(Entry entry)
        {
            // A real Button, because the HUD root is made click-through and that pass leaves the
            // framework's own controls alone by type.
            var button = new Button();
            button.AddToClassList("context-item");
            button.text = string.Empty;

            var label = new Label(entry.Label);
            label.AddToClassList("context-item__label");
            button.Add(label);

            if (!string.IsNullOrEmpty(entry.Cost))
            {
                var cost = new Label(entry.Cost);
                cost.AddToClassList("context-item__cost");
                button.Add(cost);
            }

            button.SetEnabled(entry.IsAllowed);
            button.tooltip = entry.Refusal ?? string.Empty;

            var act = entry.Act;

            var closes = entry.Closes;

            button.clicked += () =>
            {
                act?.Invoke();

                if (closes)
                {
                    Close();
                }
            };

            return button;
        }

        void Close()
        {
            m_Backdrop?.RemoveFromHierarchy();
            m_Backdrop = null;
            m_Menu = null;
        }
    }
}

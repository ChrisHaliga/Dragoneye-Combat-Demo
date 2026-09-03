using System;
using System.Collections.Generic;
using Dragoneye.Hex.Systems;
using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Prices whatever the cursor is over, and turns a click into the action it was showing.
    ///
    /// The label and the command come from one call to <see cref="ActionResolver"/>, which is the
    /// whole point of this component existing. Pricing the hover in one place and deciding the click
    /// in another is how a UI ends up offering a move the server then refuses.
    ///
    /// Replaces the plain select-or-move handling from before turns existed: selecting is still what
    /// a click on a creature does when there is no action to take.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardActionInput : MonoBehaviour
    {
        [SerializeField]
        HexPointer m_Pointer;

        [SerializeField]
        UnitIndex m_Units;

        [SerializeField]
        CreatureSelection m_Selection;

        [SerializeField]
        ArenaMap m_Map;

        [SerializeField]
        CreatureRegistry m_Creatures;

        readonly List<Hex> m_Path = new List<Hex>();
        readonly HashSet<Hex> m_Blocked = new HashSet<Hex>();

        ActionPlan m_Hovered = ActionPlan.Nothing;

        /// <summary>The action the cursor is currently over, with its price.</summary>
        public ActionPlan Hovered => m_Hovered;

        /// <summary>Raised when the hovered action changes, so the cursor label can repaint.</summary>
        public event Action<ActionPlan> HoveredChanged;

        /// <summary>
        /// The creature the local player is acting with: the active one, if they control it.
        ///
        /// Not the selection. A player may click an enemy to read its card without giving up their
        /// turn, so what is selected and what is acting are different questions.
        /// </summary>
        public CreatureState Actor
        {
            get
            {
                var turns = TurnState.Current;
                if (turns == null || turns.IsOver || m_Creatures == null)
                {
                    return null;
                }

                foreach (var creature in m_Creatures.All)
                {
                    if (creature != null && creature.TurnId == turns.ActiveId
                        && LocalPlayer.Controls(creature))
                    {
                        return creature;
                    }
                }

                return null;
            }
        }

        void OnEnable()
        {
            if (m_Pointer == null || m_Units == null || m_Selection == null
                || m_Map == null || m_Creatures == null)
            {
                Debug.LogError($"{nameof(BoardActionInput)} is missing references.", this);
                enabled = false;
                return;
            }

            m_Pointer.Clicked += OnClicked;
            m_Pointer.HoverChanged += OnHoverChanged;
        }

        void OnDisable()
        {
            if (m_Pointer != null)
            {
                m_Pointer.Clicked -= OnClicked;
                m_Pointer.HoverChanged -= OnHoverChanged;
            }
        }

        // Re-priced every frame rather than only on hover change: standing still while AP is spent,
        // a creature dies or the turn passes all change what the same hex would cost.
        void Update() => Reprice(m_Pointer.Hovered);

        void OnHoverChanged(Hex? hovered) => Reprice(hovered);

        void Reprice(Hex? hovered)
        {
            var plan = hovered.HasValue ? Price(hovered.Value) : ActionPlan.Nothing;

            if (plan.Action == m_Hovered.Action && plan.Cost == m_Hovered.Cost
                && plan.Refusal == m_Hovered.Refusal)
            {
                return;
            }

            m_Hovered = plan;
            HoveredChanged?.Invoke(plan);
        }

        ActionPlan Price(Hex hex)
        {
            var actor = Actor;
            if (actor == null)
            {
                return ActionPlan.Nothing;
            }

            var occupied = m_Units.TryGet(hex, out var occupant);
            var target = occupied ? occupant.GetComponent<CreatureState>() : null;

            return ActionResolver.Resolve(
                isActorsTurn: true,
                controlsActor: true,
                currentAp: actor.CurrentAp,
                targetOccupied: occupied,
                targetIsEnemy: target != null && target.Party != actor.Party,
                distanceToTarget: Hex.Distance(actor.Cell, hex),
                moveCost: occupied ? -1 : MoveCost(actor.Cell, hex));
        }

        /// <summary>
        /// Steps along the cheapest route, or -1 if there is none.
        ///
        /// The same search the server runs. Duplicated work rather than duplicated *rules* -- the
        /// pathfinder is one implementation used by both sides, so the client's price is the price
        /// the server will charge.
        /// </summary>
        int MoveCost(Hex from, Hex to)
        {
            if (m_Map == null || m_Map.Map == null)
            {
                return -1;
            }

            m_Blocked.Clear();
            foreach (var creature in m_Creatures.All)
            {
                if (creature != null && creature.IsAlive && creature.Cell != from)
                {
                    m_Blocked.Add(creature.Cell);
                }
            }

            return HexPathfinder.TryFindPath(m_Map.Map, from, to, m_Blocked, m_Path)
                ? m_Path.Count
                : -1;
        }

        void OnClicked(Hex hex)
        {
            // Inspecting comes first and is always allowed. Reading an enemy's card mid-turn is a
            // normal thing to want, and it costs nothing.
            if (m_Units.TryGet(hex, out var occupant))
            {
                m_Selection.Select(occupant.GetComponent<CreatureState>());
            }

            var actor = Actor;
            var plan = Price(hex);

            if (actor == null || !plan.IsAllowed)
            {
                return;
            }

            var commands = actor.GetComponent<UnitCommands>();
            if (commands == null)
            {
                return;
            }

            if (plan.Action == BoardAction.Attack)
            {
                commands.RequestAttack(hex);
            }
            else if (plan.Action == BoardAction.Move)
            {
                commands.RequestMove(hex);
            }
        }
    }
}

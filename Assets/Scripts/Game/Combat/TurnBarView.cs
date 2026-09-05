using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// The initiative bar across the top: one portrait per creature, in turn order, health over the
    /// picture, the active one enlarged and named.
    ///
    /// Reads <see cref="TurnState"/> and <see cref="CreatureRegistry"/> and writes nothing. A view
    /// that could end a turn or reorder the queue would be a second authority on both.
    ///
    /// Rebuilt wholesale on change. A handful of portraits that change when a turn passes or a
    /// creature is hurt is not worth diffing, and rebuilding keeps the order and the markup unable
    /// to drift apart.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class TurnBarView : MonoBehaviour
    {
        [SerializeField]
        CreatureRegistry m_Creatures;

        [SerializeField, Tooltip("Clicking a portrait inspects that creature.")]
        CreatureSelection m_Selection;

        VisualElement m_Order;
        Label m_Round;
        Label m_ActiveName;
        Label m_ActiveOwner;

        readonly List<CreatureState> m_Observed = new List<CreatureState>();

        TurnState m_Turns;

        void Start()
        {
            if (m_Creatures == null)
            {
                Debug.LogError($"{nameof(TurnBarView)} has no creature registry.", this);
                enabled = false;
                return;
            }

            var root = GetComponent<UIDocument>().rootVisualElement;
            CreatureDisplay.MakeClickThrough(root);

            m_Order = root.Q<VisualElement>("turn-order");
            m_Round = root.Q<Label>("round-label");
            m_ActiveName = root.Q<Label>("active-name");
            m_ActiveOwner = root.Q<Label>("active-owner");

            if (m_Order == null || m_Round == null || m_ActiveName == null || m_ActiveOwner == null)
            {
                Debug.LogError($"{nameof(TurnBarView)} could not find its elements; check ArenaHud.uxml.",
                    this);
                enabled = false;
                return;
            }

            m_Creatures.Changed += Rebuild;
            Rebuild();
        }

        void OnDestroy()
        {
            if (m_Creatures != null)
            {
                m_Creatures.Changed -= Rebuild;
            }

            Unbind();
            Unobserve();
        }

        // The turn state is a spawned network object, so it appears some frames after this does.
        // Polling for it beats an ordering assumption that would leave the bar permanently empty.
        void Update()
        {
            if (m_Turns != TurnState.Current)
            {
                Unbind();
                m_Turns = TurnState.Current;

                if (m_Turns != null)
                {
                    m_Turns.Changed += Rebuild;
                }

                Rebuild();
            }
        }

        void Unbind()
        {
            if (m_Turns != null)
            {
                m_Turns.Changed -= Rebuild;
            }
        }

        void Unobserve()
        {
            foreach (var creature in m_Observed)
            {
                if (creature != null)
                {
                    creature.Changed -= Rebuild;
                }
            }

            m_Observed.Clear();
        }

        void Rebuild()
        {
            if (m_Order == null)
            {
                return;
            }

            Unobserve();
            m_Order.Clear();

            var turns = TurnState.Current;
            var showing = turns != null && turns.Order.Count > 0 && !turns.IsOver;

            m_Round.EnableInClassList("is-hidden", !showing);
            m_ActiveName.text = string.Empty;
            m_ActiveOwner.text = string.Empty;

            if (!showing)
            {
                return;
            }

            m_Round.text = $"ROUND {turns.Round}";

            foreach (var id in turns.Order)
            {
                var creature = m_Creatures.ByTurnId(id);
                if (creature == null)
                {
                    continue;
                }

                creature.Changed += Rebuild;
                m_Observed.Add(creature);

                var active = id == turns.ActiveId;
                m_Order.Add(BuildPortrait(creature, active));

                if (active)
                {
                    // Whose turn, on the line that says whose turn it is. The second line
                    // names a person, and only when there is one -- "the computer's turn" is a
                    // sentence nobody needed, occupying the most-read strip of the screen to say
                    // that the thing without a player attached has no player attached.
                    m_ActiveName.text = $"{creature.DisplayName}'s turn";

                    var player = creature.IsComputerControlled
                        ? string.Empty
                        : CreatureDisplay.ControllerName(creature).ToUpperInvariant();

                    m_ActiveOwner.text = player;
                    m_ActiveOwner.EnableInClassList("is-hidden", player.Length == 0);
                }
            }
        }

        VisualElement BuildPortrait(CreatureState creature, bool active)
        {
            var root = new VisualElement();
            root.AddToClassList("turn-portrait");
            root.EnableInClassList("turn-portrait--active", active);
            root.EnableInClassList("turn-portrait--dimmed", !active);

            // Party colour on the border, so which side a portrait belongs to survives the
            // greying-out that marks it as not the current turn.
            var tint = PartyPalette.ForParty(creature.Party);
            root.style.borderTopColor = root.style.borderBottomColor =
                root.style.borderLeftColor = root.style.borderRightColor =
                    active ? Color.white : tint;

            CreatureDisplay.DrawPortrait(root, creature, "turn-portrait__initial");

            root.Add(BuildHealth(creature));

            // Inspecting from the bar, the same gesture the party column already offers. Reading a
            // creature costs nothing and never touches the turn, so it is allowed at any time --
            // including during another player's turn.
            if (m_Selection != null)
            {
                root.RegisterCallback<ClickEvent>(_ => m_Selection.Select(creature));
            }

            return root;
        }

        static VisualElement BuildHealth(CreatureState creature)
        {
            var bar = new VisualElement();
            bar.AddToClassList("turn-portrait__hp");

            var fill = new VisualElement();
            fill.AddToClassList("turn-portrait__hp-fill");
            fill.style.width = Length.Percent(CreatureDisplay.HealthFraction(creature) * 100f);

            var text = new Label($"{creature.CurrentHp}/{creature.MaxHp}");
            text.AddToClassList("turn-portrait__hp-text");

            bar.Add(fill);
            bar.Add(text);
            return bar;
        }
    }
}

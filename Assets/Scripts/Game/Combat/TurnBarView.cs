using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Multiplayer;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.UI;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// The initiative bar across the top: one portrait per creature, in turn order, health over the
    /// picture, the active one enlarged and named.
    ///
    /// Reads the shown fight and the registry and writes nothing. It follows the playback rather
    /// than the replicated turn state, so the creature it says is acting is the one whose turn the
    /// player is watching, not the one the server has already moved on to.
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
        Label m_Pace;

        CombatPlayback m_Playback;
        bool m_Fast;

        void Start()
        {
            if (m_Creatures == null)
            {
                Debug.LogError($"{nameof(TurnBarView)} has no creature registry.", this);
                enabled = false;
                return;
            }

            var root = GetComponent<UIDocument>().rootVisualElement;
            UiTypeface.Apply(root);
            CreatureDisplay.MakeClickThrough(root);

            m_Order = root.Q<VisualElement>("turn-order");
            m_Pace = root.Q<Label>("turn-pace");

            if (m_Order == null || m_Pace == null)
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
        }

        // The playback is made by the arena context when it wakes, which is before this starts;
        // polling covers an arena that came together in another order.
        void Update()
        {
            if (m_Playback != CombatPlayback.Current)
            {
                Unbind();
                m_Playback = CombatPlayback.Current;

                if (m_Playback != null)
                {
                    m_Playback.Changed += Rebuild;
                }

                Rebuild();
            }

            var fast = m_Playback != null && m_Playback.Speed > 1f;

            if (fast != m_Fast)
            {
                m_Fast = fast;
                RefreshPace();
            }
        }

        void Unbind()
        {
            if (m_Playback != null)
            {
                m_Playback.Changed -= Rebuild;
            }
        }

        /// <summary>
        /// The line under the turn order, which says one thing: that the fight is being played
        /// fast. The round number used to live above the bar and was dropped -- it cost a line of
        /// screen across the top of the board to say a number nobody was counting.
        /// </summary>
        void RefreshPace()
        {
            var fight = Shown.Fight;
            var fast = m_Fast && fight != null && fight.Began;

            m_Pace.text = fast ? "FAST FORWARD" : string.Empty;
            m_Pace.EnableInClassList("is-hidden", !fast);
        }

        void Rebuild()
        {
            if (m_Order == null)
            {
                return;
            }

            m_Order.Clear();

            var fight = Shown.Fight;
            var showing = fight != null && fight.Began && fight.Order.Count > 0 && !fight.IsOver;

            RefreshPace();

            if (!showing)
            {
                return;
            }


            // Where the round has got to. Everything before the active creature has had its turn
            // and everything after is still to come, and a bar that does not say which is which
            // makes "how long until I act again" a thing you count rather than a thing you see.
            var reached = 0;

            for (var i = 0; i < fight.Order.Count; i++)
            {
                if (fight.Order[i] == fight.ActiveId)
                {
                    reached = i;
                    break;
                }
            }

            var position = 0;

            foreach (var id in fight.Order)
            {
                var acted = position < reached;
                position++;

                var creature = m_Creatures.ByTurnId(id);
                var shown = fight.Of(id);

                if (creature == null || shown == null)
                {
                    continue;
                }

                m_Order.Add(BuildPortrait(creature, shown, id == fight.ActiveId, acted));
            }
        }

        /// <summary>
        /// One place in the order: a face, and its bars along the bottom.
        ///
        /// The creature acting now is drawn larger, named under its card, and is the only one whose
        /// numbers are written on. Everybody else refills the moment their own turn starts, so what
        /// they hold now says nothing about what they will have when it matters -- and a row of
        /// faces with a number on every one of them answers a question nobody asked. Action points
        /// are not here at all; they are on the line above the bar, where they are spent.
        /// </summary>
        VisualElement BuildPortrait(CreatureState creature, PresentedCreature shown, bool active, bool acted)
        {
            var root = new VisualElement();
            root.AddToClassList("turn-portrait");
            root.EnableInClassList("turn-portrait--active", active);
            root.EnableInClassList("turn-portrait--dimmed", !active);
            root.EnableInClassList("turn-portrait--acted", acted);

            // Party colour on the border, so which side a portrait belongs to survives the
            // greying-out that marks it as not the current turn.
            var tint = PartyPalette.ForParty(creature.Party);
            root.style.borderTopColor = root.style.borderBottomColor =
                root.style.borderLeftColor = root.style.borderRightColor =
                    active ? Color.white : tint;

            CreatureDisplay.DrawPortrait(root, creature, "turn-portrait__initial");
            CreatureDisplay.DrawVitals(root, creature, numbers: active);

            if (active)
            {
                var name = new Label(creature.DisplayName);
                name.AddToClassList("turn-portrait__name");
                name.pickingMode = PickingMode.Ignore;
                root.Add(name);
            }

            root.tooltip = $"{creature.DisplayName}\n{CreatureDisplay.ControllerName(creature)}"
                + "\n\nRight-click to inspect.";

            // Reading a creature costs nothing and never touches the turn, so it is allowed at any
            // time -- but only when it is asked for.
            if (m_Selection != null)
            {
                root.pickingMode = PickingMode.Position;
                root.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 1)
                    {
                        m_Selection.Select(creature);
                        evt.StopPropagation();
                    }
                });
            }

            return root;
        }

    }
}

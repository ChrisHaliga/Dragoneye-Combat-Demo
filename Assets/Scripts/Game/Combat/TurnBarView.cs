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
        Label m_ActiveName;
        Label m_ActiveOwner;

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
            m_ActiveName = root.Q<Label>("active-name");
            m_ActiveOwner = root.Q<Label>("active-owner");

            if (m_Order == null || m_ActiveName == null || m_ActiveOwner == null)
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

            m_ActiveOwner.text = fast ? "FAST FORWARD" : string.Empty;
            m_ActiveOwner.EnableInClassList("is-hidden", !fast);
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

            m_ActiveName.text = string.Empty;
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

                var active = id == fight.ActiveId;
                m_Order.Add(BuildPortrait(creature, shown, active, acted));

                if (active)
                {
                    // Whose turn, on the line that says whose turn it is: the creature and, where
                    // there is one, the person -- side by side, because every line the bar takes
                    // is a row of tiles the player cannot see.
                    var player = creature.IsComputerControlled
                        ? string.Empty
                        : CreatureDisplay.ControllerName(creature);

                    m_ActiveName.text = player.Length == 0
                        ? $"{creature.DisplayName}'s turn"
                        : $"{creature.DisplayName}'s turn  <color=#8B93A5>·  {player}</color>";

                }
            }
        }

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

            // Only the creature actually acting. Everybody else refills the moment their turn
            // starts, so what they are holding now says nothing about what they will have when it
            // matters -- and a number that is about to change is worse than no number.
            if (active)
            {
                root.Add(BuildActionPoints(creature, shown));
            }

            if (creature.MaxArmour > 0)
            {
                root.Add(BuildArmour(creature, shown));
            }

            root.Add(BuildHealth(creature, shown));

            // Inspecting from the bar, the same gesture the party column already offers. Reading a
            // creature costs nothing and never touches the turn, so it is allowed at any time.
            if (m_Selection != null)
            {
                root.RegisterCallback<ClickEvent>(_ => m_Selection.Select(creature));
            }

            return root;
        }

        static VisualElement BuildActionPoints(CreatureState creature, PresentedCreature shown)
        {
            var strip = new VisualElement();
            strip.AddToClassList("turn-portrait__ap");

            var text = new Label($"{shown.Ap}/{creature.MaxAp}");
            text.AddToClassList("turn-portrait__ap-text");
            text.tooltip = "Action points left this turn.";

            strip.Add(text);
            return strip;
        }

        /// <summary>
        /// The silver bar above the red, for creatures that have armour to lose.
        ///
        /// Numbered like the health under it, because it is read the same way: armour never comes
        /// back, so "6 of 16 left" is a fact about the rest of the match, not about this turn.
        /// </summary>
        static VisualElement BuildArmour(CreatureState creature, PresentedCreature shown)
        {
            var bar = new VisualElement();
            bar.AddToClassList("turn-portrait__armour");

            var fill = new VisualElement();
            fill.AddToClassList("turn-portrait__armour-fill");
            fill.style.width = Length.Percent(CreatureDisplay.Fraction(shown.Armour, creature.MaxArmour) * 100f);

            var text = new Label($"{shown.Armour}/{creature.MaxArmour}");
            text.AddToClassList("turn-portrait__armour-text");
            text.tooltip = "Armour. Takes every blow first, and does not come back.";

            bar.Add(fill);
            bar.Add(text);
            return bar;
        }

        static VisualElement BuildHealth(CreatureState creature, PresentedCreature shown)
        {
            var bar = new VisualElement();
            bar.AddToClassList("turn-portrait__hp");

            var fill = new VisualElement();
            fill.AddToClassList("turn-portrait__hp-fill");
            fill.style.width = Length.Percent(CreatureDisplay.Fraction(shown.Hp, creature.MaxHp) * 100f);

            var text = new Label($"{shown.Hp}/{creature.MaxHp}");
            text.AddToClassList("turn-portrait__hp-text");

            bar.Add(fill);
            bar.Add(text);
            return bar;
        }
    }
}

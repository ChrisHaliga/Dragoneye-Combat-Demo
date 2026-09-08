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

        // The line above the action bar that says whose turn it is. It never goes away, so it is
        // the one place a player can always look to answer that.
        Label m_Announce;

        // The creature that has just finished, so its card can be seen going to the back of the
        // queue rather than simply being somewhere else the next time the row is drawn.
        uint m_JustFinished;
        uint m_LastActive;

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
            m_Announce = root.Q<Label>("turn-announce");

            if (m_Order == null || m_Announce == null)
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
                RefreshAnnouncement();
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
        /// Whose turn it is, on the line above the action bar.
        ///
        /// The person where there is one, the creature where there is not: "Ada's turn" is what a
        /// player wants to know in a match, and "Wolf's turn" is what there is to say when nobody
        /// is playing it. It stays up for the whole turn rather than announcing itself and going
        /// away, because the question it answers is asked at any moment, not once.
        /// </summary>
        void RefreshAnnouncement()
        {
            var fight = Shown.Fight;

            if (fight == null || !fight.Began || fight.IsOver)
            {
                m_Announce.text = string.Empty;
                return;
            }

            var creature = m_Creatures.ByTurnId(fight.ActiveId);

            if (creature == null)
            {
                m_Announce.text = string.Empty;
                return;
            }

            // Your own turn is a different sentence, not a smaller one. A tester played a whole
            // game without being sure when it was their go, because "Ansel's turn" and "Goblin 2's
            // turn" are the same shape and you have to remember which of them is you.
            var yours = LocalPlayer.Controls(creature);

            var who = creature.IsComputerControlled
                ? creature.DisplayName
                : CreatureDisplay.ControllerName(creature);

            var line = yours ? $"YOUR TURN  ·  {creature.DisplayName}" : $"{who}'s turn";

            m_Announce.text = m_Fast ? line + "  ·  FAST FORWARD" : line;
            m_Announce.EnableInClassList("turn-announce--yours", yours);
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

            RefreshAnnouncement();

            if (!showing)
            {
                return;
            }

            // A queue rather than a list with markers on it. Whoever is acting is at the front,
            // whoever is still to act follows, and everybody who has already been is round the
            // back waiting for the next round -- which is how the question "how long until I act
            // again" gets answered by counting from the left instead of by working out which half
            // of a fixed order you are in.
            if (fight.ActiveId != m_LastActive)
            {
                m_JustFinished = m_LastActive;
                m_LastActive = fight.ActiveId;
            }

            var reached = 0;

            for (var i = 0; i < fight.Order.Count; i++)
            {
                if (fight.Order[i] == fight.ActiveId)
                {
                    reached = i;
                    break;
                }
            }

            for (var i = reached; i < fight.Order.Count; i++)
            {
                Place(fight, fight.Order[i], acted: false);
            }

            // Where the round ends. Everything past this bar acts again next round, which is the
            // one thing a flat row of faces could never say.
            if (reached > 0)
            {
                var mark = new VisualElement();
                mark.AddToClassList("turn-break");
                mark.tooltip = $"The end of round {fight.Round}.";
                mark.pickingMode = PickingMode.Ignore;
                m_Order.Add(mark);
            }

            for (var i = 0; i < reached; i++)
            {
                Place(fight, fight.Order[i], acted: true);
            }

            m_JustFinished = 0;
        }

        void Place(PresentedFight fight, uint id, bool acted)
        {
            var creature = m_Creatures.ByTurnId(id);
            var shown = fight.Of(id);

            if (creature == null || shown == null)
            {
                return;
            }

            m_Order.Add(BuildPortrait(creature, shown, id == fight.ActiveId, acted));
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
            CreatureDisplay.ShowElementsOnHover(root, creature, columns: 2, placement: "rune-grid--below");
            CreatureDisplay.DrawVitals(root, creature, numbers: active);

            if (active)
            {
                var name = new Label(creature.DisplayName);
                name.AddToClassList("turn-portrait__name");
                name.pickingMode = PickingMode.Ignore;
                root.Add(name);
            }

            root.tooltip = $"{creature.DisplayName}\n{CreatureDisplay.ControllerName(creature)}"
                + "\n\nClick to look at it.";

            // A face in a queue raises one question -- where is that -- and the answer is on the
            // board. So a click points the camera at it and opens its card, which is the pair of
            // things a player wants and neither of which touches the turn.
            if (m_Selection != null)
            {
                root.pickingMode = PickingMode.Position;
                root.RegisterCallback<PointerDownEvent>(evt =>
                {
                    TurnCameraFocus.Current?.LookAt(creature);
                    m_Selection.Select(creature);
                    evt.StopPropagation();
                });
            }

            // The one that has just finished is seen arriving at the back rather than appearing
            // there. Started off its place and released a frame later, so the transition on the
            // card carries it in.
            if (creature.TurnId == m_JustFinished && m_JustFinished != 0)
            {
                root.AddToClassList("turn-portrait--arriving");
                root.schedule.Execute(() => root.RemoveFromClassList("turn-portrait--arriving"));
            }

            return root;
        }

    }
}

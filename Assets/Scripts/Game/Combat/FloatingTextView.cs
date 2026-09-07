using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;
using UnityEngine.UIElements;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Numbers that rise off a creature and fade: what it just earned, over its head.
    ///
    /// UI Toolkit rather than world-space text, so it is drawn at the same crispness and the same
    /// scale as everything else on the HUD. The cost is that each note has to be positioned every
    /// frame from the creature it belongs to -- which is also the point: a creature that walks away
    /// takes its number with it, rather than leaving it hanging over empty ground.
    ///
    /// Presentation only. It is driven by the record as it is shown, and by the few local
    /// notices -- a miss arriving -- that a view raises about a moment of its own.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class FloatingTextView : MonoBehaviour
    {
        /// <summary>One note, and the creature it is following.</summary>
        sealed class Note
        {
            public Label Label;
            public uint TurnId;
            public float Age;

            /// <summary>How many notes were already on this creature when this one appeared.</summary>
            public int Stack;
        }

        [SerializeField]
        CreatureRegistry m_Creatures;

        [SerializeField, Tooltip("How long a note stays on screen.")]
        float m_Lifetime = 2.4f;

        [SerializeField, Tooltip("World units the note drifts upward over its life.")]
        float m_Rise = 0.9f;

        [SerializeField, Min(1f), Tooltip("How much larger a note starts than it settles at.")]
        float m_Pop = 1.6f;

        [SerializeField, Min(0.01f), Tooltip("Seconds the pop takes to settle.")]
        float m_PopTime = 0.18f;

        [SerializeField, Tooltip("Height above the creature's origin the note starts at.")]
        float m_Height = 1.6f;

        readonly List<Note> m_Notes = new List<Note>();

        VisualElement m_Layer;
        CombatPlayback m_Playback;

        void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            m_Layer = root.Q<VisualElement>("floating-text");

            if (m_Layer == null || m_Creatures == null)
            {
                Debug.LogError($"{nameof(FloatingTextView)} is missing its layer or registry.", this);
                enabled = false;
                return;
            }

            CombatNotices.Raised += OnNotice;
            Listen();
        }

        void OnDestroy()
        {
            CombatNotices.Raised -= OnNotice;

            if (m_Playback != null)
            {
                m_Playback.Presenting -= OnPresenting;
            }
        }

        void Listen()
        {
            var playback = CombatPlayback.Current;

            if (playback == null || playback == m_Playback)
            {
                return;
            }

            if (m_Playback != null)
            {
                m_Playback.Presenting -= OnPresenting;
            }

            m_Playback = playback;
            m_Playback.Presenting += OnPresenting;
        }

        /// <summary>What the record says, as it is shown, where it leaves a number or a mark.</summary>
        void OnPresenting(CombatEvent e)
        {
            switch (e.Kind)
            {
                case CombatEventKind.Damaged:
                    OnNotice(e.Target, CombatNotices.Damage(e.Amount, e.Absorbed), NoticeTone.Loss,
                        e.Amount > 0 ? NoticeMark.Hit : NoticeMark.Guard);
                    break;

                case CombatEventKind.Healed:
                case CombatEventKind.Recovered:
                    if (e.Amount > 0)
                    {
                        OnNotice(e.Actor, $"+{e.Amount} HP", NoticeTone.Gain, NoticeMark.None);
                    }

                    break;

                case CombatEventKind.ApRestored:
                    OnNotice(e.Actor, $"+{e.Amount} AP", NoticeTone.Gain, NoticeMark.None);
                    break;

                // Turned aside by an answer: nothing to count, so the mark is the whole note.
                case CombatEventKind.ClashResolved:
                    if (e.Outcome != ClashOutcome.AttackerWins)
                    {
                        OnNotice(e.Target, string.Empty, NoticeTone.Gain, NoticeMark.Guard);
                    }

                    break;

                case CombatEventKind.Fell:
                    if (e.Amount > 0 && e.Target != 0)
                    {
                        OnNotice(e.Target, $"+{e.Amount} XP", NoticeTone.Gain, NoticeMark.None);
                    }

                    break;
            }
        }

        void Update()
        {
            Listen();
            Advance(Time.deltaTime);
        }

        void OnNotice(uint turnId, string text, NoticeTone tone, NoticeMark mark)
        {
            if (m_Layer == null)
            {
                return;
            }

            var label = new Label(text);
            label.AddToClassList("floating-note");
            label.EnableInClassList("floating-note--loss", tone == NoticeTone.Loss);
            label.pickingMode = PickingMode.Ignore;

            // Hung off the left edge rather than laid out beside the text, so the note stays
            // centred on the creature: the position below is worked out from the label's width.
            if (mark != NoticeMark.None)
            {
                var shape = new VisualElement();
                shape.AddToClassList("floating-note__mark");
                shape.AddToClassList(mark == NoticeMark.Hit
                    ? "floating-note__mark--hit"
                    : "floating-note__mark--guard");

                // With no number beside it the mark is the note, so it sits in the label rather
                // than hanging off the edge of one that has no width.
                shape.EnableInClassList("floating-note__mark--alone", text.Length == 0);
                shape.pickingMode = PickingMode.Ignore;
                label.Add(shape);
            }

            // Stacked, so two things happening to one creature in the same breath do not draw on
            // top of each other and read as neither.
            m_Layer.Add(label);
            m_Notes.Add(new Note { Label = label, TurnId = turnId, Stack = StackFor(turnId) });
        }

        /// <summary>How many notes are already riding on this creature.</summary>
        int StackFor(uint turnId)
        {
            var count = 0;

            foreach (var note in m_Notes)
            {
                if (note.TurnId == turnId)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Moves every note to where its creature is, lifts it, fades it, and drops the expired.
        ///
        /// A note whose creature has gone keeps its last position rather than vanishing, because
        /// the number is about what happened and the creature is only where it happened.
        /// </summary>
        void Advance(float deltaTime)
        {
            if (m_Notes.Count == 0)
            {
                return;
            }

            var camera = Camera.main;

            for (var i = m_Notes.Count - 1; i >= 0; i--)
            {
                var note = m_Notes[i];
                note.Age += deltaTime;

                if (note.Age >= m_Lifetime)
                {
                    note.Label.RemoveFromHierarchy();
                    m_Notes.RemoveAt(i);
                    continue;
                }

                var life = note.Age / m_Lifetime;

                // Solid for most of its life and gone quickly at the end, rather than fading from
                // the moment it appears -- a number that starts disappearing as it arrives is a
                // number that was never quite there.
                note.Label.style.opacity = 1f - Mathf.Clamp01((life - 0.6f) / 0.4f);

                // Arrives large and settles, which is what makes it land rather than float up.
                var settle = Mathf.Clamp01(note.Age / m_PopTime);
                var scale = Mathf.Lerp(m_Pop, 1f, 1f - ((1f - settle) * (1f - settle)));
                note.Label.style.scale = new Scale(new Vector2(scale, scale));

                var creature = m_Creatures.ByTurnId(note.TurnId);

                if (creature == null || camera == null)
                {
                    continue;
                }

                var world = creature.transform.position
                    + Vector3.up * (m_Height + (m_Rise * life) + (note.Stack * 0.35f));

                var panel = RuntimePanelUtils.CameraTransformWorldToPanel(
                    m_Layer.panel, world, camera);

                // Centred on the creature rather than hung off its left edge, which is what the
                // half-width correction is doing -- the label sizes itself to its text.
                note.Label.style.left = panel.x - (note.Label.resolvedStyle.width * 0.5f);
                note.Label.style.top = panel.y;
            }
        }
    }
}

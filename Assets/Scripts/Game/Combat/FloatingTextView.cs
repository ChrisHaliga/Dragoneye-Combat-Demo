using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Dragoneye.Game
{
    /// <summary>
    /// Numbers that rise off a creature and fade: what it just earned, over its head.
    ///
    /// UI Toolkit rather than world-space text, so it is drawn at the same crispness and the same
    /// scale as everything else on the HUD. The cost is that each note has to be positioned every
    /// frame from the creature it belongs to -- which is also the point: a creature that walks away
    /// takes its number with it, rather than leaving it hanging over empty ground.
    ///
    /// Presentation only. It is driven by an announcement from the server and holds no state that
    /// anything else reads, so a client that misses one has missed a number and not a rule.
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
        float m_Lifetime = 5f;

        [SerializeField, Tooltip("World units the note drifts upward over its life.")]
        float m_Rise = 1.2f;

        [SerializeField, Tooltip("Height above the creature's origin the note starts at.")]
        float m_Height = 1.6f;

        readonly List<Note> m_Notes = new List<Note>();

        VisualElement m_Layer;

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

            // A static channel rather than a spawned object's event: notes come from creatures and
            // from the match, both of which appear and vanish on their own schedule, and a view
            // that had to chase each source would miss the ones raised before it caught up.
            CombatNotices.Raised += OnNotice;
        }

        void OnDestroy() => CombatNotices.Raised -= OnNotice;

        void Update() => Advance(Time.deltaTime);

        void OnNotice(uint turnId, string text, NoticeTone tone)
        {
            if (m_Layer == null)
            {
                return;
            }

            var label = new Label(text);
            label.AddToClassList("floating-note");
            label.EnableInClassList("floating-note--loss", tone == NoticeTone.Loss);
            label.pickingMode = PickingMode.Ignore;

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
        /// A note whose creature has gone -- killed in the same exchange that paid for it -- keeps
        /// its last position rather than vanishing, because the number is about what happened and
        /// the creature is only where it happened.
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
                note.Label.style.opacity = 1f - (life * life);

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

using Dragoneye.Hex;
using UnityEngine;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Draws a unit and walks it toward the cell it occupies.
    ///
    /// The contract this class exists to keep, and which is the thing most likely to get broken by
    /// a later change:
    ///
    /// - <b>Data never waits.</b> <see cref="UnitState.Cell"/> is authoritative the instant the
    ///   server writes it. Nothing here is ever read back by gameplay.
    /// - <b>The view may be arbitrarily behind.</b> Clicking again mid-walk retargets. Never queue
    ///   waypoints and never snap to catch up.
    /// - <b>Gameplay reads the cell, never this transform.</b> Range, occupancy and targeting are
    ///   all cell-space questions.
    /// - <b>Spawning teleports; changes animate.</b> Otherwise every unit slides in from the origin
    ///   on join.
    ///
    /// Scaled <c>Time.deltaTime</c>, unlike the camera: this is gameplay and should respect pause
    /// and hit-stop.
    ///
    /// This slice slides in a straight line and walks through anything in the way. That is a
    /// property of the slice, not a bug -- pathing belongs with A* over the map later.
    /// </summary>
    [RequireComponent(typeof(UnitState))]
    [DisallowMultipleComponent]
    public sealed class UnitView : MonoBehaviour
    {
        [SerializeField, Tooltip("The renderer that takes the party colour.")]
        Renderer m_Body;

        [SerializeField, Tooltip("The disc on top of the token that wears the portrait. Hidden "
             + "when this creature has no picture available on this machine.")]
        Renderer m_Portrait;

        [SerializeField, Tooltip("World units per second. One tile is about 1.7 units.")]
        float m_Speed = 6f;

        [SerializeField, Tooltip("Height above the tile surface.")]
        float m_GroundOffset = 0.5f;

        [SerializeField, Tooltip("Degrees per second the unit turns to face where it is going.")]
        float m_TurnSpeed = 540f;

        [SerializeField, Tooltip("Colour property on the material. URP Lit uses _BaseColor.")]
        string m_ColorProperty = "_BaseColor";

        UnitState m_State;
        CreatureState m_Creature;
        MaterialPropertyBlock m_PropertyBlock;
        MaterialPropertyBlock m_PortraitBlock;
        int m_ColorPropertyId;

        static readonly int k_BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int k_BaseMapSt = Shader.PropertyToID("_BaseMap_ST");

        Vector3 m_Target;
        bool m_Placed;

        void Awake()
        {
            m_State = GetComponent<UnitState>();
            m_Creature = GetComponent<CreatureState>();
            m_PropertyBlock = new MaterialPropertyBlock();
            m_PortraitBlock = new MaterialPropertyBlock();
            m_ColorPropertyId = Shader.PropertyToID(m_ColorProperty);

            if (m_Body == null)
            {
                Debug.LogError($"{nameof(UnitView)} has no body renderer assigned.", this);
                enabled = false;
            }
        }

        void OnEnable()
        {
            m_State.CellChanged += OnCellChanged;

            if (m_Creature != null)
            {
                m_Creature.Changed += Repaint;
            }

            Repaint();
        }

        void OnDisable()
        {
            m_State.CellChanged -= OnCellChanged;

            if (m_Creature != null)
            {
                m_Creature.Changed -= Repaint;
            }
        }

        void Update()
        {
            if (!m_Placed)
            {
                return;
            }

            transform.position = Step(transform.position, m_Target, m_Speed, Time.deltaTime);
            Face(m_Target, Time.deltaTime);
        }

        /// <summary>
        /// One frame of movement. Extracted as a pure function so the animation contract -- constant
        /// speed, never overshoot, always arrive -- can be asserted without a scene.
        ///
        /// MoveTowards rather than SmoothDamp: constant speed keeps "a tile takes N seconds"
        /// predictable, which eased movement does not.
        /// </summary>
        public static Vector3 Step(Vector3 current, Vector3 target, float speed, float deltaTime) =>
            Vector3.MoveTowards(current, target, Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime));

        void OnCellChanged(Hex cell)
        {
            var context = ArenaContext.Current;
            if (context == null || context.Map == null)
            {
                // Silent here meant the unit sat on the origin forever with nothing logged. This is
                // the failure mode ArenaContext exists to eliminate, so it says so.
                Debug.LogError("UnitView has no arena context; the unit cannot be placed.", this);
                return;
            }

            m_Target = context.Map.ToWorld(cell) + Vector3.up * m_GroundOffset;

            if (!m_Placed)
            {
                // First placement is a teleport; only later changes animate.
                transform.position = m_Target;
                m_Placed = true;
            }
        }

        void Face(Vector3 target, float deltaTime)
        {
            var toTarget = target - transform.position;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude < 1e-4f)
            {
                return;
            }

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(toTarget), m_TurnSpeed * deltaTime);
        }

        void Repaint()
        {
            // Party, not player. Friend-or-foe is the read a player makes constantly, and it gets
            // the largest surface; which specific player controls a creature is the ring's inner
            // accent. This used to colour by a UnitState.OwnerSlot that nothing ever wrote, so every
            // body rendered as slot -1 -- the first palette entry, for every unit on the board.
            var color = m_Creature != null ? PartyPalette.ForParty(m_Creature.Party) : Color.white;

            // A property block rather than material.color, which would leak a material instance per
            // unit and break instancing.
            m_Body.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetColor(m_ColorPropertyId, color);
            m_Body.SetPropertyBlock(m_PropertyBlock);

            RepaintPortrait();
        }

        /// <summary>
        /// Puts this creature's face on the top of its token.
        ///
        /// The disc is hidden rather than blanked when there is no picture: an empty white circle
        /// on top of a coloured checker reads as a bug, and the bare top of the token does not.
        ///
        /// Set through a property block for the same reason the body colour is -- one material for
        /// every token on the board, and no instance leaked per creature.
        /// </summary>
        void RepaintPortrait()
        {
            if (m_Portrait == null || m_Creature == null)
            {
                return;
            }

            if (!CreatureDisplay.TryPortraitTexture(m_Creature, out var texture, out var scaleOffset))
            {
                m_Portrait.enabled = false;
                return;
            }

            m_Portrait.enabled = true;

            m_Portrait.GetPropertyBlock(m_PortraitBlock);
            m_PortraitBlock.SetTexture(k_BaseMap, texture);
            m_PortraitBlock.SetVector(k_BaseMapSt, scaleOffset);
            m_Portrait.SetPropertyBlock(m_PortraitBlock);
        }
    }
}

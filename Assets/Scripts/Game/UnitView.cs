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

        Renderer m_Portrait;

        [SerializeField, Tooltip("World units per second. One tile is about 1.7 units.")]
        float m_Speed = 6f;

        [SerializeField, Tooltip("Height above the tile surface.")]
        float m_GroundOffset = 0.5f;

        [SerializeField, Min(60f), Tooltip("Degrees per second the facing mark turns once the "
             + "creature has landed. Fast: the rule is already true, this is the mark catching up.")]
        float m_FacingTurnSpeed = 540f;

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

        Transform m_Pointer;

        static Material s_FacingMaterial;

        /// <summary>
        /// Whether this unit is still walking to where it already is, as far as the rules go.
        ///
        /// The data never waits for the view -- a creature occupies its new cell the instant the
        /// server says so. This is for pacing only: something that wants a turn to be watchable can
        /// ask whether the last move has finished being drawn before starting the next one.
        /// </summary>
        public bool IsMoving =>
            m_Placed && (transform.position - m_Target).sqrMagnitude > 0.0004f;

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
                return;
            }

            // Never fatal. A unit that cannot be reshaped should still be a unit somebody can see
            // and click, and the reason should be on screen rather than inferred from an empty
            // board -- which is exactly how the last attempt at this failed.
            try
            {
                BuildToken();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"{nameof(UnitView)} could not build its token: {e}", this);
            }
        }

        /// <summary>
        /// Reshapes this unit into a token: a short cylinder wearing a disc for its face.
        ///
        /// A checker rather than a figure. The board is read from above at a shallow angle, where a
        /// standing capsule is a coloured smudge that hides the tile behind it and says nothing
        /// about who is standing there. A flat disc reads as a piece on a board, and the face is the
        /// part a player recognises.
        ///
        /// Done here rather than baked into the prefab because a shape a unit is made of is what a
        /// unit *is*, and because the editor step that used to do it failed silently -- leaving the
        /// meshes on disk, the prefab untouched, and capsules on the board with nothing to say why.
        ///
        /// Everything is positioned against the ground offset rather than assuming it, so the
        /// token's base lands on the tile whatever the prefab happens to say.
        /// </summary>
        void BuildToken()
        {
            var baseY = -m_GroundOffset;

            var bodyTransform = m_Body.transform;
            var filter = m_Body.GetComponent<MeshFilter>();

            if (filter != null)
            {
                filter.sharedMesh = CreatureToken.Cylinder;
            }

            // The mesh is one unit tall and one across, so the scale is the size.
            bodyTransform.localScale = new Vector3(
                CreatureToken.Radius * 2f, CreatureToken.Height, CreatureToken.Radius * 2f);
            bodyTransform.localPosition = new Vector3(0f, baseY + (CreatureToken.Height * 0.5f), 0f);

            // The rings sit just under the token's lip, where they read as a base rather than as
            // something the token is hovering over.
            Sit(transform.Find("Party Ring"), baseY + 0.004f);
            Sit(transform.Find("Player Accent"), baseY + 0.008f);

            m_Portrait = BuildPortrait(baseY);
            m_Pointer = BuildPointer(baseY);
        }

        /// <summary>
        /// The wedge that says which way this creature is turned.
        ///
        /// Its own child rather than part of the token, because it turns and the token does not:
        /// the portrait on the face has an up, and spinning it so the player can read a facing
        /// would make every creature look like it was falling over.
        /// </summary>
        Transform BuildPointer(float baseY)
        {
            var pointer = transform.Find("Facing");

            if (pointer == null)
            {
                pointer = new GameObject("Facing").transform;
                pointer.SetParent(transform, worldPositionStays: false);
            }

            var filter = Ensure<MeshFilter>(pointer.gameObject);
            var renderer = Ensure<MeshRenderer>(pointer.gameObject);

            filter.sharedMesh = CreatureToken.Pointer;
            renderer.sharedMaterial = FacingMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Just above the rings and just below the token's lip, so it reads as attached to the
            // base rather than as a decal on the floor.
            pointer.localPosition = new Vector3(0f, baseY + 0.012f, 0f);
            pointer.localScale = Vector3.one;

            return pointer;
        }

        /// <summary>
        /// One material for every facing mark in the arena.
        ///
        /// Shared, because a material per creature is a draw call per creature for a triangle.
        /// </summary>
        static Material FacingMaterial
        {
            get
            {
                if (s_FacingMaterial != null)
                {
                    return s_FacingMaterial;
                }

                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color");

                s_FacingMaterial = new Material(shader) { name = "Facing" };
                s_FacingMaterial.SetColor("_BaseColor", new Color(0.94f, 0.90f, 0.78f, 1f));
                s_FacingMaterial.color = new Color(0.94f, 0.90f, 0.78f, 1f);

                return s_FacingMaterial;
            }
        }

        /// <summary>
        /// The component, adding it if it is not there.
        ///
        /// Written out rather than done with <c>??</c>, which is the trap this whole thing fell
        /// into: a Unity object that has been destroyed, or was never really there, is not
        /// reference-null even though <c>== null</c> says it is. The null-coalescing operator does
        /// not go through that operator, so it happily hands back an object that then throws the
        /// moment it is touched -- which is what left the board empty, and, before that, silently
        /// stopped the editor step that was meant to build these in the first place.
        /// </summary>
        static T Ensure<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            return existing == null ? target.AddComponent<T>() : existing;
        }

        static void Sit(Transform ring, float height)
        {
            if (ring != null)
            {
                ring.localPosition = new Vector3(0f, height, 0f);
            }
        }

        /// <summary>
        /// The disc that wears the face.
        ///
        /// It borrows the body's material rather than finding a shader of its own: a shader looked
        /// up by name is a shader that can be stripped out of a build for not being referenced, and
        /// the disc faces the light anyway so lit and unlit look the same on it.
        /// </summary>
        Renderer BuildPortrait(float baseY)
        {
            var existing = transform.Find("Portrait");
            var portrait = existing != null
                ? existing.gameObject
                : new GameObject("Portrait");

            portrait.transform.SetParent(transform, false);
            portrait.transform.localPosition = new Vector3(0f, baseY + CreatureToken.Height + 0.004f, 0f);
            portrait.transform.localRotation = Quaternion.identity;

            var size = CreatureToken.Radius * 2f * CreatureToken.PortraitInset;
            portrait.transform.localScale = new Vector3(size, 1f, size);

            var filter = Ensure<MeshFilter>(portrait);
            filter.sharedMesh = CreatureToken.Disc;

            var renderer = Ensure<MeshRenderer>(portrait);
            renderer.sharedMaterial = m_Body.sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return renderer;
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
            PointTheWay();
        }

        /// <summary>
        /// Turns the facing mark to match the creature.
        ///
        /// The token itself does not turn. It used to swing round to look where it was walking,
        /// which spun the portrait on its face and, now that facing is a rule rather than a
        /// flourish, showed a direction that had nothing to do with the one the rules use.
        ///
        /// **After it has landed, not while it is walking.** A move reads as three beats -- go,
        /// arrive, turn -- and turning on the way there loses the third one entirely: the creature
        /// simply appears somewhere already facing a new way, and the player never sees the choice
        /// they just made happen.
        ///
        /// Eased rather than snapped, and quickly. The rule is already true the moment the server
        /// says so; this is only the mark catching up, and a quarter of a second of it is the
        /// difference between a piece being moved and a piece teleporting.
        /// </summary>
        void PointTheWay()
        {
            if (m_Pointer == null || m_Creature == null || IsMoving)
            {
                return;
            }

            // The hex directions run clockwise from north, which is exactly what a Y rotation of
            // sixty degrees a step describes -- but north is the arena's north, not the world's.
            // Taken from the map rather than assumed, so a board laid down at an angle keeps its
            // bearings instead of pointing every creature somewhere plausible and wrong.
            var arena = ArenaContext.Current != null ? ArenaContext.Current.Map : null;
            var basis = arena != null ? arena.transform.rotation : Quaternion.identity;
            var wanted = basis * Quaternion.Euler(0f, m_Creature.Facing.Index * 60f, 0f);

            m_Pointer.rotation = Quaternion.RotateTowards(m_Pointer.rotation, wanted,
                m_FacingTurnSpeed * Time.deltaTime);
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

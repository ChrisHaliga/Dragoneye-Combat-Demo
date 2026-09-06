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

        // The last health this token drew, and how much of a hit-flash is left to play. A number
        // rising off a creature says what happened; the creature itself going white for a tenth of
        // a second says where, which is the half the eye actually uses.
        int m_LastHp = -1;
        float m_Flash;
        Color m_BodyColour = Color.white;

        // Where the token still has to go, tile by tile, and which leg it is on. A move is one
        // instant to the rules and a walk to everybody watching.
        readonly System.Collections.Generic.List<Vector3> m_Route =
            new System.Collections.Generic.List<Vector3>();

        int m_Leg;
        Hex m_Cell;

        Transform m_Pointer;

        static Material s_FacingMaterial;
        static Material s_ShadowMaterial;

        /// <summary>How high the token sits above its tile. Anything drawn on the tile under it needs it.</summary>
        public float GroundOffset => m_GroundOffset;

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
            BuildShadow(baseY);
        }

        /// <summary>
        /// The soft dark disc under the token that makes it a thing standing on a tile.
        ///
        /// The directional light casts a real shadow too, but from a low sun it falls sideways off
        /// the tile and reads as belonging to somebody else. A contact shadow directly underneath
        /// is what the eye uses to decide whether an object is resting on a surface or floating a
        /// little above it, and every token was floating a little above it.
        ///
        /// Under the rings, above the tile: it is the lowest thing the token owns.
        /// </summary>
        void BuildShadow(float baseY)
        {
            var existing = transform.Find("Shadow");
            var shadow = existing != null ? existing.gameObject : new GameObject("Shadow");

            shadow.transform.SetParent(transform, worldPositionStays: false);
            shadow.transform.localPosition = new Vector3(0f, baseY + 0.002f, 0f);
            shadow.transform.localRotation = Quaternion.identity;

            var size = CreatureToken.Radius * 2f * 1.7f;
            shadow.transform.localScale = new Vector3(size, 1f, size);

            Ensure<MeshFilter>(shadow).sharedMesh = CreatureToken.Disc;

            var renderer = Ensure<MeshRenderer>(shadow);
            renderer.sharedMaterial = ShadowMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>One material for every contact shadow on the board.</summary>
        static Material ShadowMaterial
        {
            get
            {
                if (s_ShadowMaterial != null)
                {
                    return s_ShadowMaterial;
                }

                s_ShadowMaterial = WorldArt.Unlit("Token Shadow", WorldArt.Shadow, transparent: true);

                var tint = new Color(0f, 0f, 0f, 0.62f);
                s_ShadowMaterial.color = tint;
                s_ShadowMaterial.SetColor("_BaseColor", tint);

                return s_ShadowMaterial;
            }
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

            Walk(Time.deltaTime);
            PointTheWay();
            Flash(Time.deltaTime);
        }

        /// <summary>
        /// Plays out the hit-flash: white on the frame of the hit, back to the party colour over a
        /// quarter of a second. Quick, because it is punctuation and not a state.
        /// </summary>
        void Flash(float deltaTime)
        {
            if (m_Flash <= 0f)
            {
                return;
            }

            m_Flash = Mathf.Max(0f, m_Flash - (deltaTime / 0.25f));
            ApplyBodyColour(Color.Lerp(m_BodyColour, Color.white, m_Flash * m_Flash));
        }

        void ApplyBodyColour(Color colour)
        {
            // A property block rather than material.color, which would leak a material instance per
            // unit and break instancing.
            m_Body.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetColor(m_ColorPropertyId, colour);
            m_Body.SetPropertyBlock(m_PropertyBlock);
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
        /// One frame of walking, along the route rather than through it.
        ///
        /// A creature used to slide from where it was to where it ended up in a straight line,
        /// which took it clean through anybody standing between the two -- and the route the rules
        /// costed had already gone round them. The pathfinder was right all along; the token was
        /// drawing a different move from the one that happened.
        /// </summary>
        void Walk(float deltaTime)
        {
            while (m_Leg < m_Route.Count)
            {
                var leg = m_Route[m_Leg];
                transform.position = Step(transform.position, leg, m_Speed, deltaTime);

                if ((transform.position - leg).sqrMagnitude > 0.0004f)
                {
                    return;
                }

                // Arrived at this corner with time left in the frame; spend the rest on the next
                // one, so a fast token is not held to one tile per frame.
                m_Leg++;
            }

            transform.position = Step(transform.position, m_Target, m_Speed, deltaTime);
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

            var previous = m_Cell;
            m_Cell = cell;

            m_Target = context.Map.ToWorld(cell) + Vector3.up * m_GroundOffset;
            m_Route.Clear();
            m_Leg = 0;

            if (!m_Placed)
            {
                // First placement is a teleport; only later changes animate.
                transform.position = m_Target;
                m_Placed = true;
                return;
            }

            BuildRoute(context, previous, cell);
        }

        /// <summary>
        /// The corners the token turns on its way, from the same search that priced the move.
        ///
        /// Worked out here rather than sent from the server: every peer has the map and the
        /// occupancy, so the route is derivable, and a move that already fits in one small message
        /// should not grow a list of hexes.
        ///
        /// Both ends are excluded from what blocks it -- the tile behind, because it is being left,
        /// and the tile ahead, because this creature is already standing on it as far as the index
        /// is concerned. Anything left in between is somebody else, and the walk goes round.
        ///
        /// An empty route means there is no walkable way there, which is what a spawn or a despawn
        /// looks like. The straight line stands in for it; there is nothing better to draw.
        /// </summary>
        void BuildRoute(ArenaContext context, Hex from, Hex to)
        {
            if (context.Units == null || from == to)
            {
                return;
            }

            var board = new ArenaBoard(context.Map, context.Units);
            var path = board.PathTo(from, to, to);

            // One step is a straight line already, and anything longer only needs its corners.
            for (var i = 0; i + 1 < path.Count; i++)
            {
                m_Route.Add(context.Map.ToWorld(path[i]) + Vector3.up * m_GroundOffset);
            }
        }

        void Repaint()
        {
            // Party, not player. Friend-or-foe is the read a player makes constantly, and it gets
            // the largest surface; which specific player controls a creature is the ring's inner
            // accent. This used to colour by a UnitState.OwnerSlot that nothing ever wrote, so every
            // body rendered as slot -1 -- the first palette entry, for every unit on the board.
            m_BodyColour = m_Creature != null ? PartyPalette.ForParty(m_Creature.Party) : Color.white;

            // Health going down is a hit. Detected here rather than announced, because the number
            // is replicated to everybody already and a second message saying the same thing would
            // be a second thing to keep in step.
            if (m_Creature != null)
            {
                var hp = m_Creature.CurrentHp;

                if (m_LastHp >= 0 && hp < m_LastHp)
                {
                    m_Flash = 1f;
                }

                m_LastHp = hp;
            }

            ApplyBodyColour(m_Flash > 0f ? Color.white : m_BodyColour);

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

using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Sim;
using UnityEngine;
using Dragoneye.Game.Combat;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases, so out here the bare name Hex would still bind to the
    // Dragoneye.Hex namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// Draws a unit, and walks it where the record says it walked.
    ///
    /// The contract this class exists to keep:
    ///
    /// - <b>The token draws the shown fight, never the live one.</b> Where the rules have put the
    ///   creature is nobody's business here; where the playback has shown it going is. A walk
    ///   starts when the walk is shown, along the route the server priced, corner by corner,
    ///   through the gap in the wall it actually went through.
    /// - <b>Gameplay reads the cell, never this transform.</b> Range, occupancy and targeting are
    ///   all cell-space questions.
    /// - <b>Spawning teleports; the record animates.</b> Otherwise every unit slides in from the
    ///   origin on join.
    /// - <b>The playback waits for the walk.</b> <see cref="IsMoving"/> is what it waits on, so a
    ///   token is never asked to start a second walk before it has finished the first -- which is
    ///   what used to send one straight through a wall.
    ///
    /// Scaled <c>Time.deltaTime</c>, unlike the camera: this is gameplay and should respect pause.
    /// </summary>
    [RequireComponent(typeof(UnitState))]
    [DisallowMultipleComponent]
    public sealed class UnitView : MonoBehaviour
    {
        /// <summary>
        /// The token that draws this creature, or null on a headless server.
        ///
        /// Asked from here, on the presentation side, and never from the creature: replicated
        /// state that could reach its own renderer is state a bug in the fight could reach, and
        /// one did.
        /// </summary>
        public static UnitView Of(CreatureState creature) =>
            creature != null ? creature.GetComponent<UnitView>() : null;

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

        bool m_Placed;
        bool m_Fallen;

        // How much of a hit-flash is left to play. A number rising off a creature says what
        // happened; the creature itself going white for a tenth of a second says where, which is
        // the half the eye actually uses.
        float m_Flash;
        Color m_BodyColour = Color.white;

        // Where the token still has to go, corner by corner, and which leg it is on. A move is
        // one instant to the rules and a walk to everybody watching.
        readonly List<Vector3> m_Route = new List<Vector3>();
        int m_Leg;

        // The cell the token has been shown standing on. Where the next walk starts from.
        Cell m_Cell;

        Transform m_Pointer;
        CombatPlayback m_Playback;

        // The lean toward whatever this creature just swung at, and where its parts sit when it
        // is standing still. The offset rides on the token's parts rather than on the object
        // itself, because the object's position is where the token has been shown to be and a
        // flourish has no business writing to that.
        Vector3 m_LungeDirection;
        float m_LungeAge = -1f;
        Vector3 m_BodyRest;
        Vector3 m_PortraitRest;
        Vector3 m_PointerRest;
        bool m_RestKnown;

        /// <summary>How far forward the token throws itself, in world units.</summary>
        const float LungeReach = 0.32f;

        /// <summary>How long the whole lean takes, out and back.</summary>
        const float LungeTime = 0.28f;

        static Material s_FacingMaterial;
        static Material s_ShadowMaterial;

        /// <summary>How high the token sits above its tile. Anything drawn on the tile under it needs it.</summary>
        public float GroundOffset => m_GroundOffset;

        /// <summary>Whether this token is still walking a route it was shown. The playback waits on it.</summary>
        public bool IsMoving => m_Placed && m_Leg < m_Route.Count;

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

            m_Portrait = BuildPortrait(baseY);
            m_Pointer = BuildPointer(baseY);
            BuildShadow(baseY);
        }

        /// <summary>
        /// The soft dark disc under the token that makes it a thing standing on a tile.
        ///
        /// A contact shadow directly underneath is what the eye uses to decide whether an object
        /// is resting on a surface or floating a little above it. Under the rings, above the tile:
        /// it is the lowest thing the token owns.
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

        /// <summary>One material for every facing mark in the arena.</summary>
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
        /// Written out rather than done with <c>??</c>: a Unity object that has been destroyed, or
        /// was never really there, is not reference-null even though <c>== null</c> says it is, and
        /// the null-coalescing operator does not go through that operator.
        /// </summary>
        static T Ensure<T>(GameObject target) where T : Component
        {
            var existing = target.GetComponent<T>();
            return existing == null ? target.AddComponent<T>() : existing;
        }

        /// <summary>
        /// The disc that wears the face.
        ///
        /// It borrows the body's material rather than finding a shader of its own: a shader looked
        /// up by name is a shader that can be stripped out of a build for not being referenced.
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

            Listen();
            Repaint();
        }

        void OnDisable()
        {
            m_State.CellChanged -= OnCellChanged;

            if (m_Creature != null)
            {
                m_Creature.Changed -= Repaint;
            }

            Unlisten();
        }

        void Listen()
        {
            var playback = CombatPlayback.Current;

            if (playback == null || playback == m_Playback)
            {
                return;
            }

            Unlisten();
            m_Playback = playback;
            m_Playback.Presenting += OnPresenting;
        }

        void Unlisten()
        {
            if (m_Playback != null)
            {
                m_Playback.Presenting -= OnPresenting;
                m_Playback = null;
            }
        }

        void Update()
        {
            // The playback is made when the arena wakes, before any unit spawns; this is for a
            // unit that somehow came first.
            if (m_Playback == null)
            {
                Listen();
            }

            if (!m_Placed || m_Fallen)
            {
                return;
            }

            Walk(Time.deltaTime * (m_Playback != null ? m_Playback.Speed : 1f));
            PointTheWay();
            Flash(Time.deltaTime);
            Lean(Time.deltaTime);
        }

        /// <summary>What the record says this token did, as it is shown.</summary>
        void OnPresenting(CombatEvent e)
        {
            if (m_Creature == null)
            {
                return;
            }

            var id = m_Creature.TurnId;

            switch (e.Kind)
            {
                case CombatEventKind.Began:
                    SnapToShown();
                    break;

                case CombatEventKind.Moved when e.Actor == id:
                    WalkAlong(e.Path);
                    break;

                case CombatEventKind.Damaged when e.Target == id && e.Amount > 0:
                    m_Flash = 1f;
                    break;

                case CombatEventKind.Swung when e.Actor == id:
                case CombatEventKind.Shot when e.Actor == id:
                    LungeAt(e.Target);
                    break;

                case CombatEventKind.Acted when e.Actor == id && e.HasTarget:
                    LungeAt(e.Target);
                    break;

                case CombatEventKind.Fell when e.Actor == id:
                    Fall();
                    break;
            }
        }

        /// <summary>Where the record says this token stands, taken at the opening.</summary>
        void SnapToShown()
        {
            var context = ArenaContext.Current;
            var shown = Shown.Of(m_Creature);

            if (context == null || context.Map == null || shown == null)
            {
                return;
            }

            m_Cell = shown.Cell;
            m_Route.Clear();
            m_Leg = 0;
            transform.position = context.Map.ToWorld(m_Cell) + Vector3.up * m_GroundOffset;
            m_Placed = true;
        }

        /// <summary>
        /// Throws the token a little way toward something and brings it back.
        ///
        /// Direction only: how far it leans is the same whether the target is next to it or four
        /// tiles away, because the lean says who acted and which way, not how far the blow reached.
        /// </summary>
        void LungeAt(uint targetId)
        {
            var context = ArenaContext.Current;
            var target = Shown.Of(targetId);

            if (context == null || context.Map == null || target == null || targetId == m_Creature.TurnId)
            {
                return;
            }

            var gap = context.Map.ToWorld(target.Cell) - transform.position;
            gap.y = 0f;

            if (gap.sqrMagnitude < 1e-4f)
            {
                return;
            }

            m_LungeDirection = gap.normalized;
            m_LungeAge = 0f;
        }

        /// <summary>
        /// The body leaves the board. Hidden rather than destroyed: the object lives as long as
        /// the arena does, so the fall could be shown at all.
        /// </summary>
        void Fall()
        {
            m_Fallen = true;
            m_Route.Clear();
            m_Leg = 0;

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }
        }

        /// <summary>
        /// One frame of the lean: out fast, back slower, and nothing at all when it is over.
        /// </summary>
        void Lean(float deltaTime)
        {
            if (m_LungeAge < 0f)
            {
                return;
            }

            if (!m_RestKnown)
            {
                m_BodyRest = m_Body != null ? m_Body.transform.localPosition : Vector3.zero;
                m_PortraitRest = m_Portrait != null ? m_Portrait.transform.localPosition : Vector3.zero;
                m_PointerRest = m_Pointer != null ? m_Pointer.localPosition : Vector3.zero;
                m_RestKnown = true;
            }

            m_LungeAge += deltaTime;

            var life = Mathf.Clamp01(m_LungeAge / LungeTime);

            // Out in the first third, back over the rest: a jab rather than a sway.
            var reach = life < 0.34f
                ? life / 0.34f
                : 1f - ((life - 0.34f) / 0.66f);

            var offset = m_LungeDirection * (reach * LungeReach);

            if (m_Body != null)
            {
                m_Body.transform.localPosition = m_BodyRest + offset;
            }

            if (m_Portrait != null)
            {
                m_Portrait.transform.localPosition = m_PortraitRest + offset;
            }

            if (m_Pointer != null)
            {
                m_Pointer.localPosition = m_PointerRest + offset;
            }

            if (life >= 1f)
            {
                m_LungeAge = -1f;
            }
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
        /// Turns the facing mark to match the creature as it has been shown.
        ///
        /// **After it has landed, not while it is walking.** A move reads as three beats -- go,
        /// arrive, turn -- and turning on the way there loses the third one entirely.
        ///
        /// Eased rather than snapped, and quickly: this is only the mark catching up, and a quarter
        /// of a second of it is the difference between a piece being moved and a piece teleporting.
        /// </summary>
        void PointTheWay()
        {
            if (m_Pointer == null || m_Creature == null || IsMoving)
            {
                return;
            }

            // The hex directions run clockwise from north, which is exactly what a Y rotation of
            // sixty degrees a step describes -- but north is the arena's north, not the world's.
            var arena = ArenaContext.Current != null ? ArenaContext.Current.Map : null;
            var basis = arena != null ? arena.transform.rotation : Quaternion.identity;
            var wanted = basis * Quaternion.Euler(0f, Shown.Facing(m_Creature).Index * 60f, 0f);

            m_Pointer.rotation = Quaternion.RotateTowards(m_Pointer.rotation, wanted,
                m_FacingTurnSpeed * Time.deltaTime);
        }

        /// <summary>One frame of walking, corner to corner along the route.</summary>
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
        }

        /// <summary>
        /// One frame of movement. Extracted as a pure function so the animation contract -- constant
        /// speed, never overshoot, always arrive -- can be asserted without a scene.
        /// </summary>
        public static Vector3 Step(Vector3 current, Vector3 target, float speed, float deltaTime) =>
            Vector3.MoveTowards(current, target, Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime));

        /// <summary>
        /// The cell the rules put this unit on. Only the first placement is drawn from it; every
        /// later move is drawn from the record, when it is shown.
        /// </summary>
        void OnCellChanged(Cell cell)
        {
            var context = ArenaContext.Current;

            if (context == null || context.Map == null)
            {
                // Silent here meant the unit sat on the origin forever with nothing logged.
                Debug.LogError("UnitView has no arena context; the unit cannot be placed.", this);
                return;
            }

            if (m_Placed && Shown.Began)
            {
                return;
            }

            m_Cell = cell;
            m_Route.Clear();
            m_Leg = 0;
            transform.position = context.Map.ToWorld(cell) + Vector3.up * m_GroundOffset;
            m_Placed = true;
        }

        /// <summary>
        /// The corners the token turns on its way: for every step, the gap in the wall it goes
        /// through and then the middle of the cell beyond.
        ///
        /// The route is the server's, priced against the board as it was, so the token draws the
        /// move that happened. A straight line between two cell centres used to be enough; on a
        /// tile cut by a wall the centre of a piece can be well to one side of the gap the step
        /// went through, and the straight line went through the wall instead.
        /// </summary>
        void WalkAlong(IReadOnlyList<Cell> path)
        {
            var context = ArenaContext.Current;

            if (context == null || context.Map == null || path == null || path.Count == 0)
            {
                return;
            }

            // A walk shown while the last is still being drawn does not happen: the playback waits
            // for IsMoving. But a second Began, or a carry, can arrive on a token mid-step, so the
            // route restarts from wherever the token is rather than snapping.
            m_Route.Clear();
            m_Leg = 0;

            var from = m_Cell;
            var lift = Vector3.up * m_GroundOffset;

            foreach (var cell in path)
            {
                if (context.Map.TryCrossingPoint(from, cell, out var gap))
                {
                    m_Route.Add(gap + lift);
                }

                m_Route.Add(context.Map.ToWorld(cell) + lift);
                from = cell;
            }

            m_Cell = from;
        }

        void Repaint()
        {
            // The whole token, in the party's colour. Friend or foe is the read a player makes
            // constantly and it now gets the entire surface; which player controls which creature
            // is answered on the portraits, where there is room for it.
            m_BodyColour = m_Creature != null ? PartyPalette.ForParty(m_Creature.Party) : Color.white;
            ApplyBodyColour(m_Flash > 0f ? Color.white : m_BodyColour);
            RepaintPortrait();
        }

        /// <summary>
        /// Puts this creature's face on the top of its token.
        ///
        /// The disc is hidden rather than blanked when there is no picture: an empty white circle
        /// on top of a coloured checker reads as a bug, and the bare top of the token does not.
        /// </summary>
        void RepaintPortrait()
        {
            if (m_Portrait == null || m_Creature == null || m_Fallen)
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

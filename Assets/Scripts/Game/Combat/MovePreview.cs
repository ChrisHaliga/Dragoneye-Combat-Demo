using Dragoneye.Combat;
using UnityEngine;

namespace Dragoneye.Game
{
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// The half-there copy of a creature standing where it is about to stand, turned whichever way
    /// the cursor is pointing.
    ///
    /// A move is two decisions -- where, and which way to face when you get there -- and DE-006
    /// makes the second one part of the first rather than a follow-up action. Asking for both in
    /// one click is impossible; asking for them in two clicks with nothing on screen in between
    /// asks the player to hold a hex and a bearing in their head. So the first click puts the
    /// creature down in ghost form and the second one settles which way it is looking.
    ///
    /// Purely presentation. What is pending, and what happens when it is confirmed, belongs to
    /// <see cref="BoardActionInput"/>; this draws whatever that is holding. A computer creature
    /// never appears here -- it has nobody to show a decision to -- and simply plays the same three
    /// beats when its move arrives: go, land, turn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MovePreview : MonoBehaviour
    {
        [SerializeField, Tooltip("The board input, which owns the pending move.")]
        BoardActionInput m_Input;

        [SerializeField, Range(0f, 1f), Tooltip("How solid the ghost is.")]
        float m_Opacity = 0.4f;

        GameObject m_Ghost;
        Transform m_Pointer;
        Material m_Material;

        void Update()
        {
            var pending = m_Input != null ? m_Input.PendingMove : null;

            if (!pending.HasValue || ArenaContext.Current == null
                || ArenaContext.Current.Map == null)
            {
                Hide();
                return;
            }

            Show(pending.Value, m_Input.PendingFacing, m_Input.Actor);
        }

        void OnDestroy()
        {
            if (m_Material != null)
            {
                Destroy(m_Material);
            }
        }

        void Show(Hex hex, Facing facing, CreatureState actor)
        {
            var arena = ArenaContext.Current.Map;

            Build();

            m_Ghost.SetActive(true);
            m_Ghost.transform.position = arena.ToWorld(hex);

            m_Pointer.rotation = arena.transform.rotation
                * Quaternion.Euler(0f, facing.Index * 60f, 0f);

            // The party's own colour, so a ghost is obviously the creature that is about to move
            // rather than a marker belonging to the board.
            var tint = actor != null ? PartyPalette.ForParty(actor.Party) : Color.white;
            tint.a = m_Opacity;

            m_Material.color = tint;
            m_Material.SetColor("_BaseColor", tint);
        }

        void Hide()
        {
            if (m_Ghost != null)
            {
                m_Ghost.SetActive(false);
            }
        }

        /// <summary>
        /// Builds the ghost once and keeps it.
        ///
        /// Made from the same meshes the real token uses, so it cannot drift out of shape as the
        /// token changes -- and hidden rather than destroyed between moves, because a move is the
        /// most common thing anybody does and rebuilding it every time would allocate on every
        /// click of the game.
        /// </summary>
        void Build()
        {
            if (m_Ghost != null)
            {
                return;
            }

            m_Material = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")) { name = "Move Ghost" };

            // Transparent, and not writing depth: two overlapping translucent pieces that fought
            // over the depth buffer would flicker as the camera moved.
            m_Material.SetFloat("_Surface", 1f);
            m_Material.SetFloat("_ZWrite", 0f);
            m_Material.SetOverrideTag("RenderType", "Transparent");
            m_Material.renderQueue = 3000;
            m_Material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m_Material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m_Material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            m_Ghost = new GameObject("Move Ghost");
            m_Ghost.transform.SetParent(transform, worldPositionStays: false);

            var body = Piece("Body", CreatureToken.Cylinder);
            body.localScale = new Vector3(CreatureToken.Radius * 2f, CreatureToken.Height,
                CreatureToken.Radius * 2f);
            body.localPosition = new Vector3(0f, CreatureToken.Height * 0.5f, 0f);

            m_Pointer = Piece("Facing", CreatureToken.Pointer);
            m_Pointer.localPosition = new Vector3(0f, 0.012f, 0f);
        }

        Transform Piece(string name, Mesh mesh)
        {
            var piece = new GameObject(name).transform;
            piece.SetParent(m_Ghost.transform, worldPositionStays: false);

            piece.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = piece.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = m_Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return piece;
        }
    }
}

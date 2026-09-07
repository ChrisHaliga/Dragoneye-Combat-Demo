using Dragoneye.Combat;
using System.Collections.Generic;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// The route a move would actually take, drawn a tile at a time.
    ///
    /// The pathfinder has always gone round whoever is standing in the way; nothing on screen said
    /// so. A player looking at a straight line between two hexes and a price of three action points
    /// has to work out for themselves which of the two the game means -- and if the difference
    /// between them is a tile that provokes an attack, guessing is expensive.
    ///
    /// Purely presentation, and purely local: the route is derived from the map and the occupancy,
    /// both of which every peer has. It shows only for the creature the local player is acting
    /// with, because it answers "where would this click send me" and nobody else is clicking.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PathPreview : MonoBehaviour
    {
        [SerializeField, Tooltip("The board input, which prices the hover and owns the route.")]
        BoardActionInput m_Input;

        [SerializeField, Range(0f, 1f), Tooltip("How solid the footprints are.")]
        float m_Opacity = 0.55f;

        [SerializeField, Min(0.05f), Tooltip("Footprint size, as a fraction of a token.")]
        float m_Scale = 0.34f;

        readonly List<Transform> m_Steps = new List<Transform>();

        Material m_Material;

        void Update()
        {
            var path = m_Input != null ? m_Input.HoveredPath : null;
            var arena = ArenaContext.Current != null ? ArenaContext.Current.Map : null;

            // The last tile is where the ghost stands, and a footprint under it is a second marker
            // saying what the first one already says.
            var legs = path != null ? path.Count - 1 : 0;

            if (legs <= 0 || arena == null || m_Input.PendingMove.HasValue)
            {
                Hide();
                return;
            }

            Build();

            var tint = m_Input.Actor != null
                ? PartyPalette.ForParty(m_Input.Actor.Party)
                : Color.white;

            tint.a = m_Opacity;
            m_Material.color = tint;
            m_Material.SetColor("_BaseColor", tint);

            for (var i = 0; i < legs; i++)
            {
                Mark(i).position = arena.ToWorld(path[i]) + Vector3.up * 0.02f;
            }

            for (var i = legs; i < m_Steps.Count; i++)
            {
                m_Steps[i].gameObject.SetActive(false);
            }
        }

        void OnDestroy()
        {
            if (m_Material != null)
            {
                Destroy(m_Material);
            }
        }

        void Hide()
        {
            foreach (var step in m_Steps)
            {
                step.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// The footprint for this leg, made once and kept.
        ///
        /// Pooled rather than rebuilt: this runs every frame the cursor is over a reachable tile,
        /// and a route is remade every time the cursor crosses a hex boundary.
        /// </summary>
        Transform Mark(int index)
        {
            while (m_Steps.Count <= index)
            {
                var mark = new GameObject($"Step {m_Steps.Count}").transform;
                mark.SetParent(transform, worldPositionStays: false);

                mark.gameObject.AddComponent<MeshFilter>().sharedMesh = CreatureToken.Disc;

                var renderer = mark.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = m_Material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                mark.localScale = new Vector3(m_Scale, 1f, m_Scale);

                m_Steps.Add(mark);
            }

            m_Steps[index].gameObject.SetActive(true);
            return m_Steps[index];
        }

        /// <summary>
        /// One transparent material for every footprint.
        ///
        /// The same recipe the move ghost uses, and for the same reason: two translucent pieces
        /// that wrote depth would fight over it as the camera moved.
        /// </summary>
        void Build()
        {
            if (m_Material != null)
            {
                return;
            }

            m_Material = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")) { name = "Path Step" };

            m_Material.SetFloat("_Surface", 1f);
            m_Material.SetFloat("_ZWrite", 0f);
            m_Material.SetOverrideTag("RenderType", "Transparent");
            m_Material.renderQueue = 3000;
            m_Material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m_Material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m_Material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}

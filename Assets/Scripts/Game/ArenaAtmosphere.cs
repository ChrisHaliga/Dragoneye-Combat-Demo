using UnityEngine;
using UnityEngine.Rendering;

namespace Dragoneye.Game
{
    /// <summary>
    /// What the arena looks like when nothing is happening in it: the light, the air, and the table.
    ///
    /// The scene used to clear to Unity's default skybox and take its ambient light from it. That
    /// is a bright blue sky, with a bright blue bounce, under a game whose every other screen is
    /// near-black and lit from below by embers -- and it is the single largest reason the board read
    /// as a prototype however the pieces on it were drawn. The tokens were not the problem; the
    /// room was.
    ///
    /// Set from code rather than saved into the scene, for the reason the tokens are built in code:
    /// a setting saved in a scene file is a setting that can be lost by anybody who opens the scene,
    /// and one that nothing checks. This component is the atmosphere; if it is on the arena, the
    /// arena looks like this.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArenaAtmosphere : MonoBehaviour
    {
        [SerializeField, Min(10f), Tooltip("Width of the table under the board, in world units.")]
        float m_TableSize = 110f;

        [SerializeField, Tooltip("How far below the tile tops the table sits. Below the thickness "
             + "of a tile, so the tiles read as resting on it.")]
        float m_TableDrop = 0.2f;

        [SerializeField, Min(0f), Tooltip("Distance at which the room starts to darken.")]
        float m_FogStart = 26f;

        [SerializeField, Min(1f), Tooltip("Distance at which it has darkened completely.")]
        float m_FogEnd = 84f;

        Camera m_Dressed;

        void OnEnable()
        {
            DressRoom();
            LayTable();
        }

        // The camera is Cinemachine's output, found through the arena context, which may not have
        // resolved by the time this enables. Asked for until it turns up, then left alone.
        void Update()
        {
            if (m_Dressed != null)
            {
                return;
            }

            var context = ArenaContext.Current;
            var camera = context != null ? context.OutputCamera : Camera.main;

            if (camera == null)
            {
                return;
            }

            // Solid ink, and no sky. Everything the player sees past the table is the same dark
            // the menu frames are outlined in, so the fight and the screens either side of it are
            // plainly one game.
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = WorldArt.Ink;
            m_Dressed = camera;
        }

        /// <summary>
        /// Low, cool, flat light in the room; the directional light does the rest.
        ///
        /// Flat rather than a gradient, because a gradient ambient is a way of describing a sky and
        /// there is not one. Fog to the same ink the camera clears to, so the far edge of the table
        /// falls away instead of ending.
        /// </summary>
        static void DressRoom()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = WorldArt.Ambient;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = WorldArt.Ink;
        }

        void LayTable()
        {
            RenderSettings.fogStartDistance = m_FogStart;
            RenderSettings.fogEndDistance = m_FogEnd;

            var existing = transform.Find("Table");
            var table = existing != null ? existing.gameObject : new GameObject("Table");

            table.transform.SetParent(transform, worldPositionStays: false);
            table.transform.localPosition = new Vector3(0f, -m_TableDrop, 0f);
            table.transform.localRotation = Quaternion.identity;
            table.transform.localScale = new Vector3(m_TableSize, 1f, m_TableSize);

            var filter = table.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = table.AddComponent<MeshFilter>();
            }

            filter.sharedMesh = WorldArt.Quad;

            var renderer = table.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = table.AddComponent<MeshRenderer>();
            }

            renderer.sharedMaterial = WorldArt.Unlit("Table", WorldArt.Table, transparent: false);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}

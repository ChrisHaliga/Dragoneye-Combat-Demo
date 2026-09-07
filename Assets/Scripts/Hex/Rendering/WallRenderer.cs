using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Hex.Rendering
{
    /// <summary>
    /// Draws the walls: a box along every walled ray and every walled half-edge a tile owns.
    ///
    /// Tall where a wall blocks sight, waist-high where it only blocks feet, so what a wall does
    /// to the rules is what it looks like. Rebuilt per tile when a wall on it changes, so a door
    /// opening or a wall coming down one day is a redraw of one tile and not the map.
    ///
    /// The material is whatever the scene assigns -- the cutaway one, which fades where a creature
    /// is behind it -- with a plain lit stone as the fallback so a wall is never invisible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WallRenderer : MonoBehaviour
    {
        /// <summary>What one tile's walls are made of, so a redraw of the tile can free all of it.</summary>
        sealed class TileWalls
        {
            public GameObject Root;
            public readonly List<Mesh> Meshes = new List<Mesh>();
        }

        [SerializeField, Tooltip("Shared by every wall. The cutaway material, when the setup has made one.")]
        Material m_WallMaterial;

        [SerializeField, Min(0.05f), Tooltip("Height of a wall that blocks sight.")]
        float m_TallHeight = 0.9f;

        [SerializeField, Min(0.05f), Tooltip("Height of a wall that blocks only movement -- waist high.")]
        float m_LowHeight = 0.32f;

        [SerializeField, Min(0.01f)]
        float m_Thickness = 0.1f;

        [SerializeField, Tooltip("Fallback tint when no material is assigned.")]
        Color m_FallbackColour = new Color(0.36f, 0.34f, 0.32f, 1f);

        readonly Dictionary<Hex, TileWalls> m_ByTile = new Dictionary<Hex, TileWalls>();

        IHexMapSource m_Source;
        HexMap m_Map;
        Transform m_Root;
        Material m_Fallback;

        void Awake()
        {
            m_Source = GetComponent<IHexMapSource>();

            if (m_Source == null)
            {
                Debug.LogError($"{nameof(WallRenderer)} needs an {nameof(IHexMapSource)} on the same GameObject.", this);
                enabled = false;
            }
        }

        void OnEnable()
        {
            if (m_Source != null)
            {
                m_Source.MapBuilt += Rebuild;
            }
        }

        void OnDisable()
        {
            if (m_Source != null)
            {
                m_Source.MapBuilt -= Rebuild;
            }
        }

        void Start()
        {
            if (m_Source != null && m_Source.Map != null && m_Root == null)
            {
                Rebuild(m_Source.Map);
            }
        }

        void OnDestroy() => Clear();

        Material WallMaterial
        {
            get
            {
                if (m_WallMaterial != null)
                {
                    return m_WallMaterial;
                }

                if (m_Fallback == null)
                {
                    m_Fallback = new Material(Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard")) { name = "Wall (fallback)" };
                    m_Fallback.color = m_FallbackColour;
                    m_Fallback.SetColor("_BaseColor", m_FallbackColour);
                }

                return m_Fallback;
            }
        }

        void Rebuild(HexMap map)
        {
            Clear();

            if (map == null)
            {
                return;
            }

            m_Map = map;
            m_Map.WallChanged += OnWallChanged;

            m_Root = new GameObject("Walls").transform;
            m_Root.SetParent(transform, false);

            foreach (var tile in map.Tiles)
            {
                BuildTile(tile);
            }
        }

        void OnWallChanged(HexTile tile, AreaLayout before) => BuildTile(tile);

        void BuildTile(HexTile tile)
        {
            ClearTile(tile.Coordinates);

            if (!tile.HasWalls || m_Map == null)
            {
                return;
            }

            var size = m_Map.Layout.Size;
            var walls = new TileWalls { Root = new GameObject($"Walls {tile.Coordinates.Q},{tile.Coordinates.R}") };
            var root = walls.Root;
            root.transform.SetParent(m_Root, false);
            root.transform.localPosition = m_Map.Layout.ToWorld(tile.Coordinates);
            m_ByTile[tile.Coordinates] = walls;

            for (var ray = 0; ray < TileGeometry.Rays; ray++)
            {
                var wall = tile.Ray(ray);

                if (wall.IsSet)
                {
                    Segment(walls, Vector3.zero, HexMeshFactory.RayEnd(ray) * size, wall);
                }
            }

            for (var half = 0; half < TileGeometry.HalfEdges; half++)
            {
                if (!TileGeometry.IsOwned(half))
                {
                    continue;
                }

                var wall = tile.OwnedHalfEdge(half);

                if (wall.IsSet)
                {
                    Segment(walls, HexMeshFactory.RayEnd(half) * size,
                        HexMeshFactory.RayEnd(half + 1) * size, wall);
                }
            }
        }

        void Segment(TileWalls walls, Vector3 a, Vector3 b, Wall wall)
        {
            var height = wall.BlocksSight ? m_TallHeight : m_LowHeight;
            var mesh = HexMeshFactory.CreateWall(a, b, height, m_Thickness);
            walls.Meshes.Add(mesh);

            var piece = new GameObject("Wall", typeof(MeshFilter), typeof(MeshRenderer));
            piece.transform.SetParent(walls.Root.transform, false);
            piece.GetComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = piece.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = WallMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Clear()
        {
            if (m_Map != null)
            {
                m_Map.WallChanged -= OnWallChanged;
                m_Map = null;
            }

            foreach (var tile in new List<Hex>(m_ByTile.Keys))
            {
                ClearTile(tile);
            }

            if (m_Root != null)
            {
                Destroy(m_Root.gameObject);
                m_Root = null;
            }
        }

        void ClearTile(Hex tile)
        {
            if (!m_ByTile.TryGetValue(tile, out var walls))
            {
                return;
            }

            m_ByTile.Remove(tile);
            Destroy(walls.Root);

            foreach (var mesh in walls.Meshes)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }
        }
    }
}

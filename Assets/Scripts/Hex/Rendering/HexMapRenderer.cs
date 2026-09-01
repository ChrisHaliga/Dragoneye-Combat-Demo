using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Hex.Rendering
{
    /// <summary>
    /// Draws the map published by an <see cref="IHexMapSource"/>. Reacts to the data; owns none of it.
    ///
    /// One child object per tile, all sharing a single generated mesh and material, tinted through a
    /// <see cref="MaterialPropertyBlock"/> so no per-tile material instances are created. A tile
    /// changing terrain repaints only that tile.
    ///
    /// Note that a property block opts the renderer out of the SRP Batcher, so GPU instancing on the
    /// tile material is what keeps ~91 tiles from becoming ~91 draw calls. If tiles ever stop
    /// batching, check that flag on the material first.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HexMapRenderer : MonoBehaviour
    {
        [SerializeField, Tooltip("Shared by every tile. Enable GPU instancing on it.")]
        Material m_TileMaterial;

        [SerializeField, Tooltip("Optional. If set, this is instantiated per tile instead of the generated mesh.")]
        GameObject m_TilePrefab;

        [SerializeField, Range(0.5f, 1f), Tooltip("Shrinks tiles to leave a gutter between them.")]
        float m_TileFill = 0.94f;

        [SerializeField, Tooltip("Colour property on the material. URP Lit uses _BaseColor.")]
        string m_ColorProperty = "_BaseColor";

        readonly Dictionary<Hex, Renderer> m_TileViews = new Dictionary<Hex, Renderer>();

        // Resolved from this GameObject rather than serialised: Unity cannot serialise an interface
        // field, and naming a concrete source type here is exactly the dependency this assembly is
        // meant not to have.
        IHexMapSource m_Source;

        MaterialPropertyBlock m_PropertyBlock;
        Transform m_TileRoot;
        Mesh m_SharedMesh;
        int m_ColorPropertyId;

        // Tracked explicitly rather than read back off the source: by the time a rebuild runs, the
        // source already points at the new map, so we would unsubscribe from the wrong one and leak
        // a handler on the old.
        HexMap m_SubscribedMap;

        void Awake()
        {
            m_Source = GetComponent<IHexMapSource>();
            if (m_Source == null)
            {
                Debug.LogError($"{nameof(HexMapRenderer)} needs an {nameof(IHexMapSource)} on the "
                    + "same GameObject.", this);
                enabled = false;
                return;
            }

            m_PropertyBlock = new MaterialPropertyBlock();
            m_ColorPropertyId = Shader.PropertyToID(m_ColorProperty);
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
            // The source builds in Awake, so by Start the map usually exists already and MapBuilt
            // has been and gone. Render what is there; MapBuilt covers any later rebuild.
            if (m_Source != null && m_Source.Map != null && m_TileViews.Count == 0)
            {
                Rebuild(m_Source.Map);
            }
        }

        void OnDestroy() => Clear();

        void Rebuild(HexMap map)
        {
            Clear();

            if (map == null)
            {
                return;
            }

            map.TileChanged += OnTileChanged;
            m_SubscribedMap = map;

            m_TileRoot = new GameObject("Tiles").transform;
            m_TileRoot.SetParent(transform, false);

            if (m_TilePrefab == null)
            {
                m_SharedMesh = HexMeshFactory.Create(map.Layout.Size, m_TileFill);
            }

            foreach (var tile in map.Tiles)
            {
                var view = CreateView(tile, map.Layout);
                m_TileViews[tile.Coordinates] = view;
                Paint(view, tile);
            }
        }

        Renderer CreateView(HexTile tile, HexLayout layout)
        {
            var position = layout.ToWorld(tile.Coordinates);

            if (m_TilePrefab != null)
            {
                var instance = Instantiate(m_TilePrefab, m_TileRoot);
                instance.transform.localPosition = position;
                instance.name = $"Tile {tile.Coordinates.Q},{tile.Coordinates.R}";
                return instance.GetComponentInChildren<Renderer>();
            }

            var tileObject = new GameObject(
                $"Tile {tile.Coordinates.Q},{tile.Coordinates.R}",
                typeof(MeshFilter),
                typeof(MeshRenderer));

            tileObject.transform.SetParent(m_TileRoot, false);
            tileObject.transform.localPosition = position;

            tileObject.GetComponent<MeshFilter>().sharedMesh = m_SharedMesh;

            var renderer = tileObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_TileMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        void OnTileChanged(HexTile tile)
        {
            if (m_TileViews.TryGetValue(tile.Coordinates, out var view))
            {
                Paint(view, tile);
            }
        }

        void Paint(Renderer view, HexTile tile)
        {
            if (view == null)
            {
                return;
            }

            view.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetColor(
                m_ColorPropertyId,
                tile.Terrain != null ? tile.Terrain.Color : Color.magenta);
            view.SetPropertyBlock(m_PropertyBlock);
        }

        /// <summary>
        /// Tears down the current view. The tile objects and the mesh they share are destroyed
        /// together so no tile can outlive its mesh: Object.Destroy defers both to the same point
        /// after the update loop, which is what keeps that pairing safe.
        /// </summary>
        void Clear()
        {
            Unsubscribe();
            m_TileViews.Clear();

            if (m_TileRoot != null)
            {
                Destroy(m_TileRoot.gameObject);
                m_TileRoot = null;
            }

            if (m_SharedMesh != null)
            {
                // Generated at runtime, so nothing else will collect it.
                Destroy(m_SharedMesh);
                m_SharedMesh = null;
            }
        }

        void Unsubscribe()
        {
            if (m_SubscribedMap != null)
            {
                m_SubscribedMap.TileChanged -= OnTileChanged;
                m_SubscribedMap = null;
            }
        }
    }
}

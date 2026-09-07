using System.Collections.Generic;
using Dragoneye.Hex;
using Dragoneye.Hex.Rendering;
using UnityEngine;

namespace Dragoneye.Game
{
    /// <summary>
    /// The fan mesh for an area of a tile, made once per shape and shared.
    ///
    /// There are at most four areas per pattern of rays and only a handful of patterns on any
    /// map, so the cache is tiny, and every overlay that wants to light up "this part of this
    /// tile" draws the same mesh.
    /// </summary>
    public static class AreaMeshes
    {
        static readonly Dictionary<(int mask, byte area, int size), Mesh> s_Meshes =
            new Dictionary<(int, byte, int), Mesh>();

        /// <summary>The fan for this cell's area, at the map's tile size and the tile fill used for the floor.</summary>
        public static Mesh For(HexMap map, Cell cell, float fill = 0.94f)
        {
            if (map == null || !map.TryGetTile(cell.Tile, out var tile))
            {
                return null;
            }

            var mask = tile.MovementRayMask;
            var sizeKey = Mathf.RoundToInt(map.Layout.Size * 1000f);
            var key = (mask, cell.Area, sizeKey);

            if (s_Meshes.TryGetValue(key, out var mesh) && mesh != null)
            {
                return mesh;
            }

            mesh = HexMeshFactory.CreateArea(tile.Areas, cell.Area, map.Layout.Size, fill);
            s_Meshes[key] = mesh;
            return mesh;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace Dragoneye.Hex.Systems
{
    /// <summary>
    /// Chooses evenly spread starting tiles around the edge of a map.
    ///
    /// Shape-agnostic on purpose: it works outward from the map's own bounds rather than assuming a
    /// hexagon, so a rectangle or a hand-authored arena gets sensible spawns with no code change.
    /// </summary>
    public static class HexSpawnPlacement
    {
        /// <summary>
        /// Picks <paramref name="count"/> distinct walkable tiles, spaced around the map's rim at
        /// equal angles. Returns fewer only if the map has fewer walkable tiles than requested.
        /// </summary>
        /// <param name="startAngleDegrees">
        /// Rotates the whole arrangement. Useful to keep spawn 1 in a consistent place.
        /// </param>
        public static IReadOnlyList<Hex> ChooseSpawns(HexMap map, int count, float startAngleDegrees = 90f)
        {
            var spawns = new List<Hex>();
            if (map == null || count <= 0)
            {
                return spawns;
            }

            // Sorted so the result is stable run to run; dictionary order is not guaranteed.
            var candidates = new List<Hex>();
            foreach (var tile in map.Tiles)
            {
                if (tile.IsWalkable)
                {
                    candidates.Add(tile.Coordinates);
                }
            }

            candidates.Sort((a, b) => a.Q != b.Q ? a.Q.CompareTo(b.Q) : a.R.CompareTo(b.R));
            if (candidates.Count == 0)
            {
                return spawns;
            }

            var center = map.WorldCenter();
            var radius = 0f;
            foreach (var hex in candidates)
            {
                radius = Mathf.Max(radius, Vector3.Distance(map.Layout.ToWorld(hex), center));
            }

            var taken = new HashSet<Hex>();

            for (var i = 0; i < count && taken.Count < candidates.Count; i++)
            {
                var angle = Mathf.Deg2Rad * (startAngleDegrees + 360f * i / count);
                var target = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                // Aim past the rim, then take the nearest real tile. This lands on the edge for a
                // convex map and degrades gracefully to "as far out as possible" for anything else.
                var best = default(Hex);
                var bestDistance = float.MaxValue;
                var found = false;

                foreach (var hex in candidates)
                {
                    if (taken.Contains(hex))
                    {
                        continue;
                    }

                    var distance = Vector3.SqrMagnitude(map.Layout.ToWorld(hex) - target);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = hex;
                        found = true;
                    }
                }

                if (found)
                {
                    taken.Add(best);
                    spawns.Add(best);
                }
            }

            return spawns;
        }
    }
}

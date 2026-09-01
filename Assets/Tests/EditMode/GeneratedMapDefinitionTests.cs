using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// Covers the offset-to-axial conversion in the rectangle generator. That single line --
    /// row - (column - (column and 1)) / 2 -- relies on C# integer division truncating toward zero
    /// and on the low-bit trick correcting for it, which is exactly the kind of expression that is
    /// right until someone tidies it.
    /// </summary>
    public class GeneratedMapDefinitionTests
    {
        static GeneratedMapDefinition Make(HexMapShape shape, int radius, int width, int height)
        {
            var definition = ScriptableObject.CreateInstance<GeneratedMapDefinition>();

            // The fields are private with no setters, which is right for runtime. A test is the one
            // caller that legitimately needs to reach past that.
            Set(definition, "m_Shape", shape);
            Set(definition, "m_Radius", radius);
            Set(definition, "m_Width", width);
            Set(definition, "m_Height", height);
            Set(definition, "m_TileSize", 1f);
            return definition;
        }

        static void Set(object target, string field, object value)
        {
            var info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? target.GetType().BaseType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, $"No field '{field}'");
            info.SetValue(target, value);
        }

        static HexMap Build(HexMapShape shape, int radius = 3, int width = 6, int height = 4)
        {
            var definition = Make(shape, radius, width, height);
            try
            {
                return definition.Build(0);
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [TestCase(6, 4)]
        [TestCase(7, 5)]
        [TestCase(1, 1)]
        [TestCase(12, 10)]
        public void RectangleHasExactlyWidthTimesHeightDistinctTiles(int width, int height)
        {
            var map = Build(HexMapShape.Rectangle, width: width, height: height);

            Assert.AreEqual(width * height, map.Count);
            Assert.AreEqual(width * height, map.Coordinates.Distinct().Count(),
                "Two offset columns collapsed onto the same axial coordinate");
        }

        [TestCase(6, 4)]
        [TestCase(7, 5)]
        public void RectangleTilesAreAllConnected(int width, int height)
        {
            // A wrong offset conversion typically shears alternate columns apart, which shows up as
            // the map falling into more than one connected component.
            var map = Build(HexMapShape.Rectangle, width: width, height: height);

            var start = map.Coordinates.First();
            var seen = new HashSet<Hex> { start };
            var queue = new Queue<Hex>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                foreach (var neighbor in queue.Dequeue().Neighbors())
                {
                    if (map.Contains(neighbor) && seen.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }

            Assert.AreEqual(map.Count, seen.Count, "Rectangle is not a single connected region");
        }

        [Test]
        public void RectangleIsCentredSoNegativeColumnsAreExercised()
        {
            var map = Build(HexMapShape.Rectangle, width: 7, height: 5);

            Assert.IsTrue(map.Coordinates.Any(h => h.Q < 0), "No negative columns generated");
            Assert.IsTrue(map.Coordinates.Any(h => h.Q > 0), "No positive columns generated");
        }

        [TestCase(4)]
        [TestCase(5)]
        public void EvenAndOddWidthsBothStayRectangular(int width)
        {
            // Every offset column must hold the same number of tiles. The parity correction is what
            // guarantees that, and getting it wrong shortens alternate columns by one.
            var map = Build(HexMapShape.Rectangle, width: width, height: 5);

            var perColumn = map.Coordinates.GroupBy(h => h.Q).Select(g => g.Count()).Distinct().ToList();

            Assert.AreEqual(1, perColumn.Count, "Columns differ in height");
            Assert.AreEqual(5, perColumn[0]);
        }

        [TestCase(0, 1)]
        [TestCase(3, 37)]
        [TestCase(5, 91)]
        public void HexagonMatchesTheCentredHexagonalNumber(int radius, int expected)
        {
            Assert.AreEqual(expected, Build(HexMapShape.Hexagon, radius).Count);
        }

        [Test]
        public void EveryTileSatisfiesTheCubeInvariant()
        {
            foreach (var hex in Build(HexMapShape.Rectangle, width: 9, height: 7).Coordinates)
            {
                Assert.AreEqual(0, hex.Q + hex.R + hex.S, $"{hex} breaks q + r + s == 0");
            }
        }
    }
}

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// The arena keeps two boards: the one the fight is played on, and the one that is drawn.
    ///
    /// The fight runs ahead of what has been shown of it, so a wall it brings down must stay
    /// standing on screen until the moment it falls is played back. These are the tests that
    /// stop the drawn board being wired back to the fight's -- which is what it was, and it
    /// showed walls vanishing turns before the blow that broke them.
    /// </summary>
    public class ShownBoardTests
    {
        static readonly WallSegment North = WallSegment.Ray(Hex.Zero, 0);
        static readonly WallSegment South = WallSegment.Ray(Hex.Zero, 6);
        static readonly Wall Solid = new Wall(WallFlags.Solid, 40);

        GameObject m_Object;
        ArenaMap m_Arena;
        GeneratedMapDefinition m_Definition;

        [SetUp]
        public void SetUp()
        {
            m_Definition = ScriptableObject.CreateInstance<GeneratedMapDefinition>();
            Set(m_Definition, "m_Shape", HexMapShape.Hexagon);
            Set(m_Definition, "m_Radius", 2);
            Set(m_Definition, "m_TileSize", 1f);

            // Inactive while the component is added: ArenaMap builds in Awake, and Awake with no
            // definition assigned logs an error the test has no business causing.
            m_Object = new GameObject("Arena");
            m_Object.SetActive(false);
            m_Arena = m_Object.AddComponent<ArenaMap>();
            m_Arena.Rebuild(m_Definition);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Object);
            Object.DestroyImmediate(m_Definition);
        }

        static void Set(object target, string field, object value)
        {
            var info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? target.GetType().BaseType.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, $"No field '{field}'");
            info.SetValue(target, value);
        }

        [Test]
        public void TheTwoBoardsAreSeparateAndStartTheSame()
        {
            Assert.AreNotSame(m_Arena.Map, m_Arena.Shown, "the drawn board is not the fight's board");
            Assert.AreEqual(m_Arena.Map.Count, m_Arena.Shown.Count);

            foreach (var tile in m_Arena.Map.Tiles)
            {
                Assert.IsTrue(m_Arena.Shown.TryGetTile(tile.Coordinates, out var drawn));
                Assert.AreEqual(tile.HasWalls, drawn.HasWalls, $"{tile.Coordinates}");
                Assert.AreEqual(tile.IsWalkable, drawn.IsWalkable, $"{tile.Coordinates}");
            }
        }

        [Test]
        public void WhatIsDrawnIsTheDrawnBoard()
        {
            // The renderers take the board through the interface and never see the other one.
            Assert.AreSame(m_Arena.Shown, ((IHexMapSource)m_Arena).Shown);
        }

        [Test]
        public void AWallTheFightRaisesIsNotDrawnUntilItIsShown()
        {
            m_Arena.Map.SetWall(North, Solid);

            Assert.IsTrue(m_Arena.Map.WallAt(North).IsSet, "the fight's board has it at once");
            Assert.IsFalse(m_Arena.Shown.WallAt(North).IsSet, "and the drawn board does not");

            m_Arena.ShowWall(North, WallFlags.Solid);

            Assert.IsTrue(m_Arena.Shown.WallAt(North).IsSet, "until the moment is shown");
            Assert.AreEqual(m_Arena.Map.WallAt(North).Flags, m_Arena.Shown.WallAt(North).Flags);
        }

        [Test]
        public void AWallTheFightBreaksIsStillDrawnUntilItIsShown()
        {
            m_Arena.Map.SetWall(North, Solid);
            m_Arena.ShowWall(North, WallFlags.Solid);

            m_Arena.Map.SetWall(North, new Wall(WallFlags.None));

            Assert.IsFalse(m_Arena.Map.WallAt(North).IsSet, "gone from the fight's board");
            Assert.IsTrue(m_Arena.Shown.WallAt(North).IsSet, "still standing on screen");

            m_Arena.ShowWall(North, WallFlags.None);

            Assert.IsFalse(m_Arena.Shown.WallAt(North).IsSet, "and gone when it is shown falling");
        }

        [Test]
        public void ATileIsCutByTheWallsThatAreDrawnOnIt()
        {
            m_Arena.Map.SetWall(North, Solid);
            m_Arena.Map.SetWall(South, Solid);

            Assert.IsTrue(m_Arena.Map.TryGetTile(Hex.Zero, out var played));
            Assert.IsTrue(m_Arena.Shown.TryGetTile(Hex.Zero, out var drawn));
            Assert.AreEqual(2, played.Areas.Count, "the fight walks around the wall at once");
            Assert.AreEqual(1, drawn.Areas.Count, "the drawn tile is whole while the wall is not there");

            m_Arena.ShowWall(North, WallFlags.Solid);
            m_Arena.ShowWall(South, WallFlags.Solid);

            Assert.IsTrue(m_Arena.Shown.TryGetTile(Hex.Zero, out drawn));
            Assert.AreEqual(2, drawn.Areas.Count, "and cut in two once the wall is shown");
        }

        [Test]
        public void ShowingAWallLeavesIntegrityToTheFight()
        {
            // Integrity is the rules' business. The drawn board keeps whatever it had, so a change
            // that is only about how a wall looks cannot quietly rewrite how strong it is.
            m_Arena.Shown.SetWall(North, new Wall(WallFlags.BlocksMovement, 12));
            m_Arena.ShowWall(North, WallFlags.Solid);

            Assert.AreEqual(WallFlags.Solid, m_Arena.Shown.WallAt(North).Flags);
            Assert.AreEqual(12, m_Arena.Shown.WallAt(North).Integrity);
        }

        [Test]
        public void AChangeOffTheBoardIsIgnored()
        {
            Assert.DoesNotThrow(() => m_Arena.ShowWall(WallSegment.Ray(new Hex(40, -12), 3), WallFlags.Solid));
        }

        [Test]
        public void RebuildingGivesBothBoardsBack()
        {
            m_Arena.Map.SetWall(North, Solid);
            m_Arena.ShowWall(North, WallFlags.Solid);

            m_Arena.Rebuild();

            Assert.IsFalse(m_Arena.Map.WallAt(North).IsSet, "a new fight starts on a fresh board");
            Assert.IsFalse(m_Arena.Shown.WallAt(North).IsSet, "and so does what is drawn of it");
            Assert.AreNotSame(m_Arena.Map, m_Arena.Shown);
        }
    }
}

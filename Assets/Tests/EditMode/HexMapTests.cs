using System.Collections.Generic;
using System.Linq;
using Dragoneye.Hex.Systems;
using NUnit.Framework;
using UnityEngine;

namespace Dragoneye.Hex.Tests
{
    public class HexMapTests
    {
        static HexMap BuildHexagon(int radius, TerrainType terrain = null)
        {
            var tiles = Hex.Range(Hex.Zero, radius).Select(hex => new HexTile(hex, terrain));
            return new HexMap(new HexLayout(1f, Vector3.zero), tiles);
        }

        [Test]
        public void MapContainsEveryTileItWasBuiltWith()
        {
            var map = BuildHexagon(3);

            Assert.AreEqual(37, map.Count);
            foreach (var hex in Hex.Range(Hex.Zero, 3))
            {
                Assert.IsTrue(map.Contains(hex), $"{hex} missing from the map");
            }
        }

        [Test]
        public void LookupMissesOutsideTheMap()
        {
            var map = BuildHexagon(2);

            Assert.IsFalse(map.Contains(new Hex(99, -99)));
            Assert.IsFalse(map.TryGetTile(new Hex(99, -99), out _));
        }

        [Test]
        public void InteriorTilesHaveSixNeighborsAndEdgeTilesHaveFewer()
        {
            var map = BuildHexagon(2);

            Assert.AreEqual(6, map.NeighborsOf(Hex.Zero).Count(), "Centre tile should be surrounded");

            foreach (var hex in Hex.Ring(Hex.Zero, 2))
            {
                Assert.Less(map.NeighborsOf(hex).Count(), 6, $"{hex} is on the rim");
            }
        }

        [Test]
        public void ChangingTerrainRaisesTileChangedOnce()
        {
            var grass = ScriptableObject.CreateInstance<TerrainType>();
            var stone = ScriptableObject.CreateInstance<TerrainType>();

            try
            {
                var map = BuildHexagon(1, grass);
                var changed = new List<HexTile>();
                map.TileChanged += tile => changed.Add(tile);

                map.SetTerrain(Hex.Zero, stone);

                Assert.AreEqual(1, changed.Count);
                Assert.AreEqual(Hex.Zero, changed[0].Coordinates);
                Assert.AreSame(stone, map[Hex.Zero].Terrain);
            }
            finally
            {
                Object.DestroyImmediate(grass);
                Object.DestroyImmediate(stone);
            }
        }

        [Test]
        public void SettingTheSameTerrainIsNotAChange()
        {
            var grass = ScriptableObject.CreateInstance<TerrainType>();

            try
            {
                var map = BuildHexagon(1, grass);
                var raised = 0;
                map.TileChanged += _ => raised++;

                map.SetTerrain(Hex.Zero, grass);

                Assert.AreEqual(0, raised, "A no-op write should not repaint the tile");
            }
            finally
            {
                Object.DestroyImmediate(grass);
            }
        }

        [Test]
        public void SettingTerrainOutsideTheMapIsIgnored()
        {
            var map = BuildHexagon(1);
            var raised = 0;
            map.TileChanged += _ => raised++;

            map.SetTerrain(new Hex(50, 50), null);

            Assert.AreEqual(0, raised);
        }

        [Test]
        public void WorldCenterOfASymmetricMapIsTheOrigin()
        {
            var center = BuildHexagon(4).WorldCenter();

            Assert.AreEqual(0f, center.x, 1e-4f);
            Assert.AreEqual(0f, center.z, 1e-4f);
        }

        [Test]
        public void SpawnsAreDistinctTilesOfTheMap()
        {
            var map = BuildHexagon(5);

            var spawns = HexSpawnPlacement.ChooseSpawns(map, 4);

            Assert.AreEqual(4, spawns.Count);
            Assert.AreEqual(4, spawns.Distinct().Count(), "Two players would share a tile");
            foreach (var spawn in spawns)
            {
                Assert.IsTrue(map.Contains(spawn), $"{spawn} is not on the map");
            }
        }

        [Test]
        public void SpawnsSitOnTheRimAndAreSpreadApart()
        {
            const int radius = 5;
            var map = BuildHexagon(radius);

            var spawns = HexSpawnPlacement.ChooseSpawns(map, 4);

            foreach (var spawn in spawns)
            {
                Assert.GreaterOrEqual(Hex.Distance(Hex.Zero, spawn), radius - 1,
                    $"{spawn} is too close to the middle to be a starting position");
            }

            for (var i = 0; i < spawns.Count; i++)
            {
                for (var j = i + 1; j < spawns.Count; j++)
                {
                    Assert.Greater(Hex.Distance(spawns[i], spawns[j]), radius / 2,
                        "Spawns should be spread around the map, not bunched together");
                }
            }
        }

        [Test]
        public void SpawnPlacementIsDeterministic()
        {
            var map = BuildHexagon(5);

            var first = HexSpawnPlacement.ChooseSpawns(map, 6);
            var second = HexSpawnPlacement.ChooseSpawns(map, 6);

            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void SpawnPlacementSurvivesAskingForMoreThanFits()
        {
            var map = BuildHexagon(1);

            var spawns = HexSpawnPlacement.ChooseSpawns(map, 50);

            Assert.AreEqual(map.Count, spawns.Count);
            Assert.AreEqual(spawns.Count, spawns.Distinct().Count());
        }

        [Test]
        public void SpawnPlacementHandlesDegenerateInput()
        {
            Assert.IsEmpty(HexSpawnPlacement.ChooseSpawns(null, 4));
            Assert.IsEmpty(HexSpawnPlacement.ChooseSpawns(BuildHexagon(3), 0));
        }
    }
}

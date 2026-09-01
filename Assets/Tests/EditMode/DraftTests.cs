using System.Collections.Generic;
using System.Linq;
using Dragoneye.Game;
using NUnit.Framework;
using UnityEngine;

namespace Dragoneye.Hex.Tests
{
    public class ClaimRulesTests
    {
        [Test]
        public void CapsAlwaysSumToTheCreatureCount()
        {
            // The property that matters: if the caps summed to less, some creature could never be
            // claimed by anyone and would silently fall to the computer.
            for (var creatures = 0; creatures <= 24; creatures++)
            {
                for (var players = 1; players <= 6; players++)
                {
                    var total = 0;
                    for (var ordinal = 0; ordinal < players; ordinal++)
                    {
                        total += ClaimRules.CapFor(creatures, players, ordinal);
                    }

                    Assert.AreEqual(creatures, total, $"{creatures} creatures over {players} players");
                }
            }
        }

        [Test]
        public void RemainderGoesToTheLowestOrdinals()
        {
            // 7 creatures, 3 players -> 3, 2, 2
            Assert.AreEqual(3, ClaimRules.CapFor(7, 3, 0));
            Assert.AreEqual(2, ClaimRules.CapFor(7, 3, 1));
            Assert.AreEqual(2, ClaimRules.CapFor(7, 3, 2));
        }

        [Test]
        public void CapsNeverDifferByMoreThanOne()
        {
            for (var creatures = 0; creatures <= 20; creatures++)
            {
                for (var players = 1; players <= 5; players++)
                {
                    var caps = Enumerable.Range(0, players)
                        .Select(o => ClaimRules.CapFor(creatures, players, o))
                        .ToList();

                    Assert.LessOrEqual(caps.Max() - caps.Min(), 1,
                        $"{creatures} over {players} was lopsided");
                }
            }
        }

        [TestCase(0, 0, 0)]
        [TestCase(5, 0, 0)]
        [TestCase(0, 3, 0)]
        public void DegenerateInputsReturnZero(int creatures, int players, int ordinal)
        {
            Assert.AreEqual(0, ClaimRules.CapFor(creatures, players, ordinal));
        }

        [Test]
        public void AnOrdinalOutsideThePartyGetsNothing()
        {
            Assert.AreEqual(0, ClaimRules.CapFor(6, 2, 5));
            Assert.AreEqual(0, ClaimRules.CapFor(6, 2, -1));
        }

        [Test]
        public void NothingIsReleasedWhileWithinCap()
        {
            var claims = new List<(int, uint)> { (0, 1), (3, 2) };

            Assert.IsEmpty(ClaimRules.ClaimsToRelease(claims, 2));
            Assert.IsEmpty(ClaimRules.ClaimsToRelease(claims, 5));
        }

        [Test]
        public void OverCapReleasesNewestClaimsFirst()
        {
            // A player who has held a creature since the start has probably planned around it; the
            // one they grabbed a second ago is the decision they can most easily give up.
            var claims = new List<(int, uint)> { (5, 1), (2, 7), (9, 4) };

            var released = ClaimRules.ClaimsToRelease(claims, 1);

            Assert.AreEqual(2, released.Count);
            CollectionAssert.AreEqual(new[] { 2, 9 }, released, "Should drop sequence 7 then 4");
        }

        [Test]
        public void ReleasingToZeroDropsEverything()
        {
            var claims = new List<(int, uint)> { (1, 1), (2, 2) };

            Assert.AreEqual(2, ClaimRules.ClaimsToRelease(claims, 0).Count);
        }

        [Test]
        public void NegativeCapIsTreatedAsZero()
        {
            var claims = new List<(int, uint)> { (1, 1) };

            Assert.AreEqual(1, ClaimRules.ClaimsToRelease(claims, -3).Count);
        }

        [Test]
        public void ReleaseIsDeterministicWhenSequencesTie()
        {
            // Sequences should never collide, but if they do the answer must still be the same on
            // every client or two players would see different rosters.
            var claims = new List<(int, uint)> { (4, 3), (7, 3) };

            CollectionAssert.AreEqual(
                ClaimRules.ClaimsToRelease(claims, 1),
                ClaimRules.ClaimsToRelease(new List<(int, uint)> { (7, 3), (4, 3) }, 1));
        }

        [Test]
        public void NullClaimsAreSafe()
        {
            Assert.IsEmpty(ClaimRules.ClaimsToRelease(null, 0));
        }
    }

    public class CreatureCatalogTests
    {
        static CreatureDefinition Definition(string id)
        {
            var definition = ScriptableObject.CreateInstance<CreatureDefinition>();
            typeof(CreatureDefinition)
                .GetField("m_Id", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(definition, id);
            return definition;
        }

        static CreatureCatalog Catalog(params CreatureDefinition[] creatures)
        {
            var catalog = ScriptableObject.CreateInstance<CreatureCatalog>();
            typeof(CreatureCatalog)
                .GetField("m_Creatures", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(catalog, creatures);
            return catalog;
        }

        [Test]
        public void IdRoundTrips()
        {
            var goblin = Definition("goblin");
            var knight = Definition("knight");
            var catalog = Catalog(goblin, knight);

            try
            {
                Assert.AreSame(goblin, catalog.Resolve(catalog.IdOf(goblin)));
                Assert.AreSame(knight, catalog.Resolve(catalog.IdOf(knight)));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(goblin);
                Object.DestroyImmediate(knight);
            }
        }

        [Test]
        public void IdSurvivesReordering()
        {
            // The whole reason ids are hashed rather than indexed: reordering the catalog must not
            // change what any id means.
            var goblin = Definition("goblin");
            var knight = Definition("knight");

            var first = Catalog(goblin, knight);
            var second = Catalog(knight, goblin);

            try
            {
                Assert.AreEqual(first.IdOf(goblin), second.IdOf(goblin));
                Assert.AreSame(goblin, second.Resolve(first.IdOf(goblin)));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(goblin);
                Object.DestroyImmediate(knight);
            }
        }

        [Test]
        public void UnknownIdResolvesToNull()
        {
            var catalog = Catalog();
            try
            {
                Assert.IsNull(catalog.Resolve(12345));
                Assert.AreEqual(CreatureCatalog.NoCreature, catalog.IdOf(null));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void HashIsStableAcrossCalls()
        {
            // string.GetHashCode is randomised per process in modern .NET; two clients using it
            // would disagree about every creature.
            Assert.AreEqual(CreatureCatalog.HashId("goblin"), CreatureCatalog.HashId("goblin"));
            Assert.AreNotEqual(CreatureCatalog.HashId("goblin"), CreatureCatalog.HashId("knight"));
        }

        [Test]
        public void HashNeverReturnsTheReservedValue()
        {
            Assert.AreEqual(CreatureCatalog.NoCreature, CreatureCatalog.HashId(""));
            Assert.AreEqual(CreatureCatalog.NoCreature, CreatureCatalog.HashId(null));

            foreach (var id in new[] { "a", "goblin", "knight", "wolf", "bandit", "guard", "ogre" })
            {
                Assert.AreNotEqual(CreatureCatalog.NoCreature, CreatureCatalog.HashId(id), id);
            }
        }

        [Test]
        public void TheAuthoredDemoIdsDoNotCollide()
        {
            var ids = new[]
            {
                "hero-knight", "hero-ranger", "hero-cleric",
                "monster-goblin", "monster-ogre", "monster-wolf",
                "guard-sergeant", "guard-recruit", "guard-archer",
                "bandit-cutpurse", "bandit-brute", "bandit-scout"
            };

            var hashes = ids.Select(CreatureCatalog.HashId).ToList();

            Assert.AreEqual(ids.Length, hashes.Distinct().Count(), "Two demo creatures share a hash");
        }
    }
}

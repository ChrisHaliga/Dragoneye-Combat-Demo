using System.Collections.Generic;
using System.Linq;
using Dragoneye.Combat;
using Dragoneye.Game.Combat;
using Dragoneye.Hex.Systems;
using Dragoneye.Scenarios;
using NUnit.Framework;
using UnityEngine;
using Cell = Dragoneye.Hex.Cell;
using Hex = Dragoneye.Hex.Hex;
using HexDirection = Dragoneye.Hex.HexDirection;
using HexLayout = Dragoneye.Hex.HexLayout;
using MapRecipe = Dragoneye.Hex.MapRecipe;
using Wall = Dragoneye.Hex.Wall;
using WallFlags = Dragoneye.Hex.WallFlags;
using WallSegment = Dragoneye.Hex.WallSegment;

namespace Dragoneye.Hex.Tests
{
    /// <summary>
    /// The scenarios are fights written down, and a fight written down wrongly fails at the
    /// spawn with nothing learned. These read every scenario for the mistakes a script can
    /// contain: a key nobody has, two actors on one cell, a cell nobody can stand on.
    /// </summary>
    public class ScenarioLibraryTests
    {
        static IEnumerable<Scenario> All => ScenarioLibrary.All;

        [Test]
        public void EveryScenarioHasItsOwnIdAndASeed()
        {
            Assert.IsNotEmpty(ScenarioLibrary.All);
            CollectionAssert.AllItemsAreUnique(All.Select(s => s.Id).ToList());

            foreach (var scenario in All)
            {
                Assert.AreNotEqual(0, scenario.Seed, $"{scenario.Id} has no seed; zero would pick one");
                Assert.IsFalse(string.IsNullOrWhiteSpace(scenario.Title), $"{scenario.Id} has no title");
                Assert.IsFalse(string.IsNullOrWhiteSpace(scenario.Summary), $"{scenario.Id} has no summary");
            }
        }

        [Test]
        public void EveryActorStartsOnItsOwnStandableCell()
        {
            foreach (var scenario in All)
            {
                var grid = new GridRules(scenario.Map.Build(new HexLayout(1f, Vector3.zero), name => null));
                var taken = new HashSet<Cell>();

                foreach (var actor in scenario.Actors)
                {
                    Assert.IsTrue(grid.IsWalkable(actor.Cell), $"{scenario.Id}: {actor.Key} starts on {actor.Cell}, which nobody can stand on");
                    Assert.IsTrue(taken.Add(actor.Cell), $"{scenario.Id}: two actors start on {actor.Cell}");
                }

                CollectionAssert.AllItemsAreUnique(scenario.Actors.Select(a => a.Key).ToList(), scenario.Id);
            }
        }

        [Test]
        public void EveryOrderAndEventNamesAnActorTheScenarioHas()
        {
            foreach (var scenario in All)
            {
                var keys = new HashSet<string>(scenario.Actors.Select(a => a.Key));

                foreach (var actor in scenario.Actors)
                {
                    foreach (var turn in actor.Turns)
                    {
                        foreach (var order in turn)
                        {
                            if (order.Kind == OrderKind.UseSkill)
                            {
                                Assert.IsTrue(keys.Contains(order.Target), $"{scenario.Id}: {actor.Key} aims at '{order.Target}', who is not there");
                            }
                        }
                    }
                }

                foreach (var evt in scenario.Events)
                {
                    Assert.IsTrue(keys.Contains(evt.BeforeActor), $"{scenario.Id}: an event waits on '{evt.BeforeActor}', who is not there");
                    Assert.GreaterOrEqual(evt.Round, 1, $"{scenario.Id}: an event in round {evt.Round}");
                }
            }
        }

        [Test]
        public void ScriptsFitInTheRoundsAllowed()
        {
            foreach (var scenario in All)
            {
                foreach (var actor in scenario.Actors)
                {
                    Assert.LessOrEqual(actor.Turns.Count, scenario.MaxRounds,
                        $"{scenario.Id}: {actor.Key} has more turns scripted than rounds allowed");
                }
            }
        }

        [Test]
        public void TheRuinsRoomHasOneWayIn()
        {
            var map = Maps.Ruins().Build(new HexLayout(1f, Vector3.zero), name => null);
            var grid = new GridRules(map);
            var room = Cell.Whole(Maps.RoomLower);
            var outside = Cell.Whole(new Hex(0, -3));

            Assert.Greater(HexPathfinder.CostTo(grid, outside, room, null), 0, "the door lets you in");

            map.SetEdge(Maps.Doorway, HexDirection.North, new Wall(WallFlags.Solid));
            Assert.AreEqual(-1, HexPathfinder.CostTo(grid, outside, room, null), "and nothing else does");
        }
    }

    /// <summary>The scripted brain: one turn's orders per turn, and a refusal skips the rest of that turn.</summary>
    public class ScriptedBrainTests
    {
        static readonly Cell Here = Cell.Whole(Hex.Zero);
        static readonly Cell There = Cell.Whole(new Hex(1, 0));
        static readonly Cell Further = Cell.Whole(new Hex(2, 0));

        static BrainView View(uint id) => new BrainView(id, Here, Party.Guards, Ap.FromWhole(4), 10);

        static Actor Script() =>
            new Actor("a", "x", Party.Guards, Here, Facing.Default)
                .Then(Order.Move(There))
                .Then(Order.Use(100, "b"))
                .NextTurn()
                .Then(Order.Move(Further));

        [Test]
        public void OrdersComeOutOneTurnAtATime()
        {
            var round = 1;
            var brain = new ScriptedBrain(key => key == "b" ? 2u : 0u, () => round);
            brain.Register(1, Script());

            var first = brain.Decide(View(1), null, null);
            var second = brain.Decide(View(1), null, null);
            var third = brain.Decide(View(1), null, null);

            Assert.AreEqual(BrainAction.Move, first.Action);
            Assert.AreEqual(There, first.Destination);
            Assert.AreEqual(BrainAction.UseSkill, second.Action);
            Assert.AreEqual(100, second.SkillId);
            Assert.AreEqual(2u, second.TargetId, "the target key was resolved to its turn id");
            Assert.AreEqual(BrainAction.None, third.Action, "the turn's orders are spent");
            Assert.IsFalse(brain.AllScriptsSpent, "a second turn is still to come");

            round = 2;
            Assert.AreEqual(Further, brain.Decide(View(1), null, null).Destination);
            Assert.AreEqual(BrainAction.None, brain.Decide(View(1), null, null).Action);
            Assert.IsTrue(brain.AllScriptsSpent);
        }

        [Test]
        public void ATurnEndedEarlyLeavesTheNextTurnsOrdersForTheNextTurn()
        {
            var round = 1;
            var brain = new ScriptedBrain(key => 0u, () => round);
            brain.Register(1, Script());

            brain.Decide(View(1), null, null);   // the move, then say the fight refused it and ended the turn
            round = 2;

            var next = brain.Decide(View(1), null, null);

            Assert.AreEqual(Further, next.Destination, "round two's order, not the rest of round one's");
        }

        [Test]
        public void AnUnregisteredActorPasses()
        {
            var brain = new ScriptedBrain(key => 0u, () => 1);

            Assert.AreEqual(BrainAction.None, brain.Decide(View(9), null, null).Action);
        }

        [Test]
        public void OrdersAreAnnouncedAsTheyAreHandedOut()
        {
            var brain = new ScriptedBrain(key => 0u, () => 1);
            brain.Register(1, Script());
            var announced = new List<Order>();
            brain.Ordered += (id, order) => announced.Add(order);

            brain.Decide(View(1), null, null);
            brain.Decide(View(1), null, null);
            brain.Decide(View(1), null, null);

            Assert.AreEqual(2, announced.Count, "a pass is not an order");
            Assert.AreEqual(OrderKind.Move, announced[0].Kind);
        }
    }

    /// <summary>Recipes write the segments a straight wall lies on; segments name walls from either side.</summary>
    public class MapRecipeTests
    {
        static readonly Wall Solid = new Wall(WallFlags.Solid);

        [Test]
        public void AVerticalWallIsRaysZeroAndSixUpTheColumn()
        {
            var recipe = new MapRecipe(3, "grass").Vertical(new Hex(1, -1), 2, Solid);

            CollectionAssert.AreEquivalent(new[]
            {
                WallSegment.Ray(new Hex(1, -1), 0), WallSegment.Ray(new Hex(1, -1), 6),
                WallSegment.Ray(new Hex(1, 0), 0), WallSegment.Ray(new Hex(1, 0), 6)
            }, recipe.Walls.Select(w => w.Segment).ToList());
        }

        [Test]
        public void AHorizontalWallAlternatesRaysAndEdges()
        {
            var recipe = new MapRecipe(3, "grass").Horizontal(Hex.Zero, 3, Solid);
            var segments = recipe.Walls.Select(w => w.Segment).ToList();

            WallSegment.Edge(new Hex(1, -1), HexDirection.North, out var first, out var second);

            CollectionAssert.AreEquivalent(new[]
            {
                WallSegment.Ray(Hex.Zero, 3), WallSegment.Ray(Hex.Zero, 9),
                first, second,
                WallSegment.Ray(new Hex(2, -1), 3), WallSegment.Ray(new Hex(2, -1), 9)
            }, segments);
        }

        [Test]
        public void TheBuiltMapHasTheWallsAndTheGround()
        {
            var recipe = new MapRecipe(2, "grass").Tile(new Hex(1, 0), "stone").Edge(Hex.Zero, HexDirection.North, Solid);
            var map = recipe.Build(new HexLayout(1f, Vector3.zero), name => null);

            Assert.AreEqual(19, map.Count);
            Assert.IsTrue(map.HalfEdge(Hex.Zero, 0).BlocksSight);
            Assert.IsTrue(map.HalfEdge(Hex.Zero.Neighbor(HexDirection.North), 6).BlocksSight, "seen from the far side");
        }

        [Test]
        public void ASegmentIsTheSameWallFromEitherSide()
        {
            var map = new MapRecipe(2, "grass").Build(new HexLayout(1f, Vector3.zero), name => null);
            var north = Hex.Zero.Neighbor(HexDirection.North);

            map.SetWall(WallSegment.HalfEdge(Hex.Zero, 0), Solid);

            Assert.IsTrue(map.WallAt(WallSegment.HalfEdge(north, 5)).BlocksMovement, "the twin half-edge");
            Assert.AreEqual(WallSegment.Ray(Hex.Zero, 3), WallSegment.Ray(Hex.Zero, 15), "indices wrap");
            Assert.AreNotEqual(WallSegment.Ray(Hex.Zero, 3), WallSegment.HalfEdge(Hex.Zero, 3));
        }
    }

    /// <summary>The computer's choices are rules: the same roll gives the same answer anywhere.</summary>
    public class ComputerChoiceTests
    {
        sealed class FlatTable : IElementMatchup
        {
            public ClashOutcome Compare(Element attacker, Element defender) => ClashOutcome.Tie;
        }

        [Test]
        public void ADefenderHoldingOneKindAnswersWithIt()
        {
            var pool = ElementLedger.Starting(ElementCounts.Empty.With(Element.Geo, 2));
            var clash = ClashSequence.Begin(new[] { Element.Pyro }, new ClashSide(1), new ClashSide(2, disadvantage: true),
                pool, new FlatTable());

            var rolls = new Queue<float>(new[] { 0.9f, 0.1f });
            var answer = ClashDefenceOdds.ChooseAnswer(clash.Request, PossibleElements.Unknowable, pool.Pool,
                new FlatTable(), () => rolls.Dequeue());

            CollectionAssert.AreEqual(new[] { Element.Geo, Element.Geo }, answer, "two required, two held");
            Assert.AreEqual(0, rolls.Count, "one roll per pick");
        }

        [Test]
        public void AnElementIsNotOfferedAgainOnceItsLastIsUp()
        {
            var pool = ElementLedger.Starting(ElementCounts.Empty.With(Element.Geo, 1));
            var clash = ClashSequence.Begin(new[] { Element.Pyro }, new ClashSide(1), new ClashSide(2, disadvantage: true),
                pool, new FlatTable());

            var answer = ClashDefenceOdds.ChooseAnswer(clash.Request, PossibleElements.Unknowable, pool.Pool,
                new FlatTable(), () => 0.5f);

            CollectionAssert.AreEqual(new[] { Element.Geo }, answer, "the one it holds, and no more");
        }

        [Test]
        public void ASwingIsTakenUnlessTheRollFallsUnderTheHoldBack()
        {
            Assert.IsFalse(Combat.Opportunity.Takes(0f));
            Assert.IsFalse(Combat.Opportunity.Takes(Combat.Opportunity.HoldsBack));
            Assert.IsTrue(Combat.Opportunity.Takes(Combat.Opportunity.HoldsBack + 0.001f));
            Assert.IsTrue(Combat.Opportunity.Takes(0.99f));
        }
    }
}

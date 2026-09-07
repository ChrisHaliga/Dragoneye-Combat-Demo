using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Scenarios
{
    // Declared inside the namespace: out here the bare name Hex would bind to the Dragoneye.Hex
    // namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;
    using static ScenarioCheck;

    /// <summary>
    /// The maps the host can pick, played on: that a mansion's walls are closed and its hall
    /// runs door to door, and that the islands' bridge is the only way across water arrows fly
    /// over.
    /// </summary>
    public static class MapScenarios
    {
        static readonly Facing North = Facing.Of(0);
        static readonly Facing SouthEast = Facing.Of(2);
        static readonly Facing SouthWest = Facing.Of(4);

        /// <summary>
        /// A ranger walks in at the front door, up the hall and out at the back in one turn; a
        /// goblin on the west lawn is refused the gallery, because the only way in is round
        /// through a door and that is further than it can walk.
        /// </summary>
        public static Scenario MansionHall()
        {
            var frontStep = Cell.Whole(Maps.MansionFrontStep);
            var hall = Cell.Whole(Maps.MansionHall);
            var backStep = Cell.Whole(Maps.MansionBackStep);
            var lawn = Cell.Whole(Maps.MansionWestLawn);
            var gallery = Cell.Whole(Maps.MansionWestGallery);

            return new Scenario("mansion-hall", "The mansion's hall",
                    "A ranger walks in at the front door, up the great hall and out at the back "
                    + "door in one turn. A goblin on the lawn is refused the gallery: the wall is "
                    + "closed, and the way in is round through a door.",
                    4401, Maps.Mansion())
                .With(new Actor("ranger", Premade.Ranger, Party.Guards, frontStep, North, 3)
                    .Then(Order.Move(hall))
                    .Then(Order.Move(backStep)))
                .With(new Actor("goblin", Premade.Goblin, Party.Monsters, lawn, SouthEast)
                    .Then(Order.Move(gallery)))                     // refused: too far round
                .Expecting(world =>
                {
                    var oracle = new Oracle(MansionHall(), world);
                    oracle.Move("ranger", hall);
                    oracle.Move("ranger", backStep);

                    var throughTheHall = HexPathfinder.CostTo(world.Grid, frontStep, backStep, null);
                    var roundToTheGallery = HexPathfinder.CostTo(world.Grid, lawn, gallery, null);
                    var hallToGallery = HexPathfinder.CostTo(world.Grid, hall, gallery, null);
                    var hallToCellar = HexPathfinder.CostTo(world.Grid, Cell.Whole(Maps.MansionHallSouth),
                        Cell.Whole(Maps.MansionWestCellar), null);

                    return new[]
                    {
                        Equal("the ranger ended on the back step", backStep, world.CellOf("ranger")),
                        Equal("facing the way it walked out", oracle.FacingOf("ranger"), world.FacingOf("ranger")),
                        Equal("the hall runs door to door in six steps", 6, throughTheHall),
                        Equal("which is exactly the distance between the doors",
                            Cell.Distance(frontStep, backStep), throughTheHall),
                        That("the goblin's walk to the gallery was refused",
                            Reading.Count(world, TraceKind.Ordered, "goblin") == 1
                            && Reading.Count(world, TraceKind.Moved, "goblin") == 0),
                        Equal("because the lawn is eleven steps from the gallery, round by a door",
                            11, roundToTheGallery),
                        That("while the crow flies it in three", Cell.Distance(lawn, gallery) == 3),
                        Equal("the gallery is two steps from the hall", 2, hallToGallery),
                        Equal("and the front cellar is two steps from the hall's south end", 2, hallToCellar),
                        That("nobody swung at anybody", Reading.Count(world, TraceKind.Clash) == 0)
                    };
                });
        }

        /// <summary>
        /// A ranger crosses the bridge from landing to landing. Water is not stood on; with the
        /// bridge blocked there is no way over at all.
        /// </summary>
        public static Scenario IslandsBridge()
        {
            var westLanding = Cell.Whole(Maps.WestLanding);
            var eastLanding = Cell.Whole(Maps.EastLanding);
            var farShore = Cell.Whole(new Hex(5, -3));
            var water = Cell.Whole(new Hex(0, 2));

            return new Scenario("islands-bridge", "The bridge",
                    "A ranger crosses the bridge from one landing to the other. The water is not "
                    + "stood on, and with the bridge blocked there is no way across.",
                    4402, Maps.Islands())
                .With(new Actor("ranger", Premade.Ranger, Party.Heroes, westLanding, SouthEast, 3)
                    .Then(Order.Move(eastLanding)))
                .With(new Actor("goblin", Premade.Goblin, Party.Monsters, farShore, SouthWest))
                .Expecting(world =>
                {
                    var oracle = new Oracle(IslandsBridge(), world);
                    oracle.Move("ranger", eastLanding);

                    var route = new List<Cell>();
                    HexPathfinder.TryFindPath(world.Grid, westLanding, eastLanding, null, route, out var acrossTheBridge);

                    var onTheBridge = route.Count == 4
                        && route[0].Tile == Maps.BridgeWest && route[1].Tile == Maps.BridgeMiddle
                        && route[2].Tile == Maps.BridgeEast && route[3] == eastLanding;

                    var blocked = new HashSet<Cell>
                    {
                        Cell.Whole(Maps.BridgeWest), Cell.Whole(Maps.BridgeMiddle), Cell.Whole(Maps.BridgeEast)
                    };

                    var withoutTheBridge = HexPathfinder.CostTo(world.Grid, westLanding, eastLanding, blocked);
                    var middleToMiddle = HexPathfinder.CostTo(world.Grid, Cell.Whole(Maps.WestIsland),
                        Cell.Whole(Maps.EastIsland), null);

                    return new[]
                    {
                        Equal("the ranger ended on the east landing", eastLanding, world.CellOf("ranger")),
                        Equal("facing the way it crossed", oracle.FacingOf("ranger"), world.FacingOf("ranger")),
                        That("the crossing is four steps, every one on the bridge", onTheBridge,
                            $"cost {acrossTheBridge}, route {string.Join(" ", route)}"),
                        That("water is nowhere to stand", !world.Grid.IsWalkable(water)),
                        That("with the bridge blocked there is no way across", withoutTheBridge < 0,
                            $"cost {withoutTheBridge}"),
                        Equal("the islands' middles are eight steps apart, as the crow flies",
                            Cell.Distance(Cell.Whole(Maps.WestIsland), Cell.Whole(Maps.EastIsland)), middleToMiddle),
                        That("nobody swung at anybody", Reading.Count(world, TraceKind.Clash) == 0)
                    };
                });
        }

        /// <summary>
        /// An archer on one landing shoots a goblin on the other. Four tiles of open water are
        /// nothing to an arrow.
        /// </summary>
        public static Scenario IslandsArrows()
        {
            var westLanding = Cell.Whole(Maps.WestLanding);
            var eastLanding = Cell.Whole(Maps.EastLanding);

            return new Scenario("islands-arrows", "Arrows over water",
                    "An archer on the west landing shoots a goblin on the east landing across "
                    + "four tiles of open water, at the chance an uncovered four-tile shot gets.",
                    4403, Maps.Islands())
                .With(new Actor("archer", Premade.Archer, Party.Guards, westLanding, SouthEast, 2)
                    .Then(Order.Use(Skills.Loose, "goblin")))
                .With(new Actor("goblin", Premade.Goblin, Party.Monsters, eastLanding, SouthWest))
                .Expecting(world =>
                {
                    var oracle = new Oracle(IslandsArrows(), world);
                    var shot = oracle.Use("archer", Skills.Loose, "goblin");
                    var real = Reading.First(world, TraceKind.Shot, "archer");
                    var line = LineOfSight.Verdict(world.Grid, westLanding, eastLanding);

                    var checks = new List<ScenarioCheck>
                    {
                        Equal("the landings are four tiles apart", 4, Cell.Distance(westLanding, eastLanding)),
                        Equal("with a clear line between them over the water", LineVerdict.Clear, line),
                        That("the shot was taken", real.HasValue),
                        Equal("at the chance the rules give a four-tile shot with no cover",
                            shot.Chance, real?.Amount ?? -1),
                        Equal("and landed as the dice said", shot.Landed, real?.Landed ?? !shot.Landed),
                        Equal("the goblin's health is what the oracle worked out",
                            oracle.HpOf("goblin"), world.HpOf("goblin")),
                        Equal("the archer stayed on its own shore", westLanding, world.CellOf("archer"))
                    };

                    if (shot.Landed)
                    {
                        checks.Add(Equal("the clash came out as the oracle said", shot.Outcome,
                            Reading.First(world, TraceKind.Clash, "archer")?.Outcome ?? default));
                    }

                    return checks;
                });
        }
    }
}

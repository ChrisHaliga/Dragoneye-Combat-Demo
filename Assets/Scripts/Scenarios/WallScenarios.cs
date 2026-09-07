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
    /// What walls do: to lines, to feet, to where a creature can stand, and to a creature
    /// standing where one comes down.
    /// </summary>
    public static class WallScenarios
    {
        static readonly Facing North = Facing.Of(0);
        static readonly Facing South = Facing.Of(3);

        /// <summary>
        /// A room with a door. A shot through the wall is refused; a walk round to the doorway
        /// and a shot through it is not, and the target inside is flanked by it.
        /// </summary>
        public static Scenario RoomAndDoor()
        {
            var outside = Cell.Whole(new Hex(-1, 2));
            var doorstep = Cell.Whole(new Hex(1, -1));
            var inside = Cell.Whole(Maps.RoomUpper);

            return new Scenario("room-and-door", "The room and its door",
                    "An archer outside the ruined room cannot shoot through its wall, walks round "
                    + "to the doorway, and shoots the recruit inside through it -- from behind.",
                    1107, Maps.Ruins())
                .With(new Actor("archer", Premade.Archer, Party.Guards, outside, North, 2)
                    .Then(Order.Use(Skills.Loose, "recruit"))       // refused: no line
                    .NextTurn()
                    .Then(Order.Move(doorstep))
                    .Then(Order.Use(Skills.Loose, "recruit")))
                .With(new Actor("recruit", Premade.Recruit, Party.Bandits, inside, North))
                .Expecting(world =>
                {
                    var oracle = new Oracle(RoomAndDoor(), world);
                    oracle.Move("archer", doorstep);
                    var shot = oracle.Use("archer", Skills.Loose, "recruit");

                    var real = Reading.First(world, TraceKind.Shot, "archer");
                    var halves = HexPathfinder.CostTo(world.Grid, new Cell(Maps.WestSide, 0),
                        new Cell(Maps.WestSide, 1), null);

                    var checks = new List<ScenarioCheck>
                    {
                        That("the first shot, through the wall, was ordered",
                            Reading.Has(world, TraceKind.Ordered, "archer", 1)),
                        That("and refused: nothing was shot in round one",
                            !Reading.Has(world, TraceKind.Shot, "archer", 1)
                            && !Reading.Has(world, TraceKind.Clash, "archer", 1)),
                        Equal("the archer walked round to the doorstep", doorstep, world.CellOf("archer")),
                        That("the second shot, through the door, was taken", real.HasValue),
                        Equal("at the chance the rules give a three-tile shot with no cover",
                            shot.Chance, real?.Amount ?? -1),
                        Equal("and landed as the dice said", shot.Landed, real?.Landed ?? !shot.Landed),
                        Equal("the recruit's health is what the oracle worked out",
                            oracle.HpOf("recruit"), world.HpOf("recruit")),
                        Equal("and so is its armour", oracle.ArmourOf("recruit"), world.ArmourOf("recruit")),
                        Equal("the recruit faces the way the oracle says (it turns if it was hit from behind)",
                            oracle.FacingOf("recruit"), world.FacingOf("recruit")),
                        That("the two halves of the wall's tile have no short path between them",
                            halves > 2, $"cost {halves}")
                    };

                    if (shot.Landed)
                    {
                        checks.Add(That("the shot was a flank: the recruit was facing away from the door", shot.Flanked));
                        checks.Add(Equal("the clash came out as the oracle said", shot.Outcome,
                            Reading.First(world, TraceKind.Clash, "archer")?.Outcome ?? default));
                    }

                    return checks;
                });
        }

        /// <summary>
        /// Walls coming down mid-fight. An ogre inside the room cannot walk out through the east
        /// wall; once it falls, it can. A recruit standing in the outer half of the west wall's
        /// tile is carried onto the whole tile when that wall falls too.
        /// </summary>
        public static Scenario WallBreak()
        {
            var inside = Cell.Whole(Maps.RoomLower);
            var beyondEast = Cell.Whole(new Hex(3, 1));
            var outerWest = new Cell(Maps.WestSide, 1);

            return new Scenario("wall-break", "Walls coming down",
                    "An ogre in the room orders a walk through the east wall and is refused. The "
                    + "walls fall in round two: it walks out, and a recruit stood against the west "
                    + "wall is carried onto the tile the wall used to split.",
                    2203, Maps.Ruins())
                .With(new Actor("ogre", Premade.Ogre, Party.Monsters, inside, North, 3)
                    .Then(Order.Move(beyondEast))                   // round one: refused, no way through
                    .NextTurn()
                    .Then(Order.Move(beyondEast)))                  // round two: the wall is down
                .With(new Actor("recruit", Premade.Recruit, Party.Guards, outerWest, North))
                .At(2, "ogre", WallSegment.Ray(Maps.EastSide, 0), Wall.None)
                .At(2, "ogre", WallSegment.Ray(Maps.EastSide, 6), Wall.None)
                .At(2, "ogre", WallSegment.Ray(Maps.WestSide, 0), Wall.None)
                .At(2, "ogre", WallSegment.Ray(Maps.WestSide, 6), Wall.None)
                .Lasting(3)
                .Expecting(world =>
                {
                    var cost = HexPathfinder.CostTo(world.Grid, inside, beyondEast, null);
                    var east = world.Grid.Map[Maps.EastSide].Areas.Count;
                    var west = world.Grid.Map[Maps.WestSide].Areas.Count;

                    return new[]
                    {
                        That("the walk through the standing wall was ordered and refused",
                            Reading.Has(world, TraceKind.Ordered, "ogre", 1)
                            && !Reading.Has(world, TraceKind.Moved, "ogre", 1)),
                        Equal("four wall segments came down", 4, Reading.Count(world, TraceKind.Wall)),
                        Equal("the east wall's tile is whole again", 1, east),
                        Equal("and so is the west wall's", 1, west),
                        That("the ogre walked out through where the wall was",
                            Reading.Has(world, TraceKind.Moved, "ogre", 2)),
                        Equal("and stands beyond it", beyondEast, world.CellOf("ogre")),
                        Equal("the way out is two steps now", 2, cost),
                        Equal("the recruit was carried onto the whole tile",
                            Cell.Whole(Maps.WestSide), world.CellOf("recruit")),
                        That("nobody was hurt", world.HpOf("ogre") == world.MaxHpOf("ogre")
                            && world.HpOf("recruit") == world.MaxHpOf("recruit"))
                    };
                });
        }

        /// <summary>
        /// Feet: a route round a boulder and through a curtain, a walk a creature in plate cannot
        /// afford, and a walk from the inside half of a split tile to its outside half -- the long
        /// way round, through the door.
        /// </summary>
        public static Scenario Movement()
        {
            var rangerStart = Cell.Whole(new Hex(-2, 0));
            var rangerEnd = Cell.Whole(new Hex(-2, -2));
            var knightStart = Cell.Whole(new Hex(0, -3));
            var tooFar = Cell.Whole(new Hex(0, 0));
            var knightEnd = Cell.Whole(new Hex(0, -2));
            var goblinStart = Cell.Whole(Maps.RoomUpper);
            var insideHalf = new Cell(Maps.WestSide, 0);
            var outsideHalf = new Cell(Maps.WestSide, 1);

            return new Scenario("movement", "Steps and their price",
                    "A ranger walks round a boulder and through a curtain. A knight in plate is "
                    + "refused a three-tile walk and takes a one-tile one. A goblin steps into the "
                    + "inside half of a split tile, then reaches the outside half the only way it "
                    + "can: round through the door.",
                    3301, Maps.Ruins())
                .With(new Actor("goblin", Premade.Goblin, Party.Monsters, goblinStart, North)
                    .Then(Order.Move(insideHalf))
                    .Then(Order.Move(outsideHalf)))
                .With(new Actor("ranger", Premade.Ranger, Party.Guards, rangerStart, North, 3)
                    .Then(Order.Move(rangerEnd)))
                .With(new Actor("knight", Premade.Knight, Party.Guards, knightStart, North, 3)
                    .Then(Order.Move(tooFar))                       // refused: too far for plate
                    .NextTurn()
                    .Then(Order.Move(knightEnd)))
                .Expecting(world =>
                {
                    var roundBoulder = HexPathfinder.CostTo(world.Grid, rangerStart, rangerEnd, null);
                    var roundTheRoom = HexPathfinder.CostTo(world.Grid, insideHalf, outsideHalf, null);
                    var goblinMoves = Reading.Count(world, TraceKind.Moved, "goblin");

                    return new[]
                    {
                        Equal("the ranger reached the far side of the boulder", rangerEnd, world.CellOf("ranger")),
                        Equal("by a route one step longer than the crow flies", Cell.Distance(rangerStart, rangerEnd) + 1, roundBoulder),
                        That("the knight's three-tile walk was refused",
                            Reading.Count(world, TraceKind.Ordered, "knight") == 2
                            && Reading.Count(world, TraceKind.Moved, "knight") == 1),
                        Equal("and its one-tile walk was taken", knightEnd, world.CellOf("knight")),
                        Equal("the goblin took two walks", 2, goblinMoves),
                        Equal("the first into the inside half of the wall's tile",
                            insideHalf, Reading.First(world, TraceKind.Moved, "goblin")?.To ?? default),
                        Equal("and ended on the outside half", outsideHalf, world.CellOf("goblin")),
                        That("which is seven steps away by the door, and one by distance",
                            roundTheRoom == 7 && Cell.Distance(insideHalf, outsideHalf) == 1, $"cost {roundTheRoom}"),
                        That("nobody swung at anybody", Reading.Count(world, TraceKind.Clash) == 0)
                    };
                });
        }

        /// <summary>
        /// Eyes and arrows: a hedge is shot over at a cost and walked round at a bigger one; a
        /// curtain stops a jab from an adjacent tile and lets a step past it through.
        /// </summary>
        public static Scenario LineOfSight()
        {
            var archerCell = Cell.Whole(new Hex(-4, 1));
            var goblinCell = Cell.Whole(new Hex(-2, 1));
            var goblinEnd = Cell.Whole(new Hex(-4, 0));
            var cutpurseCell = Cell.Whole(Maps.CurtainFoot);
            var cutpurseStep = Cell.Whole(new Hex(0, -2));
            var recruitCell = Cell.Whole(new Hex(-1, -1));

            return new Scenario("line-of-sight", "Hedge and curtain",
                    "An archer and a goblin shoot at each other over a waist-high hedge, each at "
                    + "a cover penalty; the goblin then walks the long way round it. A cutpurse "
                    + "under a curtain cannot jab the recruit on the other side of it, steps out, "
                    + "and can.",
                    4409, Maps.Ruins())
                .With(new Actor("goblin", Premade.Goblin, Party.Monsters, goblinCell, Facing.Of(4))
                    .Then(Order.Use(Skills.Sling, "archer"))
                    .NextTurn()
                    .Then(Order.Move(goblinEnd)))
                .With(new Actor("cutpurse", Premade.Cutpurse, Party.Bandits, cutpurseCell, North, 2)
                    .Then(Order.Use(Skills.Jab, "recruit"))         // refused: the curtain
                    .NextTurn()
                    .Then(Order.Move(cutpurseStep))
                    .Then(Order.Use(Skills.Jab, "recruit")))
                .With(new Actor("archer", Premade.Archer, Party.Guards, archerCell, Facing.Of(1), 2)
                    .Then(Order.Use(Skills.Loose, "goblin")))
                .With(new Actor("recruit", Premade.Recruit, Party.Guards, recruitCell, South))
                .Expecting(world =>
                {
                    var oracle = new Oracle(LineOfSight(), world);

                    // Initiative: goblin (10), cutpurse (10, spawned later), archer (8), recruit
                    // (6). Round one: the sling, the refused jab, the bow. Round two: the goblin's
                    // walk round the hedge, the cutpurse's step and jab.
                    var sling = oracle.Use("goblin", Skills.Sling, "archer");
                    var loose = oracle.Use("archer", Skills.Loose, "goblin");

                    if (oracle.IsAlive("goblin"))
                    {
                        oracle.Move("goblin", goblinEnd);
                    }

                    oracle.Move("cutpurse", cutpurseStep);
                    var jab = oracle.Use("cutpurse", Skills.Jab, "recruit");

                    var slingShot = Reading.First(world, TraceKind.Shot, "goblin");
                    var looseShot = Reading.First(world, TraceKind.Shot, "archer");
                    var roundHedge = HexPathfinder.CostTo(world.Grid, goblinCell, goblinEnd, null);

                    return new[]
                    {
                        Equal("the goblin's sling was priced with the hedge in the way",
                            sling.Chance, slingShot?.Amount ?? -1),
                        Equal("and went as the dice said", sling.Landed, slingShot?.Landed ?? !sling.Landed),
                        Equal("the archer's shot was priced with the hedge in the way",
                            loose.Chance, looseShot?.Amount ?? -1),
                        Equal("and went as the dice said", loose.Landed, looseShot?.Landed ?? !loose.Landed),
                        Equal("the archer's health is what the oracle worked out", oracle.HpOf("archer"), world.HpOf("archer")),
                        Equal("and the goblin's", oracle.HpOf("goblin"), world.HpOf("goblin")),
                        That("the jab through the curtain was ordered and refused",
                            Reading.Has(world, TraceKind.Ordered, "cutpurse", 1)
                            && Reading.Count(world, TraceKind.Clash, "cutpurse") == 1),
                        Equal("the cutpurse stepped out from under the curtain", cutpurseStep, world.CellOf("cutpurse")),
                        Equal("and its jab from there came out as the oracle said",
                            jab.Outcome, Reading.First(world, TraceKind.Clash, "cutpurse")?.Outcome ?? default),
                        Equal("the recruit's health agrees", oracle.HpOf("recruit"), world.HpOf("recruit")),
                        Equal("the goblin ended where the oracle put it", oracle.CellOf("goblin"), world.CellOf("goblin")),
                        That("the hedge is walked round, not through", roundHedge >= 6, $"cost {roundHedge}")
                    };
                });
        }
    }
}

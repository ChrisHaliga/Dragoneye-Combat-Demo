using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Scenarios
{
    // Declared inside the namespace: out here the bare name Hex would bind to the Dragoneye.Hex
    // namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;
    using static ScenarioCheck;

    /// <summary>
    /// The fight itself: flanks, shots, swings at passers-by, armour, healing, dying, initiative,
    /// and the game's own opponent playing both sides.
    /// </summary>
    public static class CombatScenarios
    {
        static readonly Facing North = Facing.Of(0);
        static readonly Facing South = Facing.Of(3);

        /// <summary>
        /// A sergeant facing north is bitten from behind by a wolf, then jabbed by a goblin in
        /// front. The bite is a flank: two elements go up against it, read worst-of, and the
        /// sergeant turns to face the wolf -- which puts the goblin behind it for the jab.
        /// </summary>
        public static Scenario Flank()
        {
            var middle = Cell.Whole(Hex.Zero);
            var behind = Cell.Whole(new Hex(0, -1));
            var ahead = Cell.Whole(new Hex(0, 1));

            return new Scenario("flank", "Flanking",
                    "A wolf bites a sergeant from behind: a flank, answered with two elements read "
                    + "worst-of. The sergeant turns to face it, and the goblin in front becomes the "
                    + "one behind.",
                    5501, Maps.Open())
                .With(new Actor("wolf", Premade.Wolf, Party.Monsters, behind, North)
                    .Then(Order.Use(Skills.Bite, "sergeant")))
                .With(new Actor("goblin", Premade.Goblin, Party.Monsters, ahead, South)
                    .Then(Order.Use(Skills.Jab, "sergeant")))
                .With(new Actor("sergeant", Premade.Sergeant, Party.Guards, middle, North, 3))
                .Expecting(world =>
                {
                    var oracle = new Oracle(Flank(), world);
                    var bite = oracle.Use("wolf", Skills.Bite, "sergeant");
                    var jab = oracle.Use("goblin", Skills.Jab, "sergeant");
                    var clashes = Reading.Clashes(world);

                    return new[]
                    {
                        Equal("two attacks were answered", 2, clashes.Count),
                        That("the bite was a flank", bite.Flanked),
                        Equal("the sergeant put up two elements against it", 2,
                            clashes.Count > 0 ? clashes[0].DefenderElements.Count : -1),
                        Equal("and the bite came out as the oracle said", bite.Outcome,
                            clashes.Count > 0 ? clashes[0].Outcome : default),
                        Equal("the jab came out as the oracle said", jab.Outcome,
                            clashes.Count > 1 ? clashes[1].Outcome : default),
                        Equal("the sergeant's health agrees", oracle.HpOf("sergeant"), world.HpOf("sergeant")),
                        Equal("and its armour", oracle.ArmourOf("sergeant"), world.ArmourOf("sergeant")),
                        Equal("and which way it ended up facing", oracle.FacingOf("sergeant"), world.FacingOf("sergeant")),
                        Equal("the wolf faces what it bit", oracle.FacingOf("wolf"), world.FacingOf("wolf")),
                        Equal("the goblin faces what it jabbed", oracle.FacingOf("goblin"), world.FacingOf("goblin"))
                    };
                });
        }

        /// <summary>
        /// Shots over a body. A goblin slings at an archer three tiles off with a recruit in the
        /// way; the archer looses back over the same recruit. Distance and cover both come off the
        /// chance, the dice decide, and a shot that lands is answered like any other attack.
        /// </summary>
        public static Scenario Ranged()
        {
            var archerCell = Cell.Whole(Hex.Zero);
            var recruitCell = Cell.Whole(new Hex(1, 0));
            var goblinCell = Cell.Whole(new Hex(3, 0));

            return new Scenario("ranged", "Ranged attacks",
                    "A goblin and an archer shoot at each other over a recruit standing between "
                    + "them. Each shot is priced by distance and by the body in the way, rolled "
                    + "with the fight's dice, and answered if it lands.",
                    6607, Maps.Open())
                .With(new Actor("goblin", Premade.Goblin, Party.Monsters, goblinCell, Facing.Of(4))
                    .Then(Order.Use(Skills.Sling, "archer")))
                .With(new Actor("archer", Premade.Archer, Party.Guards, archerCell, Facing.Of(1), 2)
                    .Then(Order.Use(Skills.Loose, "goblin")))
                .With(new Actor("recruit", Premade.Recruit, Party.Guards, recruitCell, North))
                .Expecting(world =>
                {
                    var oracle = new Oracle(Ranged(), world);
                    var sling = oracle.Use("goblin", Skills.Sling, "archer");
                    var loose = oracle.Use("archer", Skills.Loose, "goblin");

                    var slingShot = Reading.First(world, TraceKind.Shot, "goblin");
                    var looseShot = Reading.First(world, TraceKind.Shot, "archer");

                    return new[]
                    {
                        That("both shots were rolled", slingShot.HasValue && looseShot.HasValue),
                        Equal("the sling's chance counts two tiles of falloff and one body", sling.Chance, slingShot?.Amount ?? -1),
                        Equal("and it went as the dice said", sling.Landed, slingShot?.Landed ?? !sling.Landed),
                        Equal("the bow's chance counts two tiles of falloff and one body", loose.Chance, looseShot?.Amount ?? -1),
                        Equal("and it went as the dice said", loose.Landed, looseShot?.Landed ?? !loose.Landed),
                        Equal("a shot that landed was answered, and one that missed was not",
                            (sling.Landed ? 1 : 0) + (loose.Landed ? 1 : 0), Reading.Count(world, TraceKind.Clash)),
                        Equal("the archer's health agrees with the oracle", oracle.HpOf("archer"), world.HpOf("archer")),
                        Equal("and the goblin's", oracle.HpOf("goblin"), world.HpOf("goblin")),
                        Equal("the recruit in the middle was never touched", world.MaxHpOf("recruit"), world.HpOf("recruit")),
                        Equal("the archer turned to face its target", oracle.FacingOf("archer"), world.FacingOf("archer"))
                    };
                });
        }

        /// <summary>
        /// Walking out from under somebody's nose. A wolf sidesteps within the sergeant's front and
        /// is not swung at; a goblin walks out of it and is -- or is let go, as the dice decide.
        /// </summary>
        public static Scenario Opportunity()
        {
            var sergeantCell = Cell.Whole(Hex.Zero);
            var goblinCell = Cell.Whole(new Hex(0, 1));
            var goblinEnd = Cell.Whole(new Hex(2, 0));
            var wolfCell = Cell.Whole(new Hex(-1, 1));
            var wolfEnd = Cell.Whole(new Hex(1, 0));

            return new Scenario("opportunity", "Opportunity attacks",
                    "A wolf sidesteps from one watched tile to another in front of a sergeant and "
                    + "is not swung at. A goblin walks out of the sergeant's front and is offered "
                    + "up: the sergeant takes its swing or lets it go, as the dice decide, and the "
                    + "goblin arrives regardless.",
                    7703, Maps.Open())
                .With(new Actor("wolf", Premade.Wolf, Party.Monsters, wolfCell, North)
                    .Then(Order.Move(wolfEnd)))
                .With(new Actor("goblin", Premade.Goblin, Party.Monsters, goblinCell, North)
                    .Then(Order.Move(goblinEnd)))
                .With(new Actor("sergeant", Premade.Sergeant, Party.Guards, sergeantCell, North, 3))
                .Expecting(world =>
                {
                    var oracle = new Oracle(Opportunity(), world);
                    oracle.Move("wolf", wolfEnd);
                    oracle.Move("goblin", goblinEnd);

                    var swing = Reading.First(world, TraceKind.Clash, "sergeant", 0, Combat.Opportunity.SkillId);
                    var held = Reading.First(world, TraceKind.HeldBack, "sergeant");
                    var offered = (swing.HasValue ? 1 : 0) + (held.HasValue ? 1 : 0);

                    return new[]
                    {
                        Equal("the sergeant was offered exactly one swing", 1, offered),
                        Equal("at the goblin, whose walk left its front; the wolf's sidestep offered nothing",
                            "goblin", swing?.Target ?? held?.Target ?? ""),
                        Equal("and took it, or let it go, as the dice said", oracle.SwingsTaken == 1, swing.HasValue),
                        Equal("the wolf arrived", wolfEnd, world.CellOf("wolf")),
                        Equal("the goblin's health is what the oracle worked out", oracle.HpOf("goblin"), world.HpOf("goblin")),
                        Equal("the goblin arrived where it was going", oracle.CellOf("goblin"), world.CellOf("goblin")),
                        Equal("facing the way it went", oracle.FacingOf("goblin"), world.FacingOf("goblin")),
                        Equal("the sergeant still faces north: the goblin left from in front of it",
                            North, world.FacingOf("sergeant"))
                    };
                });
        }

        /// <summary>
        /// Armour takes the hit first and never comes back; health comes back a little every turn.
        /// A wolf bites and mauls an ogre, whose armour soaks what it can; the ogre's toughness
        /// heals it at the start of its turn, and it mauls back.
        /// </summary>
        public static Scenario ArmourAndRegen()
        {
            var ogreCell = Cell.Whole(Hex.Zero);
            var wolfCell = Cell.Whole(new Hex(0, -1));

            return new Scenario("armour-and-regen", "Armour and regeneration",
                    "A wolf bites and mauls an ogre. The ogre's armour pool soaks what it can and "
                    + "does not come back; its toughness heals a point at the start of its turn; "
                    + "then it mauls the wolf.",
                    8809, Maps.Open())
                .With(new Actor("wolf", Premade.Wolf, Party.Monsters, wolfCell, North)
                    .Then(Order.Use(Skills.Bite, "ogre"))
                    .Then(Order.Use(Skills.Maul, "ogre")))
                .With(new Actor("ogre", Premade.Ogre, Party.Guards, ogreCell, South, 3)
                    .Then(Order.Use(Skills.Maul, "wolf")))
                .Expecting(world =>
                {
                    var oracle = new Oracle(ArmourAndRegen(), world);
                    oracle.BeginTurn("wolf");
                    var bite = oracle.Use("wolf", Skills.Bite, "ogre");
                    var maul = oracle.Use("wolf", Skills.Maul, "ogre");
                    var healed = oracle.BeginTurn("ogre");
                    var back = oracle.Use("ogre", Skills.Maul, "wolf");

                    var recovered = Reading.First(world, TraceKind.Recovered, "ogre");
                    var clashes = Reading.Clashes(world);

                    return new[]
                    {
                        Equal("three attacks were answered", 3, clashes.Count),
                        Equal("the bite came out as the oracle said", bite.Outcome, clashes.Count > 0 ? clashes[0].Outcome : default),
                        Equal("the maul came out as the oracle said", maul.Outcome, clashes.Count > 1 ? clashes[1].Outcome : default),
                        Equal("the ogre's armour soaked what the oracle said", oracle.ArmourOf("ogre"), world.ArmourOf("ogre")),
                        Equal("the ogre's health agrees", oracle.HpOf("ogre"), world.HpOf("ogre")),
                        Equal("toughness healed the ogre at the start of its turn by what the oracle said",
                            healed, recovered?.Amount ?? 0),
                        Equal("the ogre's maul came out as the oracle said", back.Outcome, clashes.Count > 2 ? clashes[2].Outcome : default),
                        Equal("the wolf's health agrees", oracle.HpOf("wolf"), world.HpOf("wolf")),
                        Equal("the wolf has no armour to lose", 0, world.ArmourOf("wolf"))
                    };
                });
        }

        /// <summary>A cleric bitten by a wolf recovers: health back, an element gone.</summary>
        public static Scenario Recovery()
        {
            var clericCell = Cell.Whole(Hex.Zero);
            var wolfCell = Cell.Whole(new Hex(0, -1));

            return new Scenario("recovery", "Healing",
                    "A wolf bites and mauls a cleric, who uses Recover: six health back, never past "
                    + "the maximum, for one Hydro and one action point.",
                    9901, Maps.Open())
                .With(new Actor("wolf", Premade.Wolf, Party.Monsters, wolfCell, North)
                    .Then(Order.Use(Skills.Bite, "cleric"))
                    .Then(Order.Use(Skills.Maul, "cleric")))
                .With(new Actor("cleric", Premade.Cleric, Party.Guards, clericCell, South, 3)
                    .Then(Order.Use(Skills.Recover, "cleric")))
                .Expecting(world =>
                {
                    var oracle = new Oracle(Recovery(), world);
                    oracle.Use("wolf", Skills.Bite, "cleric");
                    oracle.Use("wolf", Skills.Maul, "cleric");
                    var hurt = oracle.HpOf("cleric");

                    // The cleric may have put its Hydro up against the bites; then Recover is
                    // refused, and the oracle skips it as the fight does.
                    var canHeal = oracle.CanPay("cleric", Skills.Recover);

                    if (canHeal)
                    {
                        oracle.Use("cleric", Skills.Recover, "cleric");
                    }

                    return new[]
                    {
                        Equal("the cleric used Recover exactly when it still held the Hydro for it",
                            canHeal, Reading.Has(world, TraceKind.Acted, "cleric", 0, Skills.Recover)),
                        Equal("its health is what the oracle worked out: the bites, then six back, capped",
                            oracle.HpOf("cleric"), world.HpOf("cleric")),
                        That("healing never passes the maximum", world.HpOf("cleric") <= world.MaxHpOf("cleric")),
                        Equal("the cleric's pool agrees with the oracle: the Hydro Recover cost, and what the clashes took",
                            oracle.PoolOf("cleric"), world.PoolOf("cleric")),
                        That("the heal was worth having, or there was nothing to heal, or nothing to pay with",
                            !canHeal || hurt == world.MaxHpOf("cleric") || world.HpOf("cleric") > hurt)
                    };
                });
        }

        /// <summary>
        /// A goblin in front of an ogre. The ogre mauls and torches it, twice over if it has to;
        /// when it falls the match is over and the monsters have it.
        /// </summary>
        public static Scenario Kill()
        {
            var ogreCell = Cell.Whole(Hex.Zero);
            var goblinCell = Cell.Whole(new Hex(0, 1));

            return new Scenario("kill", "A kill and a win",
                    "An ogre mauls and torches a goblin over two rounds while the goblin jabs "
                    + "back. When the goblin falls it leaves the order, the match ends, and the "
                    + "monsters win.",
                    1213, Maps.Open())
                .With(new Actor("goblin", Premade.Goblin, Party.Guards, goblinCell, South)
                    .Then(Order.Use(Skills.Jab, "ogre"))
                    .NextTurn()
                    .Then(Order.Use(Skills.Jab, "ogre")))
                .With(new Actor("ogre", Premade.Ogre, Party.Monsters, ogreCell, North, 3)
                    .Then(Order.Use(Skills.Maul, "goblin"))
                    .Then(Order.Use(Skills.Torch, "goblin"))
                    .NextTurn()
                    .Then(Order.Use(Skills.Maul, "goblin"))
                    .Then(Order.Use(Skills.Torch, "goblin")))
                .Lasting(3)
                .Expecting(world =>
                {
                    var oracle = new Oracle(Kill(), world);

                    // Two rounds, goblin first (speed 10 to 5), each actor two orders a round,
                    // until somebody is down.
                    // An order the fight refuses for want of an element is skipped here too.
                    for (var round = 1; round <= 2 && oracle.IsAlive("goblin") && oracle.IsAlive("ogre"); round++)
                    {
                        oracle.BeginTurn("goblin");

                        if (oracle.CanPay("goblin", Skills.Jab))
                        {
                            oracle.Use("goblin", Skills.Jab, "ogre");
                        }

                        if (!oracle.IsAlive("ogre")) break;

                        oracle.BeginTurn("ogre");

                        if (oracle.CanPay("ogre", Skills.Maul))
                        {
                            oracle.Use("ogre", Skills.Maul, "goblin");
                        }

                        if (!oracle.IsAlive("goblin")) break;

                        if (oracle.CanPay("ogre", Skills.Torch))
                        {
                            oracle.Use("ogre", Skills.Torch, "goblin");
                        }
                    }

                    var dead = !oracle.IsAlive("goblin");

                    return new[]
                    {
                        Equal("the goblin is alive or dead as the oracle said", !dead, world.IsAlive("goblin")),
                        Equal("a fallen goblin was announced as fallen", dead ? 1 : 0, Reading.Count(world, TraceKind.Fell, "goblin")),
                        Equal("a side won exactly when somebody is dead", dead, world.IsWon),
                        That("and the monsters won it", !dead || world.Winner == Party.Monsters),
                        Equal("the ogre's health agrees with the oracle", oracle.HpOf("ogre"), world.HpOf("ogre")),
                        That("the goblin's health agrees with the oracle", dead || oracle.HpOf("goblin") == world.HpOf("goblin"),
                            $"oracle {oracle.HpOf("goblin")}")
                    };
                });
        }

        /// <summary>Who goes first: fastest first, and of two equals, whoever spawned first.</summary>
        public static Scenario Initiative()
        {
            return new Scenario("initiative", "Initiative",
                    "Four creatures with speeds twelve, ten, ten and five take a round of doing "
                    + "nothing. The order is by speed, and the two at ten go in the order they "
                    + "were placed.",
                    1414, Maps.Open())
                .With(new Actor("ogre", Premade.Ogre, Party.Monsters, Cell.Whole(new Hex(-2, 0)), North, 3))
                .With(new Actor("goblin", Premade.Goblin, Party.Monsters, Cell.Whole(new Hex(2, 0)), North))
                .With(new Actor("wolf", Premade.Wolf, Party.Guards, Cell.Whole(new Hex(0, 2)), North))
                .With(new Actor("cutpurse", Premade.Cutpurse, Party.Guards, Cell.Whole(new Hex(0, -2)), North, 2))
                .Lasting(2)
                .Expecting(world =>
                {
                    var oracle = new Oracle(Initiative(), world);
                    var expected = oracle.TurnOrder();
                    var actual = Reading.TurnsOf(world, 1);

                    return new[]
                    {
                        Equal("the first round's turns came in initiative order",
                            Reading.Join(expected), Reading.Join(actual)),
                        Equal("the wolf, at twelve, went first", "wolf", actual.Count > 0 ? actual[0] : ""),
                        Equal("the goblin, placed before the cutpurse, broke their tie", "goblin", actual.Count > 1 ? actual[1] : ""),
                        Equal("the ogre, at five, went last", "ogre", actual.Count > 3 ? actual[3] : ""),
                        That("a second round began", world.Round >= 2)
                    };
                });
        }

        /// <summary>
        /// The game's own opponent, both sides. A sergeant and a goblin start four tiles apart and
        /// are left to it. What is claimed is only what must be true of any such fight: somebody
        /// closed and swung, the match ends when and only when a side is gone, and it took no
        /// longer than the rounds allowed.
        /// </summary>
        public static Scenario BrainDuel()
        {
            return new Scenario("brain-duel", "The opponent, both sides",
                    "A sergeant and a goblin are left to the game's own opponent for up to eight "
                    + "rounds. Somebody closes the distance and attacks; the match ends when a side "
                    + "is gone and not before.",
                    1515, Maps.Open(4))
                .With(new Actor("sergeant", Premade.Sergeant, Party.Guards, Cell.Whole(new Hex(-2, 0)), Facing.Of(1), 3)
                    .RunByTheGame())
                .With(new Actor("goblin", Premade.Goblin, Party.Monsters, Cell.Whole(new Hex(2, 0)), Facing.Of(4))
                    .RunByTheGame())
                .Lasting(8)
                .Expecting(world =>
                {
                    var oneSideGone = !world.IsAlive("sergeant") || !world.IsAlive("goblin");

                    return new[]
                    {
                        That("somebody walked", Reading.Count(world, TraceKind.Moved) > 0),
                        That("somebody attacked", Reading.Count(world, TraceKind.Clash) + Reading.Count(world, TraceKind.Shot) > 0),
                        Equal("a side won exactly when a side is gone", oneSideGone, world.IsWon),
                        That("nobody's health went below zero or above its maximum",
                            (!world.IsAlive("sergeant") || (world.HpOf("sergeant") > 0 && world.HpOf("sergeant") <= world.MaxHpOf("sergeant")))
                            && (!world.IsAlive("goblin") || (world.HpOf("goblin") > 0 && world.HpOf("goblin") <= world.MaxHpOf("goblin")))),
                        That("it fit in the rounds allowed", world.Round <= 8, $"round {world.Round}")
                    };
                });
        }
    }
}

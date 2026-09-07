using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Sim
{
    /// <summary>What kind of thing the fight did.</summary>
    public enum CombatEventKind : byte
    {
        /// <summary>The fight opened: the initiative order and where everybody started.</summary>
        Began,

        /// <summary>A new round.</summary>
        RoundBegan,

        /// <summary>A creature's turn began, with its action points refilled.</summary>
        TurnBegan,

        /// <summary>Toughness put health back at the top of a turn.</summary>
        Recovered,

        /// <summary>A creature walked, and turned.</summary>
        Moved,

        /// <summary>A creature turned without walking: to strike, or to face whoever struck it.</summary>
        Faced,

        /// <summary>A contested attack began. What it is made of is not yet public.</summary>
        Swung,

        /// <summary>A shot rolled, and this is how it went.</summary>
        Shot,

        /// <summary>A skill nobody could contest landed as written.</summary>
        Acted,

        /// <summary>Both sides of a clash revealed, and who won it.</summary>
        ClashResolved,

        /// <summary>A blow arrived: what armour took, what got through, what is left.</summary>
        Damaged,

        /// <summary>Health came back from a skill.</summary>
        Healed,

        /// <summary>Action points came back from a skill.</summary>
        ApRestored,

        /// <summary>A watcher let a mover go.</summary>
        HeldBack,

        /// <summary>A creature ran out of health.</summary>
        Fell,

        /// <summary>A wall came down, went up, or changed.</summary>
        WallChanged,

        /// <summary>No more turns will be taken.</summary>
        Ended
    }

    /// <summary>Where a creature stood, and what it had, when the fight opened.</summary>
    public readonly struct CreatureStart
    {
        public readonly uint Id;
        public readonly Cell Cell;
        public readonly int Facing;
        public readonly int Hp;
        public readonly int Armour;
        public readonly int ApUnits;

        public CreatureStart(uint id, Cell cell, int facing, int hp, int armour, int apUnits)
        {
            Id = id;
            Cell = cell;
            Facing = facing;
            Hp = hp;
            Armour = armour;
            ApUnits = apUnits;
        }
    }

    /// <summary>
    /// One thing the fight did, as the record of it.
    ///
    /// The simulation runs as far ahead as it likes; what everybody watches is this stream,
    /// played back at a pace a person can follow. So an event carries everything a viewer needs
    /// to draw the fight as it was at that moment -- the health after the blow, the cell after
    /// the walk, the elements that were put up -- and nothing a viewer would have to go and read
    /// off the live state, because the live state has already moved on.
    ///
    /// Numbers and ids, never sentences and never objects. Each machine words it for itself, and
    /// a record that named objects would be a record that could not be sent.
    /// </summary>
    public sealed class CombatEvent
    {
        /// <summary>The value <see cref="Facing"/> takes when the event turned nobody.</summary>
        public const int NoFacing = -1;

        /// <summary>The value <see cref="Amount"/> takes on <see cref="CombatEventKind.Ended"/> when nobody won.</summary>
        public const int NoWinner = -2;

        public CombatEventKind Kind;

        /// <summary>The round it happened in. Every event carries it, so a viewer never counts.</summary>
        public int Round;

        /// <summary>Whose event it is. The mover, the attacker, the one who fell.</summary>
        public uint Actor;

        /// <summary>Whom it was aimed at, or zero.</summary>
        public uint Target;

        /// <summary>The skill, where one was used. <see cref="Opportunity.SkillId"/> for a swing.</summary>
        public int Skill;

        /// <summary>What was healed, what landed, the chance, the experience, the outcome of the fight.</summary>
        public int Amount;

        /// <summary>Damaged: what the armour took.</summary>
        public int Absorbed;

        /// <summary>The actor's -- or on Damaged the target's -- health afterwards.</summary>
        public int Hp;

        /// <summary>Damaged: the target's armour afterwards.</summary>
        public int Armour;

        /// <summary>The actor's action points afterwards, in half-units.</summary>
        public int ApUnits;

        /// <summary>Which way the actor is turned afterwards, or <see cref="NoFacing"/>.</summary>
        public int Facing = NoFacing;

        /// <summary>Shot: whether it arrived.</summary>
        public bool Landed;

        public ClashOutcome Outcome;

        /// <summary>The elements the actor spent: its commitment, revealed.</summary>
        public IReadOnlyList<Element> Elements = Array.Empty<Element>();

        /// <summary>ClashResolved: what the defender put up.</summary>
        public IReadOnlyList<Element> Answer = Array.Empty<Element>();

        /// <summary>Acted: elements the skill brought back.</summary>
        public IReadOnlyList<Element> Returned = Array.Empty<Element>();

        /// <summary>Moved: the cells walked, the destination last. The start is not in it.</summary>
        public IReadOnlyList<Cell> Path = Array.Empty<Cell>();

        /// <summary>Began: the initiative order.</summary>
        public IReadOnlyList<uint> Order = Array.Empty<uint>();

        /// <summary>Began: everybody, and where they stood.</summary>
        public IReadOnlyList<CreatureStart> Starts = Array.Empty<CreatureStart>();

        /// <summary>WallChanged: the flags before and after, and the segment.</summary>
        public WallSegment Segment;
        public WallFlags WallBefore;
        public WallFlags WallAfter;

        // ---------- the ways the fight writes one ----------

        public static CombatEvent BeganWith(int round, IReadOnlyList<uint> order,
            IReadOnlyList<CreatureStart> starts) =>
            new CombatEvent { Kind = CombatEventKind.Began, Round = round, Order = order, Starts = starts };

        public static CombatEvent RoundBeganAt(int round) =>
            new CombatEvent { Kind = CombatEventKind.RoundBegan, Round = round };

        public static CombatEvent TurnBeganFor(int round, uint actor, int apUnits) =>
            new CombatEvent { Kind = CombatEventKind.TurnBegan, Round = round, Actor = actor, ApUnits = apUnits };

        public static CombatEvent RecoveredBy(int round, uint actor, int amount, int hp) =>
            new CombatEvent { Kind = CombatEventKind.Recovered, Round = round, Actor = actor, Amount = amount, Hp = hp };

        public static CombatEvent MovedAlong(int round, uint actor, IReadOnlyList<Cell> path, int facing,
            int apUnits) =>
            new CombatEvent
            {
                Kind = CombatEventKind.Moved, Round = round, Actor = actor, Path = path, Facing = facing,
                ApUnits = apUnits
            };

        public static CombatEvent FacedToward(int round, uint actor, int facing) =>
            new CombatEvent { Kind = CombatEventKind.Faced, Round = round, Actor = actor, Facing = facing };

        public static CombatEvent SwungAt(int round, uint actor, uint target, int skill, int facing,
            int apUnits) =>
            new CombatEvent
            {
                Kind = CombatEventKind.Swung, Round = round, Actor = actor, Target = target, Skill = skill,
                Facing = facing, ApUnits = apUnits
            };

        public static CombatEvent ShotAt(int round, uint actor, uint target, int skill, int chance,
            bool landed, int facing, int apUnits, IReadOnlyList<Element> spent) =>
            new CombatEvent
            {
                Kind = CombatEventKind.Shot, Round = round, Actor = actor, Target = target, Skill = skill,
                Amount = chance, Landed = landed, Facing = facing, ApUnits = apUnits,
                Elements = spent ?? Array.Empty<Element>()
            };

        public static CombatEvent ActedWith(int round, uint actor, uint target, int skill, int facing,
            int apUnits, IReadOnlyList<Element> spent, IReadOnlyList<Element> returned) =>
            new CombatEvent
            {
                Kind = CombatEventKind.Acted, Round = round, Actor = actor, Target = target, Skill = skill,
                Facing = facing, ApUnits = apUnits, Elements = spent ?? Array.Empty<Element>(),
                Returned = returned ?? Array.Empty<Element>()
            };

        public static CombatEvent ClashResolvedAs(int round, uint attacker, uint defender, int skill,
            IReadOnlyList<Element> attackerElements, IReadOnlyList<Element> answer, ClashOutcome outcome) =>
            new CombatEvent
            {
                Kind = CombatEventKind.ClashResolved, Round = round, Actor = attacker, Target = defender,
                Skill = skill, Elements = attackerElements ?? Array.Empty<Element>(),
                Answer = answer ?? Array.Empty<Element>(), Outcome = outcome
            };

        public static CombatEvent DamagedBy(int round, uint attacker, uint target, int landed, int absorbed,
            int hp, int armour) =>
            new CombatEvent
            {
                Kind = CombatEventKind.Damaged, Round = round, Actor = attacker, Target = target,
                Amount = landed, Absorbed = absorbed, Hp = hp, Armour = armour
            };

        public static CombatEvent HealedBy(int round, uint actor, int amount, int hp) =>
            new CombatEvent { Kind = CombatEventKind.Healed, Round = round, Actor = actor, Amount = amount, Hp = hp };

        public static CombatEvent ApRestoredTo(int round, uint actor, int amount, int apUnits) =>
            new CombatEvent
            {
                Kind = CombatEventKind.ApRestored, Round = round, Actor = actor, Amount = amount, ApUnits = apUnits
            };

        public static CombatEvent HeldBackBy(int round, uint watcher, uint mover) =>
            new CombatEvent { Kind = CombatEventKind.HeldBack, Round = round, Actor = watcher, Target = mover };

        public static CombatEvent FellTo(int round, uint creature, uint killer, int xp) =>
            new CombatEvent { Kind = CombatEventKind.Fell, Round = round, Actor = creature, Target = killer, Amount = xp };

        public static CombatEvent WallChangedAt(int round, WallSegment segment, WallFlags before, WallFlags after) =>
            new CombatEvent
            {
                Kind = CombatEventKind.WallChanged, Round = round, Segment = segment, WallBefore = before,
                WallAfter = after
            };

        public static CombatEvent EndedWith(int round, int outcome) =>
            new CombatEvent { Kind = CombatEventKind.Ended, Round = round, Amount = outcome };

        /// <summary>Ended: whether a side won, rather than the fight simply stopping.</summary>
        public bool HasWinner => Kind == CombatEventKind.Ended && Amount >= 0;

        /// <summary>Ended: the side that won. Only meaningful when <see cref="HasWinner"/>.</summary>
        public Party Winner => (Party)(Amount < 0 ? 0 : Amount);

        public bool HasTarget => Target != 0 && Target != Actor;

        public override string ToString() =>
            $"{Kind} r{Round} actor={Actor} target={Target} skill={Skill} amount={Amount} hp={Hp}";
    }
}

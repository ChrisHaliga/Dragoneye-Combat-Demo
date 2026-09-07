using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Scenarios
{
    // Declared inside the namespace: out here the bare name Hex would bind to the Dragoneye.Hex
    // namespace rather than the type.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>What the oracle says an attack will come to.</summary>
    public readonly struct AttackForecast
    {
        /// <summary>Whether the skill rolled to hit at all.</summary>
        public readonly bool Rolled;

        public readonly int Chance;

        /// <summary>Whether it reached the defender: a swing always does, a shot when the roll allows.</summary>
        public readonly bool Landed;

        /// <summary>Whether there was a clash: somebody on the other side, and the shot reached them.</summary>
        public readonly bool Contested;

        public readonly ClashOutcome Outcome;

        /// <summary>What got through the clash, before armour.</summary>
        public readonly int Damage;

        public readonly bool Flanked;

        public AttackForecast(bool rolled, int chance, bool landed, bool contested,
            ClashOutcome outcome, int damage, bool flanked)
        {
            Rolled = rolled;
            Chance = chance;
            Landed = landed;
            Contested = contested;
            Outcome = outcome;
            Damage = damage;
            Flanked = flanked;
        }
    }

    /// <summary>
    /// The rules replayed ahead of the fight, with the fight's own dice.
    ///
    /// A scenario says what will happen by running the same pure rules the server runs, in the
    /// same order, from the same seed: the roll to hit, the computer's answer to a clash, the
    /// element matchup, the armour. Then the fight runs and the checks compare. Every rule the
    /// oracle applies is the rule the game applies -- <see cref="SkillRules"/>,
    /// <see cref="ClashSequence"/>, <see cref="ClashDefenceOdds.ChooseAnswer"/>,
    /// <see cref="Opportunity.Takes"/>, <see cref="CombatRules.Absorb"/> -- so a disagreement
    /// between the two is a bug in the plumbing, which is exactly what a scenario exists to find.
    ///
    /// It knows the order the fight consumes rolls in. A shot rolls first; a computer defender
    /// rolls once per element it puts up; a creature offered a swing rolls once on whether to
    /// take it. A scenario whose script does something the oracle does not replay -- a skill that
    /// walks into range, say -- keeps its attackers in reach instead.
    /// </summary>
    public sealed class Oracle
    {
        sealed class Fighter
        {
            public Cell Cell;
            public Facing Facing;
            public int Hp;
            public int Armour;
            public Party Party;
            public bool Advantage;

            /// <summary>What the pool holds, commitments included: the server's view.</summary>
            public ElementLedger Ledger;

            /// <summary>What everybody has been told: the public view.</summary>
            public ElementLedger Public;

            public bool Alive => CombatRules.IsAlive(Hp);
        }

        readonly Scenario m_Scenario;
        readonly IScenarioWorld m_World;
        readonly Dictionary<string, Fighter> m_Fighters = new Dictionary<string, Fighter>();
        readonly List<string> m_Order = new List<string>();
        readonly HexMap m_Map;
        readonly GridRules m_Grid;

        public Oracle(Scenario scenario, IScenarioWorld world)
        {
            m_Scenario = scenario;
            m_World = world;
            Dice = new Dice(scenario.Seed);

            // Its own board, so a wall the scenario brings down mid-fight comes down here at the
            // same moment, and a line asked about before then is asked of the wall.
            m_Map = scenario.Map.Build(new HexLayout(1f, default), world.TerrainNamed);
            m_Grid = new GridRules(m_Map);

            foreach (var actor in scenario.Actors)
            {
                m_Order.Add(actor.Key);
                m_Fighters[actor.Key] = new Fighter
                {
                    Cell = actor.Cell,
                    Facing = actor.Facing,
                    Hp = world.MaxHpOf(actor.Key),
                    Armour = world.MaxArmourOf(actor.Key),
                    Party = actor.Party,
                    Advantage = world.HasAdvantage(actor.Key),
                    Ledger = ElementLedger.Starting(world.StartingPoolOf(actor.Key)),
                    Public = ElementLedger.Starting(world.StartingPoolOf(actor.Key))
                };
            }
        }

        /// <summary>The fight's dice, at wherever the replay has got to.</summary>
        public Dice Dice { get; }

        /// <summary>Swings at passers-by the replay took, and ones it let go.</summary>
        public int SwingsTaken { get; private set; }

        public int SwingsHeld { get; private set; }

        public IGridRules Grid => m_Grid;

        public int HpOf(string key) => m_Fighters[key].Hp;

        public int ArmourOf(string key) => m_Fighters[key].Armour;

        public Cell CellOf(string key) => m_Fighters[key].Cell;

        public Facing FacingOf(string key) => m_Fighters[key].Facing;

        public bool IsAlive(string key) => m_Fighters[key].Alive;

        public ElementCounts PoolOf(string key) => m_Fighters[key].Ledger.Pool;

        /// <summary>Which way one cell lies from another, as the fight measures it.</summary>
        public Facing Bearing(Cell from, Cell to) =>
            Facing.Of((int)AreaGeometry.Direction(m_Map, from, to));

        /// <summary>The initiative order, by key: fastest first, and the earlier-spawned of equals.</summary>
        public List<string> TurnOrder()
        {
            var combatants = new List<Combatant>();

            for (var i = 0; i < m_Order.Count; i++)
            {
                combatants.Add(new Combatant((uint)(i + 1), m_World.SpeedOf(m_Order[i])));
            }

            var order = new List<string>();

            foreach (var id in Combat.TurnOrder.Build(combatants))
            {
                order.Add(m_Order[(int)id - 1]);
            }

            return order;
        }

        /// <summary>The start of a turn: health comes back by the creature's toughness.</summary>
        public int BeginTurn(string key)
        {
            var fighter = m_Fighters[key];
            var before = fighter.Hp;

            fighter.Hp = SkillRules.Apply(new SkillEffect(SkillEffectKind.Heal, m_World.RegenOf(key)),
                fighter.Hp, m_World.MaxHpOf(key));

            return fighter.Hp - before;
        }

        /// <summary>A wall changing under the fight.</summary>
        public void SetWall(WallSegment segment, Wall wall) => m_Map.SetWall(segment, wall);

        /// <summary>
        /// A creature walking somewhere, and everybody it walks out from under.
        ///
        /// Every enemy watching the cell it leaves is offered a swing in spawn order, as the
        /// fight offers them; a computer creature rolls on whether to take it and swings with its
        /// first weapon. The walk happens afterwards, facing the way it went, unless a swing
        /// ended it.
        /// </summary>
        public void Move(string key, Cell to)
        {
            var mover = m_Fighters[key];

            foreach (var watcherKey in m_Order)
            {
                var watcher = m_Fighters[watcherKey];

                if (!mover.Alive || watcherKey == key || watcher.Party == mover.Party || !watcher.Alive
                    || !Provokes(watcher, mover.Cell, to))
                {
                    continue;
                }

                var swing = Opportunity.From(Opportunity.PrimaryOf(m_World.SkillsOf(watcherKey)));

                if (swing == null || !SkillRules.TryChooseElement(swing, watcher.Ledger, out _))
                {
                    continue;
                }

                if (!Opportunity.Takes(Dice.Roll()))
                {
                    SwingsHeld++;
                    continue;
                }

                SwingsTaken++;
                Attack(watcherKey, key, swing, null);
            }

            if (!mover.Alive)
            {
                return;
            }

            var facing = Bearing(mover.Cell, to);
            mover.Cell = to;
            mover.Facing = facing;
        }

        /// <summary>A skill used on an actor, or on the user, from where the user stands.</summary>
        public AttackForecast Use(string key, int skillId, string targetKey, Element? element = null)
        {
            var skill = m_World.SkillOf(key, skillId);

            if (skill == null)
            {
                throw new System.InvalidOperationException($"{key} does not hold skill {skillId}");
            }

            return Attack(key, targetKey, skill, element);
        }

        /// <summary>
        /// Whether a creature stood here and turned this way gets a swing at a walk from one cell
        /// to another. The fight's rule, stated in the fight's terms.
        /// </summary>
        bool Provokes(Fighter watcher, Cell from, Cell to) =>
            from != to && Watches(watcher, from) && !Watches(watcher, to);

        bool Watches(Fighter watcher, Cell cell) =>
            Cell.Distance(watcher.Cell, cell) == Opportunity.Range
            && FacingRules.Threatens(watcher.Facing, Bearing(watcher.Cell, cell))
            && LineOfSight.Verdict(m_Grid, watcher.Cell, cell) != LineVerdict.Blocked;

        /// <summary>
        /// One attack, from commitment to armour. The director's order of events: the element
        /// is committed, the attacker turns, a shot rolls, the defender answers, both reveal,
        /// what is left lands, and a flanked defender turns to face what hit it.
        /// </summary>
        AttackForecast Attack(string attackerKey, string targetKey, SkillSpec skill, Element? element)
        {
            var attacker = m_Fighters[attackerKey];
            var target = m_Fighters[targetKey];
            var self = attackerKey == targetKey;

            skill = SkillRules.Settle(skill, element, attacker.Ledger);

            if (skill == null)
            {
                throw new System.InvalidOperationException($"{attackerKey} cannot pay for {skill}");
            }

            var distance = Cell.Distance(attacker.Cell, target.Cell);

            if (!self && !CombatRules.InRange(distance, skill.Range))
            {
                throw new System.InvalidOperationException(
                    $"{attackerKey} is {distance} from {targetKey}; the oracle does not walk into range");
            }

            if (skill.ElementCost > 0)
            {
                attacker.Ledger.TrySpend(skill.Element, skill.ElementCost, out attacker.Ledger, out _);
            }

            if (!self)
            {
                attacker.Facing = Bearing(attacker.Cell, target.Cell);
            }

            var contested = skill.IsContested && !self && target.Alive && target.Party != attacker.Party;

            var rolled = false;
            var chance = 100;

            if (skill.RollsToHit && contested)
            {
                rolled = true;
                var cover = Cover(attackerKey, targetKey);
                chance = SkillRules.HitChance(skill, distance, cover);

                if (!SkillRules.Hits(skill, distance, cover, Dice.Roll()))
                {
                    attacker.Public = attacker.Ledger;
                    return new AttackForecast(true, chance, false, false, default, 0, false);
                }
            }

            if (!contested)
            {
                Land(attacker, target, skill.Effect);
                return new AttackForecast(rolled, chance, true, false, default, skill.Effect.Amount, false);
            }

            var flanked = FacingRules.IsFlank(target.Facing, Bearing(target.Cell, attacker.Cell));

            var committed = new List<Element>();

            for (var i = 0; i < skill.ElementCost; i++)
            {
                committed.Add(skill.Element);
            }

            var clash = ClashSequence.Begin(committed,
                new ClashSide(1, advantage: attacker.Advantage),
                new ClashSide(2, advantage: target.Advantage, disadvantage: flanked),
                target.Ledger, m_World.Matchups);

            var answer = clash.Request.HasAnswer
                ? ClashDefenceOdds.ChooseAnswer(clash.Request, PossibleElements.Seen(attacker.Public),
                    target.Ledger.Pool, m_World.Matchups, Dice.Roll)
                : new List<Element>();

            if (answer.Count == 0)
            {
                clash.Decline();
            }
            else
            {
                clash.TryCommit(answer, target.Ledger, out _);

                foreach (var put in answer)
                {
                    target.Ledger.TrySpend(put, 1, out target.Ledger, out _);
                }
            }

            clash.TryReveal(out var reveal);

            attacker.Public = attacker.Ledger;

            if (ClashRules.Refunds(reveal.Outcome))
            {
                Refund(target, answer);
            }

            target.Public = target.Ledger;

            var effect = clash.Scale(skill.Effect);

            if (effect.Amount > 0 && skill.Effect.Kind == SkillEffectKind.Damage)
            {
                Land(attacker, target, effect);
            }

            if (flanked && target.Alive && attacker.Alive)
            {
                target.Facing = Bearing(target.Cell, attacker.Cell);
            }

            return new AttackForecast(rolled, chance, true, true, reveal.Outcome, effect.Amount, flanked);
        }

        /// <summary>What an effect does once it has arrived: armour first for damage, a heal to the user.</summary>
        void Land(Fighter actor, Fighter target, SkillEffect effect)
        {
            switch (effect.Kind)
            {
                case SkillEffectKind.Damage:
                    var through = CombatRules.Absorb(effect.Amount, target.Armour, out target.Armour);
                    target.Hp = CombatRules.Damaged(target.Hp, through);
                    break;

                case SkillEffectKind.Heal:
                    actor.Hp = SkillRules.Apply(effect, actor.Hp, m_World.MaxHpOf(KeyOf(actor)));
                    break;

                case SkillEffectKind.ReturnElement:
                    for (var i = 0; i < effect.Amount && actor.Ledger.TryReturn(out actor.Ledger, out _, out _); i++)
                    {
                    }

                    actor.Public = actor.Ledger;
                    break;
            }
        }

        /// <summary>
        /// The defender keeps what it put up. Shown, not spent: the identification stands while
        /// the elements go back in the hand, from the end of what is outstanding.
        /// </summary>
        static void Refund(Fighter defender, IReadOnlyList<Element> answer)
        {
            var ledger = defender.Ledger;
            var pool = ledger.Pool;
            var outstanding = new List<Element>(ledger.Outstanding);

            foreach (var element in answer)
            {
                pool = pool.Plus(element, 1);

                var last = outstanding.LastIndexOf(element);

                if (last >= 0)
                {
                    outstanding.RemoveAt(last);
                }
            }

            defender.Ledger = new ElementLedger(pool, ledger.Revealed, outstanding, ledger.Total,
                ledger.Identified);
        }

        /// <summary>What is in a shot's way: one for a low wall, one per body on a tile between.</summary>
        int Cover(string attackerKey, string targetKey)
        {
            var attacker = m_Fighters[attackerKey];
            var target = m_Fighters[targetKey];
            var cover = LineOfSight.Verdict(m_Grid, attacker.Cell, target.Cell) == LineVerdict.Obstructed ? 1 : 0;

            var between = new List<Hex>();
            LineOfSight.TilesBetween(attacker.Cell, target.Cell, between);

            foreach (var pair in m_Fighters)
            {
                if (pair.Value.Alive && between.Contains(pair.Value.Cell.Tile))
                {
                    cover++;
                }
            }

            return cover;
        }

        string KeyOf(Fighter fighter)
        {
            foreach (var pair in m_Fighters)
            {
                if (pair.Value == fighter)
                {
                    return pair.Key;
                }
            }

            return null;
        }
    }
}

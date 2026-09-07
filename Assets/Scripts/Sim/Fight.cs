using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Sim
{
    // Declared inside the namespace, not at file scope: C# resolves names against enclosing
    // namespaces before file-level aliases.
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// A fight, whole: the creatures in it, whose turn it is, and every rule for what happens
    /// when one of them does something.
    ///
    /// A plain object. It holds no engine type, runs on no clock, and calls nothing it was not
    /// handed: what it needs to say goes out through <see cref="IFightListener"/>, and what it is
    /// asked to do comes in through the methods below. So the same fight runs on a server with
    /// its state mirrored to clients, in a test with a scripted answerer, or in a console with
    /// nothing watching at all -- and a bug in anything that draws it cannot reach it, because
    /// there is no path by which it could.
    ///
    /// **It has no clock.** The fight resolves as fast as it is decided; the only thing it ever
    /// waits for is a person's answer, and a computer's turn is a <see cref="Step"/> the caller
    /// takes when it likes. Everything anybody watches is the record it writes, one
    /// <see cref="CombatEvent"/> per thing that happened, played back at whatever pace suits the
    /// watcher.
    ///
    /// Every rule it applies lives in <c>Dragoneye.Combat</c> -- <see cref="CombatRules"/>,
    /// <see cref="ActionResolver"/>, <see cref="TurnOrder"/>, <see cref="ClashSequence"/> -- or
    /// on the grid. This is the sequencing: what is asked of whom, in what order, and what is
    /// written down.
    /// </summary>
    public sealed partial class Fight : IOccupancy
    {
        readonly HexMap m_Map;
        readonly IGridRules m_Grid;
        readonly IElementMatchup m_Matchups;
        readonly Dice m_Dice;
        readonly IFightListener m_Listener;
        readonly FightBoard m_Board;
        readonly TurnQueue m_Turns = new TurnQueue();

        readonly List<FightCreature> m_Creatures = new List<FightCreature>();
        readonly Dictionary<uint, FightCreature> m_ById = new Dictionary<uint, FightCreature>();
        readonly Dictionary<Cell, FightCreature> m_Standing = new Dictionary<Cell, FightCreature>();

        /// <param name="map">The board the fight is played on. The fight changes its walls.</param>
        /// <param name="brain">What runs the computer's creatures.</param>
        public Fight(HexMap map, IGridRules grid, IElementMatchup matchups, Dice dice,
            ICreatureBrain brain, IFightListener listener)
        {
            m_Map = map ?? throw new System.ArgumentNullException(nameof(map));
            m_Grid = grid ?? throw new System.ArgumentNullException(nameof(grid));
            m_Matchups = matchups ?? throw new System.ArgumentNullException(nameof(matchups));
            m_Dice = dice ?? throw new System.ArgumentNullException(nameof(dice));
            m_Brain = brain ?? throw new System.ArgumentNullException(nameof(brain));
            m_Listener = listener ?? throw new System.ArgumentNullException(nameof(listener));
            m_Board = new FightBoard(grid, this);
        }

        public Dice Dice => m_Dice;

        public FightBoard Board => m_Board;

        public IReadOnlyList<FightCreature> Creatures => m_Creatures;

        public FightCreature Creature(uint id) =>
            id != 0 && m_ById.TryGetValue(id, out var creature) ? creature : null;

        public bool HasBegun { get; private set; }

        public bool IsOver => m_Turns.IsOver;

        public bool HasWinner => m_Turns.HasWinner;

        public Party Winner => m_Turns.Winner;

        public int Round => m_Turns.Round;

        public IReadOnlyList<uint> Order => m_Turns.Order;

        public int ActiveIndex => m_Turns.Index;

        public uint ActiveId => m_Turns.ActiveId;

        /// <summary><see cref="TurnQueue.Running"/>, <see cref="TurnQueue.NoWinner"/>, or the winner.</summary>
        public int Outcome => m_Turns.Outcome;

        /// <summary>
        /// Whether the fight is stopped on anybody's question at all.
        ///
        /// Wider than <see cref="IsClashPending"/>: between one swing settling and the next being
        /// offered there is no clash open, and a creature that read only the clash would take that
        /// gap as its turn resuming -- and act in the middle of its own interrupted move.
        /// </summary>
        public bool IsBusy => IsClashPending || m_Pending.Exists;

        // ---------- the match ----------

        /// <summary>Puts a creature on the board. Before <see cref="Begin"/> only.</summary>
        public void Add(FightCreature creature)
        {
            if (creature == null || HasBegun || m_ById.ContainsKey(creature.Id))
            {
                return;
            }

            m_Creatures.Add(creature);
            m_ById[creature.Id] = creature;
            m_Standing[creature.Cell] = creature;
        }

        /// <summary>
        /// Opens the fight once every creature is on the board.
        ///
        /// The first thing written down is everybody, and where they stood: the record is
        /// complete on its own, and a watcher reads nothing off the live board.
        /// </summary>
        public void Begin()
        {
            if (HasBegun)
            {
                return;
            }

            HasBegun = true;

            var combatants = new List<Combatant>();
            var starts = new List<CreatureStart>();

            foreach (var creature in m_Creatures)
            {
                if (creature.IsAlive)
                {
                    combatants.Add(new Combatant(creature.Id, creature.Speed));
                    starts.Add(new CreatureStart(creature.Id, creature.Cell, creature.Facing.Index,
                        creature.Hp, creature.Armour, creature.Ap.Units));
                }
            }

            m_Turns.Begin(combatants, IsStillFighting);

            Say(CombatEvent.BeganWith(0, new List<uint>(m_Turns.Order), starts));
            Say(CombatEvent.RoundBeganAt(0));

            BeginTurn();
        }

        /// <summary>
        /// Ends the active creature's turn and passes play on.
        ///
        /// The only way a turn ends. There is deliberately no automatic end when AP runs out: a
        /// player may want to hold what is left, and taking the decision away would foreclose
        /// reactions later.
        /// </summary>
        /// <returns>False when there is no turn to end, or somebody is being asked something.</returns>
        public bool EndTurn()
        {
            if (!HasBegun || IsOver)
            {
                return false;
            }

            // Not while somebody is being asked anything. The turn does not belong entirely to the
            // active player any more, and ending it out from under a defender mid-decision would
            // resolve their clash into a turn that had already moved on.
            if (IsBusy)
            {
                return false;
            }

            if (!m_Turns.Advance(IsStillFighting, out var wrapped))
            {
                ResolveOutcome();
                return true;
            }

            if (wrapped)
            {
                Say(CombatEvent.RoundBeganAt(0));
            }

            BeginTurn();
            return true;
        }

        void BeginTurn()
        {
            m_Budget = ActionBudget;

            var active = Creature(ActiveId);

            if (active == null)
            {
                // Unreachable while the order is built from the creatures here, and passed on
                // rather than sat on if it ever is: a turn nobody holds is a turn nobody can end,
                // and the queue only offers creatures that are here, so this cannot recurse far.
                m_Listener.Warn($"The turn passed to a creature the fight does not have (id {ActiveId}); passing it on.");
                EndTurn();
                return;
            }

            active.RefillAp();
            Say(CombatEvent.TurnBeganFor(0, active.Id, active.Ap.Units));

            // Toughness. Health comes back a little every turn and armour never does, which is
            // the whole difference between the two bars.
            if (active.Regen > 0)
            {
                var healed = active.Heal(active.Regen);

                if (healed > 0)
                {
                    Say(CombatEvent.RecoveredBy(0, active.Id, healed, active.Hp));
                }
            }
        }

        /// <summary>
        /// Stops the fight where it stands, with nobody winning it.
        ///
        /// What a test scenario calls when its script runs out. Without it the board keeps
        /// taking turns nobody has anything left to do with.
        /// </summary>
        public void Finish()
        {
            if (!HasBegun || IsOver)
            {
                return;
            }

            m_Turns.End();
            Say(CombatEvent.EndedWith(0, CombatEvent.NoWinner));
        }

        /// <summary>
        /// Whether this creature may act right now: alive, and the one whose turn it is.
        ///
        /// Who is allowed to give it orders is the caller's business -- the computer's own turns
        /// come through here with nobody behind them.
        /// </summary>
        public bool CanAct(uint id)
        {
            var creature = Creature(id);

            return creature != null && creature.IsAlive && !IsBusy && !IsOver
                && creature.Id == ActiveId;
        }

        // ---------- moving ----------

        /// <summary>
        /// Walks a creature, and turns it.
        ///
        /// The facing is part of the move rather than a follow-up, which DE-006 is explicit about:
        /// there is no turn action, so a creature that could move and then turn for free would have
        /// one. A caller that does not care which way it ends up facing gets the direction of
        /// travel, which is what a creature that walked somewhere is looking at.
        ///
        /// Anybody the walk leaves behind gets their swing first. The move is put away and taken
        /// out again once they have all had their answer, whether it landed or not.
        /// </summary>
        public bool Move(uint actorId, Cell destination, Facing? facing = null)
        {
            var actor = Creature(actorId);

            if (!CanAct(actorId))
            {
                return false;
            }

            // Priced before anybody is asked anything. An action that was going to be refused
            // must not cost its owner a round of swings on the way to being refused -- that would
            // make an unaffordable move into a way of draining the elements of everybody adjacent.
            if (!CanAffordMove(actor, destination))
            {
                return false;
            }

            if (TryInterrupt(PendingAction.Move(actor, destination, facing)))
            {
                return true;
            }

            return PerformMove(actor, destination, facing);
        }

        bool CanAffordMove(FightCreature actor, Cell destination)
        {
            var cost = m_Board.CostTo(actor.Cell, destination);

            var plan = ActionResolver.Resolve(true, true, actor.Ap,
                targetOccupied: false, moveSteps: cost, stepCost: actor.StepCost);

            return plan.IsAllowed && plan.Action == BoardAction.Move;
        }

        /// <summary>
        /// The move itself, once nobody is owed a swing at it.
        ///
        /// Re-costs the route rather than trusting the requested destination, so a client that asks
        /// for a hex it cannot afford is refused with the same arithmetic the cursor showed it.
        ///
        /// The route walked is written into the record: it was priced here, against the board as
        /// it was at this instant, and a token drawing any other route would be drawing a move
        /// that did not happen.
        /// </summary>
        bool PerformMove(FightCreature actor, Cell destination, Facing? facing)
        {
            if (actor == null || !actor.IsAlive)
            {
                return false;
            }

            var route = m_Board.PathTo(actor.Cell, destination, destination);
            var cost = m_Board.CostTo(actor.Cell, destination);

            var plan = ActionResolver.Resolve(true, true, actor.Ap,
                targetOccupied: false, moveSteps: cost, stepCost: actor.StepCost);

            if (!plan.IsAllowed || plan.Action != BoardAction.Move || !actor.SpendAp(plan.Cost))
            {
                return false;
            }

            // Read before the move, because afterwards the two hexes are the same one and the
            // bearing between them is meaningless.
            var travelled = ThreatGeometry.Bearing(m_Grid, actor.Cell, destination);
            var turned = facing ?? travelled;

            Place(actor, destination);
            actor.Face(turned);

            Say(CombatEvent.MovedAlong(0, actor.Id, route, turned.Index, actor.Ap.Units));
            return true;
        }

        // ---------- skills ----------

        /// <summary>
        /// Uses a skill, spending both costs and applying the effect.
        ///
        /// Both costs come off together or neither does: AP is taken first, and the element spend
        /// is checked before it so a refused element cannot leave the AP gone. DE-002 requires a
        /// creature that cannot pay either cost to be unable to use the skill at all.
        /// </summary>
        public bool UseSkill(uint actorId, int skillId, Cell target, out SkillRefusal refusal,
            Element? element = null) =>
            UseSkill(Creature(actorId), skillId, target, out refusal, element, provoked: false);

        bool UseSkill(FightCreature actor, int skillId, Cell target, out SkillRefusal refusal,
            Element? element, bool provoked)
        {
            refusal = SkillRefusal.NoSkill;

            if (actor == null || !CanAct(actor.Id))
            {
                refusal = SkillRefusal.NotYourTurn;
                return false;
            }

            if (!actor.TryGetSkill(skillId, out var skill))
            {
                return false;
            }

            var pool = actor.Pool;

            // The choice settled before anything reads the skill. Everything downstream -- the cost
            // check, the commitment, the clash -- asks for one element, so resolving the pick here
            // means none of it has to know that skills with options exist.
            skill = SkillRules.Settle(skill, element, pool.Private);

            if (skill == null)
            {
                refusal = SkillRefusal.NotEnoughElement;
                return false;
            }

            var occupant = At(target);

            // A skill that has to walk into range is a walk, and it provokes like one. Checked
            // before anything is spent, so an interrupted skill can be replayed from the top.
            if (!provoked && WouldApproach(actor, skill, target, out var approach)
                && CanAffordApproach(actor, skill, target)
                && TryInterrupt(PendingAction.Skill(actor, skillId, target, approach, skill.Element)))
            {
                refusal = SkillRefusal.None;
                return true;
            }

            // Walking into range is part of using a skill, not a separate order the client sends
            // first. Doing it here is what keeps the promise the cursor made -- "Strike, 1.5 + 1
            // AP" is one decision, and a client that could send the two halves separately could be
            // interrupted between them and left standing in the open having paid for nothing.
            if (!CloseTo(actor, skill, target, out refusal))
            {
                return false;
            }

            refusal = SkillRules.Check(skill, true, actor.Ap, pool.Private,
                Describe(actor, occupant, target));

            if (refusal != SkillRefusal.None)
            {
                return false;
            }

            // Element first: it is the cost that can still fail on a race, and spending AP before
            // discovering that would charge for an action that never happened.
            //
            // Committed rather than spent outright. A spend announces itself to everybody, and an
            // attacker whose commitment appeared in their public reveal record the moment they
            // swung has already told the defender what is coming -- which is the one thing DE-005
            // exists to prevent. It is announced at the reveal, with the defender's.
            if (skill.ElementCost > 0 && !pool.Commit(skill.Element, skill.ElementCost, out _))
            {
                refusal = SkillRefusal.NotEnoughElement;
                return false;
            }

            if (!actor.SpendAp(skill.ApCost))
            {
                refusal = SkillRefusal.NotEnoughAp;
                return false;
            }

            // Turning to strike is part of striking. DE-006: the attacker ends up facing whoever
            // they swung at, which opens their own flank to everybody they did not.
            var faced = CombatEvent.NoFacing;

            if (occupant != null && occupant != actor)
            {
                var turned = ThreatGeometry.Bearing(m_Grid, actor.Cell, target);
                actor.Face(turned);
                faced = turned.Index;
            }

            // A shot rolls before anybody answers it. The element is committed already -- the
            // arrow has left the bow -- so a miss spends it and shows it, and the defender is
            // never asked about an attack that did not arrive.
            if (skill.RollsToHit && IsContested(skill, actor, occupant))
            {
                var distance = Cell.Distance(actor.Cell, target);
                var cover = ShotLines.Trace(m_Grid, this, actor.Cell, target).Cover;
                var chance = SkillRules.HitChance(skill, distance, cover);
                var landed = SkillRules.Hits(skill, distance, cover, m_Dice.Roll());

                if (!landed)
                {
                    pool.AnnounceCommitted();
                    actor.RecordUse(skill.Id);
                }

                Say(CombatEvent.ShotAt(0, actor.Id, occupant.Id, skill.Id, chance, landed, faced,
                    actor.Ap.Units, landed ? null : Committed(skill)));

                if (!landed)
                {
                    return true;
                }

                BeginClash(actor, skill, occupant, announced: true);
                return true;
            }

            if (IsContested(skill, actor, occupant))
            {
                BeginClash(actor, skill, occupant);
                return true;
            }

            LandUncontested(actor, skill, occupant);
            return true;
        }

        /// <summary>The elements a skill commits, listed one per unit, for the record.</summary>
        static List<Element> Committed(SkillSpec skill)
        {
            var elements = new List<Element>();

            for (var i = 0; i < skill.ElementCost; i++)
            {
                elements.Add(skill.Element);
            }

            return elements;
        }

        /// <summary>
        /// Whether using this skill on that target means walking first, and to where.
        ///
        /// The tile is the cheapest one the target is in reach from, which is the tile the walk
        /// will end on -- and so the tile the watchers care about.
        /// </summary>
        bool WouldApproach(FightCreature actor, SkillSpec skill, Cell target, out Cell approach)
        {
            approach = actor.Cell;

            if (skill.Target == SkillTarget.Self
                || CombatRules.InRange(Cell.Distance(actor.Cell, target), skill.Range))
            {
                return false;
            }

            return m_Board.TryTileInReach(actor.Cell, target, skill.Range, out approach, out _)
                && approach != actor.Cell;
        }

        /// <summary>
        /// Whether the walk and the skill together are affordable, before either happens.
        ///
        /// Only asked to decide whether the approach is worth interrupting. The real checks still
        /// run afterwards, on the replay; this exists so an unaffordable order cannot be used to
        /// bait swings out of everybody standing next to you.
        /// </summary>
        bool CanAffordApproach(FightCreature actor, SkillSpec skill, Cell target)
        {
            var steps = m_Board.StepsToReach(actor.Cell, target, skill.Range);

            if (steps < 0 || actor.Ap < CombatRules.MoveCost(steps, actor.StepCost) + skill.ApCost)
            {
                return false;
            }

            return SkillRules.CheckAffordable(skill, true, actor.Ap, actor.Pool.Private)
                == SkillRefusal.None;
        }

        /// <summary>
        /// Moves the actor to somewhere this skill would reach, if it does not already.
        ///
        /// Affordability is checked against the whole price before a single step is taken. A
        /// creature that walks halfway and then discovers it cannot pay has spent its turn on
        /// nothing, which is exactly the failure the combined plan exists to avoid.
        /// </summary>
        bool CloseTo(FightCreature actor, SkillSpec skill, Cell target, out SkillRefusal refusal)
        {
            refusal = SkillRefusal.None;

            if (skill.Target == SkillTarget.Self
                || CombatRules.InRange(Cell.Distance(actor.Cell, target), skill.Range))
            {
                return true;
            }

            var steps = m_Board.StepsToReach(actor.Cell, target, skill.Range);

            if (steps < 0)
            {
                refusal = SkillRefusal.OutOfRange;
                return false;
            }

            var walk = CombatRules.MoveCost(steps, actor.StepCost);

            if (actor.Ap < walk + skill.ApCost)
            {
                refusal = SkillRefusal.NotEnoughAp;
                return false;
            }

            // The move itself, not Move: whether anybody gets a swing at this walk was decided
            // before the skill was allowed this far.
            return m_Board.TryTileInReach(actor.Cell, target, skill.Range, out var tile, out _)
                && PerformMove(actor, tile, null);
        }

        /// <summary>
        /// Whether using this opens a clash.
        ///
        /// Only an attack on somebody else is contested. A skill aimed at the user or at an ally
        /// has nobody on the other side of it, and a heal that stopped to ask its target whether
        /// they would like to resist it would be a bug with a straight face.
        /// </summary>
        static bool IsContested(SkillSpec skill, FightCreature actor, FightCreature target) =>
            skill != null
            && skill.IsContested
            && target != null
            && target != actor
            && target.IsAlive
            && target.Party != actor.Party;

        /// <summary>Applies whatever the clash left of the attack.</summary>
        void LandContested(FightCreature attacker, SkillSpec skill, FightCreature defender,
            SkillEffect effect)
        {
            if (effect.Amount <= 0 || skill.Effect.Kind != SkillEffectKind.Damage)
            {
                return;
            }

            Damage(attacker, defender, effect.Amount);
        }

        /// <summary>
        /// Applies an uncontested skill once it has been paid for.
        ///
        /// Anything aimed at an enemy went through the clash before it got here. What arrives is
        /// the rest: a heal, a breath, a swing at a tile or an ally -- things with nobody on the
        /// other side of them, which land as written.
        /// </summary>
        void LandUncontested(FightCreature actor, SkillSpec skill, FightCreature target)
        {
            // Uncontested, so there is no window to keep empty: it was used in the open, and what
            // it cost is said now.
            actor.Pool.AnnounceCommitted();
            actor.RecordUse(skill.Id);

            // Returning elements is settled first because the record has to name the ones that
            // actually came back -- "regained PYR" is the whole content of the message, and the
            // pool decides how many there were.
            var returned = skill.Effect.Kind == SkillEffectKind.ReturnElement
                ? ReturnElements(actor, skill.Effect.Amount)
                : null;

            // Written before the effect lands, so a blow that kills reads in the order it
            // happened: the swing, and then the body.
            Say(CombatEvent.ActedWith(0, actor.Id, target != null ? target.Id : 0u, skill.Id,
                actor.Facing.Index, actor.Ap.Units, Committed(skill), returned));

            switch (skill.Effect.Kind)
            {
                case SkillEffectKind.Damage:
                    if (target != null)
                    {
                        Damage(actor, target, skill.Effect.Amount);
                    }

                    break;

                case SkillEffectKind.Heal:
                    var healed = actor.Heal(skill.Effect.Amount);
                    Say(CombatEvent.HealedBy(0, actor.Id, healed, actor.Hp));
                    break;

                case SkillEffectKind.RestoreAp:
                    actor.RestoreAp(Ap.FromWhole(skill.Effect.Amount));
                    Say(CombatEvent.ApRestoredTo(0, actor.Id, skill.Effect.Amount, actor.Ap.Units));
                    break;

                case SkillEffectKind.ReturnElement:
                    // Already done, above.
                    break;
            }
        }

        /// <summary>
        /// Puts spent elements back, oldest first.
        ///
        /// Stops at the first refusal rather than running the loop out: a skill that returns three
        /// when two were spent returns two, and the creature has already paid its AP for the try.
        /// </summary>
        static List<Element> ReturnElements(FightCreature actor, int count)
        {
            var returned = new List<Element>();

            for (var i = 0; i < count && actor.Pool.Return(out var element, out _); i++)
            {
                returned.Add(element);
            }

            return returned;
        }

        /// <summary>
        /// A blow: armour first, health second, written down as one event, and the body taken off
        /// the board if it killed.
        /// </summary>
        void Damage(FightCreature attacker, FightCreature target, int amount)
        {
            var blow = target.ApplyDamage(amount);

            Say(CombatEvent.DamagedBy(0, attacker.Id, target.Id, blow.Landed, blow.Absorbed,
                blow.HpAfter, blow.ArmourAfter));

            if (blow.Killed)
            {
                Kill(target, attacker);
            }
        }

        /// <summary>
        /// Takes a dead creature off the board, and pays whoever put it there.
        ///
        /// The killer earns the victim's level, and only a character its owner brought can keep
        /// it: a premade somebody claimed for the afternoon is not theirs to level. Removed from
        /// the order before anything else reads it.
        ///
        /// The creature stays in the fight's list. It leaves the board -- it holds no cell and
        /// takes no turn -- but its record is still worth reading, and a watcher some way behind
        /// the fight still has to be shown it fall.
        /// </summary>
        void Kill(FightCreature creature, FightCreature killer)
        {
            var xp = killer != null && killer.IsPlayerCharacter && killer.Party != creature.Party
                ? Progression.XpForKill(creature.Level)
                : 0;

            if (xp > 0)
            {
                m_Listener.Award(killer.Id, xp);
            }

            m_Turns.Remove(creature.Id);
            Vacate(creature);
            creature.LeaveBoard();

            Say(CombatEvent.FellTo(0, creature.Id, killer != null ? killer.Id : 0u, xp));

            ResolveOutcome();
        }

        /// <summary>Ends the fight when only one side is left standing.</summary>
        void ResolveOutcome()
        {
            if (IsOver)
            {
                return;
            }

            var survivors = new List<Party>();

            foreach (var creature in m_Creatures)
            {
                if (creature.IsAlive && !survivors.Contains(creature.Party))
                {
                    survivors.Add(creature.Party);
                }
            }

            if (survivors.Count == 1)
            {
                m_Turns.DeclareWinner(survivors[0]);
                Say(CombatEvent.EndedWith(0, (int)survivors[0]));
            }
            else if (survivors.Count == 0)
            {
                // Nobody left to award it to. The fight ends and says so, rather than handing the
                // win to whichever party happens to be first in the enum.
                Finish();
            }
        }

        bool IsStillFighting(uint id)
        {
            var creature = Creature(id);
            return creature != null && creature.IsAlive;
        }

        /// <summary>What the rules need to know about whatever is being aimed at.</summary>
        SkillTargetInfo Describe(FightCreature actor, FightCreature target, Cell hex)
        {
            var distance = Cell.Distance(actor.Cell, hex);
            var line = target == actor || m_Board.HasLine(actor.Cell, hex);

            return target == null
                ? SkillTargetInfo.Tile(distance, line)
                : SkillTargetInfo.Creature(distance, target == actor,
                    target.Party == actor.Party, target.IsAlive, line);
        }

        // ---------- the walls ----------

        /// <summary>
        /// Changes a wall mid-fight: a breach, a door, a barricade going up.
        ///
        /// The fight's own map changes at once; whoever keeps a copy is told through the
        /// listener. Whoever was standing on a tile whose areas the change renumbered is carried
        /// to the ground they were on, and each carry is written down as a walk of one cell so
        /// the token follows -- after the wall, so a watcher sees the wall go and then the
        /// creatures step off it, which is the order it happened in.
        /// </summary>
        public bool SetWall(WallSegment segment, Wall wall)
        {
            if (!HasBegun || !m_Map.Contains(segment.Tile))
            {
                return false;
            }

            var before = m_Map.WallAt(segment);

            m_Carries.Clear();
            m_Map.WallChanged += PlanCarries;

            try
            {
                m_Map.SetWall(segment, wall);
            }
            finally
            {
                m_Map.WallChanged -= PlanCarries;
            }

            m_Listener.WallChanged(segment, wall);
            Say(CombatEvent.WallChangedAt(0, segment, before.Flags, wall.Flags));

            foreach (var carry in m_Carries)
            {
                Place(carry.Key, carry.Value);
                Say(CombatEvent.MovedAlong(0, carry.Key.Id, new[] { carry.Value },
                    carry.Key.Facing.Index, carry.Key.Ap.Units));
            }

            m_Carries.Clear();
            return true;
        }

        readonly List<KeyValuePair<FightCreature, Cell>> m_Carries =
            new List<KeyValuePair<FightCreature, Cell>>();

        /// <summary>
        /// Works out where everybody on a renumbered tile ends up.
        ///
        /// A ray coming down merges two areas; one going up splits an area, or leaves a sliver
        /// nobody can stand in. <see cref="AreaLayout.Carry"/> says where each creature's ground
        /// went; a creature whose ground went nowhere, or whose new cell somebody else already
        /// holds, takes the nearest free cell instead. Destinations are claimed in turn so two
        /// creatures on one tile cannot be carried onto the same cell.
        /// </summary>
        void PlanCarries(HexTile tile, AreaLayout before)
        {
            if (before == tile.Areas)
            {
                return;
            }

            var occupants = new List<FightCreature>();

            foreach (var pair in m_Standing)
            {
                if (pair.Key.Tile == tile.Coordinates)
                {
                    occupants.Add(pair.Value);
                }
            }

            if (occupants.Count == 0)
            {
                return;
            }

            var taken = new HashSet<Cell>(m_Standing.Keys);

            foreach (var occupant in occupants)
            {
                taken.Remove(occupant.Cell);
            }

            foreach (var occupant in occupants)
            {
                var area = before.Carry(occupant.Cell.Area, tile.Areas);
                var cell = new Cell(tile.Coordinates, area);

                if (area == AreaLayout.Dead || taken.Contains(cell) || !m_Grid.IsWalkable(cell))
                {
                    cell = HexSpawnPlacement.FindNearestFree(m_Grid, Cell.Whole(tile.Coordinates), taken);
                }

                taken.Add(cell);

                if (cell != occupant.Cell)
                {
                    m_Carries.Add(new KeyValuePair<FightCreature, Cell>(occupant, cell));
                }
            }
        }

        // ---------- who stands where ----------

        FightCreature At(Cell cell) => m_Standing.TryGetValue(cell, out var creature) ? creature : null;

        void Place(FightCreature creature, Cell cell)
        {
            if (m_Standing.TryGetValue(creature.Cell, out var standing) && standing == creature)
            {
                m_Standing.Remove(creature.Cell);
            }

            creature.SetCell(cell);
            m_Standing[cell] = creature;
        }

        void Vacate(FightCreature creature)
        {
            if (m_Standing.TryGetValue(creature.Cell, out var standing) && standing == creature)
            {
                m_Standing.Remove(creature.Cell);
            }
        }

        bool IOccupancy.IsOccupied(Cell cell) => m_Standing.ContainsKey(cell);

        bool IOccupancy.TryGet(Cell cell, out uint id)
        {
            if (m_Standing.TryGetValue(cell, out var creature))
            {
                id = creature.Id;
                return true;
            }

            id = 0;
            return false;
        }

        void IOccupancy.OccupantsOf(Hex tile, List<uint> into)
        {
            foreach (var pair in m_Standing)
            {
                if (pair.Key.Tile == tile)
                {
                    into.Add(pair.Value.Id);
                }
            }
        }

        void IOccupancy.CopyOccupiedTo(ICollection<Cell> into, Cell except)
        {
            foreach (var cell in m_Standing.Keys)
            {
                if (cell != except)
                {
                    into.Add(cell);
                }
            }
        }

        bool IOccupancy.IsAlive(uint id)
        {
            var creature = Creature(id);
            return creature != null && creature.IsAlive;
        }

        Party IOccupancy.PartyOf(uint id)
        {
            var creature = Creature(id);
            return creature != null ? creature.Party : default;
        }

        // ---------- the record ----------

        /// <summary>Writes one thing down, stamped with the round, and hands it out.</summary>
        void Say(CombatEvent e)
        {
            e.Round = m_Turns.Round;
            m_Listener.Announce(e);
        }
    }
}

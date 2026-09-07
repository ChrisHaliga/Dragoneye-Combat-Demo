using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;
using Dragoneye.Sim;
using Unity.Netcode;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// The server's hold on the fight.
    ///
    /// The fight itself is a <see cref="Fight"/>: a plain object in <c>Dragoneye.Sim</c> that
    /// knows nothing about this class, this engine or the network. This owns one, and does the
    /// three things only the Unity side can do:
    ///
    /// - <b>Carries orders in.</b> A client's request arrives through a command postbox, is
    ///   checked for who sent it, and is handed to the fight by creature id.
    /// - <b>Copies state out.</b> After every order, and after every decision the computer
    ///   makes, the fight's creatures are mirrored into the replicated components the views
    ///   read -- <see cref="CreatureState"/>, <see cref="UnitState"/>, <see cref="CreaturePool"/>,
    ///   <see cref="TurnState"/>. The mirror is one-way. Nothing on a NetworkBehaviour is ever
    ///   read back into the fight.
    /// - <b>Answers what the fight asks.</b> As the fight's <see cref="IFightListener"/>, it
    ///   sends the record to every machine, puts a defender's prompt on the right client, and
    ///   pays experience to the roster.
    ///
    /// It is the only class that holds a <see cref="Fight"/>. A test holds one directly, with a
    /// listener of its own, and runs the same fight with no scene loaded.
    ///
    /// Server only. Clients ask for actions through <see cref="UnitCommands"/> and read the mirror.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDirector : MonoBehaviour, IFightListener
    {
        [SerializeField, Tooltip("Every creature on the board.")]
        CreatureRegistry m_Creatures;

        [SerializeField, Tooltip("The arena being fought over.")]
        ArenaMap m_Map;

        [SerializeField, Tooltip("Seed for every roll this fight makes. Zero picks one and logs "
             + "it, so any fight can be rolled again.")]
        int m_Seed;

        Fight m_Fight;

        /// <summary>The director for the match in progress, or null outside one.</summary>
        public static CombatDirector Current { get; private set; }

        /// <summary>What this fight rolls from. Null before the match begins.</summary>
        public Dice Dice => m_Fight != null ? m_Fight.Dice : null;

        void Awake() => Current = this;

        void OnDestroy()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

        /// <summary>Whether an attack is waiting on somebody's answer.</summary>
        public bool IsClashPending => m_Fight != null && m_Fight.IsClashPending;

        /// <summary>Whether the fight is stopped on anybody's question at all.</summary>
        public bool IsBusy => m_Fight != null && m_Fight.IsBusy;

        /// <summary>
        /// Whether this creature is stood next to that one and looking at it.
        ///
        /// Answered from replicated state, so a client can ask it about a hover: position, facing
        /// and walls are all on the board for anybody to read.
        /// </summary>
        public static bool Watches(CreatureState watcher, CreatureState mover)
        {
            var grid = GridOf(Current);

            return grid != null && AreEnemies(watcher, mover)
                && ThreatGeometry.Watches(grid, watcher.Cell, watcher.Facing, mover.Cell);
        }

        /// <summary>Whether that creature walking to this cell would give this one a swing.</summary>
        public static bool Provokes(CreatureState watcher, CreatureState mover, Cell destination)
        {
            var grid = GridOf(Current);

            return grid != null && AreEnemies(watcher, mover)
                && ThreatGeometry.Provokes(grid, watcher.Cell, watcher.Facing, mover.Cell, destination);
        }

        static IGridRules GridOf(CombatDirector director) =>
            director != null && director.m_Map != null ? director.m_Map.Grid : null;

        static bool AreEnemies(CreatureState a, CreatureState b) =>
            a != null && b != null && a != b && a.IsAlive && b.IsAlive && a.Party != b.Party;

        // ---------- the match ----------

        /// <summary>
        /// Server only. Opens the fight once every creature is on the board.
        ///
        /// The fight is built here, at the one moment a match begins: every creature the registry
        /// holds becomes a <see cref="FightCreature"/> made from its profile, and from then on the
        /// fight's copy is the truth and the registry's is the mirror. A scripted fight brings
        /// its own seed and its own brain; an ordinary one takes the inspector's seed or a fresh
        /// one, logged so a fight that went wrong can be rolled again.
        /// </summary>
        /// <param name="seed">Zero for the inspector's seed, or a fresh one when that is zero too.</param>
        /// <param name="brain">What runs the computer's creatures. The basic opponent when null.</param>
        public void ServerBeginMatch(int seed = 0, ICreatureBrain brain = null)
        {
            if (!IsServer || TurnState.Current == null || m_Creatures == null
                || m_Map == null || m_Map.Map == null || m_Map.Grid == null)
            {
                return;
            }

            var chosen = seed != 0 ? seed : m_Seed != 0 ? m_Seed : unchecked((int)System.DateTime.UtcNow.Ticks);
            var dice = new Dice(chosen);
            Debug.Log($"[CombatDirector] Fight seed {dice.Seed}.", this);

            m_Fight = new Fight(m_Map.Map, m_Map.Grid, ElementMatchups.Table, dice,
                brain ?? new BasicBrain(), this);

            foreach (var creature in m_Creatures.All)
            {
                if (creature != null && creature.IsAlive)
                {
                    m_Fight.Add(Enlist(creature));
                }
            }

            m_Fight.Begin();
            Mirror();
        }

        /// <summary>
        /// A creature as the fight will know it, read once from its profile.
        ///
        /// A built character swings with its weapon and with nothing else; a premade with the
        /// first attack it was authored holding. That is the one thing about a creature the
        /// profile does not say outright, so it is worked out here.
        /// </summary>
        static FightCreature Enlist(CreatureState creature)
        {
            var skills = new List<SkillSpec>(creature.SkillCommands.Skills);
            var characters = PlayerCharacters.Current;
            var loadout = creature.IsPlayerCharacter && characters != null
                ? characters.LoadoutFor(creature.BuildSlot)
                : null;

            var weapon = loadout != null ? Opportunity.PrimaryOf(loadout) : Opportunity.PrimaryOf(skills);

            return new FightCreature(creature.TurnId, creature.Party, creature.ControllerSlot,
                creature.Level, creature.IsPlayerCharacter, creature.Cell, creature.Facing,
                creature.MaxHp, creature.MaxArmour, creature.MaxAp, creature.Regen,
                creature.StepCost, creature.Speed, creature.HasAdvantage, skills, weapon,
                creature.StartingPool);
        }

        /// <summary>Server only. Ends the active creature's turn and passes play on.</summary>
        public void ServerEndTurn()
        {
            if (IsServer && m_Fight != null && m_Fight.EndTurn())
            {
                Mirror();
            }
        }

        /// <summary>Server only. Stops the fight where it stands, with nobody winning it.</summary>
        public void ServerFinish()
        {
            if (IsServer && m_Fight != null)
            {
                m_Fight.Finish();
                Mirror();
            }
        }

        /// <summary>Server only. Walks a creature, and turns it.</summary>
        public bool ServerMove(CreatureState actor, Cell destination, Facing? facing = null)
        {
            if (!IsServer || m_Fight == null || actor == null)
            {
                return false;
            }

            var moved = m_Fight.Move(actor.TurnId, destination, facing);
            Mirror();
            return moved;
        }

        /// <summary>Server only. Uses a skill, spending both costs and applying the effect.</summary>
        public bool ServerUseSkill(CreatureState actor, int skillId, Cell target,
            out SkillRefusal refusal, Element? element = null)
        {
            refusal = SkillRefusal.NotYourTurn;

            if (!IsServer || m_Fight == null || actor == null)
            {
                return false;
            }

            var used = m_Fight.UseSkill(actor.TurnId, skillId, target, out refusal, element);
            Mirror();
            return used;
        }

        /// <summary>Server only. The defender's answer, arriving from wherever they are.</summary>
        public bool ServerAnswerClash(CreatureState defender, IReadOnlyList<Element> answer,
            out DefenceRefusal refusal)
        {
            refusal = DefenceRefusal.AlreadyResolved;

            if (!IsServer || m_Fight == null || defender == null)
            {
                return false;
            }

            var answered = m_Fight.AnswerClash(defender.TurnId, answer, out refusal);
            Mirror();
            return answered;
        }

        /// <summary>Server only. Whether the creature offered a swing takes it.</summary>
        public bool ServerAnswerOpportunity(CreatureState watcher, bool swings)
        {
            if (!IsServer || m_Fight == null || watcher == null)
            {
                return false;
            }

            var answered = m_Fight.AnswerOpportunity(watcher.TurnId, swings);
            Mirror();
            return answered;
        }

        /// <summary>Server only. Changes a wall mid-fight: a breach, a door, a barricade going up.</summary>
        public bool ServerSetWall(WallSegment segment, Wall wall)
        {
            if (!IsServer || m_Fight == null)
            {
                return false;
            }

            var changed = m_Fight.SetWall(segment, wall);
            Mirror();
            return changed;
        }

        /// <summary>
        /// Whether this creature may act right now: alive, and the one whose turn it is.
        ///
        /// Ownership and control are the caller's business -- <see cref="UnitCommands"/> checks the
        /// sender -- because the computer's own turns come through here with no client behind them.
        /// </summary>
        public bool CanAct(CreatureState actor) =>
            IsServer && m_Fight != null && actor != null && m_Fight.CanAct(actor.TurnId);

        // ---------- the computer's turns, and the watchdog ----------

        /// <summary>
        /// One decision a frame for the computer, and an eye on anybody who has left.
        ///
        /// A frame per decision rather than a whole turn at once, so a question one decision
        /// opens is on somebody's screen before the next is asked for. The fight itself has no
        /// clock: it takes a step when it is given one.
        ///
        /// **There is no timer on a decision, deliberately.** A player who takes an hour over a
        /// clash is a player thinking about it, and this game does not measure skill in seconds.
        /// A client that has *gone*, though, is not thinking. That is a closed socket rather than
        /// a slow decision, and the fight cannot wait on it.
        /// </summary>
        void Update()
        {
            if (!IsServer || m_Fight == null)
            {
                return;
            }

            var defender = m_Creatures.ByTurnId(m_Fight.AskedDefender);

            if (defender != null && !defender.IsComputerControlled && !IsStillConnected(defender))
            {
                Debug.Log("The defender left mid-clash; the attack resolves unopposed.", this);
                m_Fight.AbandonClash();
                Mirror();
            }

            var offered = m_Creatures.ByTurnId(m_Fight.OfferedWatcher);

            if (offered != null && !offered.IsComputerControlled && !IsStillConnected(offered))
            {
                Debug.Log("A creature left while being offered a swing; it declines.", this);
                m_Fight.AbandonOffer();
                Mirror();
            }

            if (m_Fight.Step())
            {
                Mirror();
            }
        }

        static bool IsStillConnected(CreatureState creature)
        {
            var manager = NetworkManager.Singleton;

            return manager != null
                && manager.ConnectedClients.ContainsKey(creature.OwnerClientId);
        }

        // ---------- the mirror ----------

        /// <summary>
        /// Copies the fight into the replicated components, so every client reads what the fight
        /// decided.
        ///
        /// Every creature, every time. A mirror that tried to copy only what changed would have
        /// to know what each order touches, and be wrong the first time an order touched
        /// something new; each replicated field only sends when its value actually differs, so
        /// copying everything costs a comparison per field and nothing on the wire.
        /// </summary>
        void Mirror()
        {
            if (m_Fight == null)
            {
                return;
            }

            foreach (var fighter in m_Fight.Creatures)
            {
                var creature = m_Creatures.ByTurnId(fighter.Id);

                if (creature == null)
                {
                    continue;
                }

                creature.ServerMirror(fighter.Hp, fighter.Armour, fighter.Ap.Units, fighter.Facing.Index);
                creature.Unit.ServerMirror(fighter.Cell, fighter.OnBoard);
                creature.Pool.ServerMirror(fighter.Pool.Hand, fighter.Pool.Ledger);
                creature.SkillCommands.ServerMirrorSeen(fighter.Seen);
            }

            TurnState.Current?.ServerMirror(m_Fight.Order, m_Fight.ActiveIndex, m_Fight.Round,
                m_Fight.Outcome);
        }

        // ---------- what the fight has to say ----------

        void IFightListener.Announce(CombatEvent e) => CombatAnnouncer.Current?.ServerSend(e);

        void IFightListener.AskDefence(uint defenderId, DefenceRequest request)
        {
            // Mirrored before the question goes out, so the prompt opens over a hand that is
            // already what the fight says it is.
            Mirror();
            ClashCommands.Current?.ServerAsk(request, m_Creatures.ByTurnId(defenderId));
        }

        void IFightListener.CloseDefence(uint defenderId) => ClashCommands.Current?.ServerClearPrompt();

        void IFightListener.OfferSwing(uint watcherId, uint moverId, SkillSpec swing)
        {
            Mirror();
            OpportunityCommands.Current?.ServerOffer(m_Creatures.ByTurnId(watcherId),
                m_Creatures.ByTurnId(moverId), swing);
        }

        void IFightListener.CloseOffer(uint watcherId) => OpportunityCommands.Current?.ServerClearOffer();

        void IFightListener.Hold(uint moverId) =>
            OpportunityCommands.Current?.ServerHold(m_Creatures.ByTurnId(moverId));

        void IFightListener.Release() => OpportunityCommands.Current?.ServerRelease();

        void IFightListener.WallChanged(WallSegment segment, Wall wall) =>
            WallCommands.Current?.ServerSet(segment, wall);

        void IFightListener.Award(uint killerId, int xp)
        {
            var killer = m_Creatures.ByTurnId(killerId);

            if (killer != null && PlayerCharacters.Current != null)
            {
                PlayerCharacters.Current.ServerAwardXp(killer.BuildSlot, xp);
            }
        }

        void IFightListener.Warn(string message) => Debug.LogWarning($"[Fight] {message}", this);
    }
}

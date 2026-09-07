using System.Collections.Generic;
using Dragoneye.Combat;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Game.Combat
{
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// A move or a skill use that has been asked for and has not happened yet.
    ///
    /// It waits because leaving a tile somebody is watching gives them a swing at you, and the
    /// swing has to land before the walk does. Both kinds are held here rather than only moves,
    /// because a skill that walks into range is a walk -- and one that provoked nothing while an
    /// ordinary move did would be a way to leave for free.
    /// </summary>
    public readonly struct PendingAction
    {
        public const int NoSkill = int.MinValue;

        public readonly CreatureState Actor;

        /// <summary>Where the move goes, or what the skill is aimed at.</summary>
        public readonly Cell Where;

        /// <summary>Where the actor will actually be standing afterwards. What the watchers care about.</summary>
        public readonly Cell Destination;

        public readonly Facing? Facing;
        public readonly int SkillId;

        /// <summary>Which element the skill was going to arrive as, where that was a choice.</summary>
        public readonly Element Element;

        public PendingAction(CreatureState actor, Cell where, Cell destination, Facing? facing,
            int skillId, Element element = default)
        {
            Actor = actor;
            Where = where;
            Destination = destination;
            Facing = facing;
            SkillId = skillId;
            Element = element;
        }

        public static PendingAction Move(CreatureState actor, Cell destination, Facing? facing) =>
            new PendingAction(actor, destination, destination, facing, NoSkill);

        public static PendingAction Skill(CreatureState actor, int skillId, Cell target,
            Cell approach, Element element) =>
            new PendingAction(actor, target, approach, null, skillId, element);

        public bool Exists => Actor != null;

        public bool IsMove => SkillId == NoSkill;
    }

    /// <summary>
    /// Holds an action back while everybody it would walk away from decides whether to swing.
    ///
    /// The rule it applies is <see cref="ThreatGeometry.Provokes"/>: leaving a watched tile.
    /// Everything else here is bookkeeping -- who has been asked, who is still to ask, and what
    /// happens once the last of them has answered -- and a queue, because two enemies can both be
    /// watching the same tile and each gets a swing in turn.
    ///
    /// Server only. It never decides a clash: a swing that is taken is handed to the host to open
    /// as an ordinary attack, and this is told when that attack is over.
    /// </summary>
    public sealed class OpportunityConductor
    {
        readonly IOpportunityHost m_Host;
        readonly CreatureRegistry m_Creatures;
        readonly Dice m_Dice;
        readonly ArenaMap m_Map;

        PendingAction m_Pending;
        readonly List<CreatureState> m_Watchers = new List<CreatureState>();
        CreatureState m_Offered;

        public OpportunityConductor(IOpportunityHost host, CreatureRegistry creatures, Dice dice,
            ArenaMap map)
        {
            m_Host = host;
            m_Creatures = creatures;
            m_Dice = dice;
            m_Map = map;
        }

        /// <summary>Whether an action is being held while somebody decides.</summary>
        public bool IsPending => m_Pending.Exists;

        /// <summary>Who is being offered a swing right now, if anybody.</summary>
        public CreatureState Offered => m_Offered;

        /// <summary>
        /// Whether this creature is stood next to that one, looking at it, with nothing it cannot
        /// see through between them.
        ///
        /// Position, facing and walls, all of which are on the board for anybody to read. Whether
        /// it can afford the swing is its own business -- that is the whole of DE-005.
        /// </summary>
        public static bool Watches(ArenaMap map, CreatureState watcher, CreatureState mover) =>
            AreEnemies(watcher, mover) && map != null
            && ThreatGeometry.Watches(map.Grid, watcher.Cell, watcher.Facing, mover.Cell);

        /// <summary>Whether that creature walking to this cell would give this one a swing.</summary>
        public static bool Provokes(ArenaMap map, CreatureState watcher, CreatureState mover,
            Cell destination) =>
            AreEnemies(watcher, mover) && map != null
            && ThreatGeometry.Provokes(map.Grid, watcher.Cell, watcher.Facing, mover.Cell, destination);

        static bool AreEnemies(CreatureState a, CreatureState b) =>
            a != null && b != null && a != b && a.IsAlive && b.IsAlive && a.Party != b.Party;

        /// <summary>
        /// Holds the action back if anybody gets a swing at it.
        ///
        /// The watchers are read once, before anything moves, so the list cannot grow halfway
        /// through as creatures turn to face each other.
        /// </summary>
        /// <returns>True when the action was suspended and will be run later.</returns>
        public bool TryInterrupt(PendingAction action)
        {
            if (m_Pending.Exists || !action.Exists || m_Creatures == null)
            {
                return false;
            }

            m_Watchers.Clear();

            foreach (var creature in m_Creatures.All)
            {
                if (CanSwing(creature, action.Actor, action.Destination))
                {
                    m_Watchers.Add(creature);
                }
            }

            if (m_Watchers.Count == 0)
            {
                return false;
            }

            m_Pending = action;
            OpportunityCommands.Current?.ServerHold(action.Actor);
            AskNext();
            return true;
        }

        /// <summary>
        /// Whether this creature could swing at that one, walking there, right now.
        ///
        /// Asked again when each offer goes out as well as when the queue is built: an earlier
        /// swing may have killed the mover, and a creature may have spent its last element
        /// answering one.
        /// </summary>
        bool CanSwing(CreatureState watcher, CreatureState mover, Cell destination)
        {
            if (!Provokes(m_Map, watcher, mover, destination))
            {
                return false;
            }

            var swing = SwingOf(watcher, out _);
            var pool = watcher.Pool;

            return swing != null && pool != null
                && SkillRules.TryChooseElement(swing, pool.ServerLedger, out _);
        }

        /// <summary>
        /// The attack this creature would swing with, and the authored skill it was made from --
        /// which is what "seen" is about. Null when it has none.
        ///
        /// A built character swings with its weapon and with nothing else; a premade with the
        /// first attack it was authored holding.
        /// </summary>
        static SkillSpec SwingOf(CreatureState watcher, out SkillSpec source)
        {
            source = null;

            if (watcher == null)
            {
                return null;
            }

            var characters = PlayerCharacters.Current;
            var loadout = watcher.IsPlayerCharacter && characters != null
                ? characters.LoadoutFor(watcher.BuildSlot)
                : null;

            if (loadout != null)
            {
                source = Opportunity.PrimaryOf(loadout);
                return Opportunity.From(source);
            }

            var commands = watcher.SkillCommands;

            source = commands != null ? Opportunity.PrimaryOf(commands.Skills) : null;
            return Opportunity.From(source);
        }

        /// <summary>
        /// Whether this creature has been watched using its weapon, so the swing it is about to
        /// take is one whose element everybody already knows.
        /// </summary>
        static bool HasShownWeapon(CreatureState watcher, SkillSpec source)
        {
            var commands = watcher != null ? watcher.SkillCommands : null;

            if (commands == null || source == null)
            {
                return false;
            }

            foreach (var id in commands.SeenSkillIds)
            {
                if (id == source.Id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Puts the offer to the next watcher, or runs the held action when none are left.</summary>
        void AskNext()
        {
            m_Offered = null;

            while (m_Watchers.Count > 0)
            {
                var watcher = m_Watchers[0];
                m_Watchers.RemoveAt(0);

                if (!m_Pending.Exists || !CanSwing(watcher, m_Pending.Actor, m_Pending.Destination))
                {
                    continue;
                }

                m_Offered = watcher;

                if (watcher.IsComputerControlled)
                {
                    Answer(watcher, Takes(watcher, m_Pending.Actor));
                    return;
                }

                if (OpportunityCommands.Current != null)
                {
                    OpportunityCommands.Current.ServerOffer(watcher, m_Pending.Actor,
                        SwingOf(watcher, out _));
                    return;
                }

                // No postbox in the arena, so nobody can be asked and nobody swings. Better than
                // hanging a move on a question that will never be answered.
                Debug.LogWarning("No opportunity commands in the arena; the swing is skipped.");
                m_Offered = null;
            }

            RunPending();
        }

        /// <summary>Whether to swing. From the client that was offered it, or from the computer.</summary>
        public bool Answer(CreatureState watcher, bool swings)
        {
            if (m_Offered == null || watcher != m_Offered || !m_Pending.Exists)
            {
                return false;
            }

            var mover = m_Pending.Actor;
            m_Offered = null;

            OpportunityCommands.Current?.ServerClearOffer();

            if (!swings || !CanSwing(watcher, mover, m_Pending.Destination))
            {
                // Said out loud. The board warned the mover a swing was coming; when it does not
                // come, the log has to say who let them go, or the warning reads as a lie.
                CombatAnnouncer.Current?.ServerHeldBack(watcher.TurnId, mover.TurnId);
                AskNext();
                return true;
            }

            var pool = watcher.Pool;
            var skill = SwingOf(watcher, out var weapon);

            // A fist has a choice of elements and nobody to ask, so it takes the first it can pay
            // for -- the same one the prompt showed, because the prompt asked the same question.
            skill = pool != null ? SkillRules.Settle(skill, null, pool.ServerLedger) : null;

            // A seen weapon is a known element. The defender is told, and the odds they are shown
            // are worked out against that one element rather than the whole hand.
            var telegraphed = skill != null && HasShownWeapon(watcher, weapon)
                ? skill.Element
                : (Element?)null;

            // Committed, not spent: a swing hides what it is made of until the answer is in, the
            // same as any other attack.
            if (pool == null || skill == null
                || !pool.ServerCommit(skill.Element, skill.ElementCost, out _))
            {
                AskNext();
                return true;
            }

            // Turning to swing, like any other attack, which opens the swinger's own back in turn.
            watcher.ServerFace(ThreatGeometry.Bearing(m_Map.Grid, watcher.Cell, mover.Cell));

            m_Host.BeginClash(watcher, skill, mover, telegraphed);
            return true;
        }

        /// <summary>The one who was offered the swing is gone. It declines.</summary>
        public void Abandon()
        {
            if (m_Offered != null)
            {
                Answer(m_Offered, false);
            }
        }

        /// <summary>A swing's clash is over: the next watcher is asked, or the action finally runs.</summary>
        public void Continue()
        {
            if (m_Pending.Exists)
            {
                AskNext();
            }
        }

        /// <summary>
        /// Whether a computer creature takes its swing.
        ///
        /// Nearly always: a free attack is worth taking, and the board has already told the mover
        /// it is coming. The roll that remains is there so the reaction is not a certainty a
        /// player can bank on -- and when it comes up, the log says so.
        /// </summary>
        bool Takes(CreatureState watcher, CreatureState mover)
        {
            var swing = SwingOf(watcher, out _);
            var pool = watcher.Pool;

            if (swing == null || pool == null
                || !SkillRules.TryChooseElement(swing, pool.ServerLedger, out _))
            {
                return false;
            }

            return Opportunity.Takes(m_Dice.Roll());
        }

        /// <summary>Runs the action everybody has now had their swing at.</summary>
        void RunPending()
        {
            var pending = m_Pending;
            m_Pending = default;
            m_Watchers.Clear();

            // Released before the action runs, so a client repricing the board on the move it
            // sees does not still read the fight as waiting.
            OpportunityCommands.Current?.ServerRelease();

            if (!pending.Exists || !pending.Actor.IsAlive)
            {
                return;
            }

            if (pending.IsMove)
            {
                m_Host.PerformMove(pending.Actor, pending.Where, pending.Facing);
                return;
            }

            // Replayed from the top. Everything it checks may have changed while the swings landed
            // -- health, action points, who is standing where -- but it is not asked again whether
            // anybody wants a swing at it: they have all had one.
            m_Host.ReplaySkill(pending.Actor, pending.SkillId, pending.Where, pending.Element);
        }
    }
}

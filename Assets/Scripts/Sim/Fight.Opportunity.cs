using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Sim
{
    /// <summary>
    /// A move or a skill use that has been asked for and has not happened yet.
    ///
    /// It waits because leaving a tile somebody is watching gives them a swing at you, and the
    /// swing has to land before the walk does. Both kinds are held here rather than only moves,
    /// because a skill that walks into range is a walk -- and one that provoked nothing while an
    /// ordinary move did would be a way to leave for free.
    /// </summary>
    readonly struct PendingAction
    {
        public const int NoSkill = int.MinValue;

        public readonly FightCreature Actor;

        /// <summary>Where the move goes, or what the skill is aimed at.</summary>
        public readonly Cell Where;

        /// <summary>Where the actor will actually be standing afterwards. What the watchers care about.</summary>
        public readonly Cell Destination;

        public readonly Facing? Facing;
        public readonly int SkillId;

        /// <summary>Which element the skill was going to arrive as, where that was a choice.</summary>
        public readonly Element Element;

        PendingAction(FightCreature actor, Cell where, Cell destination, Facing? facing,
            int skillId, Element element)
        {
            Actor = actor;
            Where = where;
            Destination = destination;
            Facing = facing;
            SkillId = skillId;
            Element = element;
        }

        public static PendingAction Move(FightCreature actor, Cell destination, Facing? facing) =>
            new PendingAction(actor, destination, destination, facing, NoSkill, default);

        public static PendingAction Skill(FightCreature actor, int skillId, Cell target,
            Cell approach, Element element) =>
            new PendingAction(actor, target, approach, null, skillId, element);

        public bool Exists => Actor != null;

        public bool IsMove => SkillId == NoSkill;
    }

    /// <summary>
    /// The swing at somebody walking past: an action held back while everybody it would walk
    /// away from decides whether to take one.
    ///
    /// The rule it applies is <see cref="ThreatGeometry.Provokes"/>: leaving a watched tile.
    /// Everything else here is bookkeeping -- who has been asked, who is still to ask, and what
    /// happens once the last of them has answered -- and a queue, because two enemies can both be
    /// watching the same tile and each gets a swing in turn. A swing that is taken is an ordinary
    /// attack, opened as a clash like any other.
    /// </summary>
    public sealed partial class Fight
    {
        PendingAction m_Pending;
        readonly List<FightCreature> m_Watchers = new List<FightCreature>();
        FightCreature m_Offered;

        /// <summary>Who is being offered a swing right now. Zero if nobody.</summary>
        public uint OfferedWatcher => m_Offered != null ? m_Offered.Id : 0u;

        /// <summary>Whose action is being held while the watchers decide. Zero if nobody's.</summary>
        public uint HeldMover => m_Pending.Exists ? m_Pending.Actor.Id : 0u;

        /// <summary>
        /// Whether this creature is stood next to that one, looking at it, with nothing it cannot
        /// see through between them.
        ///
        /// Position, facing and walls, all of which are on the board for anybody to read. Whether
        /// it can afford the swing is its own business -- that is the whole of DE-005.
        /// </summary>
        public bool Watches(uint watcherId, uint moverId)
        {
            var watcher = Creature(watcherId);
            var mover = Creature(moverId);

            return AreEnemies(watcher, mover)
                && ThreatGeometry.Watches(m_Grid, watcher.Cell, watcher.Facing, mover.Cell);
        }

        /// <summary>Whether that creature walking to this cell would give this one a swing.</summary>
        public bool Provokes(uint watcherId, uint moverId, Cell destination)
        {
            var watcher = Creature(watcherId);
            var mover = Creature(moverId);

            return AreEnemies(watcher, mover)
                && ThreatGeometry.Provokes(m_Grid, watcher.Cell, watcher.Facing, mover.Cell, destination);
        }

        static bool AreEnemies(FightCreature a, FightCreature b) =>
            a != null && b != null && a != b && a.IsAlive && b.IsAlive && a.Party != b.Party;

        /// <summary>
        /// Holds the action back if anybody gets a swing at it.
        ///
        /// The watchers are read once, before anything moves, so the list cannot grow halfway
        /// through as creatures turn to face each other.
        /// </summary>
        /// <returns>True when the action was suspended and will be run later.</returns>
        bool TryInterrupt(PendingAction action)
        {
            if (m_Pending.Exists || !action.Exists)
            {
                return false;
            }

            m_Watchers.Clear();

            foreach (var creature in m_Creatures)
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
            m_Listener.Hold(action.Actor.Id);
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
        bool CanSwing(FightCreature watcher, FightCreature mover, Cell destination)
        {
            if (!AreEnemies(watcher, mover)
                || !ThreatGeometry.Provokes(m_Grid, watcher.Cell, watcher.Facing, mover.Cell, destination))
            {
                return false;
            }

            var swing = Opportunity.From(watcher.Weapon);

            return swing != null && SkillRules.TryChooseElement(swing, watcher.Pool.Private, out _);
        }

        /// <summary>
        /// Whether this creature has been watched using its weapon, so the swing it is about to
        /// take is one whose element everybody already knows.
        /// </summary>
        static bool HasShownWeapon(FightCreature watcher) =>
            watcher.Weapon != null && watcher.HasShown(watcher.Weapon.Id);

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
                    AnswerOpportunity(watcher.Id, Takes(watcher));
                    return;
                }

                m_Listener.OfferSwing(watcher.Id, m_Pending.Actor.Id, Opportunity.From(watcher.Weapon));
                return;
            }

            RunPending();
        }

        /// <summary>Whether to swing. From the person who was offered it, or from the computer.</summary>
        public bool AnswerOpportunity(uint watcherId, bool swings)
        {
            var watcher = Creature(watcherId);

            if (m_Offered == null || watcher != m_Offered || !m_Pending.Exists)
            {
                return false;
            }

            var mover = m_Pending.Actor;
            m_Offered = null;

            m_Listener.CloseOffer(watcher.Id);

            if (!swings || !CanSwing(watcher, mover, m_Pending.Destination))
            {
                // Said out loud. The board warned the mover a swing was coming; when it does not
                // come, the log has to say who let them go, or the warning reads as a lie.
                Say(CombatEvent.HeldBackBy(0, watcher.Id, mover.Id));
                AskNext();
                return true;
            }

            // A fist has a choice of elements and nobody to ask, so it takes the first it can pay
            // for -- the same one the prompt showed, because the prompt asked the same question.
            var skill = SkillRules.Settle(Opportunity.From(watcher.Weapon), null, watcher.Pool.Private);

            // A seen weapon is a known element. The defender is told, and the odds they are shown
            // are worked out against that one element rather than the whole hand.
            var telegraphed = skill != null && HasShownWeapon(watcher)
                ? skill.Element
                : (Element?)null;

            // Committed, not spent: a swing hides what it is made of until the answer is in, the
            // same as any other attack.
            if (skill == null || !watcher.Pool.Commit(skill.Element, skill.ElementCost, out _))
            {
                AskNext();
                return true;
            }

            // Turning to swing, like any other attack, which opens the swinger's own back in turn.
            watcher.Face(ThreatGeometry.Bearing(m_Grid, watcher.Cell, mover.Cell));

            BeginClash(watcher, skill, mover, telegraphed);
            return true;
        }

        /// <summary>The one who was offered the swing is gone. It declines.</summary>
        public void AbandonOffer()
        {
            if (m_Offered != null)
            {
                AnswerOpportunity(m_Offered.Id, false);
            }
        }

        /// <summary>A swing's clash is over: the next watcher is asked, or the action finally runs.</summary>
        void ContinueAfterClash()
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
        bool Takes(FightCreature watcher)
        {
            var swing = Opportunity.From(watcher.Weapon);

            return swing != null
                && SkillRules.TryChooseElement(swing, watcher.Pool.Private, out _)
                && Opportunity.Takes(m_Dice.Roll());
        }

        /// <summary>Runs the action everybody has now had their swing at.</summary>
        void RunPending()
        {
            var pending = m_Pending;
            m_Pending = default;
            m_Watchers.Clear();

            // Released before the action runs, so a client repricing the board on the move it
            // sees does not still read the fight as waiting.
            m_Listener.Release();

            if (!pending.Exists || !pending.Actor.IsAlive)
            {
                return;
            }

            if (pending.IsMove)
            {
                PerformMove(pending.Actor, pending.Where, pending.Facing);
                return;
            }

            // Replayed from the top. Everything it checks may have changed while the swings landed
            // -- health, action points, who is standing where -- but it is not asked again whether
            // anybody wants a swing at it: they have all had one.
            UseSkill(pending.Actor, pending.SkillId, pending.Where, out _, pending.Element,
                provoked: true);
        }
    }
}

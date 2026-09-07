using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Sim
{
    /// <summary>
    /// The clash: an attack suspended on the defender's answer, from the swing to the reveal.
    ///
    /// What it holds is a <see cref="ClashSequence"/>, which is where every decision about the
    /// clash is made. This only carries messages to it and applies what it says. At most one
    /// clash is open at a time: the fight is suspended while it is.
    /// </summary>
    public sealed partial class Fight
    {
        // The attack that is waiting on an answer, and everything needed to finish it.
        ClashSequence m_Clash;
        FightCreature m_Attacker;
        FightCreature m_Defender;
        SkillSpec m_ClashSkill;
        bool m_Flanked;

        /// <summary>Whether an attack is waiting on somebody's answer.</summary>
        public bool IsClashPending => m_Clash != null;

        /// <summary>Who is being asked, while somebody is. Zero otherwise.</summary>
        public uint AskedDefender => m_Defender != null ? m_Defender.Id : 0u;

        /// <summary>
        /// Suspends the attack and asks the defender.
        ///
        /// The attacker's element has already left their pool by now -- DE-005 spends the
        /// commitment before anybody is asked anything, so an attack cannot be taken back once the
        /// defender has been made to think about it.
        ///
        /// The swing is written down here, once for every way a contested attack begins, unless
        /// the caller already wrote it -- a shot that rolled announced itself as a shot.
        /// </summary>
        void BeginClash(FightCreature actor, SkillSpec skill, FightCreature target,
            Element? telegraphed = null, bool announced = false)
        {
            if (!announced)
            {
                Say(CombatEvent.SwungAt(0, actor.Id, target.Id, skill.Id, actor.Facing.Index,
                    actor.Ap.Units));
            }

            // Which way the blow arrived, from the defender's point of view.
            var flanked = FacingRules.IsFlank(target.Facing,
                ThreatGeometry.Bearing(m_Grid, target.Cell, actor.Cell));

            m_Clash = ClashSequence.Begin(Committed(skill),
                new ClashSide((int)actor.Id, advantage: actor.HasAdvantage),
                new ClashSide((int)target.Id, advantage: target.HasAdvantage, disadvantage: flanked),
                target.Pool.Private, m_Matchups, telegraphed);

            m_Attacker = actor;
            m_Defender = target;
            m_ClashSkill = skill;
            m_Flanked = flanked;

            Ask(m_Clash.Request, target);
        }

        /// <summary>
        /// Puts the question to whoever is running the defender.
        ///
        /// A computer defender answers from <see cref="ChooseDefence"/>, which is handed the
        /// prompt and its own pool and nothing else -- so it cannot answer better than a player
        /// could for want of information a player would not have. That is a property of the
        /// signature rather than of anybody's restraint.
        /// </summary>
        void Ask(DefenceRequest request, FightCreature defender)
        {
            if (!request.HasAnswer)
            {
                // Nothing to answer with. DE-005: the attack resolves unopposed rather than
                // stopping to ask a question with no answers on it.
                SettleClash(null, declined: true);
                return;
            }

            if (defender.IsComputerControlled)
            {
                // Through the same door a player's answer comes in by, so both halves of the job
                // -- committing to the sequence and settling -- happen for both kinds of defender.
                AnswerClash(defender.Id, ChooseDefence(defender, m_Attacker, request), out _);
                return;
            }

            m_Listener.AskDefence(defender.Id, request);
        }

        /// <summary>
        /// The defender's answer, arriving from wherever they are.
        ///
        /// Checked against the sequence rather than trusted: an answer naming elements the defender
        /// does not hold, or more than were asked for, is refused there and the clash stays open.
        /// </summary>
        public bool AnswerClash(uint defenderId, IReadOnlyList<Element> answer,
            out DefenceRefusal refusal)
        {
            refusal = DefenceRefusal.None;

            if (m_Clash == null || m_Defender == null || defenderId != m_Defender.Id)
            {
                refusal = DefenceRefusal.AlreadyResolved;
                return false;
            }

            if (answer == null || answer.Count == 0)
            {
                SettleClash(null, declined: true);
                return true;
            }

            var pool = m_Defender.Pool;

            // Committed before anything is spent, so an answer the sequence refuses costs nothing
            // and the clash stays open for a better one.
            if (!m_Clash.TryCommit(answer, pool.Private, out refusal))
            {
                return false;
            }

            foreach (var element in answer)
            {
                // Committed, not spent, for the same reason the attacker's was: both are announced
                // together once neither can be used to work out the other.
                pool.Commit(element, 1, out _);
            }

            SettleClash(answer, declined: false);
            return true;
        }

        /// <summary>The defender is gone. The attack resolves unopposed.</summary>
        public void AbandonClash()
        {
            if (m_Clash != null)
            {
                SettleClash(null, declined: true);
            }
        }

        /// <summary>
        /// What a computer creature puts up.
        ///
        /// Weighed against exactly what a player is shown -- what the attacker has been proven to
        /// hold -- and then rolled for, by <see cref="ClashDefenceOdds.ChooseAnswer"/>. A defender
        /// that always answered optimally is a defender who can be hard-countered every time once
        /// somebody has learned the table, and a fight whose right answer never changes has one
        /// turn in it. The dice are the fight's, so the answer can be predicted from the seed.
        /// </summary>
        IReadOnlyList<Element> ChooseDefence(FightCreature defender, FightCreature attacker,
            DefenceRequest request) =>
            ClashDefenceOdds.ChooseAnswer(request, PossibleElements.Seen(attacker.Pool.Ledger),
                defender.Pool.Hand, m_Matchups, m_Dice.Roll);

        /// <summary>
        /// Spends what the defender put up, reveals both sides, and applies what is left of the
        /// attack.
        ///
        /// The defender's spend happens here rather than when they chose, because DE-005 wants each
        /// side's expenditure emitted after that side's own reveal -- and because an answer refused
        /// by the sequence must not have cost anything.
        /// </summary>
        void SettleClash(IReadOnlyList<Element> answer, bool declined)
        {
            var clash = m_Clash;
            var attacker = m_Attacker;
            var defender = m_Defender;
            var skill = m_ClashSkill;
            var flanked = m_Flanked;

            // Cleared before anything else can run: applying the effect can kill a creature, which
            // ends the match, and a clash still standing at that point would suspend the next one.
            m_Clash = null;
            m_Attacker = null;
            m_Defender = null;
            m_ClashSkill = null;
            m_Flanked = false;

            if (clash == null || attacker == null || defender == null || skill == null)
            {
                return;
            }

            if (declined)
            {
                clash.Decline();
            }

            if (!clash.TryReveal(out var reveal))
            {
                return;
            }

            // In order, and only now. DE-005 asks for each side's expenditure after that side's own
            // reveal, which is what these two calls are -- until this point neither pool has said a
            // word about what left it.
            attacker.Pool.AnnounceCommitted();
            defender.Pool.AnnounceCommitted(keep: ClashRules.Refunds(reveal.Outcome));

            attacker.RecordUse(skill.Id);

            Say(CombatEvent.ClashResolvedAs(0, attacker.Id, defender.Id, skill.Id,
                reveal.Attacker, reveal.Defender, reveal.Outcome));

            LandContested(attacker, skill, defender, clash.Scale(skill.Effect));

            // Caught from behind, a creature turns to face whoever did it. Flanking is worth one
            // attack, not a standing arrangement: without this, one creature could walk round a
            // back and swing from the same tile every turn. After the effect, so the blow that
            // landed is the one the position bought, and only if there is still somebody to turn.
            if (flanked && defender.IsAlive && attacker.IsAlive)
            {
                var turned = ThreatGeometry.Bearing(m_Grid, defender.Cell, attacker.Cell);
                defender.Face(turned);
                Say(CombatEvent.FacedToward(0, defender.Id, turned.Index));
            }

            // The attack is over, so whatever the pause was holding up can go on.
            m_Listener.CloseDefence(defender.Id);
            ContinueAfterClash();
        }
    }
}

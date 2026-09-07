using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;
using UnityEngine;
using Dragoneye.Game;
using Dragoneye.Game.Creatures;
using Dragoneye.Hex;
using Dragoneye.Hex.Systems;

namespace Dragoneye.Game.Combat
{
    /// <summary>
    /// Runs one clash from the swing to the reveal: asks the defender, takes the answer, spends
    /// what both sides put up, and hands the host whatever is left of the attack.
    ///
    /// What it holds is a <see cref="ClashSequence"/>, which is where every decision about the
    /// clash is made. This only carries messages to it and applies what it says -- and it is a
    /// plain class rather than a component, because a clash has no lifecycle of its own beyond
    /// the fight it is in.
    ///
    /// Server only. At most one clash is open at a time: the fight is suspended while it is.
    /// </summary>
    public sealed class ClashConductor
    {
        readonly IClashHost m_Host;
        readonly Dice m_Dice;
        readonly ArenaMap m_Map;

        // The attack that is waiting on an answer, and everything needed to finish it.
        ClashSequence m_Clash;
        CreatureState m_Attacker;
        CreatureState m_Defender;
        SkillSpec m_Skill;
        bool m_Flanked;

        public ClashConductor(IClashHost host, Dice dice, ArenaMap map)
        {
            m_Host = host;
            m_Dice = dice;
            m_Map = map;
        }

        /// <summary>Whether an attack is waiting on somebody's answer.</summary>
        public bool IsPending => m_Clash != null;

        /// <summary>Who is being asked, while somebody is.</summary>
        public CreatureState Defender => m_Defender;

        /// <summary>
        /// Suspends the attack and asks the defender.
        ///
        /// The attacker's element has already left their pool by now -- DE-005 spends the
        /// commitment before anybody is asked anything, so an attack cannot be taken back once the
        /// defender has been made to think about it.
        /// </summary>
        public void Begin(CreatureState actor, SkillSpec skill, CreatureState target,
            Element? telegraphed = null)
        {
            var pool = target.Pool;

            if (pool == null)
            {
                m_Host.LandUncontested(actor, skill, target);
                return;
            }

            // Which way the blow arrived, from the defender's point of view.
            var flanked = FacingRules.IsFlank(target.Facing,
                ThreatGeometry.Bearing(m_Map.Grid, target.Cell, actor.Cell));

            var committed = new List<Element>();

            for (var i = 0; i < skill.ElementCost; i++)
            {
                committed.Add(skill.Element);
            }

            m_Clash = ClashSequence.Begin(committed,
                new ClashSide((int)actor.TurnId, advantage: actor.HasAdvantage),
                new ClashSide((int)target.TurnId, advantage: target.HasAdvantage,
                    disadvantage: flanked),
                pool.ServerLedger, ElementMatchups.Table, telegraphed);

            m_Attacker = actor;
            m_Defender = target;
            m_Skill = skill;
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
        void Ask(DefenceRequest request, CreatureState defender)
        {
            if (!request.HasAnswer)
            {
                // Nothing to answer with. DE-005: the attack resolves unopposed rather than
                // stopping to ask a question with no answers on it.
                Settle(null, declined: true);
                return;
            }

            if (defender.IsComputerControlled)
            {
                // Through the same door a player's answer comes in by, so both halves of the job
                // -- committing to the sequence and settling -- happen for both kinds of defender.
                Answer(defender, ChooseDefence(defender, m_Attacker, request), out _);
                return;
            }

            if (ClashCommands.Current != null)
            {
                ClashCommands.Current.ServerAsk(request, defender);
                return;
            }

            Debug.LogWarning("No clash commands in the arena; the attack resolves unopposed.");
            Settle(null, declined: true);
        }

        /// <summary>
        /// The defender's answer, arriving from wherever they are.
        ///
        /// Checked against the sequence rather than trusted: an answer naming elements the defender
        /// does not hold, or more than were asked for, is refused there and the clash stays open.
        /// </summary>
        public bool Answer(CreatureState defender, IReadOnlyList<Element> answer,
            out DefenceRefusal refusal)
        {
            refusal = DefenceRefusal.None;

            if (m_Clash == null || defender != m_Defender)
            {
                refusal = DefenceRefusal.AlreadyResolved;
                return false;
            }

            if (answer == null || answer.Count == 0)
            {
                Settle(null, declined: true);
                return true;
            }

            var pool = defender.Pool;

            // Committed before anything is spent, so an answer the sequence refuses costs nothing
            // and the clash stays open for a better one.
            if (pool == null || !m_Clash.TryCommit(answer, pool.ServerLedger, out refusal))
            {
                return false;
            }

            foreach (var element in answer)
            {
                // Committed, not spent, for the same reason the attacker's was: both are announced
                // together once neither can be used to work out the other.
                pool.ServerCommit(element, 1, out _);
            }

            Settle(answer, declined: false);
            return true;
        }

        /// <summary>The defender is gone. The attack resolves unopposed.</summary>
        public void Abandon() => Settle(null, declined: true);

        /// <summary>
        /// What a computer creature puts up.
        ///
        /// Weighed against exactly what a player is shown -- what the attacker has been proven to
        /// hold -- and then rolled for. A defender that always answered optimally is a defender who
        /// can be hard-countered every time once somebody has learned the table, and a fight whose
        /// right answer never changes has one turn in it.
        ///
        /// The randomness lives here rather than in the rules, because the rules have to be able to
        /// give the same answer twice and this deliberately does not.
        /// </summary>
        IReadOnlyList<Element> ChooseDefence(CreatureState defender, CreatureState attacker,
            DefenceRequest request)
        {
            var answer = new List<Element>();
            var options = new List<Element>(request.Options);
            var attack = CreatureKnowledge.PossibleAttacks(attacker);

            var pool = defender.Pool;
            var held = pool != null ? pool.ServerLedger.Pool : ElementCounts.Empty;

            while (answer.Count < request.Required && options.Count > 0)
            {
                if (!ClashDefenceOdds.TryChoose(options, attack, ElementMatchups.Table,
                        m_Dice.Roll(), out var pick))
                {
                    break;
                }

                answer.Add(pick);

                // Only offered again if another one is actually held.
                var taken = 0;

                foreach (var chosen in answer)
                {
                    if (chosen == pick)
                    {
                        taken++;
                    }
                }

                if (held[pick] <= taken)
                {
                    options.Remove(pick);
                }
            }

            return answer;
        }

        /// <summary>
        /// Spends what the defender put up, reveals both sides, and applies what is left of the
        /// attack.
        ///
        /// The defender's spend happens here rather than when they chose, because DE-005 wants each
        /// side's expenditure emitted after that side's own reveal -- and because an answer refused
        /// by the sequence must not have cost anything.
        /// </summary>
        void Settle(IReadOnlyList<Element> answer, bool declined)
        {
            var clash = m_Clash;
            var attacker = m_Attacker;
            var defender = m_Defender;
            var skill = m_Skill;
            var flanked = m_Flanked;

            // Cleared before anything else can run: applying the effect can kill a creature, which
            // ends the match, and a clash still standing at that point would suspend the next one.
            m_Clash = null;
            m_Attacker = null;
            m_Defender = null;
            m_Skill = null;
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
            attacker.Pool?.ServerAnnounceCommitted();
            defender.Pool?
                .ServerAnnounceCommitted(keep: ClashRules.Refunds(reveal.Outcome));

            attacker.SkillCommands?.ServerRecordUse(skill.Id);

            ClashCommands.Current?.ServerAnnounce(attacker.TurnId, defender.TurnId, skill.Id,
                reveal.Attacker, reveal.Defender, reveal.Outcome);

            m_Host.LandContested(attacker, skill, defender, clash.Scale(skill.Effect));

            // Caught from behind, a creature turns to face whoever did it. Flanking is worth one
            // attack, not a standing arrangement: without this, one creature could walk round a
            // back and swing from the same tile every turn. After the effect, so the blow that
            // landed is the one the position bought, and only if there is still somebody to turn.
            if (flanked && defender.IsAlive && attacker.IsAlive)
            {
                defender.ServerFace(ThreatGeometry.Bearing(m_Map.Grid, defender.Cell, attacker.Cell));
            }

            // The attack is over, so whatever the pause was holding up can go on.
            ClashCommands.Current?.ServerClearPrompt();
            m_Host.ClashSettled();
        }
    }
}

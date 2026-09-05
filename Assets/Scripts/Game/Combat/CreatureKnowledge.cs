using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Data;

namespace Dragoneye.Game
{
    /// <summary>
    /// What anybody watching can work out about a creature's hand.
    ///
    /// One implementation, because three things ask and they must agree: the label under an
    /// attacker's cursor, the numbers on a defender's prompt, and the computer deciding what to put
    /// up. A player shown one set of odds and then beaten by a creature working from another has
    /// been lied to, however slightly.
    ///
    /// Everything it reads is public. The proven counts, what is currently spent and which skills
    /// have been seen are all replicated to everyone by design, so this runs identically on any
    /// peer and never touches the one thing that is private -- which is why it can be called from a
    /// client at all.
    /// </summary>
    public static class CreatureKnowledge
    {
        /// <summary>
        /// What this creature might answer an attack with.
        ///
        /// Anything at all, as far as an observer knows: a defence is not restricted by what skills
        /// the creature has, so every element it could be holding is a candidate.
        /// </summary>
        public static PossibleElements PossibleAnswers(CreatureState creature)
        {
            var pool = creature != null ? creature.GetComponent<CreaturePool>() : null;

            return pool != null ? PossibleElements.Seen(pool.Ledger) : PossibleElements.None;
        }

        /// <summary>
        /// What this creature might attack with.
        ///
        /// The same hand it would defend with. An attack does cost the element its skill is made
        /// of, so it was tempting to narrow this to the skills the creature has been watched using
        /// -- but the skills you have watched are a lower bound on the ones somebody has and never
        /// an upper one, and treating them as an upper bound piled every unidentified element onto
        /// whichever one a creature happened to have thrown first. That produced ninety and hundred
        /// per cent answers about a creature nobody knew anything about.
        ///
        /// <see cref="RevealedAttackElements"/> is still worth having -- it is what a creature has
        /// demonstrably got -- but it belongs on a panel showing what somebody can do, not in a
        /// guess about what they are about to do.
        /// </summary>
        public static PossibleElements PossibleAttacks(CreatureState creature)
        {
            var pool = creature != null ? creature.GetComponent<CreaturePool>() : null;

            return pool != null ? PossibleElements.Seen(pool.Ledger) : PossibleElements.None;
        }

        /// <summary>
        /// The elements of every contested skill this creature has been watched using.
        ///
        /// Only contested ones: a skill it used on itself tells you what it is holding, but it is
        /// not something that can arrive as an attack, and counting it would widen the guess with
        /// an element the creature can never throw at you.
        /// </summary>
        public static List<Element> RevealedAttackElements(CreatureState creature)
        {
            var elements = new List<Element>();
            var commands = creature != null ? creature.GetComponent<SkillCommands>() : null;
            var catalog = SkillCatalog.Current;

            if (commands == null || catalog == null)
            {
                return elements;
            }

            foreach (var id in commands.SeenSkillIds)
            {
                if (catalog.TryGetSkill(id, out var skill)
                    && skill.IsContested
                    && skill.ElementCost > 0
                    && !elements.Contains(skill.Element))
                {
                    elements.Add(skill.Element);
                }
            }

            return elements;
        }

        /// <summary>How an attack with this element is expected to go against this defender.</summary>
        public static ClashOdds Forecast(Element attacking, CreatureState defender) =>
            ClashForecast.Attacking(attacking, PossibleAnswers(defender), ElementMatchups.Table);

        /// <summary>How answering with this element is expected to go against this attacker.</summary>
        public static ClashOdds ForecastDefence(Element answering, CreatureState attacker) =>
            ClashForecast.Defending(answering, PossibleAttacks(attacker), ElementMatchups.Table);
    }
}

using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>
    /// A persistent effect an item grants its holder.
    ///
    /// Hand-assigned and permanent: passives are read by rules that will be written later, and a
    /// renumbering would silently change what a shield does.
    ///
    /// Deliberately a small closed set rather than an open string tag. A passive only means
    /// something if some rule looks for it, so a value with no rule behind it would be a promise the
    /// game does not keep -- and the compiler catching a typo is worth more than the freedom to
    /// invent one in the inspector.
    /// </summary>
    public enum Passive
    {
        /// <summary>
        /// Advantage when defending.
        ///
        /// What a shield grants. DE-006 will read it when a clash resolves; until then it is
        /// authored, resolved and queryable, which is what DE-003 asks for -- the shield is not
        /// special-cased inside the clash later, because the clash asks the loadout a question it
        /// can already answer.
        /// </summary>
        DefendAdvantage = 1
    }

    /// <summary>
    /// The passives a creature holds.
    ///
    /// A set rather than a list: holding two shields does not defend twice, and every rule that
    /// reads a passive asks whether it is present rather than how many there are.
    /// </summary>
    public sealed class PassiveSet
    {
        public static readonly PassiveSet Empty = new PassiveSet(null);

        readonly HashSet<Passive> m_Passives;

        public PassiveSet(IEnumerable<Passive> passives)
        {
            m_Passives = new HashSet<Passive>();

            if (passives == null)
            {
                return;
            }

            foreach (var passive in passives)
            {
                m_Passives.Add(passive);
            }
        }

        /// <summary>
        /// Whether the holder has this passive.
        ///
        /// The whole interface the clash needs. A rule asks the question; it never learns which item
        /// answered it, which is what stops "the shield" being named anywhere in the resolution.
        /// </summary>
        public bool Has(Passive passive) => m_Passives.Contains(passive);

        public int Count => m_Passives.Count;
    }
}

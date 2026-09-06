namespace Dragoneye.Combat
{
    /// <summary>
    /// The swing a creature gets at somebody who walks out from under its nose.
    ///
    /// It exists to stop movement being free. Without it, walking round behind somebody costs
    /// nothing but action points: the whole board can rotate through each other's backs every turn,
    /// and position stops being a thing you hold and becomes a thing you take. With it, crossing an
    /// enemy's front is a decision with a price attached, and skills that avoid the price -- a step
    /// that does not provoke, a shove, a charge -- have something to be better than.
    ///
    /// Not a skill anybody owns, which is why it is here rather than in the catalog. Any creature
    /// with an element left can take one, and it is made of whichever element they pick, so it is
    /// assembled at the moment it is used.
    ///
    /// The numbers are the tuning. Damage sits below the cheapest authored attack on purpose: an
    /// opportunity attack is a reaction that costs no action points, and one that hit as hard as a
    /// real swing would make standing still the only sensible move.
    /// </summary>
    public static class Opportunity
    {
        /// <summary>
        /// The id this attack is announced under.
        ///
        /// Negative, so it can never collide with an authored skill -- catalog ids are assigned by
        /// hand and start at zero. A client that cannot resolve it in the catalog is not looking at
        /// a missing skill; it is looking at this one, and names it accordingly.
        /// </summary>
        public const int SkillId = -1000;

        public const string Name = "Opportunity Attack";

        /// <summary>What it costs the creature taking it. One element, and no action points.</summary>
        public const int ElementCost = 1;

        /// <summary>
        /// What it does when it gets through.
        ///
        /// Below Jab, the cheapest authored attack, because this is free in action points and
        /// arrives on somebody else's turn.
        /// </summary>
        public const int Damage = 3;

        /// <summary>Adjacent only. It is a swing, not a shot.</summary>
        public const int Range = 1;

        /// <summary>
        /// The attack itself, made of whichever element the reacting creature put up.
        ///
        /// A real <see cref="SkillSpec"/> rather than a special case threaded through the resolver:
        /// it is contested, it costs an element, it does damage, and every rule that already knows
        /// what to do with those applies to it unchanged.
        /// </summary>
        public static SkillSpec For(Element element) =>
            new SkillSpec(SkillId, Name, element, Ap.Zero, ElementCost, Range,
                SkillTarget.Creature, new SkillEffect(SkillEffectKind.Damage, Damage),
                "A swing at somebody walking out from under your nose.");
    }
}

using System.Collections.Generic;

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
    /// **It is a creature's own weapon attack, not a choice of element.** Letting the swinger pick
    /// any element they held made a reaction strictly better than an action: they got to answer the
    /// matchup after seeing who was in front of them, for no action points, which is a better deal
    /// than their actual turn offers. It also made the offer a puzzle -- seven buttons and three
    /// numbers each -- at the one moment the game has interrupted somebody else to ask a question.
    /// One attack, yes or no, is the decision that was actually interesting.
    ///
    /// What it keeps from the weapon is what the weapon is: its element, what that costs, and what
    /// it does. What it drops is the action points, because a reaction is not a turn.
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

        /// <summary>Adjacent only. It is a swing, not a shot.</summary>
        public const int Range = 1;

        /// <summary>
        /// How often a computer creature keeps an element it could have swung with.
        ///
        /// A plain roll and nothing cleverer. An earlier cut also declined when the element was the
        /// last of its kind and the odds were poor -- and a level-one premade holds exactly one
        /// element, so it declined nearly every swing the board had just warned about. A warning
        /// that is usually wrong is worse than none, and the reason it was wrong was invisible.
        /// </summary>
        public const float HoldsBack = 0.15f;

        /// <summary>Whether a computer creature offered a swing takes it, on this roll in [0, 1).</summary>
        public static bool Takes(float roll) => roll > HoldsBack;

        /// <summary>
        /// The swing itself, made from the attack this creature already carries.
        ///
        /// A real <see cref="SkillSpec"/> rather than a special case threaded through the resolver:
        /// it is contested, it costs an element, it does damage, and every rule that already knows
        /// what to do with those applies to it unchanged.
        ///
        /// Reach is cut to one whatever the weapon is. A bow does not get to shoot somebody walking
        /// past four tiles away -- the whole rule is about who is close enough to interfere.
        /// </summary>
        public static SkillSpec From(SkillSpec weapon) =>
            weapon == null
                ? null
                : new SkillSpec(SkillId, Name, weapon.Element, Ap.Zero, weapon.ElementCost,
                    Range, SkillTarget.Creature, weapon.Effect,
                    $"{weapon.Name}, swung at somebody walking out from under your nose.");

        /// <summary>
        /// Which of a creature's skills its swing is made from: the first attack its weapon grants.
        ///
        /// The weapon and not simply the first attack on the list. Skills resolve species, then
        /// class, then equipment -- so a Priest or an Apostate has a contested attack from their
        /// class sitting in front of the one they are actually holding, and taking the first would
        /// have them swinging with something they are not carrying.
        ///
        /// Null when nothing in hand can be swung: a creature with no weapon, or one whose weapon
        /// grants nothing contested, simply does not get the reaction.
        /// </summary>
        public static SkillSpec PrimaryOf(Loadout loadout)
        {
            if (loadout == null)
            {
                return null;
            }

            // Empty-handed is a way of being armed, not a way of being harmless. Species resolve
            // before anything else, so the first attack on the list of somebody carrying nothing is
            // their unarmed strike -- which is exactly what they would swing with.
            if (!loadout.HasWeapon)
            {
                return FirstAttack(loadout.Skills, null);
            }

            foreach (var item in loadout.Items)
            {
                if (item == null || item.Slot != EquipmentSlot.Weapon)
                {
                    continue;
                }

                var found = FirstAttack(loadout.Skills, item.SkillIds);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// The same question for a creature with no equipment to ask about.
        ///
        /// A premade carries an authored list and nothing that granted it, so the best available
        /// reading of "what it is holding" is the first attack it has. Its list is authored in the
        /// order somebody meant, which is what makes the first one the primary one.
        /// </summary>
        public static SkillSpec PrimaryOf(IReadOnlyList<SkillSpec> skills) =>
            FirstAttack(skills, null);

        /// <summary>
        /// The first skill that is an attack somebody could answer, optionally restricted to a set.
        ///
        /// It has to cost an element: a swing with nothing committed to it resolves as an
        /// unanswerable hit, which would make the reaction free damage rather than a contest.
        /// </summary>
        static SkillSpec FirstAttack(IReadOnlyList<SkillSpec> skills, IReadOnlyList<int> only)
        {
            if (skills == null)
            {
                return null;
            }

            foreach (var skill in skills)
            {
                if (skill == null || !skill.IsContested || skill.ElementCost <= 0)
                {
                    continue;
                }

                if (only == null || Contains(only, skill.Id))
                {
                    return skill;
                }
            }

            return null;
        }

        static bool Contains(IReadOnlyList<int> ids, int id)
        {
            foreach (var candidate in ids)
            {
                if (candidate == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>Why a build is not acceptable. Ordered roughly by how early it is noticed.</summary>
    public enum BuildProblem
    {
        NameMissing,
        NameTooLong,
        SpeciesUnknown,
        ClassUnknown,
        AttributeBelowFloor,
        AttributeAboveCeiling,
        OverBudget,
        UnderBudget,
        PoolWrongSize,
        PoolElementUnknown,
        WeaponUnknown,
        WeaponNotForClass,
        ArmorUnknown,
        OffhandUnknown,
        ItemInWrongSlot
    }

    /// <summary>
    /// The result of checking a build: what is wrong, and enough detail to say so in a sentence.
    ///
    /// A list of problems rather than a boolean, because the creation screen has to tell the player
    /// which of four things to fix, and "invalid" is not an answer they can act on.
    /// </summary>
    public readonly struct BuildFault
    {
        public readonly BuildProblem Problem;

        /// <summary>The attribute at fault, when the problem is about one. Otherwise ignored.</summary>
        public readonly Attribute Attribute;

        /// <summary>A number the message needs -- the budget, the expected pool size, an item id.</summary>
        public readonly int Value;

        public BuildFault(BuildProblem problem, int value = 0,
            Attribute attribute = Attribute.Toughness)
        {
            Problem = problem;
            Value = value;
            Attribute = attribute;
        }
    }

    /// <summary>
    /// Decides whether a build is legal.
    ///
    /// Runs in two places on purpose: the creation screen calls it on every edit so the player is
    /// never offered a Save that will be refused, and the host calls it on every build that arrives
    /// from a client. One implementation means those two answers cannot disagree -- and the second
    /// call is the one that matters, because a client can send anything it likes.
    /// </summary>
    public static class BuildValidator
    {
        /// <summary>Fills <paramref name="faults"/> with everything wrong. Empty means acceptable.</summary>
        public static void Validate(CharacterBuild build, IContentIndex content, List<BuildFault> faults)
        {
            if (faults == null)
            {
                return;
            }

            faults.Clear();

            if (build == null || content == null)
            {
                faults.Add(new BuildFault(BuildProblem.ClassUnknown));
                return;
            }

            ValidateName(build, faults);

            if (!content.TryGetSpecies(build.SpeciesId, out _))
            {
                // The species contributes a baseline, so the attributes below cannot be judged
                // until it resolves.
                faults.Add(new BuildFault(BuildProblem.SpeciesUnknown, build.SpeciesId));
                return;
            }

            if (!content.TryGetClass(build.ClassId, out var classSpec))
            {
                // Everything below is measured against the class, so there is nothing further to
                // say until it resolves.
                faults.Add(new BuildFault(BuildProblem.ClassUnknown, build.ClassId));
                return;
            }

            ValidateAttributes(build, content.Rules, faults);
            ValidatePool(build, faults);
            ValidateEquipment(build, classSpec, content, faults);
        }

        /// <summary>Convenience for callers that only need a yes or no.</summary>
        public static bool IsValid(CharacterBuild build, IContentIndex content)
        {
            var faults = new List<BuildFault>();
            Validate(build, content, faults);
            return faults.Count == 0;
        }

        static void ValidateName(CharacterBuild build, List<BuildFault> faults)
        {
            var name = build.Name == null ? string.Empty : build.Name.Trim();

            if (name.Length == 0)
            {
                faults.Add(new BuildFault(BuildProblem.NameMissing));
            }
            else if (name.Length > CharacterBuild.MaxNameLength)
            {
                faults.Add(new BuildFault(BuildProblem.NameTooLong, CharacterBuild.MaxNameLength));
            }
        }

        static void ValidateAttributes(CharacterBuild build, CharacterRules rules,
            List<BuildFault> faults)
        {
            foreach (var attribute in AttributeInfo.All)
            {
                var value = build.Attributes[attribute];

                if (value < PointBuy.Floor)
                {
                    faults.Add(new BuildFault(BuildProblem.AttributeBelowFloor,
                        PointBuy.Floor, attribute));
                }
                else if (value > rules.MaxPerAttribute)
                {
                    faults.Add(new BuildFault(BuildProblem.AttributeAboveCeiling,
                        rules.MaxPerAttribute, attribute));
                }
            }

            var remaining = build.PointsRemaining(rules);

            if (remaining < 0)
            {
                faults.Add(new BuildFault(BuildProblem.OverBudget, -remaining));
            }
            else if (remaining > 0)
            {
                // Under budget is a fault, not a warning. Leaving points unspent is always a
                // mistake, and a half-finished character reaching a match is worse than being told.
                faults.Add(new BuildFault(BuildProblem.UnderBudget, remaining));
            }
        }

        /// <summary>
        /// The pool must cost exactly the character's level, in whatever spread the player chose.
        ///
        /// Only the total price is constrained, and the elements are not all the same price. Three
        /// Geo and a single Arcana both cost three, which is what makes the pool a depth-against-
        /// rarity decision rather than a count.
        /// </summary>
        static void ValidatePool(CharacterBuild build, List<BuildFault> faults)
        {
            var budget = build.PoolBudget();

            if (ElementPricing.CostOf(build.StartingPool) != budget)
            {
                faults.Add(new BuildFault(BuildProblem.PoolWrongSize, budget));
            }

            foreach (var element in ElementInfo.All)
            {
                if (build.StartingPool[element] < 0)
                {
                    faults.Add(new BuildFault(BuildProblem.PoolElementUnknown, (int)element));
                    break;
                }
            }
        }

        static void ValidateEquipment(CharacterBuild build, ClassSpec classSpec,
            IContentIndex content, List<BuildFault> faults)
        {
            if (build.WeaponId != CharacterBuild.NoEquipment)
            {
                if (!content.TryGetEquipment(build.WeaponId, out var weapon))
                {
                    faults.Add(new BuildFault(BuildProblem.WeaponUnknown, build.WeaponId));
                }
                else if (weapon.Slot != EquipmentSlot.Weapon)
                {
                    faults.Add(new BuildFault(BuildProblem.ItemInWrongSlot, build.WeaponId));
                }
                else if (!classSpec.AllowsWeapon(build.WeaponId))
                {
                    faults.Add(new BuildFault(BuildProblem.WeaponNotForClass, build.WeaponId));
                }
            }

            // Going without armour or an offhand is a real choice, not an omission.
            CheckSlot(build.ArmorId, EquipmentSlot.Armor, BuildProblem.ArmorUnknown, content, faults);
            CheckSlot(build.OffhandId, EquipmentSlot.Offhand, BuildProblem.OffhandUnknown,
                content, faults);
        }

        static void CheckSlot(int id, EquipmentSlot slot, BuildProblem unknown,
            IContentIndex content, List<BuildFault> faults)
        {
            if (id == CharacterBuild.NoEquipment)
            {
                return;
            }

            if (!content.TryGetEquipment(id, out var spec))
            {
                faults.Add(new BuildFault(unknown, id));
            }
            else if (spec.Slot != slot)
            {
                faults.Add(new BuildFault(BuildProblem.ItemInWrongSlot, id));
            }
        }
    }
}

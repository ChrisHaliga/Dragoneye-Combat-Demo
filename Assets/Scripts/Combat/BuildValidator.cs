using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>Why a build is not acceptable. Ordered roughly by how early it is noticed.</summary>
    public enum BuildProblem
    {
        NameMissing,
        NameTooLong,
        ClassUnknown,
        StatBelowMinimum,
        StatAboveMaximum,
        OverBudget,
        UnderBudget,
        PoolWrongSize,
        PoolElementUnknown,
        WeaponUnknown,
        WeaponNotForClass,
        ArmorUnknown,
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

        /// <summary>The stat at fault, when the problem is about one. Otherwise ignored.</summary>
        public readonly StatKind Stat;

        /// <summary>A number the message needs -- the budget, the expected pool size, an item id.</summary>
        public readonly int Value;

        public BuildFault(BuildProblem problem, int value = 0, StatKind stat = StatKind.Vitality)
        {
            Problem = problem;
            Value = value;
            Stat = stat;
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

            if (!content.TryGetClass(build.ClassId, out var classSpec))
            {
                // Everything below is measured against the class, so there is nothing further to
                // say until it resolves.
                faults.Add(new BuildFault(BuildProblem.ClassUnknown, build.ClassId));
                return;
            }

            ValidateAllocation(build, content.Rules, faults);
            ValidatePool(build, content.Rules, faults);
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

        static void ValidateAllocation(CharacterBuild build, CharacterRules rules,
            List<BuildFault> faults)
        {
            foreach (var stat in StatInfo.All)
            {
                var value = build.Allocation[stat];

                if (value < rules.MinPerStat)
                {
                    faults.Add(new BuildFault(BuildProblem.StatBelowMinimum, rules.MinPerStat, stat));
                }
                else if (value > rules.MaxPerStat)
                {
                    faults.Add(new BuildFault(BuildProblem.StatAboveMaximum, rules.MaxPerStat, stat));
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

        static void ValidatePool(CharacterBuild build, CharacterRules rules, List<BuildFault> faults)
        {
            if (build.ElementPicks.Count != rules.Level)
            {
                faults.Add(new BuildFault(BuildProblem.PoolWrongSize, rules.Level));
            }

            foreach (var element in build.ElementPicks)
            {
                if (!ElementInfo.IsDefined(element))
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

            if (build.ArmorId == CharacterBuild.NoEquipment)
            {
                // Going without armour is a real choice, not an omission.
                return;
            }

            if (!content.TryGetEquipment(build.ArmorId, out var armor))
            {
                faults.Add(new BuildFault(BuildProblem.ArmorUnknown, build.ArmorId));
            }
            else if (armor.Slot != EquipmentSlot.Armor)
            {
                faults.Add(new BuildFault(BuildProblem.ItemInWrongSlot, build.ArmorId));
            }
        }
    }
}

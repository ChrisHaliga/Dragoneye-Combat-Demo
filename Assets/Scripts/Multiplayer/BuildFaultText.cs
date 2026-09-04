using System.Collections.Generic;
using Dragoneye.Combat;

namespace Dragoneye.Multiplayer
{
    /// <summary>
    /// What a build problem sounds like to a player.
    ///
    /// Outside <see cref="BuildValidator"/> for the same reason <c>ActionLabels</c> sits outside
    /// <c>ActionResolver</c>: the validator runs on the host, and the host has no business holding
    /// English. One place for the wording all the same, so a new <see cref="BuildProblem"/> cannot
    /// be added without deciding what the player is told.
    ///
    /// Every message says what to do, not what is wrong. "Spend 3 more points" is actionable;
    /// "under budget" is a status.
    /// </summary>
    public static class BuildFaultText
    {
        public static string Describe(BuildFault fault)
        {
            switch (fault.Problem)
            {
                case BuildProblem.NameMissing:
                    return "Give your character a name.";
                case BuildProblem.NameTooLong:
                    return $"Names are at most {fault.Value} characters.";
                case BuildProblem.ClassUnknown:
                    return "Pick a class.";
                case BuildProblem.StatBelowMinimum:
                    return $"{StatInfo.NameOf(fault.Stat)} cannot go below {fault.Value}.";
                case BuildProblem.StatAboveMaximum:
                    return $"{StatInfo.NameOf(fault.Stat)} cannot go above {fault.Value}.";
                case BuildProblem.OverBudget:
                    return $"Remove {Points(fault.Value)}.";
                case BuildProblem.UnderBudget:
                    return $"Spend {Points(fault.Value)}.";
                case BuildProblem.PoolWrongSize:
                    return $"Choose {fault.Value} element{(fault.Value == 1 ? "" : "s")} "
                        + "for your starting pool.";
                case BuildProblem.PoolElementUnknown:
                    return "One of your pool choices is no longer a real element.";
                case BuildProblem.WeaponUnknown:
                    return "That weapon no longer exists.";
                case BuildProblem.WeaponNotForClass:
                    return "Your class cannot carry that weapon.";
                case BuildProblem.ArmorUnknown:
                    return "That armour no longer exists.";
                case BuildProblem.OffhandUnknown:
                    return "That offhand item no longer exists.";
                case BuildProblem.ItemInWrongSlot:
                    return "That item does not fit the slot it is in.";
                default:
                    return "Something about this character is not allowed.";
            }
        }

        /// <summary>
        /// The single most useful thing to say, or empty when the build is fine.
        ///
        /// One sentence rather than a list: a player fixing four things fixes them one at a time,
        /// and a wall of red under a form nobody has finished filling in reads as failure rather
        /// than guidance.
        /// </summary>
        public static string Summarise(IReadOnlyList<BuildFault> faults)
        {
            if (faults == null || faults.Count == 0)
            {
                return string.Empty;
            }

            var text = Describe(faults[0]);

            return faults.Count == 1 ? text : $"{text}  (+{faults.Count - 1} more)";
        }

        static string Points(int count) => count == 1 ? "1 point" : $"{count} points";
    }
}

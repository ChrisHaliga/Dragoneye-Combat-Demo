using Dragoneye.Combat;

namespace Dragoneye.Game
{
    /// <summary>
    /// Why a skill is unavailable, in words.
    ///
    /// Outside <see cref="SkillRules"/>, and in a different assembly, for the same reason
    /// <see cref="ActionLabels"/> sits outside <c>ActionResolver</c>: the host runs those rules and
    /// has no business holding English. One place for the wording all the same, so a new
    /// <see cref="SkillRefusal"/> cannot be added without deciding what the player is told.
    /// </summary>
    public static class SkillLabels
    {
        public static string Describe(SkillRefusal refusal)
        {
            switch (refusal)
            {
                case SkillRefusal.None:
                    return string.Empty;
                case SkillRefusal.NoSkill:
                    return "That skill no longer exists.";
                case SkillRefusal.NotYourTurn:
                    return "Not your turn.";
                case SkillRefusal.NotEnoughAp:
                    return "Not enough AP.";
                case SkillRefusal.NotEnoughElement:
                    return "Your pool is out of that element.";
                case SkillRefusal.NoTarget:
                    return "Nothing there.";
                case SkillRefusal.WrongTargetKind:
                    return "Cannot be used on that.";
                case SkillRefusal.OutOfRange:
                    return "Out of range.";
                case SkillRefusal.TargetIsSelf:
                    return "Cannot be used on yourself.";
                case SkillRefusal.TargetIsAlly:
                    return "That is an ally.";
                case SkillRefusal.TargetIsDead:
                    return "That creature is already dead.";
                default:
                    return "Unavailable.";
            }
        }
    }
}

namespace Dragoneye.Combat
{
    /// <summary>
    /// Why a skill cannot be used. <see cref="None"/> means it can.
    ///
    /// A reason rather than a boolean, because DE-002 asks the skill bar to say why something is
    /// unavailable -- and because "greyed out with no explanation" is the single most common way a
    /// tactics game loses a player mid-turn.
    /// </summary>
    public enum SkillRefusal
    {
        None,
        NoSkill,
        NotYourTurn,
        NotEnoughAp,
        NotEnoughElement,

        /// <summary>Nothing has been spent, so there is nothing to take back.</summary>
        NothingToReturn,

        NoTarget,
        WrongTargetKind,
        OutOfRange,
        TargetIsSelf,
        TargetIsAlly,
        TargetIsDead
    }

    /// <summary>
    /// What the caller knows about the thing being aimed at.
    ///
    /// Flat, so legality can be decided without a scene. Distance is a tile count and comes from the
    /// grid seam; whether the target is actually reachable through cover is DE-007's question and
    /// deliberately not asked here.
    /// </summary>
    public readonly struct SkillTargetInfo
    {
        public static readonly SkillTargetInfo None = default;

        public readonly bool Exists;
        public readonly bool IsCreature;
        public readonly bool IsSelf;
        public readonly bool IsAlly;
        public readonly bool IsAlive;
        public readonly int Distance;

        public SkillTargetInfo(bool exists, bool isCreature, bool isSelf, bool isAlly,
            bool isAlive, int distance)
        {
            Exists = exists;
            IsCreature = isCreature;
            IsSelf = isSelf;
            IsAlly = isAlly;
            IsAlive = isAlive;
            Distance = distance;
        }

        /// <summary>A creature at a known distance.</summary>
        public static SkillTargetInfo Creature(int distance, bool isSelf, bool isAlly,
            bool isAlive = true) =>
            new SkillTargetInfo(true, true, isSelf, isAlly, isAlive, distance);

        /// <summary>An empty place on the board.</summary>
        public static SkillTargetInfo Tile(int distance) =>
            new SkillTargetInfo(true, false, false, false, false, distance);
    }

    /// <summary>
    /// Whether a skill may be used, and why not.
    ///
    /// The one answer to that question. DE-002 asks the skill bar and the target highlighting to be
    /// driven off the same check that accepts the action, so a skill can never be offered and then
    /// refused -- which is the same discipline <see cref="ActionResolver"/> applies to moving.
    ///
    /// Costs are checked before targets. A skill the creature cannot pay for is unusable no matter
    /// what it is aimed at, and reporting "out of range" for something it could not afford anyway
    /// sends the player to fix the wrong thing.
    /// </summary>
    public static class SkillRules
    {
        /// <summary>
        /// Whether the creature could use this skill at all this turn, ignoring any target.
        ///
        /// Drives whether the skill bar shows a button as usable before anything is hovered.
        /// </summary>
        public static SkillRefusal CheckAffordable(SkillSpec skill, bool isActorsTurn,
            Ap currentAp, ElementLedger ledger)
        {
            if (skill == null)
            {
                return SkillRefusal.NoSkill;
            }

            if (!isActorsTurn)
            {
                return SkillRefusal.NotYourTurn;
            }

            if (currentAp < skill.ApCost)
            {
                return SkillRefusal.NotEnoughAp;
            }

            // Element cost of zero is legal and means the skill draws on nothing.
            if (skill.ElementCost > 0 && !ledger.CanSpend(skill.Element, skill.ElementCost))
            {
                return SkillRefusal.NotEnoughElement;
            }

            // Taking an element back is worth nothing when none has been spent, and charging AP for
            // a no-op is the sort of thing a player only notices after it has cost them a turn.
            if (skill.Effect.Kind == SkillEffectKind.ReturnElement && !ledger.CanReturn)
            {
                return SkillRefusal.NothingToReturn;
            }

            return SkillRefusal.None;
        }

        /// <summary>
        /// The full check: affordable, and aimed at something it can legally be aimed at.
        /// </summary>
        public static SkillRefusal Check(SkillSpec skill, bool isActorsTurn, Ap currentAp,
            ElementLedger ledger, SkillTargetInfo target)
        {
            var affordable = CheckAffordable(skill, isActorsTurn, currentAp, ledger);

            return affordable != SkillRefusal.None ? affordable : CheckTarget(skill, target);
        }

        static SkillRefusal CheckTarget(SkillSpec skill, SkillTargetInfo target)
        {
            if (skill.Target == SkillTarget.Self)
            {
                // The user is always present and always in range of themselves, so a self-directed
                // skill has nothing left to check.
                return SkillRefusal.None;
            }

            if (!target.Exists)
            {
                return SkillRefusal.NoTarget;
            }

            if (target.Distance > skill.Range)
            {
                return SkillRefusal.OutOfRange;
            }

            if (skill.Target == SkillTarget.Tile)
            {
                return target.IsCreature ? SkillRefusal.WrongTargetKind : SkillRefusal.None;
            }

            if (!target.IsCreature)
            {
                return SkillRefusal.WrongTargetKind;
            }

            if (target.IsSelf)
            {
                // Aiming a contested skill at yourself would be a clash against yourself.
                return SkillRefusal.TargetIsSelf;
            }

            if (!target.IsAlive)
            {
                return SkillRefusal.TargetIsDead;
            }

            return target.IsAlly ? SkillRefusal.TargetIsAlly : SkillRefusal.None;
        }

        /// <summary>
        /// Applies a skill's effect to a value, returning the new one.
        ///
        /// Pure arithmetic with no creature involved, so the rule can be checked directly. What the
        /// value is depends on the effect: health for damage and healing, AP units for restoration.
        /// </summary>
        public static int Apply(SkillEffect effect, int current, int maximum)
        {
            switch (effect.Kind)
            {
                case SkillEffectKind.Damage:
                    return CombatRules.Damaged(current, effect.Amount);

                case SkillEffectKind.Heal:
                case SkillEffectKind.RestoreAp:
                    var raised = current + effect.Amount;
                    return raised > maximum ? maximum : raised;

                default:
                    return current;
            }
        }
    }
}

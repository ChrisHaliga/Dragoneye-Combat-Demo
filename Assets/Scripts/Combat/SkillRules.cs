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
        TargetIsDead,

        /// <summary>Something that cannot be seen through stands between.</summary>
        NoLine
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

        /// <summary>Whether nothing opaque stands between. The board answers; the rules only ask.</summary>
        public readonly bool HasLine;

        public SkillTargetInfo(bool exists, bool isCreature, bool isSelf, bool isAlly,
            bool isAlive, int distance, bool hasLine = true)
        {
            Exists = exists;
            IsCreature = isCreature;
            IsSelf = isSelf;
            IsAlly = isAlly;
            IsAlive = isAlive;
            Distance = distance;
            HasLine = hasLine;
        }

        /// <summary>A creature at a known distance.</summary>
        public static SkillTargetInfo Creature(int distance, bool isSelf, bool isAlly,
            bool isAlive = true, bool hasLine = true) =>
            new SkillTargetInfo(true, true, isSelf, isAlly, isAlive, distance, hasLine);

        /// <summary>An empty place on the board.</summary>
        public static SkillTargetInfo Tile(int distance, bool hasLine = true) =>
            new SkillTargetInfo(true, false, false, false, false, distance, hasLine);
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
        /// <summary>Percent each point of Skill adds to a shot's chance to hit.</summary>
        public const int AccuracyPerSkill = 5;

        /// <summary>Percent each creature the shot passes over takes off its chance.</summary>
        public const int CoverPenalty = 20;

        /// <summary>A shot is never entirely hopeless: the chance never drops below this.</summary>
        public const int HitFloor = 5;

        /// <summary>
        /// Percent chance a shot lands at this distance, past this much cover.
        ///
        /// Accuracy at one tile, less the falloff for every tile past it, less the cover, floored
        /// so a long shot is still a shot and capped so nothing is surer than sure. A skill that
        /// does not roll is a hundred: swings land.
        /// </summary>
        public static int HitChance(SkillSpec skill, int distance, int cover = 0)
        {
            if (skill == null || !skill.RollsToHit)
            {
                return 100;
            }

            var beyond = distance - 1 < 0 ? 0 : distance - 1;
            var blocked = cover < 0 ? 0 : cover;
            var chance = skill.Aim.Accuracy - skill.Aim.Falloff * beyond - CoverPenalty * blocked;

            return chance < HitFloor ? HitFloor : chance > 100 ? 100 : chance;
        }

        /// <summary>Whether a roll in [0, 1) lands. Pure, so the roll can be handed in by a test.</summary>
        public static bool Hits(SkillSpec skill, int distance, int cover, float roll) =>
            roll * 100f < HitChance(skill, distance, cover);

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

            // Element cost of zero is legal and means the skill draws on nothing. A skill with
            // a choice is affordable when any one of its options is: a fist you can only afford to
            // throw as fire is still a fist you can throw.
            if (skill.ElementCost > 0 && !TryChooseElement(skill, ledger, out _))
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
        /// The first element this skill could be made of that the creature can actually pay for.
        ///
        /// In the order the skill offers them, so the answer is the one the player would predict
        /// rather than the one that happens to be best -- an automatic choice that outsmarts its
        /// owner is a choice they cannot learn to make themselves.
        ///
        /// Used to decide whether a skill with options is usable at all, to settle one that arrives
        /// without a pick, and to make an opportunity attack out of a fist.
        /// </summary>
        public static bool TryChooseElement(SkillSpec skill, ElementLedger ledger,
            out Element chosen)
        {
            chosen = default;

            if (skill == null)
            {
                return false;
            }

            chosen = skill.Element;

            if (skill.ElementCost <= 0)
            {
                return true;
            }

            foreach (var option in skill.ElementOptions)
            {
                if (ledger.CanSpend(option, skill.ElementCost))
                {
                    chosen = option;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A skill with its element decided, or null when none of its options can be paid for.
        ///
        /// A pick the skill does not offer is discarded rather than refused: the caller that sent
        /// it is either out of date or lying, and in both cases the honest answer is the one the
        /// skill would have given on its own -- the first option that can be paid for.
        /// </summary>
        public static SkillSpec Settle(SkillSpec skill, Element? element, ElementLedger ledger)
        {
            if (skill == null)
            {
                return null;
            }

            if (element.HasValue && skill.Offers(element.Value))
            {
                return skill.WithElement(element.Value);
            }

            return TryChooseElement(skill, ledger, out var chosen)
                ? skill.WithElement(chosen)
                : null;
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

            // A wall between is a wall between, whatever the reach. Checked before what the
            // target is: an ally behind a wall is still behind a wall.
            if (!target.HasLine)
            {
                return SkillRefusal.NoLine;
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

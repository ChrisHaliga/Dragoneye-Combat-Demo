using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>What a skill is aimed at.</summary>
    public enum SkillTarget
    {
        /// <summary>Another creature. Goes through a clash.</summary>
        Creature = 0,

        /// <summary>The user. Resolves immediately, with nobody to contest it.</summary>
        Self = 1,

        /// <summary>A place on the board. Resolves immediately.</summary>
        Tile = 2
    }

    /// <summary>
    /// What a skill does.
    ///
    /// An enum and an amount rather than a method, so a skill is authored rather than written. The
    /// set is deliberately small: these are the effects the game currently resolves, and a kind with
    /// no resolution behind it would be a promise the rules do not keep.
    /// </summary>
    public enum SkillEffectKind
    {
        /// <summary>Reduce health.</summary>
        Damage = 0,

        /// <summary>Restore health, never past the maximum.</summary>
        Heal = 1,

        /// <summary>Restore action points, never past the maximum.</summary>
        RestoreAp = 2,

        /// <summary>
        /// Put spent elements back into the pool, oldest first.
        ///
        /// The amount is a count of elements, not of any one element: which ones come back is
        /// decided by the order they were spent in, not by the skill.
        /// </summary>
        ReturnElement = 3
    }

    /// <summary>
    /// How a skill rolls to hit, where it rolls at all.
    ///
    /// Accuracy is the chance at one tile; every tile past that takes the falloff off it. A skill
    /// with no accuracy authored does not roll -- it is a swing, and swings land -- which keeps
    /// every melee skill exactly as it was. Skill, the attribute, is added at the edge when a
    /// fighter picks the skill up, the same way Strength is added to a swing.
    /// </summary>
    public readonly struct Aim
    {
        /// <summary>Percent chance to hit at one tile. Zero means it does not roll.</summary>
        public readonly int Accuracy;

        /// <summary>Percent taken off for every tile past the first.</summary>
        public readonly int Falloff;

        public Aim(int accuracy, int falloff)
        {
            Accuracy = accuracy < 0 ? 0 : accuracy;
            Falloff = falloff < 0 ? 0 : falloff;
        }

        public bool Rolls => Accuracy > 0;

        /// <summary>The same aim with a bonus folded into the accuracy. Nothing, for a swing.</summary>
        public Aim Plus(int bonus) => Rolls ? new Aim(Accuracy + bonus, Falloff) : this;
    }

    /// <summary>An effect and how much of it.</summary>
    public readonly struct SkillEffect
    {
        public readonly SkillEffectKind Kind;

        /// <summary>The authored number, before any attribute is added to it.</summary>
        public readonly int Amount;

        /// <summary>
        /// Which attribute is added to <see cref="Amount"/>, where one is. "4 + STR" is a weapon
        /// skill's damage; a heal or a breath has none and is the number it says.
        /// </summary>
        public readonly Attribute? Scaling;

        public SkillEffect(SkillEffectKind kind, int amount, Attribute? scaling = null)
        {
            Kind = kind;
            Amount = amount < 0 ? 0 : amount;
            Scaling = scaling;
        }

        /// <summary>Whether an attribute still has to be added before this is a number.</summary>
        public bool Scales => Scaling.HasValue;

        /// <summary>The same effect with the attribute folded in. What the fighter actually does.</summary>
        public SkillEffect Resolved(AttributeBlock attributes) =>
            Scaling.HasValue
                ? new SkillEffect(Kind, Amount + attributes[Scaling.Value])
                : this;

        /// <summary>"4 + STR", or "6" when there is nothing to add.</summary>
        public string Formula =>
            Scaling.HasValue
                ? $"{Amount} + {AttributeInfo.ShortNameOf(Scaling.Value)}"
                : Amount.ToString();
    }

    /// <summary>What an effect does, in words, for a tooltip or a line on a card.</summary>
    public static class SkillEffectInfo
    {
        /// <summary>"6 damage", "4 + STR damage", "Heals 6", "2 AP back", "1 element back".</summary>
        public static string Describe(SkillEffect effect)
        {
            switch (effect.Kind)
            {
                case SkillEffectKind.Damage:
                    return $"{effect.Formula} damage";
                case SkillEffectKind.Heal:
                    return $"Heals {effect.Formula}";
                case SkillEffectKind.RestoreAp:
                    return $"{effect.Formula} AP back";
                case SkillEffectKind.ReturnElement:
                    return effect.Amount == 1 && !effect.Scales
                        ? "1 spent element back"
                        : $"{effect.Formula} spent elements back";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>
    /// An authored skill: the six fields DE-002 asks for, and two more it did not foresee.
    ///
    /// Both costs are authored rather than derived, because deriving them would tie a skill price
    /// to stats that equipment can move.
    ///
    /// The element is usually fixed by the skill, so a creature is limited to answering with what
    /// its kit actually grants. <see cref="ElementOptions"/> is the exception: a skill may offer a
    /// choice between several, and an unarmed strike is the first that does -- a fist is not made
    /// of anything in particular, so which element it arrives as is the fighter own decision.
    ///
    /// <see cref="Conditions"/> is the other addition, and it answers a question list membership
    /// could not: not "where did this skill come from" but "when does it go away".
    /// </summary>
    public sealed class SkillSpec
    {
        public SkillSpec(int id, string name, Element element, Ap apCost, int elementCost,
            int range, SkillTarget target, SkillEffect effect, string description = "",
            int levelRequired = Progression.FirstLevel,
            IReadOnlyList<SkillCondition> conditions = null,
            IReadOnlyList<Element> elementOptions = null, Aim aim = default)
        {
            Aim = aim;
            Conditions = conditions ?? System.Array.Empty<SkillCondition>();
            ElementOptions = ResolveOptions(element, elementOptions);
            LevelRequired = levelRequired < Progression.FirstLevel
                ? Progression.FirstLevel
                : levelRequired;
            Id = id;
            Name = name ?? string.Empty;
            Element = element;
            ApCost = apCost;
            ElementCost = elementCost < 0 ? 0 : elementCost;
            Range = range < 0 ? 0 : range;
            Target = target;
            Effect = effect;
            Description = description ?? string.Empty;
        }

        /// <summary>Stable and hand-assigned; it crosses the network.</summary>
        public int Id { get; }

        public string Name { get; }

        /// <summary>
        /// What the skill is made of, or the first of the elements it may be made of.
        ///
        /// Always one of <see cref="ElementOptions"/>, and the one a caller gets if it does not
        /// choose. For the great majority of skills it is the only one.
        /// </summary>
        public Element Element { get; }

        /// <summary>
        /// Every element this skill may be made of, in the order they are offered.
        ///
        /// Never empty: a skill with nothing authored offers exactly its own element, so callers
        /// that do not care about the distinction can read this and get one answer.
        /// </summary>
        public IReadOnlyList<Element> ElementOptions { get; }

        /// <summary>Whether the user picks which element this arrives as.</summary>
        public bool ChoosesElement => ElementOptions.Count > 1;

        /// <summary>
        /// What has to be true of a character before this is one of their skills.
        ///
        /// Empty for almost everything: a skill granted by a class or a weapon is available
        /// because you have the class or the weapon, and saying so twice would be a second place
        /// for the answer to live.
        /// </summary>
        public IReadOnlyList<SkillCondition> Conditions { get; }

        public Ap ApCost { get; }

        /// <summary>How much of <see cref="Element"/> using this consumes from the pool.</summary>
        public int ElementCost { get; }

        /// <summary>Reach in tiles. Zero means the user only.</summary>
        public int Range { get; }

        public SkillTarget Target { get; }

        public SkillEffect Effect { get; }

        /// <summary>How this rolls to hit. <see cref="Aim.Sure"/> for a swing.</summary>
        public Aim Aim { get; }

        /// <summary>Whether using this on somebody rolls before they get to answer.</summary>
        public bool RollsToHit => Aim.Rolls;

        public string Description { get; }

        /// <summary>
        /// The level a creature has to reach before this is theirs.
        ///
        /// Enforced by leaving the skill out of the resolved loadout entirely rather than by showing
        /// it greyed out. A skill a character cannot use yet is not a decision they are being asked
        /// to make, and a bar full of locked buttons reads as a paywall.
        /// </summary>
        public int LevelRequired { get; }

        /// <summary>
        /// Whether using this starts a clash.
        ///
        /// Only creature-targeted skills are contested; self- and tile-directed ones have nobody to
        /// contest them and resolve where they are used.
        /// </summary>
        public bool IsContested => Target == SkillTarget.Creature;

        /// <summary>
        /// Whether a character in this situation has this skill at all.
        ///
        /// Level and conditions together, because they are the same question asked twice: a skill
        /// you are not high enough for and a skill you are not holding the right thing for are both
        /// skills you do not have yet. Left out of the resolved list entirely rather than shown
        /// disabled -- a bar full of things you cannot do reads as a paywall.
        /// </summary>
        public bool IsAvailable(SkillSituation situation) =>
            LevelRequired <= situation.Level && SkillCondition.AllMet(Conditions, situation);

        /// <summary>
        /// The same skill, settled on one element.
        ///
        /// How a choice stops being a choice. Everything downstream -- the cost check, the
        /// commitment, the clash -- reads <see cref="Element"/>, so resolving the pick into an
        /// ordinary single-element spec at the edge means none of it has to know that skills with
        /// options exist.
        /// </summary>
        public SkillSpec WithElement(Element element) =>
            element == Element && !ChoosesElement
                ? this
                : new SkillSpec(Id, Name, element, ApCost, ElementCost, Range, Target, Effect,
                    Description, LevelRequired, Conditions, new[] { element }, Aim);

        /// <summary>
        /// The same skill with a fighter's attributes folded into its effect.
        ///
        /// A skill that scales is a formula until somebody holds it; this is where it becomes a
        /// number. Everything that reads <see cref="Effect"/> after this -- the bar, the prompt,
        /// the clash, the log -- sees the number and nothing else.
        /// </summary>
        public SkillSpec Scaled(AttributeBlock attributes) =>
            Effect.Scales || Aim.Rolls
                ? new SkillSpec(Id, Name, Element, ApCost, ElementCost, Range, Target,
                    Effect.Resolved(attributes), Description, LevelRequired, Conditions,
                    ElementOptions, Aim.Plus(attributes.Skill * SkillRules.AccuracyPerSkill))
                : this;

        /// <summary>Whether this element is one the skill may be made of.</summary>
        public bool Offers(Element element)
        {
            foreach (var option in ElementOptions)
            {
                if (option == element)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The options, with the authored element guaranteed to be among them and first.
        ///
        /// Forgiving rather than strict: a spec whose default is not one of its own options is
        /// incoherent, and correcting it here means no caller downstream has to handle the case.
        /// </summary>
        static IReadOnlyList<Element> ResolveOptions(Element element,
            IReadOnlyList<Element> options)
        {
            if (options == null || options.Count == 0)
            {
                return new[] { element };
            }

            foreach (var option in options)
            {
                if (option == element)
                {
                    return options;
                }
            }

            var withDefault = new List<Element>(options.Count + 1) { element };
            withDefault.AddRange(options);

            return withDefault;
        }
    }

    /// <summary>Everything a creature can do, in a fixed order.</summary>
    public interface ISkillIndex
    {
        bool TryGetSkill(int id, out SkillSpec spec);

        IReadOnlyList<SkillSpec> Skills { get; }
    }
}

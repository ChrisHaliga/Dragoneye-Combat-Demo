using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>
    /// The kinds of thing a skill can require before it is offered.
    ///
    /// A small closed set rather than a scripting language, for the same reason
    /// <see cref="SkillEffectKind"/> is: every kind here has to be answered by something, and one
    /// with no answer behind it would be a promise the rules do not keep. Add a kind when a skill
    /// needs it, and answer it in <see cref="SkillCondition.IsMet"/> in the same commit.
    /// </summary>
    public enum SkillConditionKind
    {
        /// <summary>No requirement. What a skill with nothing authored on it has.</summary>
        Always = 0,

        /// <summary>Nothing in the weapon slot. What an unarmed strike waits for.</summary>
        NoWeapon = 1,

        /// <summary>Something in the weapon slot.</summary>
        HasWeapon = 2,

        /// <summary>Being of a particular species. The value is its id.</summary>
        Species = 3,

        /// <summary>Being of a particular class. The value is its id.</summary>
        Class = 4
    }

    /// <summary>
    /// One thing that has to be true before a skill is a skill this creature has.
    ///
    /// Authored on the skill rather than inferred from who granted it. Until now availability was
    /// entirely a question of list membership -- a class hands you its skills, a weapon hands you
    /// its own -- which answers "where did this come from" and cannot answer "when does it go
    /// away". An unarmed strike is the first skill whose availability is a fact about the character
    /// rather than about who granted it, and it will not be the last.
    ///
    /// A value type with an int payload rather than a class per condition, because these are
    /// authored in the inspector and serialised into content: a polymorphic hierarchy would need a
    /// custom drawer and a type registry to survive the round trip.
    /// </summary>
    public readonly struct SkillCondition
    {
        public readonly SkillConditionKind Kind;

        /// <summary>What the kind is about, where it is about something. An id, usually.</summary>
        public readonly int Value;

        public SkillCondition(SkillConditionKind kind, int value = 0)
        {
            Kind = kind;
            Value = value;
        }

        public static SkillCondition Always => new SkillCondition(SkillConditionKind.Always);

        public static SkillCondition NoWeapon => new SkillCondition(SkillConditionKind.NoWeapon);

        public static SkillCondition HasWeapon => new SkillCondition(SkillConditionKind.HasWeapon);

        public static SkillCondition Species(int id) =>
            new SkillCondition(SkillConditionKind.Species, id);

        public static SkillCondition Class(int id) =>
            new SkillCondition(SkillConditionKind.Class, id);

        /// <summary>Whether this holds for a character in this situation.</summary>
        public bool IsMet(SkillSituation situation)
        {
            switch (Kind)
            {
                case SkillConditionKind.NoWeapon: return !situation.HasWeapon;
                case SkillConditionKind.HasWeapon: return situation.HasWeapon;
                case SkillConditionKind.Species: return situation.SpeciesId == Value;
                case SkillConditionKind.Class: return situation.ClassId == Value;

                // Always, and anything a newer content file knows about that this build does not.
                // Permissive on purpose: a skill nobody can explain is better offered than
                // silently missing, and the alternative is a character who cannot fight because
                // their content is one version ahead.
                default: return true;
            }
        }

        /// <summary>Whether every one of these holds. An empty list always does.</summary>
        public static bool AllMet(IReadOnlyList<SkillCondition> conditions,
            SkillSituation situation)
        {
            if (conditions == null)
            {
                return true;
            }

            foreach (var condition in conditions)
            {
                if (!condition.IsMet(situation))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Everything a condition is allowed to ask about a character.
    ///
    /// Flat and small, so availability can be decided without a scene, a catalogue or a live
    /// session -- which is what lets the character sheet, the arena bar and the server all reach
    /// the same answer. Anything a condition needs that is not here is a field to add here, and
    /// that friction is the point: it keeps the set of things availability can depend on small
    /// enough to reason about.
    /// </summary>
    public readonly struct SkillSituation
    {
        public readonly int Level;
        public readonly int SpeciesId;
        public readonly int ClassId;

        /// <summary>Whether anything is in the weapon slot.</summary>
        public readonly bool HasWeapon;

        public SkillSituation(int level, int speciesId, int classId, bool hasWeapon)
        {
            Level = level < Progression.FirstLevel ? Progression.FirstLevel : level;
            SpeciesId = speciesId;
            ClassId = classId;
            HasWeapon = hasWeapon;
        }

        /// <summary>
        /// What a creature nobody built is in.
        ///
        /// A premade carries an authored list and has no equipment to ask about, so it is unarmed
        /// by definition and belongs to no species or class the conditions would recognise.
        /// </summary>
        public static SkillSituation Premade(int level) =>
            new SkillSituation(level, 0, 0, hasWeapon: false);
    }
}

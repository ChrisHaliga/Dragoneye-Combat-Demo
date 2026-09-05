using System;

namespace Dragoneye.Combat
{
    /// <summary>
    /// The seven things a player spends points on.
    ///
    /// Distinct from a *stat*: attributes are bought, stats are derived from them. See
    /// <see cref="Vitals"/> for the second half of that sentence.
    ///
    /// Hand-assigned and permanent. These index saved characters and replicated blocks, so the
    /// order cannot change once a character has been stored.
    /// </summary>
    public enum Attribute
    {
        Toughness = 0,
        Dexterity = 1,
        Strength = 2,
        Skill = 3,
        Vitality = 4,
        Willpower = 5,
        Endurance = 6
    }

    /// <summary>
    /// A value per attribute.
    ///
    /// Immutable and addable. A creature's attributes are a class baseline plus every modifier its
    /// equipment supplies, and DE-003 requires that sum to be order-independent -- addition of whole
    /// blocks is commutative, so two clients folding the same modifiers in a different order cannot
    /// arrive at different numbers.
    /// </summary>
    public readonly struct AttributeBlock : IEquatable<AttributeBlock>
    {
        public static readonly AttributeBlock Zero = default;

        public readonly int Toughness;
        public readonly int Dexterity;
        public readonly int Strength;
        public readonly int Skill;
        public readonly int Vitality;
        public readonly int Willpower;
        public readonly int Endurance;

        public AttributeBlock(int toughness, int dexterity, int strength, int skill,
            int vitality, int willpower, int endurance)
        {
            Toughness = toughness;
            Dexterity = dexterity;
            Strength = strength;
            Skill = skill;
            Vitality = vitality;
            Willpower = willpower;
            Endurance = endurance;
        }

        /// <summary>Every attribute at the same value, which is where a fresh character starts.</summary>
        public static AttributeBlock Uniform(int value) =>
            new AttributeBlock(value, value, value, value, value, value, value);

        public int this[Attribute attribute]
        {
            get
            {
                switch (attribute)
                {
                    case Attribute.Toughness: return Toughness;
                    case Attribute.Dexterity: return Dexterity;
                    case Attribute.Strength: return Strength;
                    case Attribute.Skill: return Skill;
                    case Attribute.Vitality: return Vitality;
                    case Attribute.Willpower: return Willpower;
                    case Attribute.Endurance: return Endurance;
                    default: return 0;
                }
            }
        }

        /// <summary>A copy with one attribute replaced. The original is untouched.</summary>
        public AttributeBlock With(Attribute attribute, int value)
        {
            switch (attribute)
            {
                case Attribute.Toughness:
                    return new AttributeBlock(value, Dexterity, Strength, Skill, Vitality, Willpower, Endurance);
                case Attribute.Dexterity:
                    return new AttributeBlock(Toughness, value, Strength, Skill, Vitality, Willpower, Endurance);
                case Attribute.Strength:
                    return new AttributeBlock(Toughness, Dexterity, value, Skill, Vitality, Willpower, Endurance);
                case Attribute.Skill:
                    return new AttributeBlock(Toughness, Dexterity, Strength, value, Vitality, Willpower, Endurance);
                case Attribute.Vitality:
                    return new AttributeBlock(Toughness, Dexterity, Strength, Skill, value, Willpower, Endurance);
                case Attribute.Willpower:
                    return new AttributeBlock(Toughness, Dexterity, Strength, Skill, Vitality, value, Endurance);
                case Attribute.Endurance:
                    return new AttributeBlock(Toughness, Dexterity, Strength, Skill, Vitality, Willpower, value);
                default:
                    return this;
            }
        }

        public static AttributeBlock operator +(AttributeBlock a, AttributeBlock b) =>
            new AttributeBlock(
                a.Toughness + b.Toughness, a.Dexterity + b.Dexterity, a.Strength + b.Strength,
                a.Skill + b.Skill, a.Vitality + b.Vitality, a.Willpower + b.Willpower,
                a.Endurance + b.Endurance);

        /// <summary>Raises every attribute to at least <paramref name="floor"/>.</summary>
        public AttributeBlock ClampedLow(int floor)
        {
            var result = this;

            foreach (var attribute in AttributeInfo.All)
            {
                if (result[attribute] < floor)
                {
                    result = result.With(attribute, floor);
                }
            }

            return result;
        }

        public bool Equals(AttributeBlock other)
        {
            foreach (var attribute in AttributeInfo.All)
            {
                if (this[attribute] != other[attribute])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => obj is AttributeBlock other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;

            foreach (var attribute in AttributeInfo.All)
            {
                hash = unchecked(hash * 397 ^ this[attribute]);
            }

            return hash;
        }

        public override string ToString() =>
            $"TGH {Toughness} DEX {Dexterity} STR {Strength} SKL {Skill} "
            + $"VIT {Vitality} WIL {Willpower} END {Endurance}";
    }

    public static class AttributeInfo
    {
        /// <summary>
        /// Every attribute, in a fixed order.
        ///
        /// Iterated rather than <c>Enum.GetValues</c>, which allocates and boxes on every call and
        /// gives no guarantee of order across runtimes. This order is the order they are displayed.
        /// </summary>
        public static readonly Attribute[] All =
        {
            Attribute.Toughness,
            Attribute.Dexterity,
            Attribute.Strength,
            Attribute.Skill,
            Attribute.Vitality,
            Attribute.Willpower,
            Attribute.Endurance
        };

        public static string NameOf(Attribute attribute)
        {
            switch (attribute)
            {
                case Attribute.Toughness: return "Toughness";
                case Attribute.Dexterity: return "Dexterity";
                case Attribute.Strength: return "Strength";
                case Attribute.Skill: return "Skill";
                case Attribute.Vitality: return "Vitality";
                case Attribute.Willpower: return "Willpower";
                case Attribute.Endurance: return "Endurance";
                default: return "Unknown";
            }
        }

        /// <summary>The three-letter form used wherever space is tight.</summary>
        public static string ShortNameOf(Attribute attribute)
        {
            switch (attribute)
            {
                case Attribute.Toughness: return "TGH";
                case Attribute.Dexterity: return "DEX";
                case Attribute.Strength: return "STR";
                case Attribute.Skill: return "SKL";
                case Attribute.Vitality: return "VIT";
                case Attribute.Willpower: return "WIL";
                case Attribute.Endurance: return "END";
                default: return "???";
            }
        }

        /// <summary>
        /// What raising it actually does, in full, for a hover.
        ///
        /// What the rules do with it, not what the word evokes. Three of the seven are read by
        /// nothing yet, and these say so: in a game where a point costs more than the last one, an
        /// attribute that reads as useful and is not is a point the player will not get back.
        ///
        /// Kept beside the enum rather than in the screen because two screens ask -- the creator
        /// while a character is being built, and the sheet wherever one is shown.
        /// </summary>
        public static string DescribeEffect(Attribute attribute)
        {
            switch (attribute)
            {
                case Attribute.Toughness:
                    return "Toughness -- one more health for every point.";
                case Attribute.Dexterity:
                    return "Dexterity -- one more speed for every point. Speed decides who acts "
                        + "first, and armour takes it away again.";
                case Attribute.Strength:
                    return "Strength -- how hard this creature hits. No rule reads it yet.";
                case Attribute.Skill:
                    return "Skill -- how precisely this creature fights. No rule reads it yet.";
                case Attribute.Vitality:
                    return "Vitality -- one more health for every point.";
                case Attribute.Willpower:
                    return "Willpower -- how well this creature holds itself together. No rule "
                        + "reads it yet.";
                case Attribute.Endurance:
                    return "Endurance -- one more action point and one more speed for every "
                        + "point. Action points are what a turn is spent out of.";
                default:
                    return string.Empty;
            }
        }
    }
}

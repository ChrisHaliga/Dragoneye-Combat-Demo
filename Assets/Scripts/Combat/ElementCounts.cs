using System;
using System.Collections.Generic;

namespace Dragoneye.Combat
{
    /// <summary>
    /// How many of each element something holds.
    ///
    /// One named field per element rather than an array, for the same reasons as
    /// <see cref="StatBlock"/>: it is unmanaged, so netcode can replicate it without a heap
    /// allocation per creature per frame, and adding a fifth element becomes a deliberate edit that
    /// shows up in a diff rather than a silent change in an array length.
    ///
    /// Used for both halves of DE-001. A pool and a reveal record are the same shape -- a count per
    /// element -- and giving them the same type is what lets a spend update both with one operation.
    /// </summary>
    public readonly struct ElementCounts : IEquatable<ElementCounts>
    {
        public static readonly ElementCounts Empty = default;

        public readonly int Fire;
        public readonly int Water;
        public readonly int Earth;
        public readonly int Air;

        public ElementCounts(int fire, int water, int earth, int air)
        {
            Fire = fire;
            Water = water;
            Earth = earth;
            Air = air;
        }

        public int this[Element element]
        {
            get
            {
                switch (element)
                {
                    case Element.Fire: return Fire;
                    case Element.Water: return Water;
                    case Element.Earth: return Earth;
                    case Element.Air: return Air;
                    default: return 0;
                }
            }
        }

        public int Total => Fire + Water + Earth + Air;

        public bool IsEmpty => Total == 0;

        /// <summary>A copy with one element set to <paramref name="value"/>, floored at zero.</summary>
        public ElementCounts With(Element element, int value)
        {
            var amount = value < 0 ? 0 : value;

            switch (element)
            {
                case Element.Fire: return new ElementCounts(amount, Water, Earth, Air);
                case Element.Water: return new ElementCounts(Fire, amount, Earth, Air);
                case Element.Earth: return new ElementCounts(Fire, Water, amount, Air);
                case Element.Air: return new ElementCounts(Fire, Water, Earth, amount);
                default: return this;
            }
        }

        public ElementCounts Plus(Element element, int amount) =>
            amount <= 0 ? this : With(element, this[element] + amount);

        /// <summary>Whether this holds at least <paramref name="amount"/> of an element.</summary>
        public bool Holds(Element element, int amount) => amount <= 0 || this[element] >= amount;

        /// <summary>Builds counts from a list of picks, which is how a starting pool is authored.</summary>
        public static ElementCounts From(IReadOnlyList<Element> picks)
        {
            var counts = Empty;

            if (picks == null)
            {
                return counts;
            }

            foreach (var element in picks)
            {
                if (ElementInfo.IsDefined(element))
                {
                    counts = counts.Plus(element, 1);
                }
            }

            return counts;
        }

        public bool Equals(ElementCounts other) =>
            Fire == other.Fire && Water == other.Water
            && Earth == other.Earth && Air == other.Air;

        public override bool Equals(object obj) => obj is ElementCounts other && Equals(other);

        public override int GetHashCode() =>
            unchecked(((Fire * 397 ^ Water) * 397 ^ Earth) * 397 ^ Air);

        public override string ToString() => $"F{Fire} W{Water} E{Earth} A{Air}";
    }
}

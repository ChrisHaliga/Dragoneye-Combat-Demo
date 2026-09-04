using System;

namespace Dragoneye.Combat
{
    /// <summary>
    /// How many of each element something holds.
    ///
    /// One named field per element rather than an array: it stays unmanaged, so netcode can
    /// replicate it without a heap allocation per creature, and adding an eighth element becomes a
    /// deliberate edit that shows up in a diff rather than a silent change in a length.
    ///
    /// Used for both halves of DE-001 and for a character's starting spread. A pool, a reveal record
    /// and a spread are all the same shape -- a count per element -- and giving them one type is what
    /// lets a spend update two of them in a single operation.
    /// </summary>
    public readonly struct ElementCounts : IEquatable<ElementCounts>
    {
        public static readonly ElementCounts Empty = default;

        public readonly int Geo;
        public readonly int Hydro;
        public readonly int Pyro;
        public readonly int Aero;
        public readonly int Lux;
        public readonly int Nyx;
        public readonly int Arcana;

        public ElementCounts(int geo, int hydro, int pyro, int aero, int lux, int nyx, int arcana)
        {
            Geo = geo;
            Hydro = hydro;
            Pyro = pyro;
            Aero = aero;
            Lux = lux;
            Nyx = nyx;
            Arcana = arcana;
        }

        public int this[Element element]
        {
            get
            {
                switch (element)
                {
                    case Element.Geo: return Geo;
                    case Element.Hydro: return Hydro;
                    case Element.Pyro: return Pyro;
                    case Element.Aero: return Aero;
                    case Element.Lux: return Lux;
                    case Element.Nyx: return Nyx;
                    case Element.Arcana: return Arcana;
                    default: return 0;
                }
            }
        }

        public int Total
        {
            get
            {
                var total = 0;

                foreach (var element in ElementInfo.All)
                {
                    total += this[element];
                }

                return total;
            }
        }

        public bool IsEmpty => Total == 0;

        /// <summary>A copy with one element set to <paramref name="value"/>, floored at zero.</summary>
        public ElementCounts With(Element element, int value)
        {
            var amount = value < 0 ? 0 : value;

            switch (element)
            {
                case Element.Geo: return new ElementCounts(amount, Hydro, Pyro, Aero, Lux, Nyx, Arcana);
                case Element.Hydro: return new ElementCounts(Geo, amount, Pyro, Aero, Lux, Nyx, Arcana);
                case Element.Pyro: return new ElementCounts(Geo, Hydro, amount, Aero, Lux, Nyx, Arcana);
                case Element.Aero: return new ElementCounts(Geo, Hydro, Pyro, amount, Lux, Nyx, Arcana);
                case Element.Lux: return new ElementCounts(Geo, Hydro, Pyro, Aero, amount, Nyx, Arcana);
                case Element.Nyx: return new ElementCounts(Geo, Hydro, Pyro, Aero, Lux, amount, Arcana);
                case Element.Arcana: return new ElementCounts(Geo, Hydro, Pyro, Aero, Lux, Nyx, amount);
                default: return this;
            }
        }

        public ElementCounts Plus(Element element, int amount) =>
            amount <= 0 ? this : With(element, this[element] + amount);

        public ElementCounts Minus(Element element, int amount) =>
            amount <= 0 ? this : With(element, this[element] - amount);

        /// <summary>Whether this holds at least <paramref name="amount"/> of an element.</summary>
        public bool Holds(Element element, int amount) => amount <= 0 || this[element] >= amount;

        public bool Equals(ElementCounts other)
        {
            foreach (var element in ElementInfo.All)
            {
                if (this[element] != other[element])
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) => obj is ElementCounts other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;

            foreach (var element in ElementInfo.All)
            {
                hash = unchecked(hash * 397 ^ this[element]);
            }

            return hash;
        }

        public override string ToString()
        {
            var text = string.Empty;

            foreach (var element in ElementInfo.All)
            {
                if (this[element] > 0)
                {
                    text += $"{ElementInfo.ShortNameOf(element)}{this[element]} ";
                }
            }

            return text.Length == 0 ? "empty" : text.TrimEnd();
        }
    }
}

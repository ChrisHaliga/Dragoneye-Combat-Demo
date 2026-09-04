using Dragoneye.Combat;

namespace Dragoneye.Data
{
    /// <summary>
    /// A count per element, in a shape Unity can serialise.
    ///
    /// <see cref="ElementCounts"/> is immutable with readonly fields, which is right for a value the
    /// rules pass around and wrong for something an inspector writes into. The same bargain
    /// <see cref="AttributeValues"/> makes.
    /// </summary>
    [System.Serializable]
    public struct ElementValues
    {
        public int Geo;
        public int Hydro;
        public int Pyro;
        public int Aero;
        public int Lux;
        public int Nyx;
        public int Arcana;

        public ElementCounts ToCounts() =>
            new ElementCounts(Geo, Hydro, Pyro, Aero, Lux, Nyx, Arcana);
    }
}

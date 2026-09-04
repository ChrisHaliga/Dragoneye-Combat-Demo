namespace Dragoneye.Combat
{
    /// <summary>
    /// The elements the game recognises.
    ///
    /// The values are hand-assigned and permanent: they cross the network and are written into saved
    /// characters, so reordering this enum would silently reinterpret every stored pool. Add to the
    /// end, never renumber.
    ///
    /// Which element beats which is deliberately not here. That is a seam answered outside Combat, so
    /// it can be retuned without touching the rules that spend and record elements.
    /// </summary>
    public enum Element
    {
        Geo = 0,
        Hydro = 1,
        Pyro = 2,
        Aero = 3,
        Lux = 4,
        Nyx = 5,
        Arcana = 6
    }

    public static class ElementInfo
    {
        /// <summary>
        /// Every element, in a fixed order.
        ///
        /// Iterating this rather than <c>Enum.GetValues</c> keeps the order stable across runtimes
        /// and avoids the allocation and boxing that reflection-based enumeration costs on every
        /// pool redraw.
        /// </summary>
        public static readonly Element[] All =
        {
            Element.Geo,
            Element.Hydro,
            Element.Pyro,
            Element.Aero,
            Element.Lux,
            Element.Nyx,
            Element.Arcana
        };

        public static int Count => All.Length;

        /// <summary>Position in <see cref="All"/>, for indexing a per-element array.</summary>
        public static int IndexOf(Element element)
        {
            for (var i = 0; i < All.Length; i++)
            {
                if (All[i] == element)
                {
                    return i;
                }
            }

            return -1;
        }

        public static bool IsDefined(Element element) => IndexOf(element) >= 0;

        /// <summary>
        /// What an element is called. Three letters, which is the whole name and not an
        /// abbreviation of one: the enum spells Hydro and Arcana out because the identifiers cross
        /// the network and reordering them would reinterpret saved pools, but what a player reads
        /// is Hyd and Arc. AEro counts as one letter, so Aero is written Æro.
        /// </summary>
        public static string NameOf(Element element)
        {
            switch (element)
            {
                case Element.Geo: return "Geo";
                case Element.Hydro: return "Hyd";
                case Element.Pyro: return "Pyr";
                case Element.Aero: return "Æro";
                case Element.Lux: return "Lux";
                case Element.Nyx: return "Nyx";
                case Element.Arcana: return "Arc";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// The same name, shouted. Kept as its own call rather than left to each caller, so a screen
        /// that wants the compact form cannot accidentally ship the mixed-case one.
        /// </summary>
        public static string ShortNameOf(Element element) =>
            NameOf(element).ToUpperInvariant();
    }
}

using System;
using System.Collections.Generic;
using Dragoneye.Combat;
using Dragoneye.Hex;

namespace Dragoneye.Scenarios
{
    using Hex = Dragoneye.Hex.Hex;

    /// <summary>
    /// A map the host can pick: what it is called, what to say about it, how to build it, and
    /// where each side starts on it.
    ///
    /// Anchors are optional. A map with none has its sides placed round the rim, which is what
    /// an open field wants; a map with a shape has a side for each party, and the spawner fills
    /// outward from there.
    /// </summary>
    public sealed class MapChoice
    {
        readonly Func<MapRecipe> m_Build;
        readonly Dictionary<Party, Hex> m_Anchors = new Dictionary<Party, Hex>();

        public MapChoice(string id, string title, string summary, Func<MapRecipe> build,
            bool symmetric = true)
        {
            Id = id;
            Title = title;
            Summary = summary;
            Symmetric = symmetric;
            m_Build = build;
        }

        /// <summary>Whether the map is a mirror image east to west. The ruins alone are not.</summary>
        public bool Symmetric { get; }

        public string Id { get; }

        public string Title { get; }

        public string Summary { get; }

        public MapRecipe Build() => m_Build();

        /// <summary>Whether every side has somewhere of its own to start.</summary>
        public bool HasAnchors => m_Anchors.Count > 0;

        public MapChoice Anchor(Party party, Hex hex)
        {
            m_Anchors[party] = hex;
            return this;
        }

        public bool TryAnchor(Party party, out Hex hex) => m_Anchors.TryGetValue(party, out hex);
    }

    /// <summary>
    /// Every map the host can pick, in the order the picker lists them.
    ///
    /// The pick crosses the wire as an index into this list, so the order is part of the
    /// protocol: append, never reorder.
    /// </summary>
    public static class MapLibrary
    {
        static readonly MapChoice[] s_All =
        {
            new MapChoice("field", "The Field",
                "An open hexagon of grass with a boulder in each quarter. Sides start round the rim.",
                Maps.Field),

            new MapChoice("ruins", "The Ruins",
                "A roofless room with one door, a hedge, a hanging cloth and a few boulders. "
                + "Sides start round the rim.",
                Maps.Ruins, symmetric: false),

            new MapChoice("mansion", "The Mansion",
                "A walled house with a great hall between its two doors, a cross corridor, two "
                + "galleries at the back and four small rooms at the front. Heroes arrive at the "
                + "front door, monsters at the back; guards and bandits from the grounds either side.",
                Maps.Mansion)
                .Anchor(Party.Heroes, new Hex(0, -4))
                .Anchor(Party.Monsters, new Hex(0, 4))
                .Anchor(Party.Guards, Maps.MansionWestLawn)
                .Anchor(Party.Bandits, Symmetry.Mirror(Maps.MansionWestLawn)),

            new MapChoice("islands", "The Islands",
                "Two islands joined by a bridge one tile wide. Heroes and guards hold the west "
                + "island, monsters and bandits the east. Arrows cross the water; feet do not.",
                Maps.Islands)
                .Anchor(Party.Heroes, new Hex(-5, 3))
                .Anchor(Party.Guards, new Hex(-5, 2))
                .Anchor(Party.Monsters, Symmetry.Mirror(new Hex(-5, 3)))
                .Anchor(Party.Bandits, Symmetry.Mirror(new Hex(-5, 2)))
        };

        public static IReadOnlyList<MapChoice> All => s_All;

        /// <summary>The map a fresh lobby starts on.</summary>
        public static MapChoice Default => s_All[0];

        /// <summary>The choice at an index, or the default for one that is not a choice.</summary>
        public static MapChoice At(int index) => index >= 0 && index < s_All.Length ? s_All[index] : Default;

        public static int Clamp(int index) => index >= 0 && index < s_All.Length ? index : 0;

        public static int IndexOf(string id)
        {
            for (var i = 0; i < s_All.Length; i++)
            {
                if (s_All[i].Id == id)
                {
                    return i;
                }
            }

            return 0;
        }
    }
}

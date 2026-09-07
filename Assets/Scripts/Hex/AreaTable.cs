namespace Dragoneye.Hex
{
    /// <summary>
    /// How the twelve wedges of a tile group into areas, for one pattern of walled rays.
    ///
    /// Immutable and shared: every tile with the same rays walled has the same layout.
    ///
    /// Not every piece the walls leave is an area. **A piece narrower than ninety degrees -- fewer
    /// than three wedges -- is not somewhere a creature can stand.** It is dead footing: nothing
    /// enters it, nothing is placed in it, and it has no number. A line still crosses it. That
    /// rule is what keeps a tile to at most four areas, however the rays are set.
    /// </summary>
    public sealed class AreaLayout
    {
        /// <summary>The area number of a wedge that is nobody's footing.</summary>
        public const byte Dead = byte.MaxValue;

        /// <summary>Wedges an area needs before a creature can stand in it. Ninety degrees.</summary>
        public const int MinimumWedges = 3;

        readonly byte[] m_AreaOfWedge;

        internal AreaLayout(byte[] areaOfWedge, int count)
        {
            m_AreaOfWedge = areaOfWedge;
            Count = count;
        }

        /// <summary>How many separate pieces of footing the tile has. One, for most tiles; never more than four.</summary>
        public int Count { get; }

        /// <summary>Which area a wedge belongs to, or <see cref="Dead"/> for a sliver nobody can stand in.</summary>
        public byte AreaOf(int wedge) => m_AreaOfWedge[TileGeometry.Wrap(wedge)];

        /// <summary>Whether a wedge is part of somewhere a creature can stand.</summary>
        public bool IsFooting(int wedge) => AreaOf(wedge) != Dead;

        /// <summary>Whether a wedge belongs to an area.</summary>
        public bool Contains(int wedge, byte area) => AreaOf(wedge) == area;

        /// <summary>
        /// Where an area of this layout is in another: the area there that holds this one's first
        /// wedge, or <see cref="Dead"/> when the new walls leave that ground nowhere to stand.
        ///
        /// For the day a wall through a tile goes up or comes down mid-fight. The areas renumber,
        /// and a creature standing in one is carried to the piece of ground it was on -- the same
        /// piece on every machine, since the first wedge is the same on every machine.
        /// </summary>
        public byte Carry(byte area, AreaLayout into)
        {
            for (var wedge = 0; wedge < TileGeometry.Wedges; wedge++)
            {
                if (m_AreaOfWedge[wedge] == area)
                {
                    return into.AreaOf(wedge);
                }
            }

            return Dead;
        }
    }

    /// <summary>
    /// Every way twelve rays can cut a tile, worked out once.
    ///
    /// Only rays that block movement split a tile: a sight-blocking curtain through a tile leaves
    /// the footing connected. Two wedges are in one area when the ray between them is open, and
    /// the ring closes -- wedge eleven meets wedge zero across ray zero -- so a single walled ray
    /// splits nothing. Areas are numbered in the order their first wedge appears clockwise from
    /// North, which is what makes the numbering the same on every machine.
    ///
    /// A piece narrower than ninety degrees is not an area: see <see cref="AreaLayout"/>. With
    /// that rule no pattern of rays yields more than four.
    ///
    /// Four thousand and ninety-six masks, twelve wedges each. Baked lazily and kept.
    /// </summary>
    public static class AreaTable
    {
        const int Masks = 1 << TileGeometry.Rays;

        static readonly AreaLayout[] s_Layouts = new AreaLayout[Masks];

        /// <summary>The layout for this pattern of movement-blocking rays. Bit i is ray i.</summary>
        public static AreaLayout For(int rayMask)
        {
            rayMask &= Masks - 1;

            var layout = s_Layouts[rayMask];

            if (layout == null)
            {
                layout = Build(rayMask);
                s_Layouts[rayMask] = layout;
            }

            return layout;
        }

        /// <summary>The whole tile as one area.</summary>
        public static AreaLayout Whole => For(0);

        static AreaLayout Build(int rayMask)
        {
            // Union-find over the wedges, joined across every open ray.
            var parent = new int[TileGeometry.Wedges];

            for (var i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }

            for (var ray = 0; ray < TileGeometry.Rays; ray++)
            {
                if ((rayMask & (1 << ray)) == 0)
                {
                    Union(parent, TileGeometry.WedgeBefore(ray), TileGeometry.WedgeAfter(ray));
                }
            }

            // How big each piece is, so the slivers can be told from the footing.
            var sizeOfRoot = new int[TileGeometry.Wedges];

            for (var wedge = 0; wedge < TileGeometry.Wedges; wedge++)
            {
                sizeOfRoot[Find(parent, wedge)]++;
            }

            // Number the roots in the order they are first met, walking clockwise from North.
            // A piece too narrow to stand in gets no number at all.
            var numberOfRoot = new int[TileGeometry.Wedges];

            for (var i = 0; i < numberOfRoot.Length; i++)
            {
                numberOfRoot[i] = -1;
            }

            var areaOfWedge = new byte[TileGeometry.Wedges];
            var count = 0;

            for (var wedge = 0; wedge < TileGeometry.Wedges; wedge++)
            {
                var root = Find(parent, wedge);

                if (sizeOfRoot[root] < AreaLayout.MinimumWedges)
                {
                    areaOfWedge[wedge] = AreaLayout.Dead;
                    continue;
                }

                if (numberOfRoot[root] < 0)
                {
                    numberOfRoot[root] = count++;
                }

                areaOfWedge[wedge] = (byte)numberOfRoot[root];
            }

            return new AreaLayout(areaOfWedge, count);
        }

        static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        static void Union(int[] parent, int a, int b)
        {
            var ra = Find(parent, a);
            var rb = Find(parent, b);

            if (ra != rb)
            {
                parent[rb] = ra;
            }
        }
    }
}

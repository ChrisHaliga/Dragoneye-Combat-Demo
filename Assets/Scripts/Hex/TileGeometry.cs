namespace Dragoneye.Hex
{
    /// <summary>
    /// The twelve rays and twelve half-edges of a tile, and the arithmetic between them.
    ///
    /// Everything about a wall's place on a tile is an index into one of two rings of twelve, both
    /// counted clockwise from North:
    ///
    ///   ray i        the line from the tile's centre out at 30·i degrees. Even rays reach an edge
    ///                midpoint (ray 0 is North, ray 2 is NorthEast ...); odd rays reach a corner.
    ///   wedge i      the thirty-degree slice between ray i and ray i+1.
    ///   half-edge i  the piece of perimeter that closes wedge i. Half-edges 2d-1 and 2d together
    ///                make the edge in direction d.
    ///
    /// A tile is flat-topped: its North edge is a horizontal line, its corners sit at 30, 90, 150,
    /// 210, 270 and 330 degrees. This is what lets a wall run straight through a hex grid --
    /// vertical walls follow rays 0 and 6 from midpoint to midpoint, horizontal ones follow rays 3
    /// and 9 from corner to corner and then along a neighbour's flat edge.
    ///
    /// Half-edges are shared with the neighbour across them, so each has one owner: a tile owns
    /// the halves of its North, NorthEast and SouthEast edges, and the neighbour owns the rest.
    /// <see cref="Twin"/> is the same piece of wall seen from the other side.
    ///
    /// Positions here are integers, scaled by <see cref="Scale"/>, because a bearing computed from
    /// them decides flanks on the server and on every client, and two machines must not disagree.
    /// </summary>
    public static class TileGeometry
    {
        public const int Rays = 12;
        public const int Wedges = 12;
        public const int HalfEdges = 12;

        /// <summary>Integer units per tile radius. A million is exact enough and never overflows a long.</summary>
        public const long Scale = 1000000;

        /// <summary>Degrees clockwise from North that ray <paramref name="ray"/> points along.</summary>
        public static int AngleOf(int ray) => 30 * Wrap(ray);

        /// <summary>Whether a ray reaches an edge midpoint (even) rather than a corner (odd).</summary>
        public static bool ReachesMidpoint(int ray) => (Wrap(ray) & 1) == 0;

        /// <summary>The direction whose edge this half-edge is part of.</summary>
        public static HexDirection EdgeOf(int halfEdge) =>
            (HexDirection)(((Wrap(halfEdge) + 1) / 2) % 6);

        /// <summary>The two half-edges that make up an edge, in clockwise order.</summary>
        public static void HalvesOf(HexDirection edge, out int first, out int second)
        {
            first = Wrap(2 * (int)edge - 1);
            second = Wrap(2 * (int)edge);
        }

        /// <summary>
        /// The same half-edge, indexed from the neighbour it is shared with.
        ///
        /// A mirror, not a half turn: the half of an edge nearer one corner is, from the other
        /// side, the half nearer that same corner, so the east half of a South edge is the east
        /// half of the neighbour's North edge (wedge 5 meets wedge 0, wedge 6 meets wedge 11).
        /// </summary>
        public static int Twin(int halfEdge) => Wrap(halfEdge + (halfEdge % 2 == 0 ? 5 : 7));

        /// <summary>Whether this tile keeps the record for this half-edge, or its neighbour does.</summary>
        public static bool IsOwned(int halfEdge)
        {
            var edge = (int)EdgeOf(halfEdge);
            return edge <= 2;
        }

        /// <summary>The wedge on the clockwise side of a ray.</summary>
        public static int WedgeAfter(int ray) => Wrap(ray);

        /// <summary>The wedge on the anticlockwise side of a ray.</summary>
        public static int WedgeBefore(int ray) => Wrap(ray - 1);

        public static int Wrap(int index) => ((index % 12) + 12) % 12;

        // Where each ray ends, scaled: (x east, z north). Corners at radius one, midpoints at
        // root-three over two. Written as literals rather than computed, so no trigonometric
        // call is ever part of a decision.
        static readonly long[] k_EndX =
        {
            0, 500000, 750000, 1000000, 750000, 500000,
            0, -500000, -750000, -1000000, -750000, -500000
        };

        static readonly long[] k_EndZ =
        {
            866025, 866025, 433013, 0, -433013, -866025,
            -866025, -866025, -433013, 0, 433013, 866025
        };

        /// <summary>Where a ray ends, in scaled tile-local units. X east, Z north.</summary>
        public static void RayEnd(int ray, out long x, out long z)
        {
            ray = Wrap(ray);
            x = k_EndX[ray];
            z = k_EndZ[ray];
        }

        /// <summary>The centroid of a wedge: a third of the way from the centre to the midpoint of its two ray ends.</summary>
        public static void WedgeCentroid(int wedge, out long x, out long z)
        {
            RayEnd(wedge, out var ax, out var az);
            RayEnd(wedge + 1, out var bx, out var bz);
            x = (ax + bx) / 3;
            z = (az + bz) / 3;
        }

        /// <summary>A tile's centre in the same scaled frame, for a layout of radius one.</summary>
        public static void TileCentre(Hex hex, out long x, out long z)
        {
            // Flat-top: x = 1.5 q, z = (root three over two)(q + 2r). See HexLayout.ToWorld.
            x = 1500000L * hex.Q;
            z = 866025L * (hex.Q + 2L * hex.R);
        }

        /// <summary>
        /// Which wedge a tile-local point falls in, by orientation tests against the rays.
        ///
        /// A point exactly on a ray belongs to the wedge after it (clockwise), which is the same
        /// convention <see cref="Bearing"/> uses for its boundaries.
        /// </summary>
        public static int WedgeAt(long x, long z)
        {
            for (var wedge = 0; wedge < Wedges; wedge++)
            {
                RayEnd(wedge, out var ax, out var az);
                RayEnd(wedge + 1, out var bx, out var bz);

                // Clockwise from ray `wedge` and anticlockwise from ray `wedge + 1`.
                if (Clockwise(ax, az, x, z) >= 0 && Clockwise(x, z, bx, bz) > 0)
                {
                    return wedge;
                }
            }

            // Only the origin fails every test. It is nobody's wedge; call it the first.
            return 0;
        }

        /// <summary>
        /// The direction a scaled vector points in, as one of six.
        ///
        /// Direction d covers the sixty degrees centred on ray 2d. **A bearing exactly on the
        /// boundary between two directions belongs to the one with the lower index.** That is the
        /// tiebreak two machines have to agree on, and it is written here rather than discovered:
        /// two area centres in one tile sit on these boundaries constantly.
        /// </summary>
        public static HexDirection Bearing(long x, long z)
        {
            var wedge = WedgeAt(x, z);
            var direction = ((wedge + 1) / 2) % 6;

            // On a corner ray exactly? That ray is the boundary between two directions.
            if ((wedge & 1) == 1)
            {
                RayEnd(wedge, out var rx, out var rz);

                if (Clockwise(rx, rz, x, z) == 0)
                {
                    var other = (direction + 5) % 6;
                    direction = other < direction ? other : direction;
                }
            }

            return (HexDirection)direction;
        }

        /// <summary>
        /// Positive when (bx, bz) is clockwise of (ax, az) looking down on the tile, negative when
        /// anticlockwise, zero when collinear. The one orientation test everything else is built on.
        ///
        /// With x east and z north, the usual cross product is positive *anticlockwise* -- North
        /// to East reads negative -- so this is that product with its sign turned round.
        /// </summary>
        public static long Clockwise(long ax, long az, long bx, long bz) => az * bx - ax * bz;
    }
}

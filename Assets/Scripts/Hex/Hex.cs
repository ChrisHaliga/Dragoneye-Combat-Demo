using System;
using System.Collections.Generic;

namespace Dragoneye.Hex
{
    /// <summary>
    /// A hex coordinate, stored as axial <c>(q, r)</c>.
    ///
    /// The third cube axis <see cref="S"/> is derived rather than stored, because the three always
    /// satisfy <c>q + r + s == 0</c>. Cube coordinates make distance, rings and rounding trivial;
    /// axial storage keeps the struct to two ints. Converting between the two is free.
    ///
    /// This is pure data with no notion of world space -- see <see cref="HexLayout"/> for that.
    /// </summary>
    public readonly struct Hex : IEquatable<Hex>
    {
        public static readonly Hex Zero = new Hex(0, 0);

        public readonly int Q;
        public readonly int R;

        /// <summary>The derived third cube axis. Always <c>-Q - R</c>.</summary>
        public int S => -Q - R;

        public Hex(int q, int r)
        {
            Q = q;
            R = r;
        }

        // Ordered clockwise from north to match HexDirection. Indexed by (int)HexDirection.
        static readonly Hex[] k_Directions =
        {
            new Hex(0, 1),   // North
            new Hex(1, 0),   // NorthEast
            new Hex(1, -1),  // SouthEast
            new Hex(0, -1),  // South
            new Hex(-1, 0),  // SouthWest
            new Hex(-1, 1)   // NorthWest
        };

        public static Hex Offset(HexDirection direction) => k_Directions[(int)direction];

        /// <summary>
        /// Which of the six directions best describes the way from one hex to another.
        ///
        /// Wanted for facing: an attack arrives from somewhere, and "somewhere" has to become one of
        /// six sectors before anything can ask whether it landed in a flank.
        ///
        /// Integer arithmetic, deliberately. The obvious implementation takes an angle and divides
        /// it into sixths, and an angle means a trig call whose last bit is not guaranteed to match
        /// across platforms -- which for a rule that decides a clash means two machines resolving
        /// the same attack two ways, with nothing in the logs to say why. The cube dot product
        /// falls monotonically with angle across all six (2, 1, -1, -2, -1, 1), so the largest one
        /// is the nearest direction, and it is exact.
        ///
        /// **The tiebreak is stated, not discovered.** An offset lying exactly between two
        /// directions -- (1, 1), say, which is equally north and north-east -- scores the same
        /// against both. The lower direction index wins, which means ties resolve clockwise-first
        /// from north. A hex has no direction to itself; that answers North.
        /// </summary>
        public static HexDirection DirectionTo(Hex from, Hex to)
        {
            var delta = to - from;
            var best = HexDirection.North;
            var bestScore = int.MinValue;

            for (var i = 0; i < k_Directions.Length; i++)
            {
                var candidate = k_Directions[i];
                var score = (delta.Q * candidate.Q) + (delta.R * candidate.R)
                    + (delta.S * candidate.S);

                // Strictly greater, so the first of any tie is kept.
                if (score > bestScore)
                {
                    bestScore = score;
                    best = (HexDirection)i;
                }
            }

            return best;
        }

        public Hex Neighbor(HexDirection direction) => this + k_Directions[(int)direction];

        /// <summary>The six adjacent hexes, clockwise from north.</summary>
        public IEnumerable<Hex> Neighbors()
        {
            for (var i = 0; i < 6; i++)
            {
                yield return this + k_Directions[i];
            }
        }

        /// <summary>
        /// Steps between two hexes. In cube space this is half the sum of the axis deltas, because
        /// moving one step always changes exactly two of the three axes by one.
        /// </summary>
        public static int Distance(Hex a, Hex b)
        {
            var dq = Math.Abs(a.Q - b.Q);
            var dr = Math.Abs(a.R - b.R);
            var ds = Math.Abs(a.S - b.S);
            return (dq + dr + ds) / 2;
        }

        public int DistanceTo(Hex other) => Distance(this, other);

        /// <summary>
        /// The hexes exactly <paramref name="radius"/> steps from <paramref name="center"/>.
        /// Always <c>6 * radius</c> hexes, or a single hex when the radius is zero.
        /// </summary>
        public static IEnumerable<Hex> Ring(Hex center, int radius)
        {
            if (radius < 0)
            {
                yield break;
            }

            if (radius == 0)
            {
                yield return center;
                yield break;
            }

            // Start on the south-west corner of the ring, then walk each of the six edges. Walking
            // direction i from that corner traces an edge exactly `radius` hexes long.
            var hex = center + k_Directions[(int)HexDirection.SouthWest] * radius;

            for (var direction = 0; direction < 6; direction++)
            {
                for (var step = 0; step < radius; step++)
                {
                    yield return hex;
                    hex += k_Directions[direction];
                }
            }
        }

        /// <summary>
        /// Every hex within <paramref name="radius"/> steps of <paramref name="center"/>, inclusive.
        /// Yields <c>3r² + 3r + 1</c> hexes.
        /// </summary>
        public static IEnumerable<Hex> Range(Hex center, int radius)
        {
            for (var dq = -radius; dq <= radius; dq++)
            {
                var lower = Math.Max(-radius, -dq - radius);
                var upper = Math.Min(radius, -dq + radius);

                for (var dr = lower; dr <= upper; dr++)
                {
                    yield return new Hex(center.Q + dq, center.R + dr);
                }
            }
        }

        /// <summary>
        /// The hexes along a straight line from <paramref name="a"/> to <paramref name="b"/>,
        /// inclusive of both ends.
        /// </summary>
        public static IEnumerable<Hex> Line(Hex a, Hex b)
        {
            var steps = Distance(a, b);
            if (steps == 0)
            {
                yield return a;
                yield break;
            }

            // Nudge the start off exact edge midpoints so samples never land ambiguously between
            // two hexes, which would make the line jitter depending on float rounding.
            const float epsilon = 1e-6f;
            var step = 1f / steps;

            for (var i = 0; i <= steps; i++)
            {
                var t = step * i;
                var q = a.Q + (b.Q - a.Q) * t + epsilon;
                var r = a.R + (b.R - a.R) * t + epsilon;
                yield return Round(q, r);
            }
        }

        /// <summary>
        /// Snaps fractional axial coordinates to the nearest hex.
        ///
        /// Rounding each axis independently can produce a coordinate that breaks the
        /// <c>q + r + s == 0</c> invariant, so the axis that moved furthest is recomputed from the
        /// other two.
        /// </summary>
        public static Hex Round(float q, float r)
        {
            var s = -q - r;

            var roundedQ = (int)Math.Round(q);
            var roundedR = (int)Math.Round(r);
            var roundedS = (int)Math.Round(s);

            var deltaQ = Math.Abs(roundedQ - q);
            var deltaR = Math.Abs(roundedR - r);
            var deltaS = Math.Abs(roundedS - s);

            if (deltaQ > deltaR && deltaQ > deltaS)
            {
                roundedQ = -roundedR - roundedS;
            }
            else if (deltaR > deltaS)
            {
                roundedR = -roundedQ - roundedS;
            }

            return new Hex(roundedQ, roundedR);
        }

        public static Hex operator +(Hex a, Hex b) => new Hex(a.Q + b.Q, a.R + b.R);

        public static Hex operator -(Hex a, Hex b) => new Hex(a.Q - b.Q, a.R - b.R);

        public static Hex operator *(Hex hex, int scale) => new Hex(hex.Q * scale, hex.R * scale);

        public static bool operator ==(Hex a, Hex b) => a.Q == b.Q && a.R == b.R;

        public static bool operator !=(Hex a, Hex b) => !(a == b);

        public bool Equals(Hex other) => Q == other.Q && R == other.R;

        public override bool Equals(object obj) => obj is Hex other && Equals(other);

        public override int GetHashCode() => unchecked((Q * 397) ^ R);

        public override string ToString() => $"Hex({Q}, {R}, {S})";
    }
}

using System.Numerics;

namespace ManaVoxel.Code.Gameplay.TrainYardWars.Rules
{
    /// <summary>
    /// A hex in axial coordinates, pointy-top. Direction d and d+3 are opposite; d+-2 is the gentle bend a
    /// track may take through a hex, d+-1 the sharp one it may not.
    /// </summary>
    public readonly record struct TywHex(int Q, int R)
    {
        public const int DirectionCount = 6;

        // E, NE, NW, W, SW, SE on the world XZ plane (+Z drawn toward the camera).
        private static readonly TywHex[] Directions =
        {
            new(1, 0), new(1, -1), new(0, -1), new(-1, 0), new(-1, 1), new(0, 1)
        };

        public static readonly float Sqrt3 = MathF.Sqrt(3.0f);

        public int S => -Q - R;

        public static int Wrap(int direction) => ((direction % DirectionCount) + DirectionCount) % DirectionCount;

        public static int Opposite(int direction) => Wrap(direction + 3);

        public TywHex Neighbor(int direction)
        {
            var offset = Directions[Wrap(direction)];
            return new TywHex(Q + offset.Q, R + offset.R);
        }

        public int DistanceTo(TywHex other) =>
            (Math.Abs(Q - other.Q) + Math.Abs(Q + R - other.Q - other.R) + Math.Abs(R - other.R)) / 2;

        /// <summary>The direction from this hex to an adjacent one, or -1 when they do not touch.</summary>
        public int DirectionTo(TywHex other)
        {
            for (var d = 0; d < DirectionCount; d++)
            {
                if (Neighbor(d) == other)
                {
                    return d;
                }
            }

            return -1;
        }

        /// <summary>
        /// Whether a train that came in from <paramref name="cameFrom"/> (the direction back to the hex it left)
        /// may leave toward <paramref name="goingTo"/> without stopping: straight on or a gentle bend.
        /// </summary>
        public static bool IsSmoothTurn(int cameFrom, int goingTo)
        {
            var turn = Wrap(goingTo - cameFrom);
            return turn is 2 or 3 or 4;
        }

        /// <summary>Centre on the XZ plane for hexes of <paramref name="radius"/> (centre to corner).</summary>
        public Vector2 ToWorld(float radius) =>
            new(radius * (Sqrt3 * Q + Sqrt3 * 0.5f * R), radius * 1.5f * R);

        public static TywHex FromWorld(Vector2 point, float radius)
        {
            var q = (Sqrt3 / 3.0f * point.X - point.Y / 3.0f) / radius;
            var r = (2.0f / 3.0f * point.Y) / radius;
            return Round(q, r);
        }

        private static TywHex Round(float q, float r)
        {
            var s = -q - r;
            var rq = MathF.Round(q);
            var rr = MathF.Round(r);
            var rs = MathF.Round(s);
            var dq = MathF.Abs(rq - q);
            var dr = MathF.Abs(rr - r);
            var ds = MathF.Abs(rs - s);
            if (dq > dr && dq > ds)
            {
                rq = -rr - rs;
            }
            else if (dr > ds)
            {
                rr = -rq - rs;
            }

            return new TywHex((int)rq, (int)rr);
        }

        public static IEnumerable<TywHex> Spiral(int radius)
        {
            for (var q = -radius; q <= radius; q++)
            {
                for (var r = Math.Max(-radius, -q - radius); r <= Math.Min(radius, -q + radius); r++)
                {
                    yield return new TywHex(q, r);
                }
            }
        }

        public override string ToString() => $"({Q},{R})";
    }

    /// <summary>A track link between two adjacent hexes, stored with its ends in a fixed order.</summary>
    public readonly record struct TywLink
    {
        public TywHex A { get; }
        public TywHex B { get; }

        public TywLink(TywHex a, TywHex b)
        {
            if (a.Q < b.Q || (a.Q == b.Q && a.R < b.R))
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public TywHex Other(TywHex end) => end == A ? B : A;
    }
}

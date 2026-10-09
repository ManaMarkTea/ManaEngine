using ManaVoxel.Code.Gameplay.TrainYardWars.Rules;

namespace ManaVoxel.Code.Gameplay.TrainYardWars.View
{
    /// <summary>
    /// Where track runs on the board: hex centres, edge midpoints and the curves between them. Track meshes and
    /// moving trains both read it, so a train always sits on its rails.
    /// </summary>
    public static class TywGeometry
    {
        public const float HexRadius = 2.0f;
        // The hexagon kit tiles are 0.2 thick at a scale of 2 * sqrt(3): their surface is where track lies.
        public static readonly float TopY = 0.2f * HexRadius * TywHex.Sqrt3;

        public static Vector3 Center(TywHex hex)
        {
            var p = hex.ToWorld(HexRadius);
            return new Vector3(p.X, TopY, p.Y);
        }

        public static Vector3 EdgeMid(TywHex hex, int direction) => (Center(hex) + Center(hex.Neighbor(direction))) * 0.5f;

        public static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t)
        {
            var u = 1.0f - t;
            return a * (u * u) + control * (2.0f * u * t) + b * (t * t);
        }

        /// <summary>
        /// A point <paramref name="s"/> hexes along a path. Through a plain hex the line is a smooth curve between
        /// edge midpoints; where a building stands (or at either end) it runs in to the centre and out again, which
        /// is also how the track there is drawn.
        /// </summary>
        public static Vector3 PathPoint(IReadOnlyList<TywHex> path, float s, Func<TywHex, bool> isTerminal)
        {
            var last = path.Count - 1;
            if (last <= 0)
            {
                return Center(path[0]);
            }

            if (s < 0.0f)
            {
                // Behind the start: carry on backwards along the line out of the first hex.
                var start = Center(path[0]);
                var away = start - EdgeMid(path[0], path[0].DirectionTo(path[1]));
                return start + Vector3.Normalize(away) * (-s * HexRadius * TywHex.Sqrt3);
            }

            s = MathF.Min(s, last);
            var i = Math.Clamp((int)MathF.Round(s), 0, last);
            var u = s - (i - 0.5f);
            var hex = path[i];
            var center = Center(hex);
            var entry = i > 0 ? EdgeMid(hex, hex.DirectionTo(path[i - 1])) : center;
            var exit = i < last ? EdgeMid(hex, hex.DirectionTo(path[i + 1])) : center;

            if (i == 0 || i == last || isTerminal(hex) || path[i - 1] == path[i + 1])
            {
                return u < 0.5f
                    ? Vector3.Lerp(entry, center, Math.Clamp(u * 2.0f, 0.0f, 1.0f))
                    : Vector3.Lerp(center, exit, Math.Clamp((u - 0.5f) * 2.0f, 0.0f, 1.0f));
            }

            return Bezier(entry, center, exit, Math.Clamp(u, 0.0f, 1.0f));
        }

        /// <summary>
        /// The pieces of track to draw in one hex, as point lists: a curve for each pair of links a train can run
        /// straight through, a spoke to the centre for every link at a building or with nothing to continue into.
        /// </summary>
        public static List<Vector3[]> TrackPieces(TywHex hex, IReadOnlyList<int> directions, bool terminal)
        {
            var pieces = new List<Vector3[]>();
            var center = Center(hex);
            var continued = new HashSet<int>();
            if (!terminal)
            {
                for (var i = 0; i < directions.Count; i++)
                {
                    for (var j = i + 1; j < directions.Count; j++)
                    {
                        if (!TywHex.IsSmoothTurn(directions[i], directions[j]))
                        {
                            continue;
                        }

                        var a = EdgeMid(hex, directions[i]);
                        var b = EdgeMid(hex, directions[j]);
                        pieces.Add(Enumerable.Range(0, 13).Select(k => Bezier(a, center, b, k / 12.0f)).ToArray());
                        continued.Add(directions[i]);
                        continued.Add(directions[j]);
                    }
                }
            }

            foreach (var d in directions)
            {
                if (!continued.Contains(d))
                {
                    pieces.Add(new[] { center, EdgeMid(hex, d) });
                }
            }

            return pieces;
        }
    }
}

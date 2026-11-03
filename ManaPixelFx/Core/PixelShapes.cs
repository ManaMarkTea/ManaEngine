using static ManaPixelFx.Core.PixelMath;

namespace ManaPixelFx.Core
{
    /// <summary>pixelfx's shapes.js: point lists effects keep reaching for -- lightning paths, stars, polygons, trails.</summary>
    public static class PixelShapes
    {
        /// <summary>
        /// A jagged lightning path from (x0, y0) to (x1, y1) by midpoint displacement; jag is how far a midpoint may move
        /// sideways, as a share of the segment's length.
        /// </summary>
        public static List<(double X, double Y)> Bolt(PixelRng r, double x0, double y0, double x1, double y1, double jag = 0.22, int depth = 5)
        {
            var pts = new List<(double X, double Y)> { (x0, y0), (x1, y1) };
            for (var d = 0; d < depth; d++)
            {
                var next = new List<(double X, double Y)> { pts[0] };
                for (var i = 1; i < pts.Count; i++)
                {
                    var (ax, ay) = pts[i - 1];
                    var (bx, by) = pts[i];
                    var dx = bx - ax;
                    var dy = by - ay;
                    var l = Hypot(dx, dy);
                    var off = (r.Next() * 2 - 1) * jag * l;
                    var len = l;
                    if (len == 0)
                    {
                        len = 1;
                    }
                    next.Add(((ax + bx) / 2 + (-dy / len) * off, (ay + by) / 2 + (dx / len) * off));
                    next.Add(pts[i]);
                }
                pts = next;
            }
            return pts;
        }

        /// <summary>Forks off a bolt path: count branches, each up to len long.</summary>
        public static List<List<(double X, double Y)>> Forks(PixelRng r, IReadOnlyList<(double X, double Y)> path, int count = 3, double len = 20, double jag = 0.3, double spread = 0.9)
        {
            var output = new List<List<(double X, double Y)>>();
            for (var k = 0; k < count; k++)
            {
                var i = 1 + (int)Math.Floor(r.Next() * (path.Count - 2));
                var (x, y) = path[i];
                var (px, py) = path[i - 1];
                double side = 1;
                if (r.Next() < 0.5)
                {
                    side = -1;
                }
                var a = Math.Atan2(y - py, x - px) + side * spread * (0.4 + 0.6 * r.Next());
                var l = len * (0.4 + 0.6 * r.Next());
                output.Add(Bolt(r, x, y, x + Math.Cos(a) * l, y + Math.Sin(a) * l, jag, 3));
            }
            return output;
        }

        /// <summary>A star: n points between radii r1 (tips) and r0 (inner corners).</summary>
        public static List<(double X, double Y)> Star(double cx, double cy, double r0, double r1, int n = 5, double rot = -Math.PI / 2)
        {
            var pts = new List<(double X, double Y)>();
            for (var i = 0; i < n * 2; i++)
            {
                var a = rot + (i * Math.PI) / n;
                var r = r1;
                if (i % 2 != 0)
                {
                    r = r0;
                }
                pts.Add((cx + Math.Cos(a) * r, cy + Math.Sin(a) * r));
            }
            return pts;
        }

        /// <summary>A regular polygon with n sides.</summary>
        public static List<(double X, double Y)> Ngon(double cx, double cy, double r, int n = 6, double rot = 0) =>
            Enumerable.Range(0, n).Select(i => (cx + Math.Cos(rot + (i * Tau) / n) * r, cy + Math.Sin(rot + (i * Tau) / n) * r)).ToList();

        /// <summary>Positions of pos(t - i * dt) for i = 0..n-1: a moving thing's trail, newest first.</summary>
        public static List<(double X, double Y)> Trail(Func<double, (double X, double Y)> pos, double t, int n, double dt)
        {
            var output = new List<(double X, double Y)>();
            for (var i = 0; i < n; i++)
            {
                output.Add(pos(Math.Max(0, t - i * dt)));
            }
            return output;
        }
    }
}

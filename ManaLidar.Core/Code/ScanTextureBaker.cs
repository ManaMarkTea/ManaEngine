using System.Numerics;

namespace ManaLidar;

/// <summary>
/// Paints an atlas from the photos: every texel inside a triangle is turned back into its 3D point and coloured
/// the way a vertex would be, then the colours are bled a few texels past each chart's edge.
/// </summary>
public static class ScanTextureBaker
{
    /// <returns>RGB, <c>Size * Size * 3</c> bytes, rows top to bottom.</returns>
    public static byte[] Bake(ScanAtlasMesh mesh, IReadOnlyList<ScanKeyframe> keyframes, IProgress<float>? progress = null)
    {
        var size = mesh.Size;
        var rgb = new byte[size * size * 3];
        var filled = new bool[size * size];
        var surfaces = mesh.FaceSurfaces;

        var chartFaces = new List<int>[mesh.ChartCount];
        for (var c = 0; c < chartFaces.Length; c++)
        {
            chartFaces[c] = new List<int>();
        }
        for (var f = 0; f < mesh.FaceCharts.Length; f++)
        {
            chartFaces[mesh.FaceCharts[f]].Add(f);
        }

        var done = 0;
        // Charts own separate rectangles of the atlas, so they can be painted at the same time.
        Parallel.For(0, chartFaces.Length, c =>
        {
            var faces = chartFaces[c];
            var (centre, radius) = ScanColorizer.Bounds(faces.SelectMany(f => new[]
            {
                mesh.Positions[mesh.Indices[f * 3]], mesh.Positions[mesh.Indices[f * 3 + 1]], mesh.Positions[mesh.Indices[f * 3 + 2]],
            }));
            var candidates = ScanColorizer.Candidates(centre, radius, keyframes);
            foreach (var f in faces)
            {
                PaintTriangle(mesh, f, candidates, surfaces?[f] ?? ScanSurface.None, rgb, filled);
            }
            var finished = Interlocked.Increment(ref done);
            if (finished % 64 == 0)
            {
                progress?.Report((float)finished / chartFaces.Length);
            }
        });

        Bleed(rgb, filled, size, ScanAtlas.Padding + 1);
        progress?.Report(1f);
        return rgb;
    }

    private static void PaintTriangle(ScanAtlasMesh mesh, int f, List<ScanKeyframe> candidates, ScanSurface surface,
        byte[] rgb, bool[] filled)
    {
        var size = mesh.Size;
        int i0 = mesh.Indices[f * 3], i1 = mesh.Indices[f * 3 + 1], i2 = mesh.Indices[f * 3 + 2];
        var t0 = mesh.Uvs[i0] * size;
        var t1 = mesh.Uvs[i1] * size;
        var t2 = mesh.Uvs[i2] * size;
        var area = Edge(t0, t1, t2);
        if (MathF.Abs(area) < 1e-8f)
        {
            return;
        }

        var faceNormal = Vector3.Cross(mesh.Positions[i1] - mesh.Positions[i0], mesh.Positions[i2] - mesh.Positions[i0]);
        faceNormal = faceNormal.LengthSquared() > 0 ? Vector3.Normalize(faceNormal) : mesh.Normals[i0];
        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(t0.X, MathF.Min(t1.X, t2.X))));
        var maxX = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(t0.X, MathF.Max(t1.X, t2.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(t0.Y, MathF.Min(t1.Y, t2.Y))));
        var maxY = Math.Min(size - 1, (int)MathF.Ceiling(MathF.Max(t0.Y, MathF.Max(t1.Y, t2.Y))));
        // Half a texel of slack: texels the edge clips still get this triangle's colour, so seams inside a
        // chart leave no unpainted cracks for the bleed to fill with the wrong neighbour.
        var slack = 0.5f * MathF.Sqrt(Vector2.DistanceSquared(t0, t1) + Vector2.DistanceSquared(t1, t2) + Vector2.DistanceSquared(t2, t0)) / MathF.Abs(area);

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var w0 = Edge(t1, t2, p) / area;
                var w1 = Edge(t2, t0, p) / area;
                var w2 = 1 - w0 - w1;
                if (w0 < -slack || w1 < -slack || w2 < -slack)
                {
                    continue;
                }
                var at = y * size + x;
                var inside = w0 >= 0 && w1 >= 0 && w2 >= 0;
                if (filled[at] && !inside)
                {
                    continue; // a neighbour has it properly; slack only fills gaps
                }
                var point = mesh.Positions[i0] * w0 + mesh.Positions[i1] * w1 + mesh.Positions[i2] * w2;
                var colour = ScanColorizer.TrySample(point, faceNormal, candidates, out var sampled)
                    ? sampled
                    : ScanColorizer.SurfaceColor(surface);
                rgb[at * 3] = ToByte(colour.X);
                rgb[at * 3 + 1] = ToByte(colour.Y);
                rgb[at * 3 + 2] = ToByte(colour.Z);
                filled[at] = true;
            }
        }
    }

    // Grows each chart's colours outward a few texels, so sampling at its edge (and its mipmaps) never pulls in
    // the background between charts.
    private static void Bleed(byte[] rgb, bool[] filled, int size, int passes)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            var grown = new List<(int At, byte R, byte G, byte B)>();
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var at = y * size + x;
                    if (filled[at])
                    {
                        continue;
                    }
                    int r = 0, g = 0, b = 0, n = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx;
                            var ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= size || ny >= size || !filled[ny * size + nx])
                            {
                                continue;
                            }
                            var o = (ny * size + nx) * 3;
                            r += rgb[o];
                            g += rgb[o + 1];
                            b += rgb[o + 2];
                            n++;
                        }
                    }
                    if (n > 0)
                    {
                        grown.Add((at, (byte)(r / n), (byte)(g / n), (byte)(b / n)));
                    }
                }
            }
            foreach (var (at, r, g, b) in grown)
            {
                rgb[at * 3] = r;
                rgb[at * 3 + 1] = g;
                rgb[at * 3 + 2] = b;
                filled[at] = true;
            }
        }
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private static byte ToByte(float v) => (byte)Math.Clamp((int)(v * 255f + 0.5f), 0, 255);
}

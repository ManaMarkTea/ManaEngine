using System.Numerics;

namespace ManaLidar;

/// <summary>A mesh laid out on one texture: vertices split along chart seams, each with its place in the atlas.</summary>
public sealed class ScanAtlasMesh
{
    public required Vector3[] Positions { get; init; }
    public required Vector3[] Normals { get; init; }
    /// <summary>glTF convention: (0, 0) is the image's top-left.</summary>
    public required Vector2[] Uvs { get; init; }
    public required int[] Indices { get; init; }
    public ScanSurface[]? FaceSurfaces { get; init; }
    /// <summary>Which chart each triangle is in.</summary>
    public required int[] FaceCharts { get; init; }
    public int ChartCount { get; init; }
    public int Size { get; init; }
    /// <summary>Texture detail actually reached: texels a metre of surface gets, the same everywhere.</summary>
    public float TexelsPerMetre { get; init; }
}

/// <summary>
/// Lays a mesh flat onto one square texture. Neighbouring triangles facing roughly the same way grow into a chart,
/// each chart is projected onto its own plane, and the charts are packed in rows at one shared texel density --
/// the most that fits, up to what the photos can actually resolve.
/// </summary>
public static class ScanAtlas
{
    /// <summary>Texels kept around each chart so filtering and mipmaps never reach a neighbour's colours.</summary>
    public const int Padding = 3;
    private const float ChartNormalAgreement = 0.8f;

    public static ScanAtlasMesh Build(ScanMeshPiece mesh, int size, float maxTexelsPerMetre)
    {
        var faceCount = mesh.TriangleCount;
        var faceNormals = new Vector3[faceCount];
        var faceAreas = new float[faceCount];
        for (var f = 0; f < faceCount; f++)
        {
            var a = mesh.Positions[mesh.Indices[f * 3]];
            var cross = Vector3.Cross(mesh.Positions[mesh.Indices[f * 3 + 1]] - a, mesh.Positions[mesh.Indices[f * 3 + 2]] - a);
            faceAreas[f] = cross.Length() * 0.5f;
            faceNormals[f] = faceAreas[f] > 0 ? Vector3.Normalize(cross) : Vector3.UnitY;
        }

        var charts = GrowCharts(mesh, faceNormals, faceAreas);
        var layouts = charts.Select(chart => Flatten(mesh, chart, faceNormals, faceAreas)).ToList();
        var density = Pack(layouts, size, maxTexelsPerMetre);

        // Emit: every chart gets its own copy of the vertices it uses, with that chart's UVs.
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new int[mesh.Indices.Length];
        var faceCharts = new int[faceCount];
        var surfaces = mesh.FaceSurfaces != null ? new ScanSurface[faceCount] : null;
        var face = 0;
        for (var c = 0; c < layouts.Count; c++)
        {
            var layout = layouts[c];
            var local = new Dictionary<int, int>();
            foreach (var f in layout.Faces)
            {
                for (var k = 0; k < 3; k++)
                {
                    var v = mesh.Indices[f * 3 + k];
                    if (!local.TryGetValue(v, out var at))
                    {
                        at = positions.Count;
                        local[v] = at;
                        var p = mesh.Positions[v];
                        positions.Add(p);
                        normals.Add(mesh.Normals[v]);
                        var s = Vector3.Dot(p, layout.Right) - layout.Min.X;
                        var t = layout.Max.Y - Vector3.Dot(p, layout.Up); // image rows run down, "up" runs up
                        uvs.Add(new Vector2(layout.X + Padding + s * density, layout.Y + Padding + t * density) / size);
                    }
                    indices[face * 3 + k] = at;
                }
                faceCharts[face] = c;
                if (surfaces != null && mesh.FaceSurfaces != null)
                {
                    surfaces[face] = mesh.FaceSurfaces[f];
                }
                face++;
            }
        }

        return new ScanAtlasMesh
        {
            Positions = positions.ToArray(),
            Normals = normals.ToArray(),
            Uvs = uvs.ToArray(),
            Indices = indices,
            FaceSurfaces = surfaces,
            FaceCharts = faceCharts,
            ChartCount = layouts.Count,
            Size = size,
            TexelsPerMetre = density,
        };
    }

    private sealed class ChartLayout
    {
        public required List<int> Faces;
        public Vector3 Right;
        public Vector3 Up;
        public Vector2 Min;
        public Vector2 Max;
        public int X;
        public int Y;
    }

    // Biggest triangles seed first, so walls and floors become one chart each instead of meeting halfway.
    private static List<List<int>> GrowCharts(ScanMeshPiece mesh, Vector3[] faceNormals, float[] faceAreas)
    {
        var edgeFaces = new Dictionary<(int, int), List<int>>();
        for (var f = 0; f < faceNormals.Length; f++)
        {
            for (var k = 0; k < 3; k++)
            {
                var a = mesh.Indices[f * 3 + k];
                var b = mesh.Indices[f * 3 + (k + 1) % 3];
                var key = a < b ? (a, b) : (b, a);
                if (!edgeFaces.TryGetValue(key, out var list))
                {
                    edgeFaces[key] = list = new List<int>(2);
                }
                list.Add(f);
            }
        }

        var chartOf = new int[faceNormals.Length];
        Array.Fill(chartOf, -1);
        var charts = new List<List<int>>();
        var order = Enumerable.Range(0, faceNormals.Length).OrderByDescending(f => faceAreas[f]).ToArray();
        var queue = new Queue<int>();
        foreach (var seed in order)
        {
            if (chartOf[seed] >= 0)
            {
                continue;
            }
            var chart = new List<int>();
            var id = charts.Count;
            charts.Add(chart);
            var normalSum = Vector3.Zero;
            chartOf[seed] = id;
            queue.Enqueue(seed);
            while (queue.TryDequeue(out var f))
            {
                chart.Add(f);
                normalSum += faceNormals[f] * faceAreas[f];
                var chartNormal = normalSum.LengthSquared() > 0 ? Vector3.Normalize(normalSum) : faceNormals[seed];
                for (var k = 0; k < 3; k++)
                {
                    var a = mesh.Indices[f * 3 + k];
                    var b = mesh.Indices[f * 3 + (k + 1) % 3];
                    foreach (var n in edgeFaces[a < b ? (a, b) : (b, a)])
                    {
                        // Within ~37 degrees of the chart's way: flat enough to project without folding over.
                        if (chartOf[n] < 0 && Vector3.Dot(faceNormals[n], chartNormal) >= ChartNormalAgreement
                            && Vector3.Dot(faceNormals[n], faceNormals[seed]) >= ChartNormalAgreement)
                        {
                            chartOf[n] = id;
                            queue.Enqueue(n);
                        }
                    }
                }
            }
        }
        return charts;
    }

    private static ChartLayout Flatten(ScanMeshPiece mesh, List<int> faces, Vector3[] faceNormals, float[] faceAreas)
    {
        var normal = Vector3.Zero;
        foreach (var f in faces)
        {
            normal += faceNormals[f] * faceAreas[f];
        }
        normal = normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : faceNormals[faces[0]];

        // Walls keep world up as the picture's up, so a poster is the right way round in the texture.
        var up = MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY - normal * normal.Y : Vector3.UnitZ - normal * normal.Z;
        up = Vector3.Normalize(up);
        var right = Vector3.Cross(up, normal);

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var f in faces)
        {
            for (var k = 0; k < 3; k++)
            {
                var p = mesh.Positions[mesh.Indices[f * 3 + k]];
                var q = new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up));
                min = Vector2.Min(min, q);
                max = Vector2.Max(max, q);
            }
        }
        return new ChartLayout { Faces = faces, Right = right, Up = up, Min = min, Max = max };
    }

    // Shelf packing at one density for every chart, starting from the density that would fill ~70% of the
    // atlas and backing off until everything fits.
    private static float Pack(List<ChartLayout> layouts, int size, float maxTexelsPerMetre)
    {
        var area = layouts.Sum(l => (double)(l.Max.X - l.Min.X) * (l.Max.Y - l.Min.Y));
        var density = (float)Math.Min(maxTexelsPerMetre, Math.Sqrt(size * (double)size * 0.7 / Math.Max(area, 1e-9)));
        var order = layouts.OrderByDescending(l => l.Max.Y - l.Min.Y).ToArray();
        for (var attempt = 0; attempt < 60; attempt++, density *= 0.93f)
        {
            int x = 0, y = 0, shelf = 0;
            var fits = true;
            foreach (var layout in order)
            {
                var w = (int)MathF.Ceiling((layout.Max.X - layout.Min.X) * density) + Padding * 2 + 1;
                var h = (int)MathF.Ceiling((layout.Max.Y - layout.Min.Y) * density) + Padding * 2 + 1;
                if (w > size)
                {
                    fits = false;
                    break;
                }
                if (x + w > size)
                {
                    x = 0;
                    y += shelf;
                    shelf = 0;
                }
                if (y + h > size)
                {
                    fits = false;
                    break;
                }
                layout.X = x;
                layout.Y = y;
                x += w;
                shelf = Math.Max(shelf, h);
            }
            if (fits)
            {
                return density;
            }
        }
        throw new InvalidOperationException("The scan has too many separate pieces to lay out on one texture.");
    }
}

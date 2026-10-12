using System.Numerics;
using ManaLidar;
using ManaUI;
using SharpGLTF.Schema2;

namespace ManaTests;

/// <summary>
/// ManaLidar's phone-free half: camera pixels to RGB, keyframes projecting the right way round, colouring that
/// respects what the LiDAR saw in front, a texture laid out and painted from the photos, and GLBs (vertex-coloured
/// and textured) that Mana's own loader reads back unlit.
/// </summary>
public static class ScanExportChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- camera picture format
        var rgb = Convert(y: 128, cb: 128, cr: 128);
        Check("neutral YCbCr is grey", rgb is [128, 128, 128], string.Join(",", rgb));
        rgb = Convert(y: 76, cb: 85, cr: 255);
        Check("BT.601 red comes out red", rgb[0] > 240 && rgb[1] < 15 && rgb[2] < 15, string.Join(",", rgb));

        // ---------------------------------------------------------------- projection
        var camera = Keyframe(left: new byte[] { 255, 0, 0 }, right: new byte[] { 0, 0, 255 });
        Check("a point straight ahead lands on the principal point",
            camera.Project(new Vector3(0, 0, -2), out var centre, out var distance)
                && Vector2.Distance(centre, new Vector2(camera.Cx, camera.Cy)) < 1e-3f && Math.Abs(distance - 2) < 1e-5f,
            $"{centre} at {distance}");
        camera.Project(new Vector3(0.5f, 0.5f, -2), out var upRight, out _);
        Check("right is +x in the image and up is a smaller row", upRight.X > camera.Cx && upRight.Y < camera.Cy,
            upRight.ToString());
        Check("a point behind the camera does not project", !camera.Project(new Vector3(0, 0, 2), out _, out _));

        // ---------------------------------------------------------------- colouring
        var wall = Wall(z: -2f);
        var seen = new bool[wall.Positions.Length];
        var colors = ScanColorizer.Colorize(wall, new[] { camera }, seen);
        Check("every vertex in view is seen", seen.All(s => s), $"{seen.Count(s => s)}/{seen.Length}");
        Check("the left of the wall takes the left of the photo",
            colors[0].X > 0.9f && colors[0].Z < 0.1f, colors[0].ToString());
        Check("and the right the right", colors[1].Z > 0.9f && colors[1].X < 0.1f, colors[1].ToString());

        // The LiDAR saw something 1 m away where the wall is 2 m away: that photo shows the something, not the wall.
        var blocked = Keyframe(left: new byte[] { 255, 0, 0 }, right: new byte[] { 0, 0, 255 }, depth: 1f);
        Array.Clear(seen);
        colors = ScanColorizer.Colorize(wall, new[] { blocked }, seen);
        Check("an occluded vertex is not painted with the occluder", seen.All(s => !s), $"{seen.Count(s => s)} seen");
        Check("and falls back to its surface colour",
            colors[0] == ScanColorizer.SurfaceColor(ScanSurface.Wall), colors[0].ToString());

        // A wall seen edge-on from behind says nothing about its front.
        var behind = Wall(z: -2f, normal: -Vector3.UnitZ);
        Array.Clear(seen);
        ScanColorizer.Colorize(behind, new[] { camera }, seen);
        Check("a surface facing away is not coloured", seen.All(s => !s));

        // A smeared photo and a crisp one from the same spot: the crisp one should decide the colour.
        var smeared = Keyframe(left: new byte[] { 255, 0, 0 }, right: new byte[] { 255, 0, 0 });
        var crisp = Keyframe(left: new byte[] { 0, 0, 255 }, right: new byte[] { 0, 0, 255 }, stripes: true);
        colors = ScanColorizer.Colorize(Wall(z: -2f), new[] { smeared, crisp });
        Check("a crisp photo outweighs a smeared one", colors[0].Z > colors[0].X * 3,
            $"{colors[0]} (sharpness {smeared.Sharpness:F1} vs {crisp.Sharpness:F1})");

        // ---------------------------------------------------------------- texture layout
        var box = Box();
        var boxAtlas = ScanAtlas.Build(box, 256, 1000f);
        var rects = Enumerable.Range(0, boxAtlas.ChartCount).Select(c =>
        {
            var uv = Enumerable.Range(0, boxAtlas.FaceCharts.Length).Where(f => boxAtlas.FaceCharts[f] == c)
                .SelectMany(f => new[] { boxAtlas.Uvs[boxAtlas.Indices[f * 3]], boxAtlas.Uvs[boxAtlas.Indices[f * 3 + 1]], boxAtlas.Uvs[boxAtlas.Indices[f * 3 + 2]] })
                .ToArray();
            return (Min: uv.Aggregate(Vector2.Min), Max: uv.Aggregate(Vector2.Max));
        }).ToArray();
        Check("a box lays out as one chart a side", boxAtlas.ChartCount == 6, $"{boxAtlas.ChartCount} charts");
        Check("every chart is inside the texture", rects.All(r => r.Min.X >= 0 && r.Min.Y >= 0 && r.Max.X <= 1 && r.Max.Y <= 1));
        var overlaps = rects.SelectMany((a, i) => rects.Skip(i + 1).Where(b =>
            a.Min.X < b.Max.X && b.Min.X < a.Max.X && a.Min.Y < b.Max.Y && b.Min.Y < a.Max.Y)).Count();
        Check("and no two overlap", overlaps == 0, $"{overlaps} overlapping");
        Check("at one texel density for all", boxAtlas.TexelsPerMetre > 30, $"{boxAtlas.TexelsPerMetre:F0} texels a metre");

        // ---------------------------------------------------------------- baking
        // A 1.2 x 0.8 m wall 2 m in front of the camera: its left half photographed red, its right half blue.
        var bigWall = Grid(1.2f, 0.8f, 24, 16, z: -2f);
        var bakedAtlas = ScanAtlas.Build(ScanMeshSimplifier.Simplify(bigWall, 0.004f), 256, 1000f);
        var baked = ScanTextureBaker.Bake(bakedAtlas, new[] { camera });
        Vector3 TexelNear(Vector3 world)
        {
            var nearest = Enumerable.Range(0, bakedAtlas.Positions.Length).MinBy(i => Vector3.Distance(bakedAtlas.Positions[i], world));
            var centre = bakedAtlas.Uvs.Aggregate(Vector2.Zero, (a, b) => a + b) / bakedAtlas.Uvs.Length;
            // A few texels in from the corner, toward the chart's middle: inside, never on its edge.
            var uv = bakedAtlas.Uvs[nearest] + Vector2.Normalize(centre - bakedAtlas.Uvs[nearest]) * 6f / bakedAtlas.Size;
            var at = ((int)(uv.Y * bakedAtlas.Size) * bakedAtlas.Size + (int)(uv.X * bakedAtlas.Size)) * 3;
            return new Vector3(baked[at], baked[at + 1], baked[at + 2]) / 255f;
        }
        var leftTexel = TexelNear(new Vector3(-0.6f, -0.4f, -2f));
        var rightTexel = TexelNear(new Vector3(0.6f, 0.4f, -2f));
        Check("the texture is red where the photo was", leftTexel.X > 0.9f && leftTexel.Z < 0.1f, leftTexel.ToString());
        Check("and blue where it was blue", rightTexel.Z > 0.9f && rightTexel.X < 0.1f, rightTexel.ToString());

        // ---------------------------------------------------------------- keyframe pacing
        var recording = new ScanRecording();
        Check("the first frame is a keyframe", recording.WantsKeyframe(Matrix4x4.Identity, 0));
        recording.AddKeyframe(camera);
        Check("standing still is not", !recording.WantsKeyframe(Matrix4x4.Identity, 5));
        Check("walking on is", recording.WantsKeyframe(Matrix4x4.CreateTranslation(0.3f, 0, 0), 5));
        Check("turning is", recording.WantsKeyframe(Matrix4x4.CreateRotationY(0.3f), 5));
        Check("but not faster than the interval", !recording.WantsKeyframe(Matrix4x4.CreateTranslation(1, 0, 0), 0.1));

        // ---------------------------------------------------------------- GLB through Mana's loader
        var floor = Floor(height: 1.25f, offsetX: 10f);
        var path = Path.Combine(Path.GetTempPath(), $"mana-scan-{Guid.NewGuid():N}.glb");
        var texturedPath = Path.Combine(Path.GetTempPath(), $"mana-scan-{Guid.NewGuid():N}.glb");
        try
        {
            ScanGltfExporter.SaveGlb(path, new[] { wall, floor }, new[] { camera }, new ScanExportOptions { Texture = false });

            var model = ModelRoot.Load(path);
            var primitive = model.LogicalMeshes[0].Primitives[0];
            Check("vertex colouring keeps every vertex of both pieces",
                primitive.VertexAccessors["POSITION"].Count == wall.Positions.Length + floor.Positions.Length
                    && primitive.IndexAccessor.Count == wall.Indices.Length + floor.Indices.Length,
                $"{primitive.VertexAccessors["POSITION"].Count} vertices");
            Check("its material is unlit", model.LogicalMaterials[0].Unlit);

            // The floor piece follows the wall's three vertices.
            var floorY = primitive.VertexAccessors["POSITION"].AsVector3Array().Skip(wall.Positions.Length).Max(p => p.Y);
            Check("recentring stands the floor at y = 0", Math.Abs(floorY) < 1e-4f, $"floor at {floorY}");

            var world = new Mana.ManaWorld();
            var context = new Mana.Code.ManaContext(null!, null!, world) { DeferGpuUploads = true };
            var loader = new Mana.Code.Shapes.Loaders.ManaSharpGltfLoader(context);
            loader.LoadFile(path);
            Check("Mana loads the scan as an unlit material", loader.Materials.Count == 1 && loader.Materials[0].Unlit,
                $"{loader.Materials.Count} materials");
            var loadedColors = loader.Meshes.FirstOrDefault()?.CpuData?.Colors;
            Check("with the scan's vertex colours", loadedColors is { Length: > 0 }
                && loadedColors[0].X > 0.9f && loadedColors[0].Z < 0.1f, loadedColors?.FirstOrDefault().ToString() ?? "none");

            // The default: simplified and textured.
            ScanGltfExporter.SaveGlb(texturedPath, new[] { bigWall }, new[] { camera }, new ScanExportOptions { TextureSize = 256 });
            var textured = ModelRoot.Load(texturedPath);
            var texturedPrimitive = textured.LogicalMeshes[0].Primitives[0];
            Check("a textured export carries UVs instead of vertex colours",
                texturedPrimitive.VertexAccessors.ContainsKey("TEXCOORD_0") && !texturedPrimitive.VertexAccessors.ContainsKey("COLOR_0"));
            Check("and simplifies the flat wall to a few triangles", texturedPrimitive.IndexAccessor.Count / 3 <= 8,
                $"{bigWall.TriangleCount} -> {texturedPrimitive.IndexAccessor.Count / 3}");
            Check("with its texture embedded as a PNG", textured.LogicalImages.Count == 1 && textured.LogicalImages[0].Content.IsPng);

            var texturedLoader = new Mana.Code.Shapes.Loaders.ManaSharpGltfLoader(context);
            texturedLoader.LoadFile(texturedPath);
            Check("Mana loads it with the texture on its material",
                texturedLoader.Materials.Count == 1 && texturedLoader.Materials[0].Unlit && texturedLoader.Materials[0].Albedo.TextureValue != null,
                texturedLoader.Materials.FirstOrDefault()?.Albedo.TextureValue?.OriginalFilename ?? "no texture");
        }
        catch (Exception ex)
        {
            Check("a scan exports and loads", false, ex.Message);
        }
        finally
        {
            File.Delete(path);
            File.Delete(texturedPath);
        }
    }

    // A flat w x h grid facing +Z, centred on the view axis at depth z.
    private static ScanMeshPiece Grid(float width, float height, int nx, int ny, float z)
    {
        var positions = new List<Vector3>();
        for (var y = 0; y <= ny; y++)
        {
            for (var x = 0; x <= nx; x++)
            {
                positions.Add(new Vector3(width * (x / (float)nx - 0.5f), height * (y / (float)ny - 0.5f), z));
            }
        }
        var indices = new List<int>();
        for (var y = 0; y < ny; y++)
        {
            for (var x = 0; x < nx; x++)
            {
                var a = y * (nx + 1) + x;
                indices.AddRange(new[] { a, a + 1, a + nx + 2, a, a + nx + 2, a + nx + 1 });
            }
        }
        return new ScanMeshPiece(Guid.NewGuid(), positions.ToArray(), positions.Select(_ => Vector3.UnitZ).ToArray(), indices.ToArray(),
            Enumerable.Repeat(ScanSurface.Wall, nx * ny * 2).ToArray());
    }

    // A closed 1 m box, faces outward, each side two triangles.
    private static ScanMeshPiece Box()
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        foreach (var n in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
        {
            var u = MathF.Abs(n.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var v = Vector3.Cross(n, u);
            var b = positions.Count;
            foreach (var (su, sv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                positions.Add((n + u * su + v * sv) * 0.5f);
                normals.Add(n);
            }
            // u x v = n, so (0, 1, 2) runs counter-clockwise seen from outside.
            indices.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        return new ScanMeshPiece(Guid.NewGuid(), positions.ToArray(), normals.ToArray(), indices.ToArray());
    }

    private static byte[] Convert(byte y, byte cb, byte cr)
    {
        var luma = Enumerable.Repeat(y, 16).ToArray();
        var chroma = new byte[] { cb, cr, cb, cr, cb, cr, cb, cr };
        var rgb = new byte[3];
        YCbCrImage.DownsampleToRgb(luma, 4, 4, 4, chroma, 4, 1, 1, rgb);
        return rgb;
    }

    // A 64x48 photo from the origin looking down -Z, its left half one colour and its right half another.
    private static ScanKeyframe Keyframe(byte[] left, byte[] right, float depth = float.NaN, bool stripes = false)
    {
        const int w = 64, h = 48;
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                (x < w / 2 ? left : right).CopyTo(rgb, (y * w + x) * 3);
                if (stripes && x % 2 == 0)
                {
                    rgb[(y * w + x) * 3 + 2] /= 2; // fine detail, which a smeared photo lacks
                }
            }
        }
        float[]? depthMap = float.IsNaN(depth) ? null : Enumerable.Repeat(depth, 16 * 12).ToArray();
        return new ScanKeyframe(w, h, rgb, Matrix4x4.Identity, 50, 50, w / 2f, h / 2f, 0,
            depthMap == null ? 0 : 16, depthMap == null ? 0 : 12, depthMap);
    }

    // Two points either side of the view centre, as a thin wall facing the camera.
    private static ScanMeshPiece Wall(float z, Vector3? normal = null)
    {
        var n = normal ?? Vector3.UnitZ;
        var positions = new[] { new Vector3(-0.6f, 0, z), new Vector3(0.6f, 0, z), new Vector3(0, 0.4f, z) };
        return new ScanMeshPiece(Guid.NewGuid(), positions, new[] { n, n, n }, new[] { 0, 1, 2 },
            new[] { ScanSurface.Wall });
    }

    private static ScanMeshPiece Floor(float height, float offsetX)
    {
        var positions = new List<Vector3>();
        var indices = new List<int>();
        for (var i = 0; i < 30; i++)
        {
            var b = positions.Count;
            positions.Add(new Vector3(offsetX + i * 0.1f, height, 0));
            positions.Add(new Vector3(offsetX + i * 0.1f, height, -1));
            positions.Add(new Vector3(offsetX + i * 0.1f + 0.1f, height, 0));
            indices.AddRange(new[] { b, b + 1, b + 2 });
        }
        return new ScanMeshPiece(Guid.NewGuid(), positions.ToArray(), positions.Select(_ => Vector3.UnitY).ToArray(),
            indices.ToArray(), Enumerable.Repeat(ScanSurface.Floor, 30).ToArray());
    }
}

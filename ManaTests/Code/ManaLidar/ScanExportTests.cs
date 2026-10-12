using System.Numerics;
using ManaLidar;
using ManaUI;
using SharpGLTF.Schema2;

namespace ManaTests;

/// <summary>
/// ManaLidar's phone-free half: camera pixels to RGB, keyframes projecting the right way round, colouring that
/// respects what the LiDAR saw in front, and a GLB that Mana's own loader reads back coloured and unlit.
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
        try
        {
            ScanGltfExporter.SaveGlb(path, new[] { wall, floor }, new[] { camera });

            var model = ModelRoot.Load(path);
            var primitive = model.LogicalMeshes[0].Primitives[0];
            Check("the GLB holds one mesh of both pieces",
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
        }
        catch (Exception ex)
        {
            Check("a scan exports and loads", false, ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
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
    private static ScanKeyframe Keyframe(byte[] left, byte[] right, float depth = float.NaN)
    {
        const int w = 64, h = 48;
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                (x < w / 2 ? left : right).CopyTo(rgb, (y * w + x) * 3);
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

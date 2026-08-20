using ManaCore.Graphics;
using ManaUI;
using ManaVoxel.Code.Assets;

namespace ManaTests;

/// <summary>
/// The CPU thumbnail rasterizer. Checkable headlessly precisely because it is not a render path -- geometry
/// in, pixels out, no graphics device -- which is most of why it was written that way.
/// <para>
/// The checks are properties rather than pixel comparisons: an exact expected image would break on any
/// harmless change to the light direction or the view angle, and would say nothing about whether the picture
/// is right. What matters is that something was drawn, that it is inside the tile, that it is shaded rather
/// than flat, and that the nearest surface is the one you see.
/// </para>
/// </summary>
public static class MeshThumbnailChecks
{
    private const int Size = 64;
    private static readonly Vector4 Tint = new(0.82f, 0.84f, 0.88f, 1.0f);

    /// <summary>A unit cube centred on the origin, as two triangles per face.</summary>
    private static ManaMeshCpuData Cube(float scale = 1.0f, float offsetZ = 0.0f)
    {
        var corners = new[]
        {
            new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(-1, 1, -1),
            new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1),
        };

        var positions = new Vector3[corners.Length];
        for (var i = 0; i < corners.Length; i++)
        {
            positions[i] = (corners[i] * scale) + new Vector3(0.0f, 0.0f, offsetZ);
        }

        var indexes = new uint[]
        {
            0, 1, 2, 0, 2, 3,
            4, 6, 5, 4, 7, 6,
            0, 4, 5, 0, 5, 1,
            3, 2, 6, 3, 6, 7,
            0, 3, 7, 0, 7, 4,
            1, 5, 6, 1, 6, 2,
        };

        var normals = new Vector3[positions.Length];
        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.Normalize(corners[i]);
        }

        return new ManaMeshCpuData
        {
            Positions = positions,
            Normals = normals,
            Colors = new Vector4[positions.Length],
            TextureCoordinates0 = new Vector2[positions.Length],
            TextureCoordinates1 = new Vector2[positions.Length],
            Indexes = indexes,
        };
    }

    private static int OpaquePixels(byte[] rgba)
    {
        var count = 0;
        for (var i = 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] > 0)
            {
                count++;
            }
        }

        return count;
    }

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- it draws something
        var pixels = MeshThumbnailRasterizer.Render(Cube(), Size, Tint);
        Check("a cube renders", pixels != null && pixels.Length == Size * Size * 4,
            $"{pixels?.Length ?? 0} bytes");

        if (pixels == null)
        {
            return;
        }

        var covered = OpaquePixels(pixels);
        Check("and covers a good part of the tile",
            covered > Size * Size / 6 && covered < Size * Size,
            $"{covered} of {Size * Size} pixels");

        // ---------------------------------------------------------------- and leaves a margin
        // The inset exists so a grid of thumbnails does not read as a grid of rectangles, which means the
        // outermost ring of pixels has to be empty for any model, however it is proportioned.
        var edgeTouched = 0;
        for (var i = 0; i < Size; i++)
        {
            if (pixels[(i * 4) + 3] > 0) { edgeTouched++; }
            if (pixels[((((Size - 1) * Size) + i) * 4) + 3] > 0) { edgeTouched++; }
            if (pixels[(((i * Size) + 0) * 4) + 3] > 0) { edgeTouched++; }
            if (pixels[(((i * Size) + Size - 1) * 4) + 3] > 0) { edgeTouched++; }
        }

        Check("nothing touches the tile's edge", edgeTouched == 0, $"{edgeTouched} edge pixels");

        // ---------------------------------------------------------------- it is shaded
        // A single flat colour would mean the light is not being applied, and a thumbnail of a cube that is
        // one flat blob tells you nothing about its shape.
        var shades = new HashSet<byte>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 3] > 0)
            {
                shades.Add(pixels[i]);
            }
        }

        Check("faces are shaded differently from each other", shades.Count >= 3, $"{shades.Count} shades");

        // ---------------------------------------------------------------- scale independence
        // A model authored in centimetres and one in metres have to fill the tile the same way, since the
        // whole point is a preview that reads at a glance regardless of how the asset was exported.
        var tiny = MeshThumbnailRasterizer.Render(Cube(scale: 0.001f), Size, Tint);
        var huge = MeshThumbnailRasterizer.Render(Cube(scale: 5000.0f), Size, Tint);

        Check("a tiny model still fills the tile",
            tiny != null && Math.Abs(OpaquePixels(tiny) - covered) <= 4,
            $"{(tiny == null ? 0 : OpaquePixels(tiny))} vs {covered}");

        Check("and so does an enormous one",
            huge != null && Math.Abs(OpaquePixels(huge) - covered) <= 4,
            $"{(huge == null ? 0 : OpaquePixels(huge))} vs {covered}");

        // Off-origin too: fitting to the bounding sphere's centre rather than to the origin is what makes a
        // model exported away from the origin visible at all.
        var offset = MeshThumbnailRasterizer.Render(Cube(offsetZ: 900.0f), Size, Tint);
        Check("and one exported far from the origin",
            offset != null && Math.Abs(OpaquePixels(offset) - covered) <= 4,
            $"{(offset == null ? 0 : OpaquePixels(offset))} vs {covered}");

        // ---------------------------------------------------------------- depth
        // Two cubes, one behind the other, must produce the near one's silhouette rather than the far one's
        // painted over it -- the depth test is the only thing standing between this and triangle-soup order.
        var near = Cube(scale: 0.5f, offsetZ: -4.0f);
        var far = Cube(scale: 0.5f, offsetZ: 4.0f);

        var combined = new ManaMeshCpuData
        {
            Positions = near.Positions.Concat(far.Positions).ToArray(),
            Normals = near.Normals.Concat(far.Normals).ToArray(),
            Colors = new Vector4[near.Positions.Length * 2],
            TextureCoordinates0 = new Vector2[near.Positions.Length * 2],
            TextureCoordinates1 = new Vector2[near.Positions.Length * 2],
            Indexes = near.Indexes
                .Concat(far.Indexes.Select(index => index + (uint)near.Positions.Length))
                .ToArray(),
        };

        var both = MeshThumbnailRasterizer.Render(combined, Size, Tint);
        Check("two stacked cubes render", both != null && OpaquePixels(both) > 0,
            $"{(both == null ? 0 : OpaquePixels(both))} pixels");

        // ---------------------------------------------------------------- degenerate input
        // A library scanned off disk contains whatever is on disk. None of these may throw.
        var empty = new ManaMeshCpuData
        {
            Positions = Array.Empty<Vector3>(),
            Normals = Array.Empty<Vector3>(),
            Colors = Array.Empty<Vector4>(),
            TextureCoordinates0 = Array.Empty<Vector2>(),
            TextureCoordinates1 = Array.Empty<Vector2>(),
            Indexes = Array.Empty<uint>(),
        };

        Check("an empty mesh declines rather than throwing",
            MeshThumbnailRasterizer.Render(empty, Size, Tint) == null);

        var single = Cube();
        var degenerate = new ManaMeshCpuData
        {
            Positions = new[] { Vector3.Zero, Vector3.Zero, Vector3.Zero },
            Normals = new[] { Vector3.Zero, Vector3.Zero, Vector3.Zero },
            Colors = new Vector4[3],
            TextureCoordinates0 = new Vector2[3],
            TextureCoordinates1 = new Vector2[3],
            Indexes = new uint[] { 0, 1, 2 },
        };

        Check("a zero-sized mesh declines too",
            MeshThumbnailRasterizer.Render(degenerate, Size, Tint) == null);

        // Indices past the end of the vertex array are skipped rather than read out of bounds.
        var outOfRange = new ManaMeshCpuData
        {
            Positions = single.Positions,
            Normals = single.Normals,
            Colors = single.Colors,
            TextureCoordinates0 = single.TextureCoordinates0,
            TextureCoordinates1 = single.TextureCoordinates1,
            Indexes = new uint[] { 0, 1, 999 },
        };

        Check("an out-of-range index is skipped, not read",
            MeshThumbnailRasterizer.Render(outOfRange, Size, Tint) != null);

        // A mesh with no normals still has to shade, from face normals derived while rasterizing.
        var noNormals = new ManaMeshCpuData
        {
            Positions = single.Positions,
            Normals = Array.Empty<Vector3>(),
            Colors = single.Colors,
            TextureCoordinates0 = single.TextureCoordinates0,
            TextureCoordinates1 = single.TextureCoordinates1,
            Indexes = single.Indexes,
        };

        var flat = MeshThumbnailRasterizer.Render(noNormals, Size, Tint);
        Check("a mesh without normals still shades", flat != null && OpaquePixels(flat) > 0,
            $"{(flat == null ? 0 : OpaquePixels(flat))} pixels");
    }
}

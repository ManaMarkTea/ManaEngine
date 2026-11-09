using System.IO.Compression;
using System.Text;
using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes.Loaders;
using ManaCore.Graphics;
using ManaUI;
using ManaEditor.Code.Assets;
using ManaEditor.Code.Services;
using ManaCore.Imaging;

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
    private const int Size = MeshThumbnailCache.ThumbnailSize;
    private static readonly Vector4 Tint = new(0.82f, 0.84f, 0.88f, 1.0f);
    private static readonly Vector3 ThumbnailForwardWorld = Vector3.Normalize(new Vector3(-1.0f, -1.35f, -1.0f));
    private static readonly Vector3 ThumbnailUpWorld = Vector3.UnitY;
    private static readonly Matrix4x4 ThumbnailView = CreateThumbnailView();
    private static readonly Vector3 ThumbnailLightDirection = Vector3.Normalize(Vector3.TransformNormal(
        Vector3.Normalize(new Vector3(-0.45f, 0.85f, 0.35f)),
        ThumbnailView));

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
            Colors = Array.Empty<Vector4>(),
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

        // ---------------------------------------------------------------- a picture, for a prefab drawn by a Sprite
        var coinPrefab = ManaEditor.Code.Assets.ManaPrefab.Load(Path.Combine(ManaEditor.Code.Assets.ManaContentLibrary.Root, "Prefabs", "Coin 2D.manaprefab"));
        var coinIcon = MeshThumbnailCache.SpritePicture(coinPrefab.Root);
        var middle = (MeshThumbnailCache.ThumbnailSize / 2 * MeshThumbnailCache.ThumbnailSize + MeshThumbnailCache.ThumbnailSize / 2) * 4;
        Check("a prefab drawn by a Sprite shows its picture, the coin solid in the middle and clear at the corner",
            coinIcon != null && coinIcon.Length == MeshThumbnailCache.ThumbnailSize * MeshThumbnailCache.ThumbnailSize * 4 && coinIcon[middle + 3] > 200 && coinIcon[3] == 0,
            coinIcon == null ? "no picture" : $"centre alpha {coinIcon[middle + 3]}, corner alpha {coinIcon[3]}");

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

        // The CPU fallback loader used by thumbnails has to flatten the glTF scene in the same rest pose as
        // the real importer. Some asset packs store mesh vertices in a local basis and rotate the node upright
        // in the scene; ignoring that node transform makes the thumbnail lie sideways while the dropped model
        // is correct.
        CheckCpuOnlyLoaderMatchesNodeTransforms(Check);
        CheckKenneyToiletNodeRotations(Check);
        CheckStandaloneGenerationWithResolver(Check);
        CheckPrebuiltPreviewIsPreferred(Check);

        // ---------------------------------------------------------------- depth
        // Two cubes, one behind the other, must produce the near one's silhouette rather than the far one's
        // painted over it -- the depth test is the only thing standing between this and triangle-soup order.
        var near = Cube(scale: 0.5f, offsetZ: -4.0f);
        var far = Cube(scale: 0.5f, offsetZ: 4.0f);

        var combined = new ManaMeshCpuData
        {
            Positions = near.Positions.Concat(far.Positions).ToArray(),
            Normals = near.Normals.Concat(far.Normals).ToArray(),
            Colors = Array.Empty<Vector4>(),
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
            Colors = Array.Empty<Vector4>(),
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

        // ---------------------------------------------------------------- shading direction
        // Two mesh-space normals chosen so that, after the rasterizer's fixed top-down isometric view, one
        // lands well-aligned with its light direction and the other well AGAINST it -- while both already
        // face the camera, so neither needs the camera-facing flip and this isolates the light term alone.
        //
        // Rendered each as a single triangle filling most of the tile, so the average red channel over
        // opaque pixels is that face's brightness. This is a regression test for one exact numeric bug
        // (MathF.Abs(lambert) instead of a clamp), so it deliberately mirrors the rasterizer's rotation and
        // light constants rather than avoiding them -- there is no way to aim a specific direction at "well
        // lit" vs "well shadowed" without knowing where the light and camera actually are.
        var view = ThumbnailView;
        var lightDirection = ThumbnailLightDirection;
        Matrix4x4.Invert(view, out var inverseView);

        // Already facing the camera (negative view-space Z) and pointing opposite the light: the darkest a
        // surface can be. Literally -lightDirection, since the light's own Z is positive.
        var shadowedMeshNormal = Vector3.Transform(-lightDirection, inverseView);

        // Same idea, Z negated so it also faces the camera without a flip, but otherwise aligned with the
        // light -- well lit.
        var litMeshNormal = Vector3.Transform(
            new Vector3(lightDirection.X, lightDirection.Y, -lightDirection.Z), inverseView);

        var lit = SingleTriangle(litMeshNormal, 2.0f);
        var shadowed = SingleTriangle(shadowedMeshNormal, 2.0f);

        var litPixels = MeshThumbnailRasterizer.Render(lit, Size, Tint);
        var shadowedPixels = MeshThumbnailRasterizer.Render(shadowed, Size, Tint);

        Check("both faces render", litPixels != null && shadowedPixels != null);

        if (litPixels != null && shadowedPixels != null)
        {
            var litShade = AverageRed(litPixels);
            var shadowedShade = AverageRed(shadowedPixels);

            // Under the old MathF.Abs(lambert) shading these two would have rendered at THE SAME brightness
            // -- |N.L| does not distinguish "facing the light" from "facing directly away from it" -- which
            // is exactly what makes a convex bump read as a dent. Correct shading must not just differ, it
            // must order them the intuitive way: the face angled toward the light is the brighter one.
            Check("a face facing away from the light renders darker than one facing toward it",
                shadowedShade < litShade,
                $"lit {litShade:F0}, shadowed {shadowedShade:F0}");

            Check("and darker by a wide margin, not a rounding difference",
                shadowedShade < litShade * 0.6,
                $"lit {litShade:F0}, shadowed {shadowedShade:F0}");
        }

        // ---------------------------------------------------------------- colour priority
        // A channel dominance check rather than an exact colour match: shading multiplies every channel by
        // the same factor, so the ratio between channels survives regardless of how bright the face ended
        // up -- which means these do not need to worry about facing the light at all, only about facing
        // the camera (SingleTriangle's default direction does).
        var camera = new Vector3(0.3f, 0.2f, -1.0f);
        var red = new Vector4(1.0f, 0.0f, 0.0f, 1.0f);
        var green = new Vector4(0.0f, 1.0f, 0.0f, 1.0f);
        var redTexture = SolidColorPng(255, 0, 0);
        var uvSpread = new[] { new Vector2(0.0f, 0.0f), new Vector2(1.0f, 0.0f), new Vector2(0.0f, 1.0f) };

        var vertexColored = SingleTriangle(camera, 2.0f, colors: new[] { red, red, red });
        Check("vertex colour drives the preview, not the fallback tint",
            IsChannelDominant(MeshThumbnailRasterizer.Render(vertexColored, Size, Tint), channel: 0));

        var factored = SingleTriangle(camera, 2.0f, albedoFactor: green);
        Check("a material's base colour factor drives the preview when there are no vertex colours",
            IsChannelDominant(MeshThumbnailRasterizer.Render(factored, Size, Tint), channel: 1));

        var textured = SingleTriangle(camera, 2.0f, albedoTextureBytes: redTexture, uvs: uvSpread);
        Check("a base colour texture drives the preview",
            IsChannelDominant(MeshThumbnailRasterizer.Render(textured, Size, Tint), channel: 0));

        var texturedOverVertexColor = SingleTriangle(
            camera, 2.0f, colors: new[] { green, green, green }, albedoTextureBytes: redTexture, uvs: uvSpread);
        Check("a texture outranks vertex colour when a mesh somehow carries both",
            IsChannelDominant(MeshThumbnailRasterizer.Render(texturedOverVertexColor, Size, Tint), channel: 0));

        var whiteTexture = SolidColorPng(255, 255, 255);
        var factorOverTexture = SingleTriangle(
            camera, 2.0f, albedoFactor: green, albedoTextureBytes: whiteTexture, uvs: uvSpread);
        Check("a base colour factor multiplies a texture rather than being ignored, matching glTF",
            IsChannelDominant(MeshThumbnailRasterizer.Render(factorOverTexture, Size, Tint), channel: 1));

        // ---------------------------------------------------------------- alpha cutout and the depth test
        // A texture is usually a shared sheet with transparent padding between sprites, and PNG encoders
        // routinely zero out the RGB under a transparent pixel. A fragment sampled there must not become a
        // solid black pixel -- and because it is not really part of the surface, it must not win the depth
        // test against whatever is actually behind it either. Both used to happen at once: an opaque black
        // hole that also incorrectly occluded.
        var fullyTransparent = SolidColorPng(0, 0, 0, alpha: 0);
        var invisible = SingleTriangle(camera, 2.0f, albedoTextureBytes: fullyTransparent, uvs: uvSpread);
        var invisiblePixels = MeshThumbnailRasterizer.Render(invisible, Size, Tint);
        Check("a fully transparent texture paints nothing, not black",
            invisiblePixels != null && OpaquePixels(invisiblePixels) == 0);

        // Left half opaque blue, right half fully transparent -- one texture, two UV regions, so both
        // triangles below share the single albedo a mesh actually carries.
        var splitTexture = SplitColorPng(0, 0, 255);
        var opaqueHalfUvs = new[] { new Vector2(0.1f, 0.1f), new Vector2(0.4f, 0.1f), new Vector2(0.1f, 0.9f) };
        var transparentHalfUvs = new[] { new Vector2(0.6f, 0.1f), new Vector2(0.9f, 0.1f), new Vector2(0.6f, 0.9f) };

        // Moving further along this axis increases the rotated (rasterizer-space) Z, i.e. moves away from
        // the viewer -- computed rather than guessed, so "near" and "far" below are not a coin flip.
        var cameraDirection = Vector3.Normalize(camera);
        var awayFromViewer = cameraDirection * MathF.Sign(Vector3.Transform(cameraDirection, view).Z);

        // Larger and further back, so it fills the tile behind the smaller near triangle regardless of the
        // small screen-space shift the offset picks up from the view rotation.
        var farTriangle = SingleTriangle(
            camera, 6.0f, albedoTextureBytes: splitTexture, uvs: opaqueHalfUvs, offset: awayFromViewer * 6.0f);

        // Same texture, sampling its transparent half -- this triangle should contribute nothing at all.
        var nearCutout = SingleTriangle(
            camera, 2.0f, albedoTextureBytes: splitTexture, uvs: transparentHalfUvs, offset: awayFromViewer * 1.0f);

        var layered = MeshThumbnailRasterizer.Render(Combine(nearCutout, farTriangle), Size, Tint);
        Check("nothing paints black where the near triangle's cutout should reveal what is behind it",
            HasNoBlackOpaquePixels(layered));

        Check("and what is behind it is genuinely visible there, not occluded by the invisible cutout",
            IsChannelDominant(layered, channel: 2));
    }

    private static void CheckCpuOnlyLoaderMatchesNodeTransforms(Action<string, bool, string> Check)
    {
        var gltf = Encoding.UTF8.GetBytes(NodeRotatedTriangleGltf());
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var fullLoader = new ManaSharpGltfLoader(context);
        var cpuLoader = new ManaSharpGltfLoader(context);

        try
        {
            var root = fullLoader.LoadFileFromMemory(gltf, "node-rotated.gltf");
            root.UpdateTransform();

            var renderInstances = new List<ManaRenderMeshInstance>();
            root.CollectRenderMeshInstances(renderInstances);
            var fullBounds = BoundsOf(renderInstances.SelectMany(instance =>
                instance.MeshData.Positions.Select(position => Vector3.Transform(position, instance.WorldTransform))));

            // The thumbnail cache's own flatten step, not a second loader method: ManaSharpGltfLoader has
            // no CPU-only fast path the way Assimp's ManaGltfLoader did, so MeshThumbnailCache builds the
            // preview mesh by walking the full scene graph and baking node transforms in itself -- this is
            // exactly what that walk is meant to get right.
            var cpuRoot = cpuLoader.LoadFileFromMemory(gltf, "node-rotated.gltf");
            var cpuMesh = MeshThumbnailCache.FlattenSharpGltfSceneToCpuMesh(cpuRoot);
            var cpuBounds = BoundsOf(cpuMesh?.Positions ?? Array.Empty<Vector3>());

            Check("CPU fallback thumbnails compose glTF node transforms",
                NearlyEqual(cpuBounds.Min, fullBounds.Min) && NearlyEqual(cpuBounds.Max, fullBounds.Max),
                $"full {Format(fullBounds.Min)}..{Format(fullBounds.Max)}, cpu {Format(cpuBounds.Min)}..{Format(cpuBounds.Max)}");
        }
        catch (Exception ex)
        {
            Check("CPU fallback thumbnails compose glTF node transforms", false, ex.Message);
        }
    }

    private static string NodeRotatedTriangleGltf()
    {
        var positions = new[]
        {
            0.0f, 0.0f, 0.0f,
            1.0f, 0.0f, 0.0f,
            0.0f, 0.0f, 2.0f
        };

        var bytes = new byte[positions.Length * sizeof(float)];
        Buffer.BlockCopy(positions, 0, bytes, 0, bytes.Length);
        var base64 = Convert.ToBase64String(bytes);

        return $$"""
        {
          "asset": { "version": "2.0" },
          "buffers": [
            { "uri": "data:application/octet-stream;base64,{{base64}}", "byteLength": {{bytes.Length}} }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": {{bytes.Length}}, "target": 34962 }
          ],
          "accessors": [
            {
              "bufferView": 0,
              "componentType": 5126,
              "count": 3,
              "type": "VEC3",
              "min": [0, 0, 0],
              "max": [1, 0, 2]
            }
          ],
          "meshes": [
            { "primitives": [ { "attributes": { "POSITION": 0 }, "mode": 4 } ] }
          ],
          "nodes": [
            { "mesh": 0, "translation": [3, 4, 5], "rotation": [0.70710677, 0, 0, 0.70710677] }
          ],
          "scenes": [
            { "nodes": [0] }
          ],
          "scene": 0
        }
        """;
    }

    /// <summary>
    /// Tools/ThumbnailBaker reads a published AssetLibrary/, where every file is renamed to its content
    /// hash and flattened into one directory -- a loose glTF's relative buffer/texture URI ("geometry.bin")
    /// no longer resolves against anything there, unlike a real checkout. GenerateViaCpuStandalone's
    /// optional resolver exists for exactly this; this proves both halves -- that loading genuinely fails
    /// without one, and genuinely works with one.
    /// </summary>
    private static void CheckStandaloneGenerationWithResolver(Action<string, bool, string> Check)
    {
        var positions = new[] { 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f };
        var posBytes = new byte[positions.Length * sizeof(float)];
        Buffer.BlockCopy(positions, 0, posBytes, 0, posBytes.Length);

        // An explicit index accessor, not left implicit: MeshThumbnailRasterizer draws from
        // ManaMeshCpuData.Indexes, which SharpGLTF only populates for an indexed primitive -- an
        // attributes-only primitive parses fine but flattens to zero triangles to actually draw.
        var indices = new ushort[] { 0, 1, 2 };
        var idxBytes = new byte[indices.Length * sizeof(ushort)];
        Buffer.BlockCopy(indices, 0, idxBytes, 0, idxBytes.Length);

        var combinedBytes = new byte[posBytes.Length + idxBytes.Length];
        Buffer.BlockCopy(posBytes, 0, combinedBytes, 0, posBytes.Length);
        Buffer.BlockCopy(idxBytes, 0, combinedBytes, posBytes.Length, idxBytes.Length);

        var gltfJson = $$"""
        {
          "asset": { "version": "2.0" },
          "buffers": [ { "uri": "geometry.bin", "byteLength": {{combinedBytes.Length}} } ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": {{posBytes.Length}}, "target": 34962 },
            { "buffer": 0, "byteOffset": {{posBytes.Length}}, "byteLength": {{idxBytes.Length}}, "target": 34963 }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
            { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
          ],
          "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1, "mode": 4 } ] } ],
          "nodes": [ { "mesh": 0 } ],
          "scenes": [ { "nodes": [0] } ],
          "scene": 0
        }
        """;

        var directory = Directory.CreateTempSubdirectory("mana-thumbnail-standalone-");
        try
        {
            // Named after a content hash, like a real published AssetLibrary entry -- "geometry.bin" (what
            // the glTF itself asks for) exists nowhere near it under that name.
            var flatGltfPath = Path.Combine(directory.FullName, "a1b2c3.gltf");
            File.WriteAllText(flatGltfPath, gltfJson);

            var context = new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true };

            var failedWithoutResolver = false;
            try { MeshThumbnailCache.GenerateViaCpuStandalone(context, flatGltfPath); }
            catch { failedWithoutResolver = true; }
            Check("a flattened glTF with an external buffer fails to load with no resolver",
                failedWithoutResolver, "");

            var pixels = MeshThumbnailCache.GenerateViaCpuStandalone(context, flatGltfPath,
                assetName => assetName == "geometry.bin" ? combinedBytes : null);
            Check("but succeeds once a resolver supplies the sibling buffer",
                pixels != null && pixels.Length == MeshThumbnailCache.ThumbnailSize * MeshThumbnailCache.ThumbnailSize * 4,
                pixels == null ? "null" : $"{pixels.Length} bytes");
        }
        finally { directory.Delete(recursive: true); }
    }

    /// <summary>
    /// A pre-built preview (Tools/ThumbnailBaker's own output) has to win over the live CPU/GPU render path
    /// when both are possible, and has to be reachable even when the model itself has not been downloaded
    /// at all yet -- that instant-preview-before-the-full-download case is the entire point of baking one.
    /// </summary>
    private static void CheckPrebuiltPreviewIsPreferred(Action<string, bool, string> Check)
    {
        var directory = Directory.CreateTempSubdirectory("mana-thumbnail-prebuilt-");
        var previousCatalog = ManaCore.Assets.ManaAssetCatalog.Current;
        MeshThumbnailCache? cache = null;
        try
        {
            var expectedPixels = new byte[MeshThumbnailCache.ThumbnailSize * MeshThumbnailCache.ThumbnailSize * 4];
            new Random(1).NextBytes(expectedPixels);
            // Alpha fully opaque throughout, or a PNG round trip is free to premultiply/drop
            // colour on fully transparent pixels, which would break the later byte-for-byte comparison.
            for (var i = 3; i < expectedPixels.Length; i += 4)
            {
                expectedPixels[i] = 255;
            }

            var previewPngBytes = ManaImageCodec.EncodePng(new ManaImage(
                MeshThumbnailCache.ThumbnailSize, MeshThumbnailCache.ThumbnailSize, expectedPixels));

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                version = 1,
                files = new object[]
                {
                    new { path = "Assets/flighthelmet.glb", url = "AssetLibrary/flighthelmet.glb", sha256 = "flighthelmet", size = 3,
                        dependencies = Array.Empty<string>() },
                    new { path = "Assets/flighthelmet.glb.thumb.png", url = "AssetLibrary/flighthelmetthumb.png",
                        sha256 = "thumb", size = previewPngBytes.LongLength, dependencies = Array.Empty<string>() },
                    new { path = "Assets/nothumb.glb", url = "AssetLibrary/nothumb.glb", sha256 = "n", size = 3,
                        dependencies = Array.Empty<string>() }
                },
                models = new object[]
                {
                    new { path = "Assets/flighthelmet.glb", thumbnail = "Assets/flighthelmet.glb.thumb.png" },
                    new { path = "Assets/nothumb.glb" }
                }
            });

            var requestedUrls = new List<string>();
            var catalog = new ManaCore.Assets.ManaAssetCatalog(json, directory.FullName, (url, _, _) =>
            {
                lock (requestedUrls) requestedUrls.Add(url);
                return Task.FromResult(url.EndsWith("flighthelmetthumb.png") ? previewPngBytes : new byte[] { 1, 2, 3 });
            });
            ManaCore.Assets.ManaAssetCatalog.Current = catalog;

            // Neither Assets/flighthelmet.glb nor Assets/nothumb.glb is ever written to directory.FullName --
            // the whole point is proving a preview arrives without the model itself being present at all.
            cache = new MeshThumbnailCache(
                new ManaContext(null!, null!, new ManaWorld()) { DeferGpuUploads = true },
                cacheDirectory: Path.Combine(directory.FullName, "cache"));

            cache.Request(catalog.Models[0]);
            Check("a not-yet-downloaded model is accepted when a pre-built preview exists",
                cache.RequestedCount == 1, $"{cache.RequestedCount}");

            cache.Request(catalog.Models[1]);
            Check("but not one with neither a preview nor its own file present",
                cache.RequestedCount == 1, $"{cache.RequestedCount}");

            (string Path, byte[]? Pixels)? finished = null;
            for (var i = 0; i < 200 && finished == null; i++)
            {
                var batch = cache.TakeFinished().ToList();
                if (batch.Count > 0)
                {
                    finished = batch[0];
                }
                else
                {
                    Thread.Sleep(10);
                }
            }

            Check("the worker actually finishes", finished != null, finished == null ? "timed out" : "finished");
            Check("with the pre-built preview's own pixels, not a fresh render",
                finished?.Pixels != null && finished.Value.Pixels!.SequenceEqual(expectedPixels),
                finished?.Pixels == null ? "null" : $"{finished.Value.Pixels!.Length} bytes");
            Check("counted as a prebuilt hit, not a generated one",
                cache.PrebuiltHitCount == 1 && cache.GeneratedCount == 0,
                $"prebuilt {cache.PrebuiltHitCount}, generated {cache.GeneratedCount}");
            Check("fetched through the ordinary verified download path, not a side channel",
                requestedUrls.Contains("AssetLibrary/flighthelmetthumb.png"), string.Join(",", requestedUrls));
        }
        finally
        {
            cache?.Dispose();
            ManaCore.Assets.ManaAssetCatalog.Current = previousCatalog;
            directory.Delete(recursive: true);
        }
    }

    private static Matrix4x4 CreateThumbnailView()
    {
        var view = Matrix4x4.CreateLookAt(-ThumbnailForwardWorld, Vector3.Zero, ThumbnailUpWorld);
        return view * Matrix4x4.CreateScale(1.0f, 1.0f, -1.0f);
    }

    private static (Vector3 Min, Vector3 Max) BoundsOf(IEnumerable<Vector3> positions)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var found = false;

        foreach (var position in positions)
        {
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
            found = true;
        }

        return found ? (min, max) : (Vector3.Zero, Vector3.Zero);
    }

    private static bool NearlyEqual(Vector3 a, Vector3 b)
    {
        return MathF.Abs(a.X - b.X) < 0.001f
            && MathF.Abs(a.Y - b.Y) < 0.001f
            && MathF.Abs(a.Z - b.Z) < 0.001f;
    }

    private static string Format(Vector3 value) =>
        $"{value.X:0.###}, {value.Y:0.###}, {value.Z:0.###}";

    private static void CheckKenneyToiletNodeRotations(Action<string, bool, string> Check)
    {
        var zipPath = Path.Combine(Environment.CurrentDirectory, "Assets", "kenney_furniture-kit.zip");
        if (!File.Exists(zipPath))
        {
            Check("Kenney toilet asset exists for transform regression", false, zipPath);
            return;
        }

        try
        {
            CheckFurnitureMultiMaterialImport(zipPath, Check);
            var toilet = LoadZipGltf(zipPath, "Models/GLTF format/toilet.glb");
            toilet.UpdateTransform();
            var toiletForward = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, toilet.LocalTransform));
            Check("toilet.glb keeps its imported yaw on the Y axis",
                MathF.Abs(toiletForward.X) > 0.9f && MathF.Abs(toiletForward.Y) < 0.01f,
                Format(toiletForward));

            var square = LoadZipGltf(zipPath, "Models/GLTF format/toiletSquare.glb");
            square.UpdateTransform();
            var lid = square.Children.FirstOrDefault(node => string.Equals(node.Name, "Group", StringComparison.Ordinal));
            var lidForward = lid == null
                ? Vector3.Zero
                : Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, lid.LocalTransform));
            Check("toiletSquare.glb keeps the lid rotation on the Y axis",
                lid != null && MathF.Abs(lidForward.X) > 0.9f && MathF.Abs(lidForward.Y) < 0.01f,
                lid == null ? "missing lid node" : Format(lidForward));
        }
        catch (Exception ex)
        {
            Check("Kenney toilet node transforms load", false, ex.Message);
        }
    }

    private static ManaSceneNode LoadZipGltf(string zipPath, string entryPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry(entryPath)
            ?? throw new FileNotFoundException($"Entry '{entryPath}' not found in '{zipPath}'.", zipPath);

        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);

        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var loader = new ManaSharpGltfLoader(context);
        return loader.LoadFileFromMemory(memory.ToArray(), entryPath, ZipModelSidecars.CreateZipSidecarResolver(archive, entryPath));
    }

    private static void CheckFurnitureMultiMaterialImport(string zipPath, Action<string, bool, string> Check)
    {
        var root = LoadZipGltf(zipPath, "Models/GLTF format/chairCushion.glb");
        var mesh = root.Components.OfType<MeshRendererComponent>().Single().Mesh;
        var colors = mesh.GetOrCreateCpuData().Colors;
        var distinctColors = colors
            .Select(color => new Vector3(color.X, color.Y, color.Z))
            .Distinct()
            .Count();

        Check("chair cushion keeps per-material colors", distinctColors > 1, $"{distinctColors} colors");
        Check("chair cushion does not keep one material tint", mesh.Materials[0].Albedo.Value == Vector4.One,
            mesh.Materials[0].Albedo.Value.ToString());
    }

    /// <summary>A single-colour PNG, encoded in memory -- what a decoded albedo texture looks like.</summary>
    private static byte[] SolidColorPng(byte r, byte g, byte b, byte alpha = 255)
    {
        var image = new ManaImage(4, 4);
        for (var i = 0; i < 16; i++)
        {
            image.Pixels[i * 4] = r;
            image.Pixels[i * 4 + 1] = g;
            image.Pixels[i * 4 + 2] = b;
            image.Pixels[i * 4 + 3] = alpha;
        }

        return ManaImageCodec.EncodePng(image);
    }

    /// <summary>
    /// A PNG whose left half is opaque <paramref name="r"/>/<paramref name="g"/>/<paramref name="b"/> and
    /// whose right half is fully transparent, RGB zeroed -- exactly how a sprite sheet's padding is
    /// typically encoded, and the shape that broke both the colour and the depth test.
    /// </summary>
    private static byte[] SplitColorPng(byte r, byte g, byte b)
    {
        var image = new ManaImage(8, 4);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width / 2; x++)
            {
                var i = (y * image.Width + x) * 4;
                image.Pixels[i] = r;
                image.Pixels[i + 1] = g;
                image.Pixels[i + 2] = b;
                image.Pixels[i + 3] = 255;
            }
        }

        return ManaImageCodec.EncodePng(image);
    }

    /// <summary>True when no opaque pixel is (near-)black -- the signature of a transparent sample painted solid.</summary>
    private static bool HasNoBlackOpaquePixels(byte[]? rgba)
    {
        if (rgba == null)
        {
            return false;
        }

        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0)
            {
                continue;
            }

            if (rgba[i] < 8 && rgba[i + 1] < 8 && rgba[i + 2] < 8)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when one RGB channel clearly outweighs the other two among the opaque pixels.</summary>
    private static bool IsChannelDominant(byte[]? rgba, int channel)
    {
        if (rgba == null)
        {
            return false;
        }

        var others = Enumerable.Range(0, 3).Where(c => c != channel).ToArray();
        var dominantSum = 0L;
        var otherSum = 0L;
        var count = 0;

        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0)
            {
                continue;
            }

            dominantSum += rgba[i + channel];
            otherSum += rgba[i + others[0]] + rgba[i + others[1]];
            count++;
        }

        return count > 0 && dominantSum > otherSum;
    }

    /// <summary>
    /// One large triangle whose outward normal is <paramref name="direction"/>, duplicated across its three
    /// vertices so the shader reads it directly rather than averaging it with any neighbour.
    /// </summary>
    private static ManaMeshCpuData SingleTriangle(
        Vector3 direction,
        float scale,
        Vector4[]? colors = null,
        Vector4? albedoFactor = null,
        byte[]? albedoTextureBytes = null,
        Vector2[]? uvs = null,
        Vector3 offset = default)
    {
        var normal = Vector3.Normalize(direction);

        // Any triangle whose plane has this normal; the two tangents just have to be non-parallel to it.
        var tangent = Vector3.Normalize(Vector3.Cross(normal, Math.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var bitangent = Vector3.Cross(normal, tangent);

        var positions = new[]
        {
            offset + normal + (tangent * scale),
            offset + normal + (bitangent * scale),
            offset + normal - (tangent * scale) - (bitangent * scale),
        };

        return new ManaMeshCpuData
        {
            Positions = positions,
            Normals = new[] { normal, normal, normal },
            Colors = colors ?? Array.Empty<Vector4>(),
            TextureCoordinates0 = uvs ?? new Vector2[3],
            TextureCoordinates1 = new Vector2[3],
            Indexes = new uint[] { 0, 1, 2 },
            AlbedoFactor = albedoFactor,
            AlbedoTextureBytes = albedoTextureBytes,
        };
    }

    /// <summary>Combines two single-triangle meshes into one, index offsets included -- for depth-test checks.</summary>
    /// <summary>
    /// Combines two single-triangle meshes into one, index offsets included, for depth-test checks. Both
    /// meshes must share the same albedo (texture and/or factor) if either carries one -- the rasterizer
    /// takes a single texture per <c>Render</c> call, applied to every UV-bearing triangle in it, so there
    /// is no way for two differently-textured triangles to coexist in one combined mesh.
    /// </summary>
    private static ManaMeshCpuData Combine(ManaMeshCpuData a, ManaMeshCpuData b)
    {
        return new ManaMeshCpuData
        {
            Positions = a.Positions.Concat(b.Positions).ToArray(),
            Normals = a.Normals.Concat(b.Normals).ToArray(),
            Colors = a.Colors.Length == a.Positions.Length && b.Colors.Length == b.Positions.Length
                ? a.Colors.Concat(b.Colors).ToArray()
                : Array.Empty<Vector4>(),
            TextureCoordinates0 = a.TextureCoordinates0.Concat(b.TextureCoordinates0).ToArray(),
            TextureCoordinates1 = a.TextureCoordinates1.Concat(b.TextureCoordinates1).ToArray(),
            Indexes = a.Indexes.Concat(b.Indexes.Select(index => index + (uint)a.Positions.Length)).ToArray(),
            AlbedoTextureBytes = a.AlbedoTextureBytes ?? b.AlbedoTextureBytes,
            AlbedoFactor = a.AlbedoFactor ?? b.AlbedoFactor,
        };
    }

    private static double AverageRed(byte[] rgba)
    {
        long sum = 0;
        var count = 0;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0)
            {
                continue;
            }

            sum += rgba[i];
            count++;
        }

        return count == 0 ? 0.0 : (double)sum / count;
    }
}

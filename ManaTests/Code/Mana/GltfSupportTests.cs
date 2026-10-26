using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes.Loaders;
using ManaUI;

namespace ManaTests;

/// <summary>
/// Two things the editor used to fail at silently: an asset it cannot read, and a view preset that parks
/// the camera where there is nothing to see.
/// </summary>
public static class GltfSupportChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        try
        {
            RunChecked(fonts, check);
        }
        finally
        {
            Cleanup();
        }
    }

    private static void RunChecked(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // ---------------------------------------------------------------- unsupported extensions
        // SharpGLTF implements no compression extension, and what it says when it meets one is that the
        // file is malformed -- which sends you looking for a corrupt download. The file is fine.
        var draco = Header("{\"asset\":{\"version\":\"2.0\"},"
            + "\"extensionsUsed\":[\"KHR_draco_mesh_compression\"],"
            + "\"extensionsRequired\":[\"KHR_draco_mesh_compression\"]}");

        var reason = ManaGltfSupport.DescribeUnsupported(draco);
        Check("a Draco-compressed glTF is reported as unreadable", reason != null, reason ?? "no reason");
        Check("and the reason names Draco rather than blaming the file",
            reason?.Contains("Draco", StringComparison.OrdinalIgnoreCase) == true
                && !reason.Contains("malformed", StringComparison.OrdinalIgnoreCase),
            reason ?? "no reason");

        // An extension nobody wrote a sentence for still beats "malformed".
        var exotic = Header("{\"asset\":{\"version\":\"2.0\"},\"extensionsRequired\":[\"VENDOR_something_new\"]}");
        Check("an unknown required extension is named outright",
            ManaGltfSupport.DescribeUnsupported(exotic)?.Contains("VENDOR_something_new", StringComparison.Ordinal) == true,
            ManaGltfSupport.DescribeUnsupported(exotic) ?? "no reason");

        // ---------------------------------------------------------------- everything else still loads
        var plain = Header("{\"asset\":{\"version\":\"2.0\"},\"meshes\":[]}");
        Check("an ordinary glTF is not refused", ManaGltfSupport.DescribeUnsupported(plain) == null);

        var supported = Header("{\"asset\":{\"version\":\"2.0\"},"
            + "\"extensionsRequired\":[\"KHR_materials_unlit\"]}");
        Check("nor is one requiring an extension the loader does handle",
            ManaGltfSupport.DescribeUnsupported(supported) == null,
            ManaGltfSupport.DescribeUnsupported(supported) ?? "accepted");

        // A .gltf can carry its buffers inline as base64 and run to tens of megabytes. Only the header is
        // read, so the check has to survive being handed a truncated prefix of a much larger file.
        var padded = Header("{\"asset\":{\"version\":\"2.0\"},"
            + "\"extensionsRequired\":[\"KHR_draco_mesh_compression\"],"
            + "\"buffers\":[{\"uri\":\"data:application/octet-stream;base64,"
            + new string('A', 200_000) + "\"}]}");
        Check("and a file with a huge inline buffer is still judged on its header",
            ManaGltfSupport.DescribeUnsupported(padded)?.Contains("Draco", StringComparison.OrdinalIgnoreCase) == true,
            ManaGltfSupport.DescribeUnsupported(padded) ?? "no reason");

        var notGltf = Header("this is not json at all");
        Check("something that is not glTF is left for the loader to reject",
            ManaGltfSupport.DescribeUnsupported(notGltf) == null,
            ManaGltfSupport.DescribeUnsupported(notGltf) ?? "passed through");

        // Exercise the actual loader, not only the header check. Khronos sample sets ship the same
        // model several times over in sibling "glTF*" folders, so
        // FindSupportedAlternative finds the plain glTF copy and LoadFile succeeds transparently rather
        // than refusing outright -- the refusal path above is what fires for a pack with no such sibling.
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository != null && !File.Exists(Path.Combine(repository.FullName, "ManaRenderer.sln")))
        {
            repository = repository.Parent;
        }

        if (repository != null)
        {
            var dracoModel = Path.Combine(repository.FullName, "Assets", "Models", "Lantern", "glTF-Draco", "Lantern.gltf");

            Check("the shipped Draco Lantern has a plain sibling to fall back to",
                ManaGltfSupport.FindSupportedAlternative(dracoModel) != null,
                ManaGltfSupport.FindSupportedAlternative(dracoModel) ?? "no sibling found");

            var world = new ManaWorld();
            var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
            var loader = new ManaSharpGltfLoader(context);

            Exception? caught = null;
            ManaSceneNode? loaded = null;
            try
            {
                loaded = loader.LoadFile(dracoModel);
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            Check("and importing the Draco Lantern itself succeeds via that sibling",
                caught == null && loaded != null,
                caught?.Message ?? "loaded");
        }

        // ---------------------------------------------------------------- one mesh, two textured materials
        // A ManaMesh draws with one material, and merging the parts used to paint them all in the first one --
        // Bistro's string lights came out in the wire's black.
        {
            var parts = LoadTwoMaterialMesh();
            var names = parts.Select(part => part.Mesh.Materials.FirstOrDefault()?.Name ?? "none").ToArray();
            Check("a mesh whose textured parts wear different materials draws each in its own",
                names.Length == 2 && names.Contains("Wire") && names.Contains("Bulb"), string.Join(", ", names));
            Check("and each part keeps only its own triangles",
                parts.All(part => part.Mesh.CpuData?.Indexes.Length == 3),
                string.Join(", ", parts.Select(part => part.Mesh.CpuData?.Indexes.Length ?? -1)));
        }

        // ---------------------------------------------------------------- the view preset's range
        // A camera parked further from its pivot than the far plane renders nothing, and the presets were
        // happy to put it there: the orbit distance came from a field the fly camera never updates, so after
        // any flight it was a stale number describing empty space.
        const float near = 0.1f;
        const float far = 1000.0f;

        Check("a preset never parks the camera past the far plane",
            CameraComponent.ClampViewPresetDistance(50_000.0f, near, far) <= far,
            $"{CameraComponent.ClampViewPresetDistance(50_000.0f, near, far):0}");

        Check("and leaves the pivot comfortably inside it",
            CameraComponent.ClampViewPresetDistance(50_000.0f, near, far) <= far * 0.5f + 0.01f,
            $"{CameraComponent.ClampViewPresetDistance(50_000.0f, near, far):0} of {far:0}");

        Check("nor closer than the near plane",
            CameraComponent.ClampViewPresetDistance(0.0f, near, far) > near,
            $"{CameraComponent.ClampViewPresetDistance(0.0f, near, far):0.###}");

        Check("an ordinary range is left alone",
            MathF.Abs(CameraComponent.ClampViewPresetDistance(25.0f, near, far) - 25.0f) < 0.001f,
            $"{CameraComponent.ClampViewPresetDistance(25.0f, near, far):0.##}");

        // NaN reaches this from a degenerate selection bounds -- a zero-radius mesh divided by a sine.
        Check("and a non-finite one cannot strand the camera",
            float.IsFinite(CameraComponent.ClampViewPresetDistance(float.NaN, near, far)),
            $"{CameraComponent.ClampViewPresetDistance(float.NaN, near, far)}");
    }

    /// <summary>Loads a glTF of one mesh with two triangles, each textured in a material of its own; returns every renderer.</summary>
    private static List<MeshRendererComponent> LoadTwoMaterialMesh()
    {
        var picture = Convert.ToBase64String(ManaCore.Imaging.ManaImageCodec.EncodePng(new ManaCore.Imaging.ManaImage(1, 1)));
        var buffer = new MemoryStream();
        var writer = new BinaryWriter(buffer);
        foreach (var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 1, 0, 1, 1 })
        {
            writer.Write(value);
        }
        foreach (var value in new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1 })
        {
            writer.Write(value);
        }
        foreach (var index in new ushort[] { 0, 1, 2, 0, 1, 2 })
        {
            writer.Write(index);
        }
        writer.Flush();
        var data = Convert.ToBase64String(buffer.ToArray());
        var json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0,\"name\":\"Lights\"}],"
            + $"\"buffers\":[{{\"byteLength\":{buffer.Length},\"uri\":\"data:application/octet-stream;base64,{data}\"}}],"
            + "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":72},{\"buffer\":0,\"byteOffset\":72,\"byteLength\":48},{\"buffer\":0,\"byteOffset\":120,\"byteLength\":12}],"
            + "\"accessors\":["
            + "{\"bufferView\":0,\"byteOffset\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},"
            + "{\"bufferView\":0,\"byteOffset\":36,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,1],\"max\":[1,1,1]},"
            + "{\"bufferView\":1,\"byteOffset\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC2\"},"
            + "{\"bufferView\":1,\"byteOffset\":24,\"componentType\":5126,\"count\":3,\"type\":\"VEC2\"},"
            + "{\"bufferView\":2,\"byteOffset\":0,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"},"
            + "{\"bufferView\":2,\"byteOffset\":6,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}],"
            + $"\"images\":[{{\"uri\":\"data:image/png;base64,{picture}\"}}],\"textures\":[{{\"source\":0}}],"
            + "\"materials\":[{\"name\":\"Wire\",\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}},"
            + "{\"name\":\"Bulb\",\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}],"
            + "\"meshes\":[{\"primitives\":["
            + "{\"attributes\":{\"POSITION\":0,\"TEXCOORD_0\":2},\"indices\":4,\"material\":0},"
            + "{\"attributes\":{\"POSITION\":1,\"TEXCOORD_0\":3},\"indices\":5,\"material\":1}]}]}";
        var path = Header(json);

        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        var root = new ManaSharpGltfLoader(context).LoadFile(path);
        var renderers = new List<MeshRendererComponent>();
        void Collect(ManaSceneNode node)
        {
            renderers.AddRange(node.Components.OfType<MeshRendererComponent>());
            foreach (var child in node.Children)
            {
                Collect(child);
            }
        }
        Collect(root);
        return renderers;
    }

    /// <summary>A scratch .gltf holding just the given JSON, deleted once the check has read it.</summary>
    private static string Header(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mana-gltf-{Guid.NewGuid():N}.gltf");
        File.WriteAllText(path, json);
        Scratch.Add(path);
        return path;
    }

    private static readonly List<string> Scratch = new();

    /// <summary>Deletes every scratch file this run created, even if a check above threw.</summary>
    private static void Cleanup()
    {
        foreach (var path in Scratch)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }

        Scratch.Clear();
    }
}

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
        while (repository != null && !Directory.Exists(Path.Combine(repository.FullName, "Mana", "Assets")))
        {
            repository = repository.Parent;
        }

        if (repository != null)
        {
            var dracoModel = Path.Combine(repository.FullName, "Mana", "Assets", "Models", "Lantern", "glTF-Draco", "Lantern.gltf");

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

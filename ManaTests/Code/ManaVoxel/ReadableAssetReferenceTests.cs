using System.IO.Compression;
using Mana;
using Mana.Code;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaCore.Graphics;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Levels;

namespace ManaTests;

/// <summary>
/// The readable form of a zip-packed model reference -- <c>zip:pack.zip/Models/x.glb</c> -- and the reason it
/// exists: it is found by name, so moving the asset packs (or a model within a pack) does not break a level.
/// </summary>
public static class ReadableAssetReferenceChecks
{
    private const string Pack = "kenney_city-kit-roads.zip";
    private const string Entry = "Models/GLB format/construction-cone.glb";

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        // Prefabs use source-relative paths even when launched outside the repository root.
        var lamp = ManaAssetPaths.Resolve("Assets/Models/Kenny/lightpost-single.glb", Path.GetTempPath());
        Check("a prefab model resolves from the centralized build with an unrelated working directory",
            File.Exists(lamp), lamp);
        var missingAssetPath = ManaAssetPaths.Resolve("missing-" + Guid.NewGuid().ToString("N") + ".glb", Path.GetTempPath());
        Check("missing assets fall back to the supplied launch directory", Path.GetDirectoryName(missingAssetPath) == Path.TrimEndingDirectorySeparator(Path.GetTempPath()), missingAssetPath);

        var readable = ZipAssetReferences.EncodeReadableZip(Pack, Entry);
        Check("a reference reads as the pack and the model", readable == "zip:" + Pack + "/" + Entry, readable);

        var hasPack = ZipAssetReferences.TryDecodeZip(readable, out var zipPath, out var entryPath) && File.Exists(zipPath);
        Check("it decodes to the model's entry", entryPath == Entry, entryPath);

        if (hasPack)
        {
            Check("and finds the pack on disk", Path.GetFileName(zipPath).Equals(Pack, StringComparison.OrdinalIgnoreCase), zipPath);

            // A level saved when the packs lived elsewhere: the folder it names is wrong, the file name is not.
            var moved = "zip:Some/Old/Folder/" + Pack + "/" + Entry;
            Check("a pack that has moved folders is still found, by its name",
                ZipAssetReferences.TryDecodeZip(moved, out var movedZip, out _) && File.Exists(movedZip)
                && Path.GetFileName(movedZip).Equals(Pack, StringComparison.OrdinalIgnoreCase),
                movedZip);

            Check("and the reference is matched without regard to case",
                ZipAssetReferences.TryDecodeZip("ZIP:" + Pack.ToUpperInvariant() + "/" + Entry, out var shouted, out _) && File.Exists(shouted),
                shouted);

            var oldStyle = MeshAssetLibrary.EncodeRelativeZip("Assets/" + Pack, Entry);
            Check("an old encoded reference is rewritten in the readable form",
                ZipAssetReferences.ToReadableReference(oldStyle) == readable, ZipAssetReferences.ToReadableReference(oldStyle));

            using var archive = ZipFile.OpenRead(zipPath);
            Check("and the model is really in the pack", ZipAssetReferences.FindZipEntry(archive, entryPath) != null);
        }

        Check("a reference already readable is left alone", ZipAssetReferences.ToReadableReference(readable) == readable);
        Check("as is a plain path", ZipAssetReferences.ToReadableReference("Mana/Assets/Models/x.gltf") == "Mana/Assets/Models/x.gltf");
        var elsewhere = MeshAssetLibrary.EncodeZip(Path.Combine(Path.GetTempPath(), "somewhere-else", "mine.zip"), "a/b.glb");
        Check("a zip outside every Assets folder keeps the encoded form that carries its full path",
            ZipAssetReferences.ToReadableReference(elsewhere) == elsewhere);

        // ---------------------------------------------------------------- entries inside a pack
        using var stream = new MemoryStream();
        using (var writer = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "Models/GLB format/tree.glb", "Models/OBJ format/tree.obj", "Models/FBX format/tree.glb", "Other/Cone.glb" })
            {
                using var entryStream = writer.CreateEntry(name).Open();
                entryStream.WriteByte(1);
            }
        }

        stream.Position = 0;
        using var pack = new ZipArchive(stream, ZipArchiveMode.Read);
        Check("an entry is found by the exact path", ZipAssetReferences.FindZipEntry(pack, "Models/GLB format/tree.glb")?.FullName == "Models/GLB format/tree.glb");
        Check("or with backslashes", ZipAssetReferences.FindZipEntry(pack, "Models\\GLB format\\tree.glb")?.FullName == "Models/GLB format/tree.glb");
        Check("or, when it has moved within the pack, by its name",
            ZipAssetReferences.FindZipEntry(pack, "Old Folder/tree.glb")?.Name == "tree.glb");
        Check("preferring the GLB format copy when there are several",
            ZipAssetReferences.FindZipEntry(pack, "Old Folder/tree.glb")?.FullName == "Models/GLB format/tree.glb",
            ZipAssetReferences.FindZipEntry(pack, "Old Folder/tree.glb")?.FullName);
        Check("regardless of case", ZipAssetReferences.FindZipEntry(pack, "elsewhere/CONE.GLB")?.FullName == "Other/Cone.glb");
        Check("and a model that is not there is not there", ZipAssetReferences.FindZipEntry(pack, "Models/GLB format/nothing.glb") == null);

        // ---------------------------------------------------------------- saving a level
        var world = new ManaWorld();
        var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
        world.DefaultScene = new ManaScene(context) { Name = "Default Scene" };
        world.Scenes.Add(world.DefaultScene);
        var placed = new ManaSceneNode(context, Matrix4x4.Identity)
        {
            Name = "Cone",
            LevelSource = ManaLevelSource.ForMesh(MeshAssetLibrary.EncodeRelativeZip("Assets/" + Pack, Entry))
        };
        world.DefaultScene.Children.Add(placed);

        var saved = ManaLevelCapture.CaptureScene(world, world.DefaultScene, "Saved").Nodes.Single();
        if (hasPack)
        {
            Check("saving writes a model the readable way, whichever way it was placed", saved.Asset == readable, saved.Asset);
        }

        // ---------------------------------------------------------------- a model that is a plain file, not in a zip
        var lighthousePath = MeshAssetLibrary.IsSupported(".glb") ? ManaAssetPaths.Resolve("Assets/Models/Lighthouse/lighthouse.glb") : string.Empty;
        if (File.Exists(lighthousePath))
        {
            var lighthouseWorld = new ManaWorld();
            var lighthouseContext = new ManaContext(null!, null!, lighthouseWorld) { DeferGpuUploads = true };
            lighthouseWorld.Context = lighthouseContext;
            lighthouseWorld.DefaultScene = new ManaScene(lighthouseContext) { Name = "Default Scene" };
            lighthouseWorld.Scenes.Add(lighthouseWorld.DefaultScene);
            var lighthouse = ManaMeshAssetLoader.For(lighthouseWorld).Load("Assets/Models/Lighthouse/lighthouse.glb", lighthouseWorld.DefaultScene);
            lighthouse.UpdateTransform();
            var lighthouseMeshes = new List<ManaRenderMeshInstance>();
            lighthouse.CollectRenderMeshInstances(lighthouseMeshes);
            Check("the lighthouse loads, with a part for each colour it was given", lighthouseMeshes.Count >= 9, $"{lighthouseMeshes.Count} meshes");
            var reds = lighthouseMeshes.Count(mesh => mesh.Albedo.X > 0.6f && mesh.Albedo.Y < 0.3f);
            var whites = lighthouseMeshes.Count(mesh => mesh.Albedo.X > 0.85f && mesh.Albedo.Y > 0.85f);
            Check("including the red and white of the tower", reds >= 1 && whites >= 1, $"{reds} red, {whites} white");
            Check("and a lit lamp room", lighthouseMeshes.Any(mesh => mesh.Emission.X > 0.5f && mesh.EmissionStrength > 0.0f),
                string.Join("; ", lighthouseMeshes.Select(mesh => $"{mesh.Name}: emission {mesh.Emission} x{mesh.EmissionStrength}")));
        }

        // ---------------------------------------------------------------- the unsaved-changes check is not run every frame
        // Times passed in: the last activity at 900, so the check is in its active window until 1900.
        var dirtyScene = new object();
        var computed = 0;
        bool Compute() { computed++; return computed > 1; }

        var first = SceneDirtyCache.IsDirty(dirtyScene, Compute, 1000, 900);
        var again = SceneDirtyCache.IsDirty(dirtyScene, Compute, 1100, 900);
        var later = SceneDirtyCache.IsDirty(dirtyScene, Compute, 1000 + SceneDirtyCache.LifetimeMilliseconds + 10, 900);
        Check("asking again straight away reuses the answer instead of capturing the scene again", computed == 2 && first == again, $"{computed} captures");
        Check("and asking after a moment while the editor is in use works it out afresh", later && computed == 2, $"{computed} captures");
        SceneDirtyCache.IsDirty(dirtyScene, Compute, 2500, 900);
        var settled = computed;
        SceneDirtyCache.IsDirty(dirtyScene, Compute, 9000, 900);
        SceneDirtyCache.IsDirty(dirtyScene, Compute, 60000, 900);
        Check("a second after the last activity the answer stands, however long nothing happens", computed == settled, $"{computed - settled} more captures");
        SceneDirtyCache.IsDirty(dirtyScene, Compute, 60010, 60000);
        Check("until something happens again", computed == settled + 1, $"{computed - settled} more captures");
        SceneDirtyCache.Invalidate(dirtyScene);
        SceneDirtyCache.IsDirty(dirtyScene, Compute, 60020, 60000);
        Check("saving forgets the answer at once", computed == settled + 2, $"{computed - settled} more captures");
        var otherScene = new object();
        var otherComputed = 0;
        SceneDirtyCache.IsDirty(otherScene, () => { otherComputed++; return false; }, 1005, 900);
        Check("and each scene has its own", otherComputed == 1);

        // ---------------------------------------------------------------- every bundled level
        // Content/Levels and each game's own (Games/<Name>/Content/Levels).
        var levelsDirectory = Path.GetDirectoryName(ManaLevelLibrary.ResolvePath("Little Pond"))!;
        var missing = new List<string>();
        var checkedModels = 0;
        foreach (var file in ManaLevelLibrary.Folders(levelsDirectory).SelectMany(folder => Directory.EnumerateFiles(folder.Folder, "*.manalevel")))
        {
            var level = ManaLevel.Load(file);
            foreach (var node in Walk(level.Nodes))
            {
                if (node.Asset == null || !ZipAssetReferences.TryDecodeZip(node.Asset, out var zip, out var entry))
                {
                    continue;
                }

                checkedModels++;
                if (!File.Exists(zip))
                {
                    missing.Add($"{Path.GetFileName(file)}: {node.Name} -> pack {Path.GetFileName(zip)} not found");
                    continue;
                }

                using var levelArchive = ZipFile.OpenRead(zip);
                if (ZipAssetReferences.FindZipEntry(levelArchive, entry) == null)
                {
                    missing.Add($"{Path.GetFileName(file)}: {node.Name} -> {entry} not in {Path.GetFileName(zip)}");
                }
            }
        }

        Check("every model in every bundled level is really in its pack", missing.Count == 0,
            checkedModels + " models; " + string.Join("; ", missing.Take(5)));
    }

    private static IEnumerable<ManaLevelNode> Walk(IEnumerable<ManaLevelNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node.Children != null)
            {
                foreach (var child in Walk(node.Children))
                {
                    yield return child;
                }
            }
        }
    }
}

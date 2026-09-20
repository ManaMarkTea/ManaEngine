using System.IO.Compression;
using Mana;
using Mana.Code;
using Mana.Code.Levels;
using Mana.Code.Nodes;
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

        var readable = MeshAssetLibrary.EncodeReadableZip(Pack, Entry);
        Check("a reference reads as the pack and the model", readable == "zip:" + Pack + "/" + Entry, readable);

        var hasPack = MeshAssetLibrary.TryDecodeZip(readable, out var zipPath, out var entryPath) && File.Exists(zipPath);
        Check("it decodes to the model's entry", entryPath == Entry, entryPath);

        if (hasPack)
        {
            Check("and finds the pack on disk", Path.GetFileName(zipPath).Equals(Pack, StringComparison.OrdinalIgnoreCase), zipPath);

            // A level saved when the packs lived elsewhere: the folder it names is wrong, the file name is not.
            var moved = "zip:Some/Old/Folder/" + Pack + "/" + Entry;
            Check("a pack that has moved folders is still found, by its name",
                MeshAssetLibrary.TryDecodeZip(moved, out var movedZip, out _) && File.Exists(movedZip)
                && Path.GetFileName(movedZip).Equals(Pack, StringComparison.OrdinalIgnoreCase),
                movedZip);

            Check("and the reference is matched without regard to case",
                MeshAssetLibrary.TryDecodeZip("ZIP:" + Pack.ToUpperInvariant() + "/" + Entry, out var shouted, out _) && File.Exists(shouted),
                shouted);

            var oldStyle = MeshAssetLibrary.EncodeRelativeZip("Assets/" + Pack, Entry);
            Check("an old encoded reference is rewritten in the readable form",
                MeshAssetLibrary.ToReadableReference(oldStyle) == readable, MeshAssetLibrary.ToReadableReference(oldStyle));

            using var archive = ZipFile.OpenRead(zipPath);
            Check("and the model is really in the pack", MeshAssetLibrary.FindZipEntry(archive, entryPath) != null);
        }

        Check("a reference already readable is left alone", MeshAssetLibrary.ToReadableReference(readable) == readable);
        Check("as is a plain path", MeshAssetLibrary.ToReadableReference("Mana/Assets/Models/x.gltf") == "Mana/Assets/Models/x.gltf");
        var elsewhere = MeshAssetLibrary.EncodeZip(Path.Combine(Path.GetTempPath(), "somewhere-else", "mine.zip"), "a/b.glb");
        Check("a zip outside every Assets folder keeps the encoded form that carries its full path",
            MeshAssetLibrary.ToReadableReference(elsewhere) == elsewhere);

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
        Check("an entry is found by the exact path", MeshAssetLibrary.FindZipEntry(pack, "Models/GLB format/tree.glb")?.FullName == "Models/GLB format/tree.glb");
        Check("or with backslashes", MeshAssetLibrary.FindZipEntry(pack, "Models\\GLB format\\tree.glb")?.FullName == "Models/GLB format/tree.glb");
        Check("or, when it has moved within the pack, by its name",
            MeshAssetLibrary.FindZipEntry(pack, "Old Folder/tree.glb")?.Name == "tree.glb");
        Check("preferring the GLB format copy when there are several",
            MeshAssetLibrary.FindZipEntry(pack, "Old Folder/tree.glb")?.FullName == "Models/GLB format/tree.glb",
            MeshAssetLibrary.FindZipEntry(pack, "Old Folder/tree.glb")?.FullName);
        Check("regardless of case", MeshAssetLibrary.FindZipEntry(pack, "elsewhere/CONE.GLB")?.FullName == "Other/Cone.glb");
        Check("and a model that is not there is not there", MeshAssetLibrary.FindZipEntry(pack, "Models/GLB format/nothing.glb") == null);

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

        // ---------------------------------------------------------------- every bundled level
        var levelsDirectory = Path.GetDirectoryName(ManaLevelLibrary.ResolvePath("Little Pond"))!;
        var missing = new List<string>();
        var checkedModels = 0;
        foreach (var file in Directory.EnumerateFiles(levelsDirectory, "*.manalevel"))
        {
            var level = ManaLevel.Load(file);
            foreach (var node in Walk(level.Nodes))
            {
                if (node.Asset == null || !MeshAssetLibrary.TryDecodeZip(node.Asset, out var zip, out var entry))
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
                if (MeshAssetLibrary.FindZipEntry(levelArchive, entry) == null)
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

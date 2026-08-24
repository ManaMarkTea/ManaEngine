using System.Text;
using ManaUI;
using ManaVoxel.Code.Assets;

namespace ManaTests;

/// <summary>
/// The mesh asset library: what the import list contains, before anything draws it.
/// <para>
/// Filtering, grouping a model's formats into one row, collapsing empty folder chains and encoding a
/// reference to a mesh inside an archive are all decisions about a list of paths. None of them involve a
/// UI, and the archive encoding is shared with the importer rather than the panel -- which is why it lives
/// here and not with the view that happens to be its first caller.
/// </para>
/// </summary>
public static class MeshAssetLibraryChecks
{
    private static readonly string[] Sample =
    {
        "/w/Assets/Models/Kenney/Nature/tree.glb",
        "/w/Assets/Models/Kenney/Nature/tree.obj",
        "/w/Assets/Models/Kenney/Nature/rock.glb",
        "/w/Assets/Models/Helmet/FlightHelmet.gltf",
        "/w/Assets/Models/Helmet/FlightHelmet.fbx",
        "/w/Assets/Props/crate.obj",
        "/w/Assets/Props/barrel.stl"
    };

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        static MeshAssetLibrary Library(MeshAssetFormats formats = MeshAssetFormats.All)
        {
            var library = new MeshAssetLibrary { Formats = formats };
            library.SetAssets(Sample);
            return library;
        }

        static IEnumerable<MeshAssetEntry> AllEntries(MeshAssetFolder folder)
        {
            foreach (var entry in folder.Entries)
            {
                yield return entry;
            }

            foreach (var child in folder.Folders)
            {
                foreach (var entry in AllEntries(child))
                {
                    yield return entry;
                }
            }
        }

        static IEnumerable<MeshAssetFolder> AllFolders(MeshAssetFolder folder)
        {
            foreach (var child in folder.Folders)
            {
                yield return child;

                foreach (var nested in AllFolders(child))
                {
                    yield return nested;
                }
            }
        }

        // ---------------------------------------------------------------- grouping
        var all = Library();
        var entries = AllEntries(all.Tree).ToList();

        // Seven files, five models: tree ships as .glb and .obj, FlightHelmet as .gltf and .fbx.
        Check("a model's formats become one row", entries.Count == 5, $"{entries.Count} rows from {Sample.Length} files");

        var tree = entries.FirstOrDefault(e => e.DisplayName.StartsWith("tree", StringComparison.OrdinalIgnoreCase));
        Check("and the row knows its variants", tree is { Variants.Count: 2 }, $"{tree?.Variants.Count ?? 0}");

        // glb beats obj, so the format that actually carries materials is the one that gets imported.
        Check("the best format is the one imported",
            tree != null && tree.PrimaryPath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase),
            tree?.PrimaryPath ?? "none");

        var helmet = entries.FirstOrDefault(e => e.DisplayName.Contains("Helmet", StringComparison.OrdinalIgnoreCase));
        Check("gltf beats fbx too",
            helmet != null && helmet.PrimaryPath.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase),
            helmet?.PrimaryPath ?? "none");

        // Sorted within a folder rather than globally -- the tree is read one folder at a time.
        var props = AllFolders(all.Tree).First(f => f.Name.EndsWith("Props", StringComparison.Ordinal));
        Check("rows are sorted by name",
            props.Entries.Select(e => e.DisplayName).SequenceEqual(
                props.Entries.Select(e => e.DisplayName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)),
            string.Join(", ", props.Entries.Select(e => e.DisplayName)));

        // ---------------------------------------------------------------- folder collapsing
        var folders = AllFolders(all.Tree).Select(f => f.Name).ToList();

        // Models/Kenney/Nature is one row, not three: two of those folders hold nothing but the next folder.
        Check("empty folder chains collapse into one row",
            folders.Any(name => name.Contains('/')),
            string.Join(" | ", folders));

        Check("and a folder that holds things does not",
            folders.Any(name => name == "Props"),
            string.Join(" | ", folders));

        // ---------------------------------------------------------------- format filters
        var gltfOnly = Library(MeshAssetFormats.Glb | MeshAssetFormats.Gltf);
        var gltfEntries = AllEntries(gltfOnly.Tree).ToList();

        Check("turning a format off drops its files",
            gltfEntries.Count == 3, $"{gltfEntries.Count} rows");

        // crate.obj and barrel.stl are gone, so the folder holding only them goes too rather than sitting
        // there as an empty row.
        Check("and a folder left with nothing goes with them",
            !AllFolders(gltfOnly.Tree).Any(f => f.Name.Contains("Props", StringComparison.Ordinal)),
            string.Join(" | ", AllFolders(gltfOnly.Tree).Select(f => f.Name)));

        // A model whose best format was filtered out falls back rather than disappearing.
        var objOnly = Library(MeshAssetFormats.Obj);
        var objEntries = AllEntries(objOnly.Tree).ToList();

        Check("a model survives losing its preferred format",
            objEntries.Any(e => e.PrimaryPath.EndsWith("tree.obj", StringComparison.OrdinalIgnoreCase)),
            string.Join(", ", objEntries.Select(e => e.PrimaryPath)));

        // ---------------------------------------------------------------- text filter
        var filtered = Library();
        filtered.Filter = "helmet";

        Check("the filter narrows the list",
            AllEntries(filtered.Tree).Count() == 1, $"{AllEntries(filtered.Tree).Count()} rows");

        // Matched on the whole path, so typing a folder narrows to it -- which is how anyone with a deep
        // asset tree searches.
        filtered.Filter = "Kenney";
        Check("and matches folders as well as names",
            AllEntries(filtered.Tree).Count() == 2, $"{AllEntries(filtered.Tree).Count()} rows");

        filtered.Filter = "";
        Check("clearing it brings everything back", AllEntries(filtered.Tree).Count() == 5);

        // ---------------------------------------------------------------- folder-only tree
        // FolderTree backs the import panel's Unreal-style folder sidebar, which must not shrink just
        // because something is typed into the search box above the main list.
        filtered.Filter = "helmet";
        Check("FolderTree ignores the text filter",
            AllFolders(filtered.FolderTree).Any(f => f.Name.Contains("Props", StringComparison.Ordinal)),
            string.Join(" | ", AllFolders(filtered.FolderTree).Select(f => f.Name)));

        Check("even though Tree itself is still narrowed",
            !AllFolders(filtered.Tree).Any(f => f.Name.Contains("Props", StringComparison.Ordinal)),
            string.Join(" | ", AllFolders(filtered.Tree).Select(f => f.Name)));

        filtered.Filter = "";

        // ---------------------------------------------------------------- selection
        var selecting = Library();
        selecting.SelectedAsset = Sample[0];
        selecting.SetAssets(new[] { Sample[5] });

        // A selection that outlives its file would leave the Place button enabled and pointing at nothing.
        Check("a rescan drops a selection that no longer exists",
            selecting.SelectedAsset == null, selecting.SelectedAsset ?? "null");

        // ---------------------------------------------------------------- archives
        var zipPath = Path.GetFullPath("/w/Assets/pack.zip");
        var zipRef = MeshAssetLibrary.EncodeZip(zipPath, "models/ship.glb");

        Check("an archive entry round-trips",
            MeshAssetLibrary.TryDecodeZip(zipRef, out var zip, out var entry)
                && zip == zipPath && entry == "models/ship.glb",
            zipRef);

        // Checked against the format itself rather than against the decoder above, which is the mistake
        // that let this ship broken: the picker was rewritten with its own separator, agreed with itself
        // perfectly, and disagreed with the importer -- so every archived mesh was handed on as a filename
        // and failed to open. Both halves are base64 because a Windows path contains a colon and an entry
        // name can contain nearly anything, leaving no punctuation free to separate them.
        var body = zipRef.StartsWith("zipasset:", StringComparison.Ordinal) ? zipRef["zipasset:".Length..] : "";
        var parts = body.Split(':');

        Check("and is written the way the importer reads it",
            parts.Length == 2
                && Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])) == zipPath
                && Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])) == "models/ship.glb",
            zipRef);

        // A real reference, with the drive letter and backslashes that broke it.
        var windowsRef = MeshAssetLibrary.EncodeZip(
            "C:" + Path.DirectorySeparatorChar + "Assets" + Path.DirectorySeparatorChar + "kenney_train-kit.zip",
            "Models" + Path.DirectorySeparatorChar + "GLB format" + Path.DirectorySeparatorChar + "train-diesel-a.glb");

        Check("a Windows path with a drive letter survives",
            MeshAssetLibrary.TryDecodeZip(windowsRef, out var winZip, out var winEntry)
                && winZip.EndsWith("kenney_train-kit.zip", StringComparison.Ordinal)
                && winEntry.EndsWith("train-diesel-a.glb", StringComparison.Ordinal)
                && !winEntry.Contains('\\'),
            winZip + " | " + winEntry);

        Check("and reads as a normal asset",
            MeshAssetLibrary.FileName(zipRef) == "ship.glb"
                && MeshAssetLibrary.DisplayName(zipRef) == "ship.glb"
                && MeshAssetLibrary.Extension(zipRef) == ".glb"
                && MeshAssetLibrary.FormatOf(zipRef) == MeshAssetFormats.Glb,
            MeshAssetLibrary.FileName(zipRef));

        var genericChair = MeshAssetLibrary.EncodeZip(zipPath, "models/chair/model.glb");
        var genericTable = MeshAssetLibrary.EncodeZip(zipPath, "models/table/model.glb");
        var genericZipLibrary = new MeshAssetLibrary { Formats = MeshAssetFormats.All };
        genericZipLibrary.SetAssets(new[] { genericChair, genericTable });
        var genericZipEntries = AllEntries(genericZipLibrary.Tree).ToList();

        Check("archive entries with generic file names keep their path",
            genericZipEntries.Any(e => e.DisplayName == "models/chair/model.glb")
                && genericZipEntries.Any(e => e.DisplayName == "models/table/model.glb"),
            string.Join(", ", genericZipEntries.Select(e => e.DisplayName)));

        Check("and same-named archive entries in different folders stay separate",
            genericZipEntries.Count == 2,
            string.Join(", ", genericZipEntries.Select(e => e.DisplayName)));

        Check("archive tooltips decode to paths, not the internal token",
            MeshAssetLibrary.Tooltip(genericChair).Contains("models/chair/model.glb", StringComparison.Ordinal)
                && !MeshAssetLibrary.Tooltip(genericChair).Contains("zipasset:", StringComparison.Ordinal),
            MeshAssetLibrary.Tooltip(genericChair));

        Check("a plain path is not mistaken for one",
            !MeshAssetLibrary.TryDecodeZip("/w/Assets/ship.glb", out _, out _));

    }
}

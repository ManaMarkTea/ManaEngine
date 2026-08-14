using System.Text;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The mesh asset library, and the import panel built on it.
/// <para>
/// All of this lived inside the ImGui panel and was rebuilt at draw time, which is why none of it was ever
/// tested. Filtering, grouping a model's formats into one row and collapsing empty folder chains are
/// decisions about what the list contains, and they are checked here against a list of paths -- no disk, no
/// window.
/// </para>
/// </summary>
public static class MeshImportChecks
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
                && MeshAssetLibrary.Extension(zipRef) == ".glb"
                && MeshAssetLibrary.FormatOf(zipRef) == MeshAssetFormats.Glb,
            MeshAssetLibrary.FileName(zipRef));

        Check("a plain path is not mistaken for one",
            !MeshAssetLibrary.TryDecodeZip("/w/Assets/ship.glb", out _, out _));

        // ---------------------------------------------------------------- the panel
        var root = new UiRoot(fonts);
        var viewModel = new ManaVoxel.Code.UI.VoxelEditorViewModel();
        var library = Library();

        MeshImportRequests.Clear();
        root.Content = MeshImportManaView.Build(root.Theme, viewModel, library);
        root.UpdateLayout(new Vector2(320, 700));

        var drawn = root.Render();
        Check("the panel renders", drawn.Instances.Length > 0, $"{drawn.Instances.Length} instances");
        Check("in one draw call", drawn.Batches.Count == 1, $"{drawn.Batches.Count} batches");

        // Seven format toggles in a 320px panel do not fit on one line, which is the reason WrapPanel exists.
        var wrap = Find<WrapPanel>(root.Content!);
        Check("the format toggles wrap", wrap is { LineCount: > 1 }, $"{wrap?.LineCount ?? 0} lines");

        Check("and stay inside the panel",
            wrap != null && wrap.Children.All(c => c.Bounds.Right <= root.Content!.Bounds.Right + 0.5f),
            "within bounds");

        // Measured as well as arranged. Arrange wraps on its own, so a panel whose measure did not would
        // still look right while asking for a width nothing can give it -- and everything sized against it
        // would be wrong.
        Check("and the panel asks for a width that fits",
            wrap != null && wrap.DesiredSize.X <= wrap.Bounds.Width + 0.5f,
            $"wants {wrap?.DesiredSize.X ?? 0.0f:F0}px in {wrap?.Bounds.Width ?? 0.0f:F0}px");

        // The row it sits in has to be tall enough for the lines it wrapped onto, or it draws over whatever
        // is below it.
        Check("and gets a row tall enough for them",
            wrap != null && wrap.Children.All(c => c.Bounds.Bottom <= wrap.Bounds.Bottom + 0.5f),
            $"{wrap?.Bounds.Height ?? 0.0f:F0}px tall for {wrap?.LineCount ?? 0} lines");

        // The tree shows the library's contents, not a snapshot taken when the panel was built.
        var treeView = Find<TreeView>(root.Content!);
        Check("the panel found the tree", treeView != null);

        var visibleNames = treeView!.VisibleItems.OfType<MeshAssetEntry>().Select(e => e.DisplayName).ToList();
        Check("rows appear for the assets", visibleNames.Count > 0, string.Join(", ", visibleNames));

        library.Filter = "helmet";
        root.UpdateLayout(new Vector2(320, 700));
        root.Render();

        var afterFilter = treeView.VisibleItems.OfType<MeshAssetEntry>().ToList();
        Check("filtering the library updates the tree",
            afterFilter.Count == 1 && afterFilter[0].DisplayName.Contains("Helmet", StringComparison.OrdinalIgnoreCase),
            $"{afterFilter.Count} rows");

        library.Filter = "";
        root.UpdateLayout(new Vector2(320, 700));
        root.Render();

        // ---------------------------------------------------------------- a narrow panel
        // Shrunk until the fixed rows alone want more than there is. A grid hands out slots regardless, so
        // the overflow lands on top of whatever is below it -- which is how the tree ended up running
        // through the Place button.
        root.UpdateLayout(new Vector2(300, 260));
        root.Render();

        var panelBounds = root.Content!.Bounds;

        // Checked on what is drawn rather than on element bounds: a scroll viewer arranges its content
        // taller than itself on purpose, so the tree legitimately overhangs. What must not overhang is the
        // viewer, and what must not appear is any pixel outside the panel.
        var squeezedViewer = Find<ScrollViewer>(root.Content!);
        var squeezedPlace = FindAll<Button>(root.Content!)
            .FirstOrDefault(b => b.Text.Contains("Place", StringComparison.Ordinal));

        Check("the list gives up its space rather than holding a minimum",
            squeezedViewer != null && squeezedPlace != null
                && squeezedViewer.Bounds.Bottom <= squeezedPlace.Bounds.Y + 0.5f,
            $"list ends {squeezedViewer?.Bounds.Bottom:0}, button starts {squeezedPlace?.Bounds.Y:0}");

        // An instance is only visible where its rect meets its clip, which is how this pipeline confines
        // anything at all -- so that intersection is what has to stay inside the panel.
        var leaked = root.Render().Instances.ToArray()
            .Select(i => (
                Rect: UiRect.FromEdges(i.Rect.X, i.Rect.Y, i.Rect.Z, i.Rect.W),
                Clip: UiRect.FromEdges(i.Clip.X, i.Clip.Y, i.Clip.Z, i.Clip.W)))
            .Select(pair => (
                Top: MathF.Max(pair.Rect.Y, pair.Clip.Y),
                Bottom: MathF.Min(pair.Rect.Bottom, pair.Clip.Bottom)))
            .Count(visible => visible.Bottom > visible.Top && visible.Bottom > panelBounds.Bottom + 0.5f);

        Check("and nothing is drawn outside the panel",
            leaked == 0, leaked == 0 ? $"{panelBounds.Height:0}px tall" : $"{leaked} instances past the edge");

        root.UpdateLayout(new Vector2(320, 700));
        root.Render();

        // ---------------------------------------------------------------- placing
        library.SelectedAsset = null;
        var place = FindAll<Button>(root.Content!).FirstOrDefault(b => b.Text.Contains("Place", StringComparison.Ordinal));

        Check("there is a place button", place != null, place?.Text ?? "none");

        root.UpdateLayout(new Vector2(320, 700));
        root.Render();
        Check("disabled with nothing selected", place is { IsEnabled: false });

        library.SelectedAsset = Sample[0];
        root.UpdateLayout(new Vector2(320, 700));
        root.Render();
        Check("and enabled once something is", place is { IsEnabled: true });

        MeshImportRequests.Clear();
        Press(root, place!.Bounds.Center);

        // The panel asks rather than acts: it has no idea where the viewport is, and the game mode drains
        // this each frame.
        Check("pressing it asks for a placement",
            MeshImportRequests.TakePlacement() == Sample[0],
            "requested");

        Check("and the request is only taken once",
            MeshImportRequests.TakePlacement() == null, "cleared");

        // ---------------------------------------------------------------- dragging a row out
        MeshImportRequests.Clear();

        var row = FindAll<MeshAssetRow>(root.Content!).FirstOrDefault();
        Check("rows are draggable", row != null);

        var start = row!.Bounds.Center;
        root.Input.InjectPointerMoved(start);
        root.Input.InjectPointerPressed(start, PointerButton.Left, 0.0);
        root.Input.InjectPointerMoved(start + new Vector2(1.0f, 0.0f));

        // A press that has barely moved is still a click, and stealing it would make rows unselectable.
        Check("a small movement is not a drag", MeshImportRequests.Dragging == null, "not yet");

        root.Input.InjectPointerMoved(start + new Vector2(40.0f, 20.0f));
        Check("moving out of the panel is", MeshImportRequests.Dragging != null, MeshImportRequests.Dragging ?? "none");

        // The release is the drop, and whoever acts on it reads the mailbox later in the same frame. So it
        // survives the button coming up -- clearing it here meant the drop was gone before anyone looked,
        // and placing a mesh took a second click.
        root.Input.InjectPointerReleased(start + new Vector2(40.0f, 20.0f), PointerButton.Left);

        Check("the drop survives the button coming up", MeshImportRequests.Dragging != null,
            MeshImportRequests.Dragging ?? "cleared too early");

        // A drag nobody claimed must not block the next one, which is the other half: leave it set forever
        // and every later drag is ignored because something is already being dragged.
        root.Input.InjectPointerMoved(start);
        root.Input.InjectPointerPressed(start, PointerButton.Left, 0.0);

        Check("and a new press clears one nobody claimed", MeshImportRequests.Dragging == null,
            MeshImportRequests.Dragging ?? "cleared");

        root.Input.InjectPointerMoved(start + new Vector2(40.0f, 20.0f));

        Check("so a second drag still starts", MeshImportRequests.Dragging != null,
            MeshImportRequests.Dragging ?? "none");

        root.Input.InjectPointerReleased(start + new Vector2(40.0f, 20.0f), PointerButton.Left);
        MeshImportRequests.Clear();
    }

    private static void Press(UiRoot root, Vector2 point)
    {
        root.Input.InjectPointerMoved(point);
        root.Input.InjectPointerPressed(point, PointerButton.Left, 0.0);
        root.Input.InjectPointerReleased(point, PointerButton.Left);
    }

    private static T? Find<T>(UiElement element) where T : UiElement => FindAll<T>(element).FirstOrDefault();

    private static List<T> FindAll<T>(UiElement element) where T : UiElement
    {
        var found = new List<T>();
        Walk(element, found);
        return found;

        static void Walk(UiElement element, List<T> found)
        {
            if (element is T match)
            {
                found.Add(match);
            }

            foreach (var child in element.VisualChildren)
            {
                Walk(child, found);
            }
        }
    }
}

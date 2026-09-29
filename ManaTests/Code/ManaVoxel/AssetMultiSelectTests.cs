using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// Picking several models in the import panel -- Ctrl toggles, Shift selects a run -- and dragging them out
/// together, which a grid map palette takes all at once.
/// </summary>
public static class AssetMultiSelectChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var library = new MeshAssetLibrary { Formats = MeshAssetFormats.All };
        var paths = Enumerable.Range(0, 6).Select(i => $"/w/Assets/Kit/piece-{i}.glb").ToList();
        library.SetAssets(paths);
        var order = library.FilteredEntries.Select(entry => entry.PrimaryPath).ToList();
        string Names() => string.Join(", ", library.SelectedAssets.Select(Path.GetFileNameWithoutExtension));

        library.Click(order[1], toggle: false, range: false);
        Check("a click selects one", library.SelectedAssets.SequenceEqual([order[1]]), Names());
        library.Click(order[4], toggle: true, range: false);
        Check("Ctrl adds another", library.SelectedAssets.SequenceEqual([order[1], order[4]]) && library.SelectedAsset == order[4], Names());
        library.Click(order[1], toggle: true, range: false);
        Check("and Ctrl on a selected one takes it out", library.SelectedAssets.SequenceEqual([order[4]]), Names());
        library.Click(order[2], toggle: false, range: true);
        Check("Shift selects the run from the last one clicked, in the order shown",
            library.SelectedAssets.SequenceEqual([order[2], order[3], order[4]]), Names());
        library.SelectedAsset = order[0];
        Check("choosing one outright selects it alone", library.SelectedAssets.SequenceEqual([order[0]]), Names());

        // Dragged out of the grid: two selected, and the drag carries both.
        library.Click(order[0], toggle: false, range: false);
        library.Click(order[3], toggle: true, range: false);
        MeshImportRequests.Clear();
        var root = new UiRoot(fonts);
        root.Content = MeshImportManaView.Build(root.Theme, new VoxelEditorViewModel(), library, thumbnails: null);
        var size = new Vector2(1200, 700);
        root.UpdateLayout(size);
        root.Render();

        var row = Find<MeshAssetRow>(root.Content).FirstOrDefault(candidate => candidate.Bounds.Width > 0.0f
            && candidate.Library != null && Find<TextBlock>(candidate).Any(text => text.Text.StartsWith("piece-3", StringComparison.Ordinal)));
        Check("a selected tile is on screen to drag", row != null);
        if (row == null)
        {
            return;
        }

        var start = row.Bounds.Center;
        root.Input.InjectPointerMoved(start);
        root.Input.InjectPointerPressed(start, PointerButton.Left, 0.0);
        root.Input.InjectPointerMoved(start + new Vector2(60.0f, 30.0f));
        Check("dragging one of the selection drags them all",
            MeshImportRequests.DraggingAll.SequenceEqual([order[0], order[3]]) && MeshImportRequests.Dragging == order[3],
            string.Join(", ", MeshImportRequests.DraggingAll.Select(Path.GetFileNameWithoutExtension)));
        root.UpdateLayout(size);
        root.Render();
        Check("and the ghost says how many", Find<TextBlock>(root).Any(text => text.Text == "2 models"));
        root.Input.InjectPointerReleased(start + new Vector2(60.0f, 30.0f), PointerButton.Left);

        var taken = MeshImportRequests.TakeDragAll();
        Check("taking the drop takes all of it, and leaves nothing behind",
            taken.Count == 2 && MeshImportRequests.Dragging == null && MeshImportRequests.DraggingAll.Count == 0);

        // Dragging a tile that is not selected carries just that one.
        var other = Find<MeshAssetRow>(root.Content).First(candidate => candidate.Bounds.Width > 0.0f
            && Find<TextBlock>(candidate).Any(text => text.Text.StartsWith("piece-5", StringComparison.Ordinal)));
        root.Input.InjectPointerMoved(other.Bounds.Center);
        root.Input.InjectPointerPressed(other.Bounds.Center, PointerButton.Left, 0.0);
        root.Input.InjectPointerMoved(other.Bounds.Center + new Vector2(60.0f, 30.0f));
        Check("but a tile outside the selection drags alone", MeshImportRequests.DraggingAll.SequenceEqual([order[5]]));
        root.Input.InjectPointerReleased(other.Bounds.Center + new Vector2(60.0f, 30.0f), PointerButton.Left);
        MeshImportRequests.Clear();
    }

    private static List<T> Find<T>(UiElement element) where T : UiElement
    {
        var found = new List<T>();
        void Walk(UiElement current)
        {
            if (current is T typed)
            {
                found.Add(typed);
            }

            foreach (var child in current.VisualChildren)
            {
                Walk(child);
            }
        }

        Walk(element);
        return found;
    }
}

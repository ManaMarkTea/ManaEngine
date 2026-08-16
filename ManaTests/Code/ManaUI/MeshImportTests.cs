using System.Text;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The import panel: how the asset list is drawn, and the two ways a mesh gets into the scene.
/// <para>
/// What the list contains is decided by MeshAssetLibrary and checked separately -- none of that needs a
/// window. This is the half that does.
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

        // ---------------------------------------------------------------- the panel
        var root = new UiRoot(fonts);
        var viewModel = new ManaVoxel.Code.UI.VoxelEditorViewModel();
        var library = Library();

        // The panel is three panes wide now (folders | list | placement), each with a fixed share, so the
        // window has to be wide enough for all three -- 320px for the centre list, same as the whole panel
        // used to get, plus the resizable folder sidebar (with its drag splitter) and the placement sidebar
        // either side of it.
        const float FolderColumn = 210.0f;
        const float FolderSplitterWidth = 5.0f;
        const float PlacementColumn = 220.0f;
        const float DividerWidth = 1.0f;
        const float DefaultCenterWidth = 320.0f;
        var defaultSize = new Vector2(
            FolderColumn + FolderSplitterWidth + DefaultCenterWidth + DividerWidth + PlacementColumn, 700.0f);

        MeshImportRequests.Clear();
        root.Content = MeshImportManaView.Build(root.Theme, viewModel, library);
        root.UpdateLayout(defaultSize);

        var drawn = root.Render();
        Check("the panel renders", drawn.Instances.Length > 0, $"{drawn.Instances.Length} instances");
        Check("in one draw call", drawn.Batches.Count == 1, $"{drawn.Batches.Count} batches");

        // Seven format toggles in a 320px-wide list column do not fit on one line, which is the reason
        // WrapPanel exists.
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

        // There are two trees now -- the folder sidebar and the asset list -- distinguished by what they
        // actually show rather than by position: only the asset list's ChildrenSelector ever yields a
        // MeshAssetEntry, so the folder tree (folders only) can never satisfy this.
        var treeView = FindAll<TreeView>(root.Content!)
            .FirstOrDefault(t => t.VisibleItems.OfType<MeshAssetEntry>().Any());
        Check("the panel found the asset tree", treeView != null);

        var visibleNames = treeView!.VisibleItems.OfType<MeshAssetEntry>().Select(e => e.DisplayName).ToList();
        Check("rows appear for the assets", visibleNames.Count > 0, string.Join(", ", visibleNames));

        library.Filter = "helmet";
        root.UpdateLayout(defaultSize);
        root.Render();

        var afterFilter = treeView.VisibleItems.OfType<MeshAssetEntry>().ToList();
        Check("filtering the library updates the tree",
            afterFilter.Count == 1 && afterFilter[0].DisplayName.Contains("Helmet", StringComparison.OrdinalIgnoreCase),
            $"{afterFilter.Count} rows");

        library.Filter = "";
        root.UpdateLayout(defaultSize);
        root.Render();

        // ---------------------------------------------------------------- a narrow panel
        // Shrunk until the fixed rows alone want more than there is. A grid hands out slots regardless, so
        // the overflow lands on top of whatever is below it -- which is how the tree ended up running
        // through the Place button. The side columns keep their fixed widths regardless, so only the
        // centre list column is actually being squeezed here.
        var squeezedSize = new Vector2(FolderColumn + FolderSplitterWidth + 100.0f + DividerWidth + PlacementColumn, 260.0f);
        root.UpdateLayout(squeezedSize);
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

        root.UpdateLayout(defaultSize);
        root.Render();

        // ---------------------------------------------------------------- placing
        library.SelectedAsset = null;
        var place = FindAll<Button>(root.Content!).FirstOrDefault(b => b.Text.Contains("Place", StringComparison.Ordinal));

        Check("there is a place button", place != null, place?.Text ?? "none");

        root.UpdateLayout(defaultSize);
        root.Render();
        Check("disabled with nothing selected", place is { IsEnabled: false });

        library.SelectedAsset = Sample[0];
        root.UpdateLayout(defaultSize);
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

        // A ghost follows the cursor, because a drag that leaves the panel has nothing else on screen
        // saying what is being carried.
        root.UpdateLayout(defaultSize);
        root.Render();

        // Identified by living in a popup rather than by where it is: the row it was made from carries the
        // same text, and picking by position is how the first version of this check found the row instead.
        var carried = FindAll<TextBlock>(root)
            .FirstOrDefault(t => t.Text.Contains("FlightHelmet", StringComparison.Ordinal) && IsInsideGhost(t));

        Check("a ghost of the row follows the cursor", carried != null, carried?.Text ?? "none");

        var cursor = start + new Vector2(40.0f, 20.0f);

        // Held on screen ahead of being offset, which is why the check below needs room to see the offset
        // at all: in a panel this narrow the ghost is already against the right edge.
        Check("the ghost stays on screen",
            carried != null && carried.Bounds.Right <= root.Size.X + 0.5f && carried.Bounds.X >= -0.5f,
            $"{carried?.Bounds} in {root.Size.X:0}");

        Check("and below the pointer rather than under it",
            carried != null && carried.Bounds.Y > cursor.Y, $"{carried?.Bounds.Y:0} vs {cursor.Y:0}");

        // Given room, it sits down and to the right -- up and left would put it over whatever is being
        // dragged towards, which is the one place it must not hide.
        root.UpdateLayout(new Vector2(1600.0f, 700.0f));
        root.Render();

        var roomy = FindAll<TextBlock>(root)
            .FirstOrDefault(t => t.Text.Contains("FlightHelmet", StringComparison.Ordinal) && IsInsideGhost(t));

        Check("and to the right when there is room",
            roomy != null && roomy.Bounds.X > cursor.X, $"{roomy?.Bounds.X:0} vs cursor {cursor.X:0}");

        root.UpdateLayout(defaultSize);
        root.Render();

        // Transparent to the pointer, or it would be what every drop is tested against -- it sits under
        // the cursor for the whole gesture.
        var hit = root.HitTest(start + new Vector2(48.0f, 26.0f));
        Check("and cannot be hit",
            hit == null || !IsInsideGhost(hit),
            hit?.GetType().Name ?? "nothing");

        // The release is the drop, and whoever acts on it reads the mailbox later in the same frame. So it
        // survives the button coming up -- clearing it here meant the drop was gone before anyone looked,
        // and placing a mesh took a second click.
        root.Input.InjectPointerReleased(start + new Vector2(40.0f, 20.0f), PointerButton.Left);
        root.UpdateLayout(defaultSize);
        root.Render();

        // A ghost that outlived its gesture would sit over the scene until something else happened to
        // close it.
        var lingering = FindAll<TextBlock>(root)
            .Count(t => t.Text.Contains("FlightHelmet", StringComparison.Ordinal) && IsInsideGhost(t));

        Check("the ghost goes when the drag ends", lingering == 0, $"{lingering} still showing");

        // Where the drop landed, not merely that it happened. The editor works the scene out from the ImGui
        // viewport, which spans the whole window with the panels drawn over it -- so without this, letting
        // go over the import list placed the mesh in the world behind it.
        Check("a drop over the panel is not a drop into the scene",
            MeshImportRequests.PointerOverUi && !MeshImportRequests.CanDrop,
            $"over ui {MeshImportRequests.PointerOverUi}, can drop {MeshImportRequests.CanDrop}");

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

        // And releasing clear of the panel is a real drop. Anything past the panel's own width is scene --
        // which is the whole difference between the two cases.
        MeshImportRequests.Clear();
        root.Input.InjectPointerMoved(start);
        root.Input.InjectPointerPressed(start, PointerButton.Left, 0.0);

        var outside = new Vector2(defaultSize.X + 50.0f, 400.0f);
        root.Input.InjectPointerMoved(outside);

        Check("dragging out past the panel is over the scene",
            !MeshImportRequests.PointerOverUi, $"over ui {MeshImportRequests.PointerOverUi}");

        root.Input.InjectPointerReleased(outside, PointerButton.Left);

        Check("and dropping there would place it",
            MeshImportRequests.CanDrop, $"can drop {MeshImportRequests.CanDrop}");

        MeshImportRequests.Clear();

        // ---------------------------------------------------------------- resizing the folder column
        // Last, since dragging the splitter changes the folder column's width for the rest of this Grid's
        // lifetime -- every size-dependent check above assumes the untouched default.
        root.UpdateLayout(defaultSize);
        root.Render();

        var folderTree = FindAll<TreeView>(root.Content!)
            .FirstOrDefault(t => !t.VisibleItems.OfType<MeshAssetEntry>().Any());
        Check("the panel found the folder tree", folderTree != null);

        var folderWidthBefore = folderTree!.Bounds.Width;
        var grab = new Vector2(
            folderTree.Bounds.Right + root.Theme.Spacing + FolderSplitterWidth * 0.5f,
            folderTree.Bounds.Y + 10.0f);

        root.Input.InjectPointerMoved(grab);
        Check("the splitter asks for a resize cursor", root.RequestedCursor == UiCursor.SizeWE,
            root.RequestedCursor.ToString());

        root.Input.InjectPointerPressed(grab, PointerButton.Left, 0.0);
        root.Input.InjectPointerMoved(grab + new Vector2(60.0f, 0.0f));
        root.UpdateLayout(defaultSize);
        root.Render();
        root.Input.InjectPointerReleased(grab + new Vector2(60.0f, 0.0f), PointerButton.Left);

        Check("dragging the splitter widens the folder column",
            folderTree.Bounds.Width > folderWidthBefore + 40.0f,
            $"{folderWidthBefore:0} -> {folderTree.Bounds.Width:0}");
    }

    /// <summary>True when this element or an ancestor is the popup a drag ghost lives in.</summary>
    private static bool IsInsideGhost(UiElement element)
    {
        for (var current = element; current != null; current = current.Parent)
        {
            if (current is UiPopup)
            {
                return true;
            }
        }

        return false;
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

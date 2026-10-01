using System.Text;
using Mana;
using Mana.Code;
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

    /// <summary>
    /// Asset names from a grid of tiles, taken from each tile's tooltip -- the tooltip is the asset's full
    /// path, while the visible caption is a display name that ellipsises at tile width.
    /// </summary>
    private static List<string> TileNames(Panel? grid)
    {
        var names = new List<string>();
        if (grid == null)
        {
            return names;
        }

        foreach (var child in grid.Children)
        {
            if (child is MeshAssetRow tile && tile.Tooltip is { Length: > 0 } tooltip)
            {
                names.Add(tooltip);
            }
        }

        return names;
    }

    private static void CheckThrottle(Action<string, bool, string> check)
    {
        var reads = 0;
        var throttled = MeshImportManaView.Throttled(() => ++reads);

        MeshImportManaView.ThumbnailPollMilliseconds = 100_000;
        var first = throttled();
        var second = throttled();
        var third = throttled();
        check("a throttled poll works its answer out once and reuses it", reads == 1 && first == second && second == third, $"{reads} reads");

        MeshImportManaView.ThumbnailPollMilliseconds = 0;
        throttled();
        throttled();
        check("with the throttle off every poll reads afresh, which is how tests see each change", reads == 3, $"{reads} reads");
    }

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        CheckThrottle(Check);

        // The tiles do their slow-changing work a few times a second in the running editor; these checks drive the
        // layout a handful of times in microseconds and expect each pass to see what they just changed.
        MeshImportManaView.ThumbnailPollMilliseconds = 0;

        static MeshAssetLibrary Library(MeshAssetFormats formats = MeshAssetFormats.All)
        {
            var library = new MeshAssetLibrary { Formats = formats };
            library.SetAssets(Sample);
            return library;
        }

        static byte[] ThumbnailPixels(byte value)
        {
            var pixels = new byte[MeshThumbnailCache.ThumbnailSize * MeshThumbnailCache.ThumbnailSize * 4];
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i] = value;
                pixels[i + 1] = value;
                pixels[i + 2] = value;
                pixels[i + 3] = 255;
            }

            return pixels;
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
        root.Content = MeshImportManaView.Build(root.Theme, viewModel, library, thumbnails: null);
        root.UpdateLayout(defaultSize);

        var drawn = root.Render();
        Check("the panel renders", drawn.Instances.Length > 0, $"{drawn.Instances.Length} instances");
        Check("in one draw call", drawn.Batches.Count == 1, $"{drawn.Batches.Count} batches");

        // The real editor builds the ManaUI workspace before VoxelEditorGameMode.PumpMeshThumbnails has
        // created the shared cache. Tiles therefore have to notice the cache later; a construction-time-only
        // request is what made thumbnails appear only after right-click -> Refresh Thumbnail.
        var thumbnailWorld = new ManaWorld();
        var thumbnailContext = new ManaContext(null!, null!, thumbnailWorld) { DeferGpuUploads = true };
        var lateCache = new MeshThumbnailCache(
            thumbnailContext,
            webGpuContext: null,
            cacheDirectory: Path.Combine(Path.GetTempPath(), $"ManaTests-Thumbnails-{Guid.NewGuid():N}"));
        try
        {
            ManaUiDockDemo.MeshThumbnails = lateCache;
            root.UpdateLayout(defaultSize);
            root.Render();

            Check("tiles request thumbnails when the cache appears after build",
                lateCache.RequestedCount == library.FilteredEntries.Count,
                $"{lateCache.RequestedCount} of {library.FilteredEntries.Count}");
        }
        finally
        {
            ManaUiDockDemo.MeshThumbnails = null;
            lateCache.Dispose();
        }

        // A full asset library can be much larger than the image atlas now that generated thumbnails are
        // 112px. Requesting every tile as soon as the panel is built fills the atlas with early-sorted packs,
        // leaving later filtered folders such as kitchen assets stuck on format badges.
        var manyLibrary = new MeshAssetLibrary { Formats = MeshAssetFormats.All };
        manyLibrary.SetAssets(Enumerable.Range(0, 80).Select(i => $"/w/Assets/Bulk/{i:000}.glb"));
        var visibleCache = new MeshThumbnailCache(
            thumbnailContext,
            webGpuContext: null,
            cacheDirectory: Path.Combine(Path.GetTempPath(), $"ManaTests-Thumbnails-{Guid.NewGuid():N}"));
        var visibleRoot = new UiRoot(fonts);
        visibleRoot.Content = MeshImportManaView.Build(visibleRoot.Theme, viewModel, manyLibrary, thumbnails: null);
        visibleRoot.UpdateLayout(new Vector2(defaultSize.X, 360.0f));
        visibleRoot.Render();
        try
        {
            ManaUiDockDemo.MeshThumbnails = visibleCache;
            visibleRoot.UpdateLayout(new Vector2(defaultSize.X, 360.0f));
            visibleRoot.Render();

            Check("thumbnail requests start with visible tiles rather than the whole library",
                visibleCache.RequestedCount > 0
                    && visibleCache.RequestedCount < manyLibrary.FilteredEntries.Count,
                $"{visibleCache.RequestedCount} of {manyLibrary.FilteredEntries.Count}");
        }
        finally
        {
            ManaUiDockDemo.MeshThumbnails = null;
            visibleCache.Dispose();
        }

        // The image atlas is a page for what is on screen, not storage for a whole asset library. A tiny
        // atlas that fits only one generated preview catches the failure mode directly: the second visible
        // thumbnail has to evict/repack instead of being lost forever, and the first one has to come back.
        MeshThumbnailImages.Reset();
        using (var rollingFonts = new ManaFontManager(
            images: new ManaImageAtlas(
                MeshThumbnailCache.ThumbnailSize + 8,
                MeshThumbnailCache.ThumbnailSize + 8)))
        {
            var rollingRoot = new UiRoot(rollingFonts);
            const string firstPath = "/w/Assets/Library/first.glb";
            const string secondPath = "/w/Assets/Library/second.glb";

            MeshThumbnailImages.Touch(rollingRoot, firstPath);
            MeshThumbnailImages.AcceptFinished(rollingRoot, firstPath, ThumbnailPixels(80));

            Check("a visible thumbnail becomes resident",
                MeshThumbnailImages.HasImage(firstPath) && MeshThumbnailImages.ResidentCount == 1,
                $"{MeshThumbnailImages.ResidentCount} resident");

            var generationBeforeRefresh = rollingRoot.Images.Generation;
            MeshThumbnailImages.AcceptFinished(rollingRoot, firstPath, ThumbnailPixels(96));
            Check("refreshing a resident thumbnail replaces its atlas entry",
                MeshThumbnailImages.HasImage(firstPath)
                    && rollingRoot.Images.Generation > generationBeforeRefresh,
                $"generation {generationBeforeRefresh}->{rollingRoot.Images.Generation}");

            var generationBeforeRecycle = rollingRoot.Images.Generation;
            MeshThumbnailImages.Touch(rollingRoot, secondPath);
            MeshThumbnailImages.AcceptFinished(rollingRoot, secondPath, ThumbnailPixels(160));

            Check("a full thumbnail atlas is recycled for the next visible asset",
                MeshThumbnailImages.HasImage(secondPath)
                    && rollingRoot.Images.Generation > generationBeforeRecycle,
                $"generation {generationBeforeRecycle}->{rollingRoot.Images.Generation}");

            MeshThumbnailImages.Touch(rollingRoot, firstPath);
            Check("and an earlier thumbnail can return after recycling",
                MeshThumbnailImages.HasImage(firstPath) && MeshThumbnailImages.StoredCount == 2,
                $"{MeshThumbnailImages.ResidentCount} resident, {MeshThumbnailImages.StoredCount} stored");
        }

        MeshThumbnailImages.Reset();

        // Seven checkboxes in a row used to be the format filter, wrapping to two lines in a narrow panel.
        // That whole row collapsed into one dropdown button instead, so what matters now is: the button
        // itself is compact and always in the panel, opening it reveals all eight formats (PNG for sprites among them) as checkboxes in
        // a popup (unconstrained by the panel's own width), and toggling one actually changes what the
        // library considers enabled.
        var formatsButton = FindAll<Button>(root.Content!)
            .FirstOrDefault(b => b.Text.StartsWith("Formats", StringComparison.Ordinal));
        Check("the panel has a formats dropdown button", formatsButton != null, formatsButton?.Text ?? "none");

        // Library() defaults to MeshAssetFormats.All, so every format starts enabled.
        Check("it starts showing every format enabled", formatsButton?.Text == "Formats (8/8)", formatsButton?.Text ?? "none");

        Press(root, formatsButton!.Bounds.Center);
        root.UpdateLayout(defaultSize);
        root.Render();

        var formatsPopup = root.Popups.Topmost;
        var formatToggles = formatsPopup != null ? FindAll<CheckBox>(formatsPopup) : new List<CheckBox>();
        Check("clicking it opens a popup with all eight formats", formatToggles.Count == 8, $"{formatToggles.Count} toggles");

        // The popup floats in the popup layer rather than being laid out inside the panel, so it is never
        // constrained by the panel's own width -- unlike the old inline row, it cannot overflow or need to
        // wrap regardless of how narrow the panel gets.
        Check("every toggle stays inside its popup",
            formatToggles.Count > 0 && formatToggles.All(t => formatsPopup!.Bounds.Contains(t.Bounds.Center)),
            "within bounds");

        var glbToggle = formatToggles.FirstOrDefault(t => t.Text == "GLB");
        Check("GLB starts checked", glbToggle is { IsChecked: true }, glbToggle?.IsChecked.ToString() ?? "missing");

        Press(root, glbToggle!.Bounds.Center);
        root.UpdateLayout(defaultSize);
        root.Render();

        Check("unchecking it disables the format in the library",
            !library.IsFormatEnabled(MeshAssetFormats.Glb), $"{library.IsFormatEnabled(MeshAssetFormats.Glb)}");

        Check("and the button's own count reflects it",
            formatsButton.Text == "Formats (7/8)", formatsButton.Text);

        // Re-enabled so the rest of this run (which assumes the default library.Formats = All) is unaffected.
        library.SetFormatEnabled(MeshAssetFormats.Glb, true);
        root.Popups.CloseAll();
        root.UpdateLayout(defaultSize);
        root.Render();

        // The asset list is a grid of preview tiles now, not a second tree. A tree spent the panel's whole
        // width on one filename per row; tiles spend it on assets. Only the folder sidebar is still a tree,
        // which is why this looks for tiles and asserts the sidebar is unaffected further down.
        var tileGrid = FindAll<Panel>(root.Content!)
            .FirstOrDefault(panel => panel.Children.Any(child => child is MeshAssetRow));
        Check("the panel found the asset grid", tileGrid != null,
            $"{tileGrid?.Children.Count ?? 0} tiles");

        // Every tile carries its asset's path as a tooltip, which is also what identifies it here -- the
        // caption is the display name and truncates, so it is not something to match against.
        var tileNames = TileNames(tileGrid);
        Check("a tile appears for each asset", tileNames.Count > 0, string.Join(", ", tileNames));

        Check("no asset is a bare tile with nothing to say",
            tileNames.TrueForAll(name => name.Length > 0), $"{tileNames.Count} tiles");

        var zipPath = Path.GetFullPath("/w/Assets/pack.zip");
        var zipRef = MeshAssetLibrary.EncodeZip(zipPath, "models/kitchen/model.glb");
        var zipRoot = new UiRoot(fonts);
        var zipLibrary = new MeshAssetLibrary { Formats = MeshAssetFormats.All };
        zipLibrary.SetAssets(new[] { zipRef });
        zipRoot.Content = MeshImportManaView.Build(zipRoot.Theme, viewModel, zipLibrary, thumbnails: null);
        zipRoot.UpdateLayout(defaultSize);
        zipRoot.Render();

        var zipTooltip = FindAll<MeshAssetRow>(zipRoot.Content!).FirstOrDefault()?.Tooltip ?? string.Empty;
        Check("zip tiles show decoded paths instead of the internal token",
            zipTooltip.Contains(zipPath, StringComparison.OrdinalIgnoreCase)
                && zipTooltip.Contains("models/kitchen/model.glb", StringComparison.Ordinal)
                && !zipTooltip.Contains("zipasset:", StringComparison.Ordinal),
            zipTooltip);

        Check("generic zip tile captions keep the entry path",
            FindAll<TextBlock>(zipRoot.Content!).Any(t => t.Text == "models/kitchen/model.glb"),
            string.Join(", ", FindAll<TextBlock>(zipRoot.Content!).Select(t => t.Text).Where(t => t.Contains("model", StringComparison.OrdinalIgnoreCase))));

        Check("zip fallback badges use the real mesh format",
            FindAll<TextBlock>(zipRoot.Content!).Any(t => t.Text == "GLB"),
            string.Join(", ", FindAll<TextBlock>(zipRoot.Content!).Select(t => t.Text).Where(t => t.Length <= 5)));

        var sizeSlider = FindAll<Slider>(root.Content!)
            .FirstOrDefault(slider =>
                MathF.Abs(slider.Minimum - 56.0f) < 0.01f
                && MathF.Abs(slider.Maximum - 112.0f) < 0.01f);
        Check("the import panel has a thumbnail size slider", sizeSlider != null,
            sizeSlider == null ? "none" : $"{sizeSlider.Minimum:0}..{sizeSlider.Maximum:0}");

        if (sizeSlider != null && FindAll<MeshAssetRow>(root.Content!).FirstOrDefault() is { } firstTile)
        {
            var originalSize = sizeSlider.Value;
            var tileWidthBefore = firstTile.Bounds.Width;
            var tileHeightBefore = firstTile.Bounds.Height;

            sizeSlider.Value = sizeSlider.Maximum;
            root.UpdateLayout(defaultSize);
            root.Render();

            var grown = FindAll<MeshAssetRow>(root.Content!).FirstOrDefault();
            Check("moving the thumbnail size slider resizes tiles",
                grown != null
                    && grown.Bounds.Width > tileWidthBefore + 20.0f
                    && grown.Bounds.Height > tileHeightBefore + 20.0f,
                $"{tileWidthBefore:0}x{tileHeightBefore:0} -> {grown?.Bounds.Width:0}x{grown?.Bounds.Height:0}");

            sizeSlider.Value = originalSize;
            root.UpdateLayout(defaultSize);
            root.Render();
        }

        library.Filter = "helmet";
        root.UpdateLayout(defaultSize);
        root.Render();

        var afterFilter = TileNames(FindAll<Panel>(root.Content!)
            .FirstOrDefault(panel => panel.Children.Any(child => child is MeshAssetRow)));

        Check("filtering the library updates the grid",
            afterFilter.Count == 1 && afterFilter[0].Contains("Helmet", StringComparison.OrdinalIgnoreCase),
            $"{afterFilter.Count} tiles: {string.Join(", ", afterFilter)}");

        library.Filter = "";
        root.UpdateLayout(defaultSize);
        root.Render();

        // ---------------------------------------------------------------- no footer, no Place button
        // A select-then-press-Place row (plus a "what's selected" label) used to sit under the grid. Both
        // were removed: right-click already offers Place on any tile without selecting it first (checked
        // further down), and dragging always has, so the button and label were just a slower third way to
        // do what the other two already do, and removing them hands their row back to the asset grid.
        Check("there is no Place button any more",
            !FindAll<Button>(root.Content!).Any(b => b.Text.Contains("Place", StringComparison.Ordinal)));

        Check("and the panel no longer prints its own placement instructions",
            !FindAll<TextBlock>(root.Content!).Any(t =>
                t.Text.Contains("Escape cancels", StringComparison.OrdinalIgnoreCase)));

        // ---------------------------------------------------------------- a narrow panel
        // Shrunk until the fixed rows alone want more than there is. A grid hands out slots regardless, so
        // the overflow lands on top of whatever is below it -- which is how the tree ended up running
        // through the row beneath it. The side columns keep their fixed widths regardless, so only the
        // centre list column is actually being squeezed here.
        var squeezedSize = new Vector2(FolderColumn + FolderSplitterWidth + 100.0f + DividerWidth + PlacementColumn, 260.0f);
        root.UpdateLayout(squeezedSize);
        root.Render();

        var panelBounds = root.Content!.Bounds;

        // Checked on what is drawn rather than on element bounds: a scroll viewer arranges its content
        // taller than itself on purpose, so the tree legitimately overhangs. What must not overhang is the
        // viewer, and what must not appear is any pixel outside the panel.
        // The first ScrollViewer in the tree is the folder sidebar's, which spans the panel by design. The one
        // this check is about is the asset grid's, in the picker column.
        var squeezedViewer = FindAll<ScrollViewer>(root.Content!)
            .FirstOrDefault(viewer => viewer.Bounds.X > FolderColumn);

        // The size slider is the last fixed row now, right below the grid -- the same "does the list give
        // up space instead of pushing what comes after it off-panel" property the Place button used to
        // stand in for.
        var squeezedSizeSlider = FindAll<Slider>(root.Content!)
            .FirstOrDefault(slider =>
                MathF.Abs(slider.Minimum - 56.0f) < 0.01f && MathF.Abs(slider.Maximum - 112.0f) < 0.01f);

        Check("the list gives up its space rather than holding a minimum",
            squeezedViewer != null && squeezedSizeSlider != null
                && squeezedViewer.Bounds.Bottom <= squeezedSizeSlider.Bounds.Y + 0.5f,
            $"list ends {squeezedViewer?.Bounds.Bottom:0}, size row starts {squeezedSizeSlider?.Bounds.Y:0}");

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

        // ---------------------------------------------------------------- a drag whose release never arrives
        // The real MouseUp is an OS event: losing window focus mid-drag (alt-tab, a modal stealing it, the
        // release landing outside the window) can skip it entirely. Without a recovery path the row never
        // sees OnPointerReleased, so its ghost is stuck on screen and its pointer capture black-holes every
        // later click -- ForceEndActiveDrag is what VoxelEditorGameMode calls, off its own polled button
        // state, to recover in that case.
        root.Input.InjectPointerMoved(start);
        root.Input.InjectPointerPressed(start, PointerButton.Left, 0.0);
        root.Input.InjectPointerMoved(start + new Vector2(40.0f, 20.0f));

        Check("a third drag starts normally", MeshImportRequests.Dragging != null,
            MeshImportRequests.Dragging ?? "none");
        Check("the row holds pointer capture mid-drag", row!.HasPointerCapture,
            row.HasPointerCapture ? "captured" : "not captured");

        // No InjectPointerReleased here -- this is the scenario where the OS never delivered one.
        MeshAssetRow.ForceEndActiveDrag();
        root.UpdateLayout(defaultSize);
        root.Render();

        var strandedGhost = FindAll<TextBlock>(root)
            .Count(t => t.Text.Contains("FlightHelmet", StringComparison.Ordinal) && IsInsideGhost(t));

        Check("ForceEndActiveDrag clears a ghost whose release never arrived", strandedGhost == 0,
            $"{strandedGhost} still showing");
        Check("and releases the row's pointer capture", !row.HasPointerCapture,
            row.HasPointerCapture ? "still captured" : "released");

        Check("calling it again with nothing dragging is a no-op", true, "no throw");
        MeshAssetRow.ForceEndActiveDrag();

        // The row must not be left thinking it is still pressed/dragging either, or the very next press
        // would be misread as a continuation of the old gesture instead of a fresh click.
        MeshImportRequests.Clear();
        root.Input.InjectPointerMoved(start);
        root.Input.InjectPointerPressed(start, PointerButton.Left, 0.0);
        root.Input.InjectPointerReleased(start, PointerButton.Left);

        Check("and a plain click works normally afterwards", MeshImportRequests.Dragging == null,
            MeshImportRequests.Dragging ?? "no drag pending");

        MeshImportRequests.Clear();

        // ---------------------------------------------------------------- right-click context menu
        // Filtered to "crate" specifically: it is the one sample with no same-named variant, so its tile's
        // tooltip is the bare path rather than the "(N formats available)" form a grouped entry gets --
        // exactly what the checks below need to compare against.
        library.Filter = "crate";
        library.SelectedAsset = null;
        MeshImportRequests.Clear();
        root.UpdateLayout(defaultSize);
        root.Render();

        var contextRow = FindAll<MeshAssetRow>(root.Content!).First();
        var expectedPath = contextRow.Tooltip ?? string.Empty;
        var rowCenter = contextRow.Bounds.Center;

        Check("the filtered tile is the unambiguous crate asset",
            expectedPath == Sample[5], expectedPath);

        root.Input.InjectPointerMoved(rowCenter);
        root.Input.InjectPointerPressed(rowCenter, PointerButton.Right, 0.0);
        root.UpdateLayout(defaultSize);
        root.Render();

        Check("right-clicking a tile opens a menu instead of selecting it",
            root.Popups.HasOpenPopups && library.SelectedAsset == null,
            $"popup open: {root.Popups.HasOpenPopups}, selected: {library.SelectedAsset ?? "none"}");

        root.Input.InjectPointerReleased(rowCenter, PointerButton.Right);

        Check("and does not start a drag either",
            MeshImportRequests.Dragging == null, MeshImportRequests.Dragging ?? "none");

        var contextMenuRows = FindAll<Control>(root.Popups.Topmost!).Where(IsMenuRow).ToList();
        Check("the context menu has Place, Refresh, Show in Explorer and Copy Path",
            contextMenuRows.Count == 4, $"{contextMenuRows.Count} rows");

        // "Place"
        root.Input.InjectPointerMoved(contextMenuRows[0].Bounds.Center);
        root.Input.InjectPointerPressed(contextMenuRows[0].Bounds.Center, PointerButton.Left, 1.0);
        root.Input.InjectPointerReleased(contextMenuRows[0].Bounds.Center, PointerButton.Left);

        Check("Place asks for a placement of the right-clicked asset",
            MeshImportRequests.TakePlacement() == expectedPath, "requested");
        Check("and closes the menu", !root.Popups.HasOpenPopups);

        // "Refresh Thumbnail" -- no cache exists in this test (Build was called with thumbnails: null and
        // nothing else has set the static ManaUiDockDemo.MeshThumbnails), so this only has to not throw.
        root.Input.InjectPointerMoved(rowCenter);
        root.Input.InjectPointerPressed(rowCenter, PointerButton.Right, 2.0);
        root.Input.InjectPointerReleased(rowCenter, PointerButton.Right);
        root.UpdateLayout(defaultSize);
        root.Render();

        contextMenuRows = FindAll<Control>(root.Popups.Topmost!).Where(IsMenuRow).ToList();
        var threw = false;
        try
        {
            root.Input.InjectPointerMoved(contextMenuRows[1].Bounds.Center);
            root.Input.InjectPointerPressed(contextMenuRows[1].Bounds.Center, PointerButton.Left, 3.0);
            root.Input.InjectPointerReleased(contextMenuRows[1].Bounds.Center, PointerButton.Left);
        }
        catch
        {
            threw = true;
        }

        Check("Refresh Thumbnail does nothing harmful with no cache wired up", !threw);

        // "Copy Path" -- deliberately not clicking "Show in Explorer" (row 2), which launches a real OS
        // file browser process and has no business doing that from a headless test run.
        root.Input.InjectPointerMoved(rowCenter);
        root.Input.InjectPointerPressed(rowCenter, PointerButton.Right, 4.0);
        root.Input.InjectPointerReleased(rowCenter, PointerButton.Right);
        root.UpdateLayout(defaultSize);
        root.Render();

        contextMenuRows = FindAll<Control>(root.Popups.Topmost!).Where(IsMenuRow).ToList();
        root.Input.InjectPointerMoved(contextMenuRows[3].Bounds.Center);
        root.Input.InjectPointerPressed(contextMenuRows[3].Bounds.Center, PointerButton.Left, 5.0);
        root.Input.InjectPointerReleased(contextMenuRows[3].Bounds.Center, PointerButton.Left);

        Check("Copy Path puts the asset's path on the clipboard",
            TextBox.Clipboard.Text == expectedPath, TextBox.Clipboard.Text);

        library.Filter = "";
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

        CheckFolderExpandCollapsePersists(fonts, check, defaultSize);
        CheckDownloadProgress(fonts, check, defaultSize);
    }

    /// <summary>
    /// MeshAssetLibrary.FolderTree used to hand back a freshly built tree -- new MeshAssetFolder instances
    /// throughout -- on every single call. The folder sidebar's TreeView tracks which nodes are expanded by
    /// object identity (deliberately: see TreeView's own comment on why it uses reference equality), and
    /// collapsing or expanding a node queues exactly the kind of rebuild that re-fetches FolderTree. The
    /// result: the click's own state change was real, but the very next layout pass re-fetched a FolderTree
    /// full of brand-new objects that did not match anything in the just-updated collapsed/expanded set, so
    /// every folder fell back to IsExpandedByDefault (true) and looked untouched -- clicking the arrow did
    /// nothing, visibly, even though ToggleExpanded ran correctly.
    /// </summary>
    private static void CheckFolderExpandCollapsePersists(ManaFontManager fonts, Action<string, bool, string> check, Vector2 panelSize)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var library = new MeshAssetLibrary { Formats = MeshAssetFormats.All };
        library.SetAssets(Sample);

        var root = new UiRoot(fonts);
        var viewModel = new ManaVoxel.Code.UI.VoxelEditorViewModel();
        root.Content = MeshImportManaView.Build(root.Theme, viewModel, library, thumbnails: null);
        root.UpdateLayout(panelSize);
        root.Render();

        var tree = Find<TreeView>(root.Content!);
        Check("the panel has a folder tree", tree != null);
        if (tree == null)
        {
            return;
        }

        // "Models" (from Sample's Kenney/Nature and Helmet paths) has two children once single-child
        // chains collapse, so it survives as its own row rather than merging away -- exactly the shape
        // that needs a real toggle rather than always being the only thing on screen.
        var before = tree.VisibleItems.OfType<MeshAssetFolder>().FirstOrDefault(f => f.Name == "Models");
        Check("found a folder with children to toggle", before != null,
            string.Join(", ", tree.VisibleItems.OfType<MeshAssetFolder>().Select(f => f.Name)));
        if (before == null)
        {
            return;
        }

        Check("starts expanded", tree.IsExpanded(before));

        tree.ToggleExpanded(before);
        root.UpdateLayout(panelSize);
        root.Render();

        var afterCollapse = tree.VisibleItems.OfType<MeshAssetFolder>().FirstOrDefault(f => f.Name == "Models");
        Check("collapsing it survives a later layout pass rather than silently reverting",
            afterCollapse != null && !tree.IsExpanded(afterCollapse),
            afterCollapse == null ? "row vanished" : $"IsExpanded={tree.IsExpanded(afterCollapse)}");

        if (afterCollapse != null)
        {
            tree.ToggleExpanded(afterCollapse);
        }

        root.UpdateLayout(panelSize);
        root.Render();

        var afterExpand = tree.VisibleItems.OfType<MeshAssetFolder>().FirstOrDefault(f => f.Name == "Models");
        Check("and expanding it again also survives",
            afterExpand != null && tree.IsExpanded(afterExpand),
            afterExpand == null ? "row vanished" : $"IsExpanded={tree.IsExpanded(afterExpand)}");
    }

    /// <summary>
    /// A tile's download progress bar, driven the same way ViewportOverlaysTests.cs drives the corner
    /// status widget -- through a real ManaAssetCatalog with a gated download delegate, since Started/
    /// Finished are internal to ManaCore.Assets and there is no InternalsVisibleTo for tests.
    /// <para>
    /// ManaAssetCatalog.Current is a static hand-off the real editor sets once at browser startup; a test
    /// that assigns it must restore whatever was there before, or a later suite in the same process would
    /// see this test's now-defunct catalog instead of the null it expects on desktop.
    /// </para>
    /// </summary>
    private static void CheckDownloadProgress(ManaFontManager fonts, Action<string, bool, string> check, Vector2 panelSize)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var previousCatalog = ManaCore.Assets.ManaAssetCatalog.Current;
        var directory = System.IO.Directory.CreateTempSubdirectory("mana-import-progress-");
        try
        {
            var library = new MeshAssetLibrary { Formats = MeshAssetFormats.All };
            library.SetAssets(new[] { "Assets/flighthelmet.glb" });

            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                version = 1,
                files = new[]
                {
                    new { path = "Assets/flighthelmet.glb", url = "AssetLibrary/flighthelmet.glb", sha256 = "flighthelmet", size = 3,
                        dependencies = Array.Empty<string>() }
                },
                models = new object[] { new { path = "Assets/flighthelmet.glb" } }
            });
            var gate = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var catalog = new ManaCore.Assets.ManaAssetCatalog(json, directory.FullName,
                async (_, _, _) => await gate.Task);
            ManaCore.Assets.ManaAssetCatalog.Current = catalog;

            var root = new UiRoot(fonts);
            var viewModel = new ManaVoxel.Code.UI.VoxelEditorViewModel();
            root.Content = MeshImportManaView.Build(root.Theme, viewModel, library, thumbnails: null);
            root.UpdateLayout(panelSize);

            var bar = Find<ProgressBar>(root.Content!);
            Check("the tile has a progress bar", bar != null);
            Check("hidden before anything asks for the asset", bar!.Visibility == Visibility.Collapsed);

            var row = Find<MeshAssetRow>(root.Content!);
            Check("the tile has a tooltip naming its size and source",
                row?.Tooltip is { } beforeTooltip
                    && beforeTooltip.Contains("Size:") && beforeTooltip.Contains("Source: AssetLibrary/flighthelmet.glb"),
                row?.Tooltip ?? "none");
            Check("but nothing about a download that has not happened yet",
                !(row?.Tooltip ?? "").Contains("Downloaded in"), row?.Tooltip ?? "none");

            var download = catalog.EnsureAsync("Assets/flighthelmet.glb");
            root.UpdateLayout(panelSize);
            Check("shown while its download is in flight", bar.Visibility == Visibility.Visible);
            Check("indeterminate -- no byte-level progress is available", bar.IsIndeterminate);

            gate.SetResult(new byte[] { 1, 2, 3 });
            download.GetAwaiter().GetResult();
            root.UpdateLayout(panelSize);
            Check("hidden again once the download completes", bar.Visibility == Visibility.Collapsed);
            Check("the tooltip picks up how long that download took",
                row?.Tooltip is { } afterTooltip && afterTooltip.Contains("Downloaded in"), row?.Tooltip ?? "none");
        }
        finally
        {
            ManaCore.Assets.ManaAssetCatalog.Current = previousCatalog;
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A context menu's rows, identified by type name rather than type since MenuRow is internal to ManaUI
    /// -- the same idiom MenusTests.cs already uses for the main menu bar's own dropdowns.
    /// </summary>
    private static bool IsMenuRow(Control control) => control.GetType().Name == "MenuRow";

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

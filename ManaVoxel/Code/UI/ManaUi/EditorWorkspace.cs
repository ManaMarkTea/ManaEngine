using Mana;
using Mana.Code.UI.ManaUi;
using ManaCore.Graphics;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Search;

using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The editor's ManaUI workspace: a full-width menu, a toolbar strip, two resizable
    /// columns, and a status bar -- arranged from a named preset and remembered across restarts.
    /// </summary>
    public sealed class EditorWorkspace
    {
        // Both were set to comfortable round numbers; these are what the content actually needs at the
        // base theme's font size, and every pixel here is one the viewport does not get. MenuBarMinHeight
        // is a floor, not the real answer -- see Build's menuBarHeight local, which grows this per-theme
        // for a font (LCARS's Antonio) whose line height exceeds it.
        private const float MenuBarMinHeight = 28.0f;
        private const float StatusBarHeight = 24.0f;

        private const float DefaultLeftWidth = 300.0f;
        private const float DefaultRightWidth = 380.0f;
        private const float DefaultBottomHeight = 220.0f;

        public static IReadOnlyDictionary<string, string> PanelTitles => EditorPanelRegistry.Titles;

        private readonly System.Collections.Concurrent.ConcurrentQueue<string> PanelRequests = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UiRoot, EditorWorkspace> Workspaces = new();
        private EventHandler<UiTheme>? _themeChanged;

        private EditorWorkspace() { }

        public static EditorWorkspace For(UiRoot root) => Workspaces.GetValue(root, _ => new EditorWorkspace());

        public void RequestPanel(string id) => PanelRequests.Enqueue(id);

        public static void RequestPanel(ManaWorld world, string id)
        {
            if (world.Context.GameMode?.ManaUi?.Root is { } root)
            {
                For(root).RequestPanel(id);
            }
        }

        public DockDragController? DragController { get; private set; }

        public EditorAssetServices Assets { get; private set; } = new();

        public PlaceSearchModel PlaceSearch { get; } = new();

        public static IReadOnlyList<string> PanelIds => EditorPanelRegistry.Ids;

        /// <summary>Builds the workspace over a <see cref="UiRoot"/> so it can be exercised headlessly. Panels needing <paramref name="editor"/>/<paramref name="world"/> just don't appear without them.</summary>
        public static UiElement Attach(
            UiRoot root,
            ManaUiShowcaseViewModel vm,
            Func<string>? liveStat = null,
            bool persist = true,
            VoxelEditorViewModel? editor = null,
            global::Mana.ManaWorld? world = null,
            string? layoutPath = null)
            => For(root).AttachCore(root, vm, liveStat, persist, editor, world, layoutPath);

        private UiElement AttachCore(
            UiRoot root, ManaUiShowcaseViewModel vm, Func<string>? liveStat, bool persist,
            VoxelEditorViewModel? editor, ManaWorld? world, string? layoutPath)
        {
            if (world != null)
            {
                Assets = EditorAssetServices.For(world);
            }
            // Overridable so persistence can be exercised against a scratch file rather than the user's real one.
            layoutPath ??= EditorWorkspaceLayout.DefaultPath;
            if (_themeChanged is { } oldThemeHandler)
            {
                root.ThemeChanged -= oldThemeHandler;
                _themeChanged = null;
            }

            // Applied before anything is built: views read theme colours as they construct, not at render time.
            var saved = EditorWorkspaceLayout.Load(layoutPath, persist);
            if (saved != null && root.Theme.Kind != saved.Theme)
            {
                root.Theme = UiTheme.Create(saved.Theme);
            }

            ManaUiDensity.Apply(root.Theme);

            if (saved?.FontFamily is { Length: > 0 } savedFont)
            {
                root.Theme.SetFontFamily(savedFont);
            }

            if (saved != null)
            {
                root.Theme.BackdropBlur = saved.BackdropBlur;
            }

            ApplyGridTheme(world, root.Theme);

            // Owned by the workspace so a theme switch can swap the whole thing out from under itself.
            var shell = new ContentHost();

            ManaUiDensity.PresetChanged -= _densityChanged;
            _densityChanged = DensityHandler(root);
            ManaUiDensity.PresetChanged += _densityChanged;

            shell.Child = Build(root, shell, vm, liveStat, persist, editor, world, layoutPath);
            return shell;
        }

        private void BindDesktopAssets(DockDragController controller)
        {
            foreach (var window in controller.DesktopWindows)
            {
                For(window.Root).Assets = Assets;
            }
        }

        // The process-wide preference event must not keep closed workspace roots alive.
        private static EventHandler DensityHandler(UiRoot root)
        {
            var weakRoot = new WeakReference<UiRoot>(root);
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                if (weakRoot.TryGetTarget(out var target))
                {
                    target.Theme = UiTheme.Create(target.Theme.Kind);
                }
                else
                {
                    ManaUiDensity.PresetChanged -= handler;
                }
            };
            return handler;
        }

        /// <summary>The Voxel Tools window, which floats while a voxel node is selected or a voxel tool is on.</summary>
        public const string VoxelToolsPanelId = "voxeltools";

        // Where each tool window was last left, for the next time it comes up. Per run: a window that opens by
        // itself should not come back somewhere a screen change has made odd.
        private readonly Dictionary<string, (Vector2? Position, Vector2 Size)> ToolWindowPlaces = new()
        {
            [GridMapToolManaView.PanelId] = (null, new Vector2(300.0f, 520.0f)),
            [MeasureToolManaView.PanelId] = (null, new Vector2(300.0f, 460.0f)),
            [TerrainToolManaView.PanelId] = (null, new Vector2(300.0f, 360.0f)),
            [VoxelToolsPanelId] = (null, new Vector2(300.0f, 460.0f)),
            [GridMapWfcManaView.PanelId] = (null, new Vector2(340.0f, 600.0f)),
            [SpriteAssetManaView.PanelId] = (null, new Vector2(400.0f, 640.0f)),
            [PrefabChangesManaView.PanelId] = (null, new Vector2(480.0f, 320.0f)),
            [UiDesignerManaView.PanelId] = (null, new Vector2(1480.0f, 860.0f)),
            // Floats over the viewport like the tool windows, so the scan it receives stays in view.
            ["livescan"] = (null, new Vector2(340.0f, 460.0f))
        };

        /// <summary>The live density subscription, so re-attaching replaces it rather than stacking a second one.</summary>
        private EventHandler? _densityChanged;

        private UiElement Build(
            UiRoot root,
            ContentHost shell,
            ManaUiShowcaseViewModel vm,
            Func<string>? liveStat,
            bool persist,
            VoxelEditorViewModel? editor,
            global::Mana.ManaWorld? world,
            string layoutPath)
        {
            DragController?.CloseDesktopWindows();
            // Before anything reads a token: the preset tightens the theme's own metrics rather than being
            // consulted control by control.
            ManaUiDensity.Apply(root.Theme);

            var theme = root.Theme;
            var debug = root.Debug;

            // Same formula MenuBarHeading/MenuFlyout size themselves by -- see their matching comment for
            // why theme.ControlHeight alone isn't enough once the active font's own line height (LCARS's
            // Antonio) is taller than it. Computed once here so the bar's own container, the dock area
            // below it, and the viewport-overlay offset all agree with what the bar actually measures to,
            // instead of the container clipping to a fixed 28px while the content wants more and spills
            // into the toolbar strip underneath it.
            var menuBarHeight = MathF.Max(MenuBarMinHeight, MathF.Max(theme.ControlHeight, root.Fonts.Get(theme.FontFamily, theme.FontSize, root.Scale).LineHeight));

            var top = new DockHost();
            var left = new DockHost();
            var right = new DockHost();
            var bottom = new DockHost();

            // Read each frame: the main camera is replaced on scene load.
            global::Mana.CameraComponent? camera() => world?.MainCamera;
            Action<global::Mana.ManaScene>? saveScene = editor == null
                ? null
                : scene => editor.Levels.SaveScene(scene);
            Func<global::Mana.ManaScene, bool>? sceneHasChanges = editor == null
                ? null
                : scene => editor.Levels.SceneHasChanges(scene);

            Func<float> projectFolderWidth = () => 210f;

            // Persistence stores ids and asks for the panel back by name, since content is application-owned.
            DockItem? Resolve(string id)
            {
                var item = BuildPanel(id);
                if (item != null)
                {
                    item.Content.ProfileName = item.Title;
                }
                return item;
            }

            var panelContext = new EditorPanelRegistry.BuildContext(
                theme, vm, liveStat, editor, world, () => projectFolderWidth(), saveScene, sceneHasChanges, Assets);
            DockItem? BuildPanel(string id) => EditorPanelRegistry.Build(id, panelContext);

            var saved = EditorWorkspaceLayout.Load(layoutPath, persist);
            var state = new EditorWorkspaceLayout.State
            {
                Preset = saved?.Preset ?? WorkspacePreset.RightColumn,
                Theme = root.Theme.Kind,
                BackdropBlur = root.Theme.BackdropBlur
            };

            // One controller over every host, so a panel torn out of any of them can be dropped into any other.
            var dragController = new DockDragController(root, new[] { top, left, right, bottom });
            DragController = dragController;

            var layoutSession = new EditorWorkspaceLayout(root, top, left, right, bottom, dragController, Resolve, persist, layoutPath);
            layoutSession.Restore(saved, state.Preset);
            BindDesktopAssets(dragController);

            // AllowResize off rather than dropping ResizablePanel entirely: DockHost/DockTabGroup always
            // measure to whatever height they are given (DockTabGroup.MeasureOverride literally returns
            // availableSize back unchanged) rather than to their own content, the same way every other
            // dock strip in this workspace relies on a ResizablePanel to say how tall it is. Wrapping the
            // toolbar in a plain auto-sizing container instead asked it to measure against an infinite
            // height and made the whole strip disappear.
            //
            // The height itself is still tied to the theme rather than a flat hand-picked constant (the
            // old DefaultTopHeight, 38, was Dark-theme-specific and silently wrong the moment a theme with
            // a different ControlHeight -- LCARS -- came along): every control on the row shares
            // theme.ControlHeight, and 14 is the toolbar's own vertical padding (VoxelEditorToolbarManaView,
            // 3 top + 3 bottom) plus the tab-strip chrome HideStripWhenSingle leaves behind, measured once
            // against Dark's ControlHeight of 24 giving the original 38.
            var topPanelHeight = theme.ControlHeight + 14.0f;
            var topPanel = new ResizablePanel
            {
                Edge = Dock.Top,
                AllowResize = false,
                MinimumSize = topPanelHeight,
                MaximumSize = topPanelHeight,
                Size = topPanelHeight,
                Background = theme.Frosted(theme.Background),
                BackdropBlur = theme.BackdropBlur,
                Child = top.CenterV()
            };

            var leftPanel = new ResizablePanel
            {
                Edge = Dock.Left,
                MinimumSize = 200.0f,
                Size = saved is { LeftWidth: > 0.0f } ? saved.LeftWidth : DefaultLeftWidth,
                Background = theme.Frosted(theme.Background),
                BackdropBlur = theme.BackdropBlur,
                Child = left
            };

            var rightPanel = new ResizablePanel
            {
                Edge = Dock.Right,
                MinimumSize = 220.0f,
                Size = saved is { RightWidth: > 0.0f } ? saved.RightWidth : DefaultRightWidth,
                Background = theme.Frosted(theme.Background),
                BackdropBlur = theme.BackdropBlur,
                Child = right
            };

            var bottomPanel = new ResizablePanel
            {
                Edge = Dock.Bottom,
                MinimumSize = 120.0f,
                Size = saved is { BottomHeight: > 0.0f } ? saved.BottomHeight : DefaultBottomHeight,
                Background = theme.Frosted(theme.Background),
                BackdropBlur = theme.BackdropBlur,
                Child = bottom
            };

            projectFolderWidth = () => leftPanel.Visibility != Visibility.Collapsed
                && bottom.AllItems().Any(item => item.Id == "project")
                ? MathF.Max(120f, leftPanel.Size - 8f - theme.Spacing) : 210f;

            void Save() => layoutSession.Save(state, leftPanel.Size, rightPanel.Size, bottomPanel.Size);

            // An empty strip would leave a resize handle floating against nothing, so it hides itself until
            // it has something to hold.
            void SyncColumnVisibility()
            {
                topPanel.Visibility = top.RootNode == null ? Visibility.Collapsed : Visibility.Visible;
                leftPanel.Visibility = left.RootNode == null ? Visibility.Collapsed : Visibility.Visible;
                rightPanel.Visibility = right.RootNode == null ? Visibility.Collapsed : Visibility.Visible;
                bottomPanel.Visibility = bottom.RootNode == null ? Visibility.Collapsed : Visibility.Visible;
            }

            // Voxel Tools is the strip's only occupant in every preset, so its tab header is pure chrome --
            // a title bar for a toolbar that never has anything to tab against. Re-applied on every layout
            // change rather than once, so a panel later dragged into the strip gets its header back the
            // moment it stops being the sole occupant.
            void HideTopStripHeaderWhenSingle()
            {
                if (top.RootNode is DockTabGroup group)
                {
                    group.HideStripWhenSingle = true;
                }
            }

            HideTopStripHeaderWhenSingle();

            top.LayoutChanged += (_, _) =>
            {
                HideTopStripHeaderWhenSingle();
                SyncColumnVisibility();
                Save();
            };

            left.LayoutChanged += (_, _) =>
            {
                SyncColumnVisibility();
                Save();
            };

            right.LayoutChanged += (_, _) =>
            {
                SyncColumnVisibility();
                Save();
            };

            bottom.LayoutChanged += (_, _) =>
            {
                SyncColumnVisibility();
                Save();
            };

            // Committed, not every pixel of the drag: a resize would otherwise rewrite the file on every
            // frame it moved.
            // Floating panels are part of the layout too: created, moved, docked back or closed.
            dragController.FloatingChanged += (_, _) =>
            {
                BindDesktopAssets(dragController);
                Save();
            };

            leftPanel.SizeCommitted += (_, _) => Save();
            rightPanel.SizeCommitted += (_, _) => Save();
            bottomPanel.SizeCommitted += (_, _) => Save();

            SyncColumnVisibility();

            // A column emptied by dragging its last panel out hides, so it gets a band along its window edge to
            // drop back into, previewed at the size it will open at.
            const float EmptyZoneBand = 40.0f;
            float TopEdge() => menuBarHeight + (top.RootNode == null ? 0.0f : topPanel.Size);
            float BottomEdge() => root.Size.Y - StatusBarHeight;
            float BottomStripTop() => BottomEdge() - (bottom.RootNode == null ? 0.0f : bottomPanel.Size);
            float RightEdge() => root.Size.X - (right.RootNode == null ? 0.0f : rightPanel.Size);

            left.EmptyDropZone = () => new UiRect(0.0f, TopEdge(), EmptyZoneBand, BottomStripTop() - TopEdge());
            left.EmptyDropPreview = () => new UiRect(0.0f, TopEdge(), leftPanel.Size, BottomStripTop() - TopEdge());
            right.EmptyDropZone = () => new UiRect(root.Size.X - EmptyZoneBand, TopEdge(), EmptyZoneBand, BottomEdge() - TopEdge());
            right.EmptyDropPreview = () => new UiRect(root.Size.X - rightPanel.Size, TopEdge(), rightPanel.Size, BottomEdge() - TopEdge());
            bottom.EmptyDropZone = () => new UiRect(0.0f, BottomEdge() - EmptyZoneBand, RightEdge(), EmptyZoneBand);
            bottom.EmptyDropPreview = () => new UiRect(0.0f, BottomEdge() - bottomPanel.Size, RightEdge(), bottomPanel.Size);
            top.EmptyDropZone = () => new UiRect(0.0f, menuBarHeight, root.Size.X, EmptyZoneBand * 0.6f);
            top.EmptyDropPreview = () => new UiRect(0.0f, menuBarHeight, root.Size.X, EmptyZoneBand);

            if (_themeChanged is { } previousThemeHandler)
            {
                root.ThemeChanged -= previousThemeHandler;
                _themeChanged = null;
            }
            EventHandler<UiTheme> themeHandler = (_, changed) =>
            {
                state.Theme = changed.Kind;
                state.BackdropBlur = changed.BackdropBlur;
                ManaUiDensity.Apply(changed);
                ApplyGridTheme(world, changed);
                Save();
                shell.Child = Build(root, shell, vm, liveStat, persist, editor, world, layoutPath);
            };
            _themeChanged = themeHandler;
            root.ThemeChanged += themeHandler;

            void ChooseTheme(UiThemeKind kind) => root.RequestTheme(kind);

            void ChooseBlur(float blur)
            {
                if (MathF.Abs(root.Theme.BackdropBlur - blur) < 0.01f)
                {
                    return;
                }

                root.Theme.BackdropBlur = blur;
                state.BackdropBlur = blur;
                Save();
                shell.Child = Build(root, shell, vm, liveStat, persist, editor, world, layoutPath);
            }

            void ChooseFont(string family)
            {
                if (root.Theme.FontFamily == family && root.Theme.FontFamilyMedium == family)
                {
                    return;
                }

                root.Theme.SetFontFamily(family);

                // Every measured width in the tree was computed with the old face, so this needs a full relayout.
                root.InvalidateAll();
                Save();
                shell.Child = Build(root, shell, vm, liveStat, persist, editor, world, layoutPath);
            }

            // Set while a tool window is closed because what it was for went away, not by hand.
            var closingToolWindow = false;

            void TogglePanel(string id)
            {
                if (dragController.FindDesktop(id) is { } desktop)
                {
                    desktop.Focus();
                    return;
                }
                // Floating windows count as open, or reopening one builds a second copy on screen.
                if (dragController.FindFloating(id) is { } floating)
                {
                    floating.BringToFront();
                    floating.Flash();
                    return;
                }

                var docked = layoutSession.Docked.FirstOrDefault(item => item.Id == id);

                if (docked != null)
                {
                    docked.Owner?.Remove(docked);
                    return;
                }

                if (Resolve(id) is not { } item)
                {
                    return;
                }

                // The tool windows float over the viewport's top-left corner, where they cover neither the
                // Outliner nor the Inspector, and come back wherever and however big they were last left.
                if (ToolWindowPlaces.TryGetValue(id, out var place))
                {
                    var corner = new Vector2(
                        (leftPanel.Visibility == Visibility.Collapsed ? 0.0f : leftPanel.Size) + 12.0f,
                        menuBarHeight + (topPanel.Visibility == Visibility.Collapsed ? 0.0f : topPanel.Size) + 12.0f);
                    var window = new UiWindow(item.Title)
                    {
                        Width = place.Size.X,
                        Height = place.Size.Y,
                        Child = item.Content
                    };
                    window.Closed += (_, _) =>
                    {
                        if (window.Bounds.Width > 0.0f)
                        {
                            ToolWindowPlaces[id] = (new Vector2(window.Bounds.X, window.Bounds.Y), new Vector2(window.Bounds.Width, window.Bounds.Height));
                        }

                        // Closed by hand, the voxel tools put the tool down, or the viewport would go on drawing
                        // with no window saying so; the generator stays shut until asked for again.
                        if (!closingToolWindow && id == VoxelToolsPanelId && editor != null)
                        {
                            editor.IsSelectToolActive = true;
                        }

                        if (!closingToolWindow && id == SpriteAssetManaView.PanelId)
                        {
                            SpriteAssetEditing.Close();
                        }

                        // Likewise measuring: closing its window puts the ruler away.
                        if (!closingToolWindow && id == MeasureToolManaView.PanelId && editor != null)
                        {
                            editor.Measure.Active = false;
                        }

                        if (!closingToolWindow && id == GridMapWfcManaView.PanelId)
                        {
                            ManaVoxel.Code.GridMaps.GridMapWfcEditing.WindowOpen = false;
                        }

                        if (!closingToolWindow && id == PrefabChangesManaView.PanelId && world != null)
                        {
                            ProjectAssetViewModel.For(world).ReviewOpen = false;
                        }
                    };
                    root.ShowWindow(window, place.Position ?? corner);
                    dragController.RegisterFloating(window, item);
                    return;
                }

                // Voxel Tools goes back to its own strip, Import to its own, and everything else prefers the
                // right column.
                var host = id == "tools" ? top
                    : id is "import" or "project" or "scenes" ? bottom
                    : right.RootNode != null ? right : left;
                host.Dock(item, DockSide.Center, DockHost.FindFirstGroup(host.RootNode));
            }

            // Edit > Preferences: brings the panel up, or to the front if it is already floating. A panel that
            // is already docked is on screen, so unlike TogglePanel this never closes it.
            void OpenPanel(string id)
            {
                if (layoutSession.Docked.FirstOrDefault(item => item.Id == id) is not { } docked)
                {
                    TogglePanel(id);
                    return;
                }

                // Already docked, perhaps behind another tab: bring it to the front.
                foreach (var host in new[] { top, left, right, bottom })
                {
                    if (host.GroupOf(docked) is { } group)
                    {
                        group.SelectedIndex = group.Items.ToList().IndexOf(docked);
                    }
                }
            }

            bool IsPanelOpen(string id) =>
                layoutSession.Docked.Any(item => item.Id == id)
                || dragController.FindFloating(id) != null || dragController.FindDesktop(id) != null;

            // The tool windows exist only while there is something for them: the Grid Map window while a grid map
            // is selected, Voxel Tools while a voxel node is or a voxel tool is on. Each comes up when that starts
            // and goes when it stops -- only then, so one closed by hand stays closed. Checked on the first frame
            // too, so a layout saved with one up cannot bring it back at start-up with nothing to show.
            var toolWindowWanted = new Dictionary<string, bool>();
            global::Mana.Code.Nodes.ManaSceneNode? watchedSelection = null;
            void SyncToolWindow(string id, bool wanted)
            {
                if (toolWindowWanted.TryGetValue(id, out var was) && was == wanted)
                {
                    return;
                }

                toolWindowWanted[id] = wanted;
                if (wanted == IsPanelOpen(id))
                {
                    return;
                }

                if (wanted)
                {
                    TogglePanel(id);
                    return;
                }

                var docked = layoutSession.Docked.FirstOrDefault(item => item.Id == id);
                docked?.Owner?.Remove(docked);
                closingToolWindow = true;
                try
                {
                    dragController.FindFloating(id)?.Close();
                }
                finally
                {
                    closingToolWindow = false;
                }
            }

            void SyncGridMapPanel()
            {
                var selected = world?.Selection.SelectedNode;

                // Editing windows are put away while playing, and come back as they were when play stops.
                var editing = editor?.Levels.IsPlaying != true;
                SyncToolWindow(GridMapToolManaView.PanelId, editing && selected is ManaVoxel.Code.GridMaps.ManaGridMapNode);
                SyncToolWindow(TerrainToolManaView.PanelId, editing && selected is global::Mana.Code.Nodes.ManaTerrainNode);
                // Up while measuring.
                SyncToolWindow(MeasureToolManaView.PanelId, editing && editor?.Measure.Active == true);
                // Up until closed, whatever is selected: it works on the last grid map when another thing is.
                SyncToolWindow(GridMapWfcManaView.PanelId, editing && ManaVoxel.Code.GridMaps.GridMapWfcEditing.WindowOpen);
                // Up while an asset is open in it; Edit in the Inspector or the Project panel opens one.
                SyncToolWindow(SpriteAssetManaView.PanelId, editing && SpriteAssetEditing.WindowOpen);
                // Up while asked for, and gone once its instance has left the level.
                if (world != null)
                {
                    var review = ProjectAssetViewModel.For(world);
                    SyncToolWindow(PrefabChangesManaView.PanelId, editing && review.ReviewOpen && review.CurrentReview != null);
                }
                if (editor == null)
                {
                    return;
                }

                // A voxel node selected is the one the tools draw into, level-loaded ones included.
                if (!ReferenceEquals(selected, watchedSelection) && selected is global::Mana.Code.Nodes.ManaVoxelsNode voxels)
                {
                    editor.ActivateVoxelNode(voxels);
                }

                watchedSelection = selected;
                SyncToolWindow(VoxelToolsPanelId, editing && (selected is global::Mana.Code.Nodes.ManaVoxelsNode || !editor.IsSelectToolActive));
            }

            // Polled, because selection changes from everywhere -- a viewport click, the outliner, undo --
            // and none of them know about panels. A binding is read once a frame before layout, which is
            // exactly a per-frame hook; the watcher is a zero-size text that never actually changes.
            var selectionWatcher = new TextBlock().Width(0.0f).Height(0.0f);
            selectionWatcher.BindPolled(text => text.Text, () =>
            {
                while (PanelRequests.TryDequeue(out var requested))
                {
                    // The About box is a dialog, not a panel; scripts reach it by this id.
                    if (requested == "about-box")
                    {
                        AboutBoxManaView.Show(root, () => OpenPanel("about"));
                        continue;
                    }

                    OpenPanel(requested);
                }

                SyncGridMapPanel();
                return string.Empty;
            });

            void Choose(WorkspacePreset preset)
            {
                state.Preset = preset;
                layoutSession.ApplyPreset(preset);
                SyncColumnVisibility();
                Save();
            }

            // Unlike an ordinary preset switch (Choose, above), which deliberately leaves a torn-out panel
            // floating where the user put it, resetting means back to a clean slate -- so every floating
            // window closes first. ToList() snapshots the windows before closing any of them: each Close()
            // fires FloatingChanged synchronously, which would otherwise mutate FloatingWindows out from
            // under this same foreach.
            void ResetPanels()
            {
                dragController.CloseDesktopWindows();
                foreach (var window in dragController.FloatingWindows.ToList())
                {
                    window.Close();
                }

                Choose(state.Preset);
            }

            // Rows: the toolbar strip, the main area, and the Import strip -- but the Import strip only
            // spans the left column and the viewport (columns 0-1). The right column spans both of the
            // lower rows so it reaches the status bar unbroken, matching Unreal's own Outliner/Details
            // column running the full height beside a Content Browser docked under the viewport.
            var viewportToolbarHeight = editor == null ? 0f : menuBarHeight;
            var viewportToolbar = new Border().Height(viewportToolbarHeight)
                .Fill(theme.Surface).Outline(theme.Border, 1f);
            if (editor != null)
            {
                viewportToolbar.Child = ViewportToolbarManaView.BuildBar(theme, editor, camera, world);
            }

            var dockArea = new Grid()
                .Rows("Auto, *, Auto")
                .Columns("Auto, *, Auto")
                .Children(
                    topPanel.GridRow(0).GridColumn(0, span: 3),
                    leftPanel.GridRow(1).GridColumn(0),
                    new Grid().Rows("Auto, *").Columns("*").Children(viewportToolbar).GridRow(1).GridColumn(1),
                    rightPanel.GridRow(1, span: 2).GridColumn(2),
                    bottomPanel.GridRow(2).GridColumn(0, span: 2))
                .Anchor(top: menuBarHeight, left: 0.0f, right: 0.0f, bottom: StatusBarHeight);

            // Overlay first so the dock area draws over it; it works out the viewport from what the strips
            // cover. The right column already excludes itself via rightWidth regardless of row span, so the
            // bottom strip's height only needs to be subtracted once for the left+centre area it actually sits under.
            return new Canvas().Children(
                new ViewportOverlayLayer(
                    theme,
                    () => leftPanel.Visibility == Visibility.Collapsed ? 0.0f : leftPanel.Size,
                    () => rightPanel.Visibility == Visibility.Collapsed ? 0.0f : rightPanel.Size,
                    () => menuBarHeight + viewportToolbarHeight + (topPanel.Visibility == Visibility.Collapsed ? 0.0f : topPanel.Size),
                    () => StatusBarHeight + (bottomPanel.Visibility == Visibility.Collapsed ? 0.0f : bottomPanel.Size),
                    camera,
                    editor)
                    .Anchor(left: 0.0f, top: 0.0f, right: 0.0f, bottom: 0.0f),

                dockArea,

                selectionWatcher,

                new Border()
                    .Fill(theme.Frosted(theme.Surface))
                    .Blur(theme.BackdropBlur)
                    .Outline(theme.Border, 1.0f)
                    .Height(menuBarHeight)
                    .Content(new Grid().Columns("*, Auto, Auto").Rows("Auto").Children(
                        BuildMenuBar(vm, state, Choose, ResetPanels, debug, root, ChooseTheme, ChooseFont, ChooseBlur, TogglePanel, IsPanelOpen, OpenPanel, editor, world)
                            .GridColumn(0),
                        (world == null ? new Border() : SkyClockManaView.Build(theme, world.IrradianceSettings))
                            .Margin(0.0f, 0.0f, 12.0f, 0.0f).CenterV().GridColumn(1),
                        BuildClock(theme).Margin(0.0f, 0.0f, 12.0f, 0.0f).GridColumn(2)))
                    .Anchor(top: 0.0f, left: 0.0f, right: 0.0f),

                new Border()
                    .Fill(theme.Frosted(theme.Surface))
                    .Blur(theme.BackdropBlur)
                    .Outline(theme.Border, 1.0f)
                    .Height(StatusBarHeight)
                    .PaddingOf(8.0f, 2.0f)
                    .Content(new Grid().Columns("Auto, *, Auto, Auto", theme.Spacing * 2.0f).Rows("Auto").Children(
                        BuildVersion(theme, root).GridColumn(0),
                        BuildStatusMessage(theme, editor).GridColumn(1),
                        BuildSelectionSummary(theme, world).GridColumn(2),
                        ManaUiPerformanceStrip.Build(theme).GridColumn(3).CenterV()))
                    .Anchor(left: 0.0f, right: 0.0f, bottom: 0.0f));
        }

        /// <summary>The build, bottom left; the About box has the rest.</summary>
        private static UiElement BuildVersion(UiTheme theme, UiRoot root)
        {
            return new TextBlock()
                .Text(ManaCore.Diagnostics.ManaVersion.Display)
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextMuted)
                .CenterV()
                // The GPU too: the first thing to know when a machine runs slowly. Polled, as it can be found after the bar is built.
                .BindPolled(text => text.Tooltip, VersionTooltip);
        }

        private static string VersionTooltip()
        {
            var gpu = ManaCore.Diagnostics.ManaGpuInfo.Description;
            if (gpu.Length == 0)
            {
                return "This build. View > About for more.";
            }
            return $"This build, drawing with {gpu}. View > About for more.";
        }

        /// <summary>
        /// What just happened -- saved, opened, failed. This used to be a fixed 210px field wedged into the
        /// toolbar between Play and the voxel tools; it is a status message, which is what a status bar is,
        /// and the left half of the bar was empty anyway.
        /// </summary>
        private static UiElement BuildStatusMessage(UiTheme theme, VoxelEditorViewModel? editor)
        {
            var text = new TextBlock()
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextMuted)
                .Ellipsis()
                .CenterV();

            if (editor != null)
            {
                text.BindTextPolled(() => editor.Levels.Status);
            }

            return text;
        }

        /// <summary>
        /// What is selected, in the one place that is always visible regardless of which panel is on top.
        /// The inspector says the same thing in more detail, but only while its tab is the one showing --
        /// and in the default layout it shares a tab strip with eight other panels.
        /// </summary>
        private static UiElement BuildSelectionSummary(UiTheme theme, global::Mana.ManaWorld? world)
        {
            var text = new TextBlock()
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextMuted)
                .Ellipsis()
                .CenterV()
                .Configure(block => block.MaxWidth = 320.0f)
                .WithTooltip("What is selected. Click something in the viewport or the Outliner.");

            if (world != null)
            {
                text.BindTextPolled(() => SelectionText(world));
            }

            return text;
        }

        private static string SelectionText(global::Mana.ManaWorld world)
        {
            var selection = world.Selection;
            var node = selection.SelectedNode;
            if (node != null)
            {
                var component = selection.SelectedComponent;
                return component == null ? node.Name : $"{node.Name}  /  {component.GetType().Name}";
            }

            return selection.SelectedScene is { } scene ? scene.Name : "Nothing selected";
        }

        // ----- menu ---------------------------------------------------------------------------------

        /// <summary>The editor's real menu bar, in ManaUI.</summary>
        private MenuBar BuildMenuBar(
            ManaUiShowcaseViewModel vm,
            EditorWorkspaceLayout.State state,
            Action<WorkspacePreset> choose,
            Action resetPanels,
            UiDebugOverlay debug,
            UiRoot root,
            Action<UiThemeKind> chooseTheme,
            Action<string> chooseFont,
            Action<float> chooseBlur,
            Action<string> togglePanel,
            Func<string, bool> isPanelOpen,
            Action<string> openPanel,
            VoxelEditorViewModel? editor,
            global::Mana.ManaWorld? world)
        {
            var bar = new MenuBar();

            // Editor menus first, so the bar reads the way the ImGui one did. Owned by the editor, not this chrome.
            if (editor != null)
            {
                foreach (var menu in VoxelEditorMainMenuManaView.BuildMenus(editor, null, () => openPanel("preferences"), () => PlaceSearchManaView.Show(root, editor, PlaceSearch),
                    () => AboutBoxManaView.Show(root, () => openPanel("about"))))
                {
                    bar.Add(menu);
                }
            }

            return bar.AddRange(
                BuildDebugMenu(editor, debug),

                new MenuItem("Layout").WithItems(
                    PresetItem("Right column", WorkspacePreset.RightColumn, state, choose),
                    PresetItem("Unity", WorkspacePreset.Unity, state, choose),
                    PresetItem("Unreal", WorkspacePreset.Unreal, state, choose),
                    PresetItem("Split", WorkspacePreset.Split, state, choose),
                    MenuItem.Separator(),
                    new MenuItem("Reset panels", resetPanels)),

                new MenuItem("Panels").WithItems(BuildPanelsMenu(root, isPanelOpen, togglePanel, openPanel)),

                // "Appearance" rather than "View": the editor's own View menu already sits in this bar.
                new MenuItem("Appearance").WithItems(
                    new MenuItem("Theme").WithItems(
                        new MenuItem("Dark", () => chooseTheme(UiThemeKind.Dark))
                            .Checkable(() => root.Theme.Kind == UiThemeKind.Dark),
                        new MenuItem("Light", () => chooseTheme(UiThemeKind.Light))
                            .Checkable(() => root.Theme.Kind == UiThemeKind.Light),
                        new MenuItem("LCARS", () => chooseTheme(UiThemeKind.Lcars))
                            .Checkable(() => root.Theme.Kind == UiThemeKind.Lcars),
                        new MenuItem("Kenney", () => chooseTheme(UiThemeKind.Kenney))
                            .Checkable(() => root.Theme.Kind == UiThemeKind.Kenney),
                        new MenuItem("Windows 3.1", () => chooseTheme(UiThemeKind.Windows31))
                            .Checkable(() => root.Theme.Kind == UiThemeKind.Windows31),
                        new MenuItem("Windows 11", () => chooseTheme(UiThemeKind.Windows11))
                            .Checkable(() => root.Theme.Kind == UiThemeKind.Windows11),
                        new MenuItem("macOS", () => chooseTheme(UiThemeKind.MacOS))
                            .Checkable(() => root.Theme.Kind == UiThemeKind.MacOS)),

                    new MenuItem("Backdrop blur").WithItems(
                        BlurChoices.Select(blur => new MenuItem(BlurLabel(blur), () => chooseBlur(blur))
                            .Checkable(() => MathF.Abs(root.Theme.BackdropBlur - blur) < 0.01f))
                            .ToArray()),

                    new MenuItem("Font").WithItems(
                        root.Fonts.AvailableFamilies(FontChoices)
                            .Select(family => new MenuItem(FontLabel(family), () => chooseFont(family))
                                .Checkable(() => root.Theme.FontFamily == family))
                            .ToArray()),
                    MenuItem.Separator(),
                    // vm.ShowWireframe is the standalone ManaUiShowcase page's own dummy field (nothing
                    // renders off it) -- with a live editor attached this has to flip the real one instead
                    // (VoxelEditorGameMode.SyncWireframeViewOption reads editor.ShowWireframe), or toggling
                    // this exact menu item silently did nothing in the one place it actually mattered.
                    new MenuItem("Wireframe", () =>
                        {
                            if (editor != null)
                            {
                                editor.ShowWireframe = !editor.ShowWireframe;
                            }
                            else
                            {
                                vm.ShowWireframe = !vm.ShowWireframe;
                            }
                        })
                        .Checkable(() => editor?.ShowWireframe ?? vm.ShowWireframe),
                    new MenuItem("Sky").WithItems(
                        new[]
                        {
                            (ManaCore.Graphics.ManaSkyMode.Day, "Day"),
                            (ManaCore.Graphics.ManaSkyMode.DayNightCycle, "Day and Night"),
                            (ManaCore.Graphics.ManaSkyMode.Space, "Space"),
                            (ManaCore.Graphics.ManaSkyMode.Photo, "Photo")
                        }.Select(choice => new MenuItem(choice.Item2, () =>
                            {
                                if (world != null)
                                {
                                    world.IrradianceSettings.Sky = choice.Item1;
                                }
                            })
                            .Checkable(() => world?.IrradianceSettings.Sky == choice.Item1)
                            .EnabledWhen(() => world != null)).Append(MenuItem.Separator()).Append(
                            new MenuItem("Constellation lines", () =>
                                {
                                    if (world != null)
                                    {
                                        world.IrradianceSettings.ConstellationLines = !world.IrradianceSettings.ConstellationLines;
                                    }
                                })
                                .Checkable(() => world?.IrradianceSettings.ConstellationLines == true)
                                .EnabledWhen(() => world != null)).ToArray()),
                    MenuItem.Separator(),
                    new MenuItem("Tool").WithItems(
                        vm.Tools.Select((tool, index) => new MenuItem(tool, () => vm.SelectedToolIndex = index)
                            .Checkable(() => vm.SelectedToolIndex == index)).ToArray())));
        }

        private static MenuItem[] BuildPanelsMenu(UiRoot root, Func<string, bool> isPanelOpen, Action<string> togglePanel, Action<string> openPanel)
        {
            var available = EditorPanelRegistry.Panels.Where(panel => panel.MenuGroup != null).Select(panel => panel.Id).ToList();
            string TitleOf(string id) => PanelTitles.TryGetValue(id, out var title) ? title : id;

            var items = new List<MenuItem>
            {
                new MenuItem("Find Panel…", () => PanelSearchManaView.Show(root, available.Select(id => (id, TitleOf(id))).ToList(), openPanel))
            };

            void AddGroup(string heading, IEnumerable<string> ids)
            {
                var members = ids.Where(available.Contains).ToList();
                if (members.Count == 0)
                {
                    return;
                }

                items.Add(MenuItem.Separator());
                items.Add(MenuItem.Label(heading));
                foreach (var id in members)
                {
                    items.Add(new MenuItem(TitleOf(id), () => togglePanel(id)).Checkable(() => isPanelOpen(id)));
                }
            }

            foreach (var group in EditorPanelRegistry.Panels.Where(panel => panel.MenuGroup != null)
                .GroupBy(panel => panel.MenuGroup)
                .OrderBy(group => group.Key switch { "Editor" => 0, "Assets" => 1, "Debugging" => 2, "Settings" => 3, _ => 4 }))
            {
                AddGroup(group.Key ?? "Other", group.OrderBy(panel => panel.MenuOrder).Select(panel => panel.Id));
            }
            return items.ToArray();
        }

        /// <summary>The room's six faces (and, in LCARS, its four wall-to-wall coves) VoxelEditorGameMode names when it builds the scene.</summary>
        private static readonly string[] GridPlaneNames =
        {
            "Grid Plane XZ", "Grid Plane Ceiling",
            "Grid Plane YX", "Grid Plane ZY", "Grid Plane YX Far", "Grid Plane ZY Far",
            "Grid Cove X0-Z0", "Grid Cove X0-ZFar", "Grid Cove XFar-ZFar", "Grid Cove XFar-Z0"
        };

        /// <summary>
        /// Pushes the theme's viewport grid colours onto the live grid quads' materials.
        /// <para>
        /// Looked up by name rather than through a GameMode reference -- the grid quads are plain scene
        /// nodes VoxelEditorGameMode creates once at startup, and nothing in that engine layer needs to
        /// know a UI theme exists. This runs once at startup and again on every theme switch, so both the
        /// initial paint and a live switch land on the same nodes.
        /// </para>
        /// </summary>
        private static void ApplyGridTheme(global::Mana.ManaWorld? world, UiTheme theme)
        {
            if (world == null)
            {
                return;
            }

            // LCARS is a starship's bridge: its sky is space. Every other theme gets a day.
            world.IrradianceSettings.Sky = theme.Kind == UiThemeKind.Lcars
                ? ManaCore.Graphics.ManaSkyMode.Space
                : ManaCore.Graphics.ManaSkyMode.Day;

            lock (world.SceneLock)
            {
                foreach (var scene in world.Scenes)
                {
                    foreach (var node in scene.Children)
                    {
                        if (Array.IndexOf(GridPlaneNames, node.Name) < 0)
                        {
                            continue;
                        }

                        foreach (var renderer in node.GetComponents<global::Mana.MeshRendererComponent>())
                        {
                            renderer.Material.Albedo.Value = theme.GridFloor;
                            renderer.Material.Emission.Value = theme.GridLine;
                            renderer.Material.EmissionStrength = theme.GridLineThickness;
                            renderer.Material.Metallic.Value = theme.GridCheckerRatio;
                        }
                    }
                }
            }
        }

        /// <summary>Powers of two: the blur is a mip chain, so anything between two levels is interpolated.</summary>
        public static readonly float[] BlurChoices = { 0.0f, 4.0f, 8.0f, 16.0f, 32.0f };

        private static string BlurLabel(float blur) => blur <= 0.0f ? "Off" : $"{blur:0} px";

        /// <summary>Fonts offered in the Font menu, filtered at build time to whichever are actually installed.</summary>
        private static readonly string[] FontChoices =
        {
            "Roboto",
            "KenneyFutureNarrow",
            "KenneyFuture",
            "BarlowCondensed-Regular",
            "segoeui",
            "SegoeUI",
            "bahnschrift",
            "calibri",
            "verdana",
            "tahoma",
            "trebuc",
            "consola",
            "CascadiaMono",
            "Helvetica",
            "SFNSDisplay",
            "DejaVuSans"
        };

        /// <summary>Turns a file stem into something worth reading in a menu.</summary>
        private static string FontLabel(string family)
        {
            return family switch
            {
                "segoeui" or "SegoeUI" => "Segoe UI",
                "bahnschrift" => "Bahnschrift",
                "calibri" => "Calibri",
                "verdana" => "Verdana",
                "tahoma" => "Tahoma",
                "trebuc" => "Trebuchet MS",
                "consola" => "Consolas",
                "BarlowCondensed-Regular" => "Barlow Condensed",
                "KenneyFuture" => "Kenney Future",
                "KenneyFutureNarrow" => "Kenney Future Narrow",
                "SFNSDisplay" => "SF Pro",
                "DejaVuSans" => "DejaVu Sans",
                _ => family
            };
        }

        /// <summary>One toggle per debug flag, so they can be combined rather than being a single mode.</summary>
        private static MenuItem DebugItem(string label, UiDebugFlags flag, UiDebugOverlay debug)
        {
            return new MenuItem(label, () => debug.Flags ^= flag)
                .Checkable(() => (debug.Flags & flag) != 0);
        }

        private static MenuItem BuildDebugMenu(VoxelEditorViewModel? editor, UiDebugOverlay debug)
        {
            var menu = new MenuItem("Debug");

            if (editor != null)
            {
                menu.Items.Add(new MenuItem("Scene").WithItems(
                    Toggle("Selected collider bounds",
                        () => editor.ShowSelectedColliderBounds,
                        value => editor.ShowSelectedColliderBounds = value),
                    Toggle("Track visual bounds",
                        () => editor.ShowTrackBounds,
                        value => editor.ShowTrackBounds = value),
                    Toggle("Track pivots",
                        () => editor.ShowTrackPivots,
                        value => editor.ShowTrackPivots = value),
                    Toggle("Track snap points",
                        () => editor.ShowTrackSnapPoints,
                        value => editor.ShowTrackSnapPoints = value),
                    MenuItem.Separator(),
                    new MenuItem("All scene debug off", () =>
                    {
                        editor.ShowSelectedColliderBounds = false;
                        editor.ShowTrackBounds = false;
                        editor.ShowTrackPivots = false;
                        editor.ShowTrackSnapPoints = false;
                    })));
                menu.Items.Add(MenuItem.Separator());
            }

            menu.Items.Add(new MenuItem("UI").WithItems(
                DebugItem("Element bounds", UiDebugFlags.ElementBounds, debug),
                DebugItem("Clip rects", UiDebugFlags.ClipRects, debug),
                DebugItem("Hover path", UiDebugFlags.HoverPath, debug),
                DebugItem("Stats", UiDebugFlags.Stats, debug)));
            menu.Items.Add(MenuItem.Separator());
            menu.Items.Add(new MenuItem("All UI debug on", () => debug.Flags = UiDebugFlags.All));
            menu.Items.Add(new MenuItem("All debug off", () =>
            {
                debug.Flags = UiDebugFlags.None;
                if (editor != null)
                {
                    editor.ShowSelectedColliderBounds = false;
                    editor.ShowTrackBounds = false;
                    editor.ShowTrackPivots = false;
                    editor.ShowTrackSnapPoints = false;
                }
            }));
            return menu;
        }

        private static MenuItem Toggle(string label, Func<bool> read, Action<bool> write)
        {
            return new MenuItem(label, () => write(!read())).Checkable(read);
        }

        private static MenuItem PresetItem(
            string label,
            WorkspacePreset preset,
            EditorWorkspaceLayout.State state,
            Action<WorkspacePreset> choose)
        {
            return new MenuItem(label, () => choose(preset))
                .Checkable(() => state.Preset == preset);
        }

        /// <summary>The one thing worth glancing at on a menu bar that otherwise never changes.</summary>
        private static UiElement BuildClock(UiTheme theme)
        {
            var clock = new TextBlock()
                .FontFamily(theme.FontFamilyMono)
                .Foreground(theme.TextMuted)
                .CenterV();

            clock.BindTextPolled(() => DateTime.Now.ToString("h:mm:ss tt"));
            return clock;
        }
    }
}

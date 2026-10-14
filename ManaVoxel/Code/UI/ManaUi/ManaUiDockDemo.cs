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
    public static class ManaUiDockDemo
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

        /// <summary>
        /// Bump when a panel is added, removed or renamed; missing panels get merged in on load. Also bump
        /// when a strip's content changes shape enough that a saved size for it is stale -- 18 is the compact
        /// toolbar, whose old saved height was more than twice what its content now measures, and 21 is the
        /// default panel set shrinking (see OpensByDefault). 23 adds the Grid Map tool panel; 25 adds Scenes.
        /// </summary>
        private const int LayoutVersion = 25;

        private static string DefaultLayoutPath =>
            ManaUiSettingsStorage.SharedSettingsFilePath("manaui-dock.json");

        /// <summary>What each panel is called, for the Panels menu. Kept separate from the factory so drawing the menu doesn't require building every panel.</summary>
        public static readonly Dictionary<string, string> PanelTitles = new(StringComparer.Ordinal)
        {
            ["level"] = "Level",
            ["tools"] = "Toolbar",
            ["import"] = "Import",
            ["project"] = "Project",
            ["scenes"] = "Scenes",
            ["blurtest"] = "Blur Test",
            ["outliner"] = "Outliner",
            ["inspector"] = "Inspector",
            // Named for its tab, though it never appears in the Panels menu: it follows the selection.
            [GridMapToolManaView.PanelId] = "Grid Map",
            [MeasureToolManaView.PanelId] = "Measure",
            [VoxelToolsPanelId] = "Voxel Tools",
            [GridMapWfcManaView.PanelId] = "Generator",
            [SpriteAssetManaView.PanelId] = "Sprite Asset",
            [PrefabChangesManaView.PanelId] = "Review Changes",
            [UiDesignerManaView.PanelId] = "UI Designer",
            ["livescan"] = "Live Scan",
            ["showcase"] = "ManaUI",
            ["about"] = "Graphics Diagnostics",
            ["worldperf"] = "World Perf",
            ["debug"] = "Debug",
            ["preferences"] = "Preferences"
        };

        private static readonly System.Collections.Concurrent.ConcurrentQueue<string> PanelRequests = new();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UiRoot, EventHandler<UiTheme>> ThemeHandlers = new();

        /// <summary>Brings a panel up on the next frame, from code with no hold on the workspace -- a command file.</summary>
        public static void RequestPanel(string id) => PanelRequests.Enqueue(id);

        /// <summary>Test seam: the most recently built workspace's drag controller.</summary>
        public static DockDragController? LastDragController { get; private set; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UiRoot, DockDragController> Controllers = new();

        /// <summary>Shared, not per-panel, so a theme rebuild doesn't throw the scanned list away.</summary>
        public static MeshAssetLibrary MeshAssets { get; } = new();

        /// <summary>Shared for the same reason -- also keeps results across a theme rebuild.</summary>
        public static PlaceSearchModel PlaceSearch { get; } = new();

        /// <summary>
        /// Set by the host once it has a graphics context, since generating a preview means importing a mesh.
        /// Null in a headless workspace (and in the tests), where the grid simply shows format badges -- the
        /// panel has to work without previews, because previews are the part that can be unavailable.
        /// </summary>
        public static MeshThumbnailCache? MeshThumbnails { get; set; }

        /// <summary>Every panel this workspace can build, in merge order. Must agree with the factory below, or newly added panels silently never merge in.</summary>
        public static readonly string[] PanelIds =
        {
            "level",
            "tools",
            "import",
            "project",
            "scenes",
            "blurtest",
            "outliner",
            "inspector",
            GridMapToolManaView.PanelId,
            MeasureToolManaView.PanelId,
            VoxelToolsPanelId,
            GridMapWfcManaView.PanelId,
            SpriteAssetManaView.PanelId,
            PrefabChangesManaView.PanelId,
            UiDesignerManaView.PanelId,
            "livescan",
            "showcase",
            "about",
            "worldperf",
            "debug",
            "preferences"
        };

        /// <summary>Builds the workspace over a <see cref="UiRoot"/> so it can be exercised headlessly. Panels needing <paramref name="editor"/>/<paramref name="world"/> just don't appear without them.</summary>
        public static UiElement Attach(
            UiRoot root,
            ManaUiShowcaseViewModel vm,
            Func<string>? liveStat = null,
            bool persist = true,
            VoxelEditorViewModel? editor = null,
            global::Mana.ManaWorld? world = null,
            string? layoutPath = null)
        {
            // Overridable so persistence can be exercised against a scratch file rather than the user's real one.
            layoutPath ??= DefaultLayoutPath;
            if (ThemeHandlers.TryGetValue(root, out var oldThemeHandler))
            {
                root.ThemeChanged -= oldThemeHandler;
                ThemeHandlers.Remove(root);
            }

            // Applied before anything is built: views read theme colours as they construct, not at render time.
            var saved = persist ? ManaUiWorkspaceLayout.Load(layoutPath) : null;
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

            // The density preset tightens the same tokens a theme sets, and controls read those as they
            // are constructed -- so changing it rebuilds the tree, exactly as changing theme does. The
            // previous subscription is dropped first: Attach runs once per app, but the tests call it many
            // times, and a stale handler holds a dead root that would rebuild into nothing.
            ManaUiDensity.PresetChanged -= _densityChanged;
            _densityChanged = (_, _) =>
            {
                root.Theme = UiTheme.Create(root.Theme.Kind);
            };
            ManaUiDensity.PresetChanged += _densityChanged;

            shell.Child = Build(root, shell, vm, liveStat, persist, editor, world, layoutPath);
            return shell;
        }

        /// <summary>The Voxel Tools window, which floats while a voxel node is selected or a voxel tool is on.</summary>
        public const string VoxelToolsPanelId = "voxeltools";

        // Where each tool window was last left, for the next time it comes up. Per run: a window that opens by
        // itself should not come back somewhere a screen change has made odd.
        private static readonly Dictionary<string, (Vector2? Position, Vector2 Size)> ToolWindowPlaces = new()
        {
            [GridMapToolManaView.PanelId] = (null, new Vector2(300.0f, 520.0f)),
            [MeasureToolManaView.PanelId] = (null, new Vector2(300.0f, 460.0f)),
            [VoxelToolsPanelId] = (null, new Vector2(300.0f, 460.0f)),
            [GridMapWfcManaView.PanelId] = (null, new Vector2(340.0f, 600.0f)),
            [SpriteAssetManaView.PanelId] = (null, new Vector2(400.0f, 640.0f)),
            [PrefabChangesManaView.PanelId] = (null, new Vector2(480.0f, 320.0f)),
            [UiDesignerManaView.PanelId] = (null, new Vector2(1120.0f, 640.0f)),
            // Floats over the viewport like the tool windows, so the scan it receives stays in view.
            ["livescan"] = (null, new Vector2(340.0f, 460.0f))
        };

        /// <summary>The live density subscription, so re-attaching replaces it rather than stacking a second one.</summary>
        private static EventHandler? _densityChanged;

        private static UiElement Build(
            UiRoot root,
            ContentHost shell,
            ManaUiShowcaseViewModel vm,
            Func<string>? liveStat,
            bool persist,
            VoxelEditorViewModel? editor,
            global::Mana.ManaWorld? world,
            string layoutPath)
        {
            if (Controllers.TryGetValue(root, out var previousController)) previousController.CloseDesktopWindows();
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

            DockItem? BuildPanel(string id) => id switch
            {
                "showcase" => new DockItem("ManaUI", ManaUiShowcaseView.BuildContent(theme, vm, liveStat).Scrollable().Configure(viewer => viewer.Padding = new Thickness(0.0f, 4.0f, 0.0f, 4.0f)))
                {
                    Id = "showcase"
                },
                // Build() already wraps itself in a horizontal ScrollViewer -- this is a toolbar strip now,
                // not a side column, so it scrolls sideways rather than the usual vertical wrap.
                "level" when editor != null => new DockItem(
                    "Level",
                    LevelManaView.Build(theme, editor.Levels).Scrollable().Configure(viewer => viewer.Padding = new Thickness(0.0f, 4.0f, 0.0f, 4.0f))) { Id = "level" },

                "tools" when editor != null => new DockItem(
                    "Toolbar",
                    VoxelEditorToolbarManaView.Build(theme, editor, world)) { Id = "tools" },

                "import" when editor != null => new DockItem(
                    "Import",
                    MeshImportManaView.Build(theme, editor, MeshAssets, MeshThumbnails)) { Id = "import" },
                "project" when editor != null && world != null => new DockItem(
                    "Project", ProjectManaView.Build(theme, world, editor, () => projectFolderWidth())) { Id = "project" },
                "scenes" when editor != null => new DockItem(
                    "Scenes", ScenesManaView.Build(theme, editor.Levels)) { Id = "scenes" },
                "blurtest" => new DockItem("Blur Test", BackdropTestManaView.Build(theme).Scrollable().Configure(viewer => viewer.Padding = new Thickness(0.0f, 4.0f, 0.0f, 4.0f)))
                {
                    Id = "blurtest"
                },

                // Null until the backend has reported its start-up snapshot; the panel says so rather than showing an empty frame.
                "about" => new DockItem("Graphics Diagnostics", new DynamicContent()
                    .Showing<ManaGraphicsDiagnosticsSnapshot>(
                        () => ManaUiDiagnostics.Snapshot,
                        captured => AboutManaView.Build(theme, captured, ManaUiDiagnostics.BackendName))
                    .Empty(AboutManaView.Build(theme, null, ManaUiDiagnostics.BackendName)))
                {
                    Id = "about"
                },
                // world.Selection is the same instance the ImGui outliner and viewport use, so they can't drift apart.
                "outliner" when world != null => new DockItem(
                    "Outliner",
                    SceneOutlinerManaView.Build(theme, world, world.Selection, saveScene, sceneHasChanges, editor?.History)) { Id = "outliner" },
                "inspector" when world != null => new DockItem(
                    "Inspector",
                    SceneInspectorManaView.Build(
                        theme,
                        world.Selection,
                        world.Context,
                        history: editor?.History)) { Id = "inspector" },
                MeasureToolManaView.PanelId when editor != null => new DockItem(
                    "Measure",
                    MeasureToolManaView.Build(theme, editor)) { Id = MeasureToolManaView.PanelId },
                GridMapToolManaView.PanelId when world != null => new DockItem(
                    "Grid Map",
                    GridMapToolManaView.Build(theme, world, editor?.History)) { Id = GridMapToolManaView.PanelId },
                UiDesignerManaView.PanelId => new DockItem("UI Designer", UiDesignerManaView.Build(theme)) { Id = UiDesignerManaView.PanelId },
#if !MANA_BROWSER
                "livescan" when world != null && editor != null => new DockItem("Live Scan", LiveScanManaView.Build(theme, world, editor)) { Id = "livescan" },
#endif
                PrefabChangesManaView.PanelId when world != null => new DockItem(
                    "Review Changes", PrefabChangesManaView.Build(theme, world)) { Id = PrefabChangesManaView.PanelId },
                SpriteAssetManaView.PanelId => new DockItem("Sprite Asset", SpriteAssetManaView.Build(theme)) { Id = SpriteAssetManaView.PanelId },
                GridMapWfcManaView.PanelId when world != null => new DockItem(
                    "Generator",
                    GridMapWfcManaView.Build(theme, world, editor?.History)) { Id = GridMapWfcManaView.PanelId },
                VoxelToolsPanelId when editor != null => new DockItem(
                    "Voxel Tools",
                    VoxelToolPaletteManaView.BuildWindow(theme, editor)) { Id = VoxelToolsPanelId },
                "worldperf" when world != null => new DockItem(
                    "World Perf",
                    WorldPerfManaView.Build(theme, world).Scrollable().Configure(viewer => viewer.Padding = new Thickness(0.0f, 4.0f, 0.0f, 4.0f))) { Id = "worldperf" },
                "debug" when editor != null => new DockItem(
                    "Debug",
                    DebugManaView.Build(theme, editor)) { Id = "debug" },
                "preferences" when editor != null => new DockItem(
                    "Preferences",
                    PreferencesManaView.Build(theme, editor)) { Id = "preferences" },
                _ => null
            };

            var saved = persist ? ManaUiWorkspaceLayout.Load(layoutPath) : null;
            var state = new WorkspaceState
            {
                Preset = saved?.Preset ?? WorkspacePreset.RightColumn,
                Theme = root.Theme.Kind,
                BackdropBlur = root.Theme.BackdropBlur
            };

            // One controller over every host, so a panel torn out of any of them can be dropped into any other.
            var dragController = new DockDragController(root, new[] { top, left, right, bottom });
            Controllers.Remove(root);
            Controllers.Add(root, dragController);
            LastDragController = dragController;

            // Before the columns are filled, so a floating panel doesn't also get placed in a column.
            RestoreFloating(root, dragController, saved, Resolve);

            HashSet<string> FloatingIds() => dragController.FloatingItems
                .Select(item => item.Id ?? string.Empty)
                .ToHashSet(StringComparer.Ordinal);

            // Non-short-circuiting: every host must be restored, or && would skip later ones once one came back empty.
            var restored = saved != null
                && (DockLayout.Apply(top, saved.Top, Resolve)
                    | DockLayout.Apply(left, saved.Left, Resolve)
                    | DockLayout.Apply(right, saved.Right, Resolve)
                    | DockLayout.Apply(bottom, saved.Bottom, Resolve));

            if (!restored)
            {
                ApplyPreset(state.Preset, top, left, right, bottom, Resolve, FloatingIds());
            }
            else if (saved!.Version != LayoutVersion)
            {
                // Merges any panel that didn't exist when this was saved, so upgrades don't lose new panels.
                MergeMissingPanels(top, left, right, bottom, Resolve, FloatingIds());

                // Voxel Tools and Import used to live in a column; move each to its own strip once, on upgrade.
                RelocateToStrip("tools", top, new[] { left, right, bottom }, Resolve, FloatingIds());
                RelocateToStrip("import", bottom, new[] { left, right, top }, Resolve, FloatingIds());
            }

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

            void Save()
            {
                if (!persist)
                {
                    return;
                }

                // Only a genuine override is worth remembering -- saving whatever font happens to be
                // active would just as happily capture the current theme's own default (e.g. LCARS's
                // Antonio-Bold), and Attach re-applies a saved FontFamily unconditionally on top of
                // whatever theme it loads. That is exactly how the "theme" font stopped working: it wasn't
                // the Font dropdown overriding it on purpose, it was a completely incidental font getting
                // baked in by the next unrelated Save() and then clobbering a differently-themed default
                // on the next launch or theme switch.
                var isDefaultFont = root.Theme.FontFamily == UiTheme.Create(state.Theme).FontFamily;

                new ManaUiWorkspaceLayout
                {
                    Version = LayoutVersion,
                    Preset = state.Preset,
                    Theme = state.Theme,
                    FontFamily = isDefaultFont ? null : root.Theme.FontFamily,
                    BackdropBlur = state.BackdropBlur,
                    LeftWidth = leftPanel.Size,
                    RightWidth = rightPanel.Size,
                    BottomHeight = bottomPanel.Size,
                    Top = DockLayout.Capture(top),
                    Left = DockLayout.Capture(left),
                    Right = DockLayout.Capture(right),
                    Bottom = DockLayout.Capture(bottom),
                    Floating = CaptureFloating(dragController)
                }.Save(layoutPath);
            }

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
            dragController.FloatingChanged += (_, _) => Save();

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

            if (ThemeHandlers.TryGetValue(root, out var previousThemeHandler))
            {
                root.ThemeChanged -= previousThemeHandler;
                ThemeHandlers.Remove(root);
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
            ThemeHandlers.Add(root, themeHandler);
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

                var docked = AllDocked(top, left, right, bottom).FirstOrDefault(item => item.Id == id);

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
                if (AllDocked(top, left, right, bottom).FirstOrDefault(item => item.Id == id) is not { } docked)
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
                AllDocked(top, left, right, bottom).Any(item => item.Id == id)
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

                var docked = AllDocked(top, left, right, bottom).FirstOrDefault(item => item.Id == id);
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
                ApplyPreset(preset, top, left, right, bottom, Resolve, FloatingIds());
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
                .WithTooltip("This build. View > About for more.");
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

        private static IEnumerable<DockItem> AllDocked(DockHost top, DockHost left, DockHost right, DockHost bottom)
        {
            return top.AllItems().Concat(left.AllItems()).Concat(right.AllItems()).Concat(bottom.AllItems());
        }

        /// <summary>Mutable bits the menu and the save hook both need to see.</summary>
        private sealed class WorkspaceState
        {
            public WorkspacePreset Preset { get; set; }

            /// <summary>Theme to persist -- not always what the root is showing, since a switch saves before it swaps.</summary>
            public UiThemeKind Theme { get; set; }

            public float BackdropBlur { get; set; }
        }

        // ----- presets ------------------------------------------------------------------------------

        /// <summary>Rebuilds every host from scratch. Only runs when a preset is chosen; edits afterward save over it.</summary>
        private static void ApplyPreset(
            WorkspacePreset preset,
            DockHost top,
            DockHost left,
            DockHost right,
            DockHost bottom,
            Func<string, DockItem?> resolve,
            HashSet<string>? alreadyOpen = null)
        {
            top.RootNode = null;
            left.RootNode = null;
            right.RootNode = null;
            bottom.RootNode = null;

            // Voxel Tools and Import get their own strips regardless of preset, so a preset only has to
            // arrange the two columns.
            Place(top, resolve, "tools", DockSide.Center);
            var import = Place(bottom, resolve, "import", DockSide.Center);
            Place(bottom, resolve, "project", DockSide.Center, import);
            Place(bottom, resolve, "scenes", DockSide.Center, import);

            switch (preset)
            {
                case WorkspacePreset.Unity:
                    BuildUnity(left, right, resolve);
                    break;
                case WorkspacePreset.Unreal:
                    BuildUnreal(left, right, resolve);
                    break;
                case WorkspacePreset.Split:
                    BuildSplit(left, right, resolve);
                    break;
                default:
                    BuildRightColumn(right, resolve);
                    break;
            }

            // Anything a preset didn't name (a newly added panel) still needs a home rather than vanishing.
            PlaceRemaining(top, left, right, bottom, resolve, alreadyOpen);
        }

        /// <summary>Docks whatever a preset didn't name, preferring the right column, same as an upgrade merge.</summary>
        private static void PlaceRemaining(
            DockHost top,
            DockHost left,
            DockHost right,
            DockHost bottom,
            Func<string, DockItem?> resolve,
            HashSet<string>? alreadyOpen = null)
        {
            var present = AllDocked(top, left, right, bottom)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);

            present.UnionWith(alreadyOpen ?? Enumerable.Empty<string>());

            foreach (var id in PanelIds)
            {
                if (present.Contains(id) || !OpensByDefault(id) || resolve(id) is not { } item)
                {
                    continue;
                }

                var host = right.RootNode != null ? right : left;
                host.Dock(item, DockSide.Center, DockHost.FindFirstGroup(host.RootNode));
            }
        }

        /// <summary>Everything tabbed in one column.</summary>
        private static void BuildRightColumn(DockHost right, Func<string, DockItem?> resolve)
        {
            var outliner = Place(right, resolve, "outliner", DockSide.Center);

            // Tabbed with the outliner: master and detail, and side by side in one column would leave
            // neither enough width for a transform row.
            Place(right, resolve, "inspector", DockSide.Bottom, outliner);
            Place(right, resolve, "showcase", DockSide.Center, outliner);
            Place(right, resolve, "about", DockSide.Center, outliner);
        }

        /// <summary>Hierarchy on the left and Inspector on the right, Unity's own arrangement.</summary>
        private static void BuildUnity(DockHost left, DockHost right, Func<string, DockItem?> resolve)
        {
            Place(left, resolve, "outliner", DockSide.Center);

            // The extras this editor has that Unity does not go where a custom add-on panel usually lands
            // in Unity -- tabbed alongside the Inspector, rather than crowding the Hierarchy.
            var inspector = Place(right, resolve, "inspector", DockSide.Center);
            Place(right, resolve, "showcase", DockSide.Center, inspector);
            Place(right, resolve, "about", DockSide.Center, inspector);
        }

        /// <summary>Outliner as a shallow top band, details filling below it -- roughly Unreal's proportions.</summary>
        private static void BuildUnreal(DockHost left, DockHost right, Func<string, DockItem?> resolve)
        {
            var placement = Place(left, resolve, "debug", DockSide.Center);
            Place(left, resolve, "about", DockSide.Center, placement);
            Place(left, resolve, "showcase", DockSide.Bottom);

            var outliner = Place(right, resolve, "outliner", DockSide.Center);
            Place(right, resolve, "inspector", DockSide.Bottom, outliner);
        }

        /// <summary>Outliner far left, inspector far right, viewport clear between them.</summary>
        private static void BuildSplit(DockHost left, DockHost right, Func<string, DockItem?> resolve)
        {
            var outliner = Place(left, resolve, "outliner", DockSide.Center);
            Place(left, resolve, "about", DockSide.Center, outliner);

            var inspector = Place(right, resolve, "inspector", DockSide.Center);
            Place(right, resolve, "showcase", DockSide.Center, inspector);
        }

        /// <summary>Docks a panel by id, or no-ops if unavailable this session. Returns the node it landed in.</summary>
        private static DockNode? Place(
            DockHost host,
            Func<string, DockItem?> resolve,
            string id,
            DockSide side,
            DockNode? target = null)
        {
            // Checked here rather than by editing each preset's list, so a panel can be taken out of the
            // default set in one place instead of four -- and so the two rules cannot drift apart.
            if (!OpensByDefault(id))
            {
                return null;
            }

            var item = resolve(id);
            if (item == null)
            {
                return null;
            }

            // Center with no target would land in the first group rather than beside the intended panel.
            if (target == null && host.RootNode != null && side == DockSide.Center)
            {
                side = DockSide.Bottom;
            }

            return host.Dock(item, side, target);
        }

        /// <summary>Docks any panel no host contains. Only runs on a version mismatch, so closed panels stay closed otherwise.</summary>
        private static void MergeMissingPanels(
            DockHost top,
            DockHost left,
            DockHost right,
            DockHost bottom,
            Func<string, DockItem?> resolve,
            HashSet<string>? alreadyOpen = null)
        {
            var present = AllDocked(top, left, right, bottom)
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);

            present.UnionWith(alreadyOpen ?? Enumerable.Empty<string>());

            foreach (var id in PanelIds)
            {
                if (present.Contains(id) || !OpensByDefault(id))
                {
                    continue;
                }

                var item = resolve(id);
                if (item == null)
                {
                    continue;
                }

                // Voxel Tools and Import go straight to their own strips; everything else prefers the
                // right column.
                if (id == "tools")
                {
                    top.Dock(item, DockSide.Center, DockHost.FindFirstGroup(top.RootNode));
                    continue;
                }

                if (id is "import" or "project" or "scenes")
                {
                    bottom.Dock(item, DockSide.Center, DockHost.FindFirstGroup(bottom.RootNode));
                    continue;
                }

                var host = right.RootNode != null ? right : left;
                host.Dock(item, DockSide.Center, DockHost.FindFirstGroup(host.RootNode));
            }
        }

        /// <summary>
        /// Whether a fresh layout puts this panel on screen.
        /// <para>
        /// Everything used to, which is how the right column arrived at eleven tabs in one strip -- four
        /// visible and seven behind an overflow chip. Five of those seven are not editor panels at all:
        /// ManaUI is the framework's own showcase, Blur Test is a shader toy, and About/World Perf/Debug
        /// are diagnostics. They are worth keeping and worth reaching from the Panels menu; they are not
        /// worth a tab each, every session, in front of the panels somebody is actually editing with.
        /// </para>
        /// <para>
        /// Only new and reset layouts are affected: a saved layout is restored as it was saved.
        /// </para>
        /// </summary>
        private static bool OpensByDefault(string id)
        {
            return id switch
            {
                "level" => false,
                "showcase" => false,
                "blurtest" => false,
                "about" => false,
                "worldperf" => false,
                "debug" => false,
                // Opened on demand from Edit > Preferences, not something to leave in front of the tools.
                "preferences" => false,
                // Opened by selecting a grid map, and closed again with it.
                GridMapToolManaView.PanelId => false,
                MeasureToolManaView.PanelId => false,
                VoxelToolsPanelId => false,
                GridMapWfcManaView.PanelId => false,
                SpriteAssetManaView.PanelId => false,
                PrefabChangesManaView.PanelId => false,
                UiDesignerManaView.PanelId => false,
                "livescan" => false,
                _ => true
            };
        }

        /// <summary>One-time migration: moves a panel that used to live in a column into its own dedicated strip.</summary>
        private static void RelocateToStrip(
            string id,
            DockHost home,
            IReadOnlyList<DockHost> others,
            Func<string, DockItem?> resolve,
            HashSet<string> floatingIds)
        {
            if (home.AllItems().Any(item => item.Id == id) || floatingIds.Contains(id))
            {
                return;
            }

            var existing = others.SelectMany(host => host.AllItems()).FirstOrDefault(item => item.Id == id);
            if (existing != null)
            {
                existing.Owner?.Remove(existing);
                home.Dock(existing, DockSide.Center, DockHost.FindFirstGroup(home.RootNode));
                return;
            }

            if (resolve(id) is { } item)
            {
                home.Dock(item, DockSide.Center, DockHost.FindFirstGroup(home.RootNode));
            }
        }

        private static List<FloatingPanelLayout> CaptureFloating(DockDragController controller)
        {
            var captured = new List<FloatingPanelLayout>();

            foreach (var window in controller.DesktopWindows)
            {
                var items = window.Frame.Dock.AllItems().Where(item => !string.IsNullOrEmpty(item.Id)).ToList();
                if (items.Count == 0)
                {
                    continue;
                }

                var grouped = items.Count > 1;
                captured.Add(new FloatingPanelLayout
                {
                    Id = items[0].Id!,
                    Desktop = true,
                    HomeHostIndex = controller.DesktopHomeIndex(window),
                    Layout = grouped ? DockLayout.Capture(window.Frame.Dock) : null,
                    ItemHomes = grouped ? items.ToDictionary(item => item.Id!, controller.HomeIndexOf) : null,
                    X = window.Position.X, Y = window.Position.Y,
                    Width = window.Size.X, Height = window.Size.Y
                });
            }

            foreach (var window in controller.FloatingWindows)
            {
                if (controller.ItemFor(window) is not { Id: { Length: > 0 } id })
                {
                    continue;
                }

                captured.Add(new FloatingPanelLayout
                {
                    Id = id,
                    X = window.Position.X,
                    Y = window.Position.Y,

                    // Arranged size, not requested: a resized window comes back the size it was left. One opened
                    // this frame has not been arranged yet, and keeps the size it asked for.
                    Width = window.Bounds.Width > 0.0f ? window.Bounds.Width : Requested(window.Width, FloatingFallback.X),
                    Height = window.Bounds.Height > 0.0f ? window.Bounds.Height : Requested(window.Height, FloatingFallback.Y)
                });
            }

            return captured;
        }

        private static float Requested(float size, float fallback) => float.IsFinite(size) && size > 0.0f ? size : fallback;

        /// <summary>Size a floating panel comes back at when the file has nothing usable recorded.</summary>
        private static readonly Vector2 FloatingFallback = new(320.0f, 360.0f);

        private static void RestoreFloating(
            UiRoot root,
            DockDragController controller,
            ManaUiWorkspaceLayout? saved,
            Func<string, DockItem?> resolve)
        {
            if (saved?.Floating is not { Count: > 0 } floating)
            {
                return;
            }

            foreach (var entry in floating)
            {
                if (entry.Desktop && controller.CanPopOut)
                {
                    try
                    {
                        var desktop = entry.Layout != null
                            ? controller.PopOutLayout(
                                entry.Layout,
                                resolve,
                                id => entry.ItemHomes?.GetValueOrDefault(id, entry.HomeHostIndex) ?? entry.HomeHostIndex)
                            : resolve(entry.Id) is { } single ? controller.PopOut(single, entry.HomeHostIndex) : null;

                        if (desktop != null)
                        {
                            desktop.Size = new Vector2(entry.Width, entry.Height);
                            desktop.Position = new Vector2(entry.X, entry.Y);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        ManaConsole.WriteDiagnostic($"Could not restore panel window: {ex.Message}");
                    }
                }

                // No native windows here (or one failed): each panel comes back as an in-app float instead.
                foreach (var id in entry.Layout != null ? LayoutIds(entry.Layout) : new List<string> { entry.Id })
                {
                    if (resolve(id) is { } item)
                    {
                        RestoreInAppFloat(root, controller, entry, item);
                    }
                }
            }
        }

        private static List<string> LayoutIds(DockLayoutNode node)
        {
            var ids = new List<string>(node.Items ?? new List<string>());
            foreach (var child in node.Children ?? new List<DockLayoutNode>())
            {
                ids.AddRange(LayoutIds(child));
            }

            return ids;
        }

        private static void RestoreInAppFloat(
            UiRoot root,
            DockDragController controller,
            FloatingPanelLayout entry,
            DockItem item)
        {
            var window = new UiWindow(item.Title)
            {
                Width = entry.Width > 0.0f ? entry.Width : FloatingFallback.X,
                Height = entry.Height > 0.0f ? entry.Height : FloatingFallback.Y,
                Child = item.Content
            };

            // Clamped onto the screen so a detached monitor or a shrunk window doesn't strand it.
            var position = new Vector2(
                Math.Clamp(entry.X, 0.0f, MathF.Max(0.0f, root.Size.X - 80.0f)),
                Math.Clamp(entry.Y, 0.0f, MathF.Max(0.0f, root.Size.Y - 40.0f)));

            root.ShowWindow(window, position);
            controller.RegisterFloating(window, item);
        }

        // ----- menu ---------------------------------------------------------------------------------

        /// <summary>The editor's real menu bar, in ManaUI.</summary>
        private static MenuBar BuildMenuBar(
            ManaUiShowcaseViewModel vm,
            WorkspaceState state,
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

        /// <summary>Panel ids grouped for the Panels menu: the ones used all day first, debugging last.</summary>
        private static readonly (string Heading, string[] Ids)[] PanelGroups =
        {
            ("Editor", new[] { "level", "outliner", "inspector", "tools" }),
            ("Assets", new[] { "import", "project", "scenes", UiDesignerManaView.PanelId }),
            ("Debugging", new[] { "worldperf", "debug", "about", "showcase", "blurtest" }),
            ("Settings", new[] { "preferences" })
        };

        private static MenuItem[] BuildPanelsMenu(UiRoot root, Func<string, bool> isPanelOpen, Action<string> togglePanel, Action<string> openPanel)
        {
            // The tool windows follow the selection; opening them by hand would only show nothing.
            var hidden = new HashSet<string>
            {
                GridMapToolManaView.PanelId, MeasureToolManaView.PanelId, VoxelToolsPanelId, GridMapWfcManaView.PanelId,
                SpriteAssetManaView.PanelId, PrefabChangesManaView.PanelId
            };
            var available = PanelIds.Where(id => !hidden.Contains(id)).ToList();
            string TitleOf(string id) => PanelTitles.TryGetValue(id, out var title) ? title : id;

            var items = new List<MenuItem>
            {
                new MenuItem("Find Panel…", () => PanelSearchManaView.Show(root, available.Select(id => (id, TitleOf(id))).ToList(), openPanel))
            };

            var placed = new HashSet<string>();
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
                    placed.Add(id);
                    items.Add(new MenuItem(TitleOf(id), () => togglePanel(id)).Checkable(() => isPanelOpen(id)));
                }
            }

            foreach (var (heading, ids) in PanelGroups)
            {
                AddGroup(heading, ids);
            }

            // A panel added later still shows up, under Other, until someone files it.
            AddGroup("Other", available.Where(id => !placed.Contains(id)));
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
            WorkspaceState state,
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

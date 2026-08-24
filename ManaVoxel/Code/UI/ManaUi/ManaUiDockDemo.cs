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
        // theme's font size, and every pixel here is one the viewport does not get.
        private const float MenuBarHeight = 28.0f;
        private const float StatusBarHeight = 24.0f;

        private const float DefaultLeftWidth = 300.0f;
        private const float DefaultRightWidth = 380.0f;
        /// <summary>
        /// The toolbar strip. Sized to what one row of controls plus its padding actually measures (see
        /// VoxelToolPaletteManaView), rather than to a round number with slack in it.
        /// </summary>
        private const float DefaultTopHeight = 38.0f;
        private const float DefaultBottomHeight = 220.0f;

        /// <summary>
        /// Bump when a panel is added, removed or renamed; missing panels get merged in on load. Also bump
        /// when a strip's content changes shape enough that a saved size for it is stale -- 18 is the compact
        /// toolbar, whose old saved height was more than twice what its content now measures.
        /// </summary>
        private const int LayoutVersion = 20;

        private static string DefaultLayoutPath =>
            ManaImGuiSettingsStorage.SharedSettingsFilePath("manaui-dock.json");

        /// <summary>What each panel is called, for the Panels menu. Kept separate from the factory so drawing the menu doesn't require building every panel.</summary>
        public static readonly Dictionary<string, string> PanelTitles = new(StringComparer.Ordinal)
        {
            ["level"] = "Level",
            ["tools"] = "Toolbar",
            ["import"] = "Import",
            ["search"] = "Search",
            ["blurtest"] = "Blur Test",
            ["placement"] = "Placement",
            ["physics"] = "Physics",
            ["voxelstats"] = "Voxel Stats",
            ["lighting"] = "Lighting",
            ["outliner"] = "Outliner",
            ["inspector"] = "Inspector",
            ["showcase"] = "ManaUI",
            ["about"] = "About",
            ["worldperf"] = "World Perf",
            ["debug"] = "Debug"
        };

        /// <summary>Test seam: the most recently built workspace's drag controller.</summary>
        public static DockDragController? LastDragController { get; private set; }

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
            "search",
            "blurtest",
            "placement",
            "physics",
            "voxelstats",
            "lighting",
            "outliner",
            "inspector",
            "showcase",
            "about",
            "worldperf",
            "debug"
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

            // Applied before anything is built: views read theme colours as they construct, not at render time.
            var saved = persist ? ManaUiWorkspaceLayout.Load(layoutPath) : null;
            if (saved != null && root.Theme.Kind != saved.Theme)
            {
                root.Theme = UiTheme.Create(saved.Theme);
            }

            if (saved?.FontFamily is { Length: > 0 } savedFont)
            {
                root.Theme.FontFamily = savedFont;
            }

            if (saved != null)
            {
                root.Theme.BackdropBlur = saved.BackdropBlur;
            }

            // Owned by the workspace so a theme switch can swap the whole thing out from under itself.
            var shell = new ContentHost();
            shell.Child = Build(root, shell, vm, liveStat, persist, editor, world, layoutPath);
            return shell;
        }

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
            var theme = root.Theme;
            var debug = root.Debug;
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

            // Persistence stores ids and asks for the panel back by name, since content is application-owned.
            DockItem? Resolve(string id) => id switch
            {
                "showcase" => new DockItem("ManaUI", ManaUiShowcaseView.BuildContent(theme, vm, liveStat).Scrollable())
                {
                    Id = "showcase"
                },
                // Build() already wraps itself in a horizontal ScrollViewer -- this is a toolbar strip now,
                // not a side column, so it scrolls sideways rather than the usual vertical wrap.
                "level" when editor != null => new DockItem(
                    "Level",
                    LevelManaView.Build(theme, editor.Levels).Scrollable()) { Id = "level" },

                "tools" when editor != null => new DockItem(
                    "Toolbar",
                    VoxelEditorToolbarManaView.Build(theme, editor, world)) { Id = "tools" },

                "import" when editor != null => new DockItem(
                    "Import",
                    MeshImportManaView.Build(theme, editor, MeshAssets, MeshThumbnails)) { Id = "import" },
                "search" when editor != null => new DockItem(
                    "Search",
                    PlaceSearchManaView.Build(theme, editor, PlaceSearch)) { Id = "search" },
                "blurtest" => new DockItem("Blur Test", BackdropTestManaView.Build(theme).Scrollable())
                {
                    Id = "blurtest"
                },
                "placement" when editor != null => new DockItem(
                    "Placement",
                    VoxelEditorPanelsManaView.BuildPlacement(theme, editor)) { Id = "placement" },
                "physics" when editor != null => new DockItem(
                    "Physics",
                    VoxelEditorPanelsManaView.BuildPhysics(theme, editor)) { Id = "physics" },
                "voxelstats" when editor != null => new DockItem(
                    "Voxel Stats",
                    VoxelEditorPanelsManaView.BuildStats(theme, editor)) { Id = "voxelstats" },
                "lighting" when world != null => new DockItem(
                    "Lighting",
                    IrradianceSettingsManaView.Build(theme, world.IrradianceSettings)) { Id = "lighting" },

                // Null until the backend has reported its start-up snapshot; the panel says so rather than showing an empty frame.
                "about" => new DockItem("About", new DynamicContent()
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
                    SceneOutlinerManaView.Build(theme, world, world.Selection, saveScene, sceneHasChanges)) { Id = "outliner" },
                "inspector" when world != null => new DockItem(
                    "Inspector",
                    SceneInspectorManaView.Build(
                        theme,
                        world.Selection,
                        world.Context,
                        history: editor?.History)) { Id = "inspector" },
                "worldperf" when world != null => new DockItem(
                    "World Perf",
                    WorldPerfManaView.Build(theme, world).Scrollable()) { Id = "worldperf" },
                "debug" when editor != null => new DockItem(
                    "Debug",
                    DebugManaView.Build(theme, editor).Scrollable()) { Id = "debug" },
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

            // MinimumSize before Size in each of these: Size's setter clamps against MinimumSize immediately,
            // and an object initializer runs in order -- reversed, it clamps against the class's stale
            // default (180) instead of the value below it, which is how the toolbar's 56px became 180px.
            var topPanel = new ResizablePanel
            {
                Edge = Dock.Top,
                MinimumSize = 30.0f,
                // Deliberately NOT restored from an older layout version. The toolbar's height is a
                // function of its content, so a size saved before the content changed is stale by
                // definition -- and because the toolbar stretches to fill its strip, nothing would ever
                // shrink it back. Every other strip is genuinely the user's choice and is restored as-is.
                Size = saved is { TopHeight: > 0.0f } && saved.Version == LayoutVersion
                    ? saved.TopHeight
                    : DefaultTopHeight,
                Background = theme.Frosted(theme.Background),
                BackdropBlur = theme.BackdropBlur,
                Child = top
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

            void Save()
            {
                if (!persist)
                {
                    return;
                }

                new ManaUiWorkspaceLayout
                {
                    Version = LayoutVersion,
                    Preset = state.Preset,
                    Theme = state.Theme,
                    FontFamily = root.Theme.FontFamily,
                    BackdropBlur = state.BackdropBlur,
                    TopHeight = topPanel.Size,
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

            topPanel.SizeCommitted += (_, _) => Save();
            leftPanel.SizeCommitted += (_, _) => Save();
            rightPanel.SizeCommitted += (_, _) => Save();
            bottomPanel.SizeCommitted += (_, _) => Save();

            SyncColumnVisibility();

            void ChooseTheme(UiThemeKind kind)
            {
                if (root.Theme.Kind == kind)
                {
                    return;
                }

                // Saved before the swap: the columns whose widths it records are about to be rebuilt.
                state.Theme = kind;
                Save();
                root.Theme = UiTheme.Create(kind);

                // Rebuilt, not repainted: views capture theme colours as they're constructed.
                shell.Child = Build(root, shell, vm, liveStat, persist, editor, world, layoutPath);
            }

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
                if (root.Theme.FontFamily == family)
                {
                    return;
                }

                root.Theme.FontFamily = family;

                // Every measured width in the tree was computed with the old face, so this needs a full relayout.
                root.InvalidateAll();
                Save();
                shell.Child = Build(root, shell, vm, liveStat, persist, editor, world, layoutPath);
            }

            void TogglePanel(string id)
            {
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

                // Voxel Tools goes back to its own strip, Import to its own, everything else prefers the
                // right column.
                var host = id == "tools" ? top : id == "import" ? bottom : right.RootNode != null ? right : left;
                host.Dock(item, DockSide.Center, DockHost.FindFirstGroup(host.RootNode));
            }

            bool IsPanelOpen(string id) =>
                AllDocked(top, left, right, bottom).Any(item => item.Id == id)
                || dragController.FindFloating(id) != null;

            void Choose(WorkspacePreset preset)
            {
                state.Preset = preset;
                ApplyPreset(preset, top, left, right, bottom, Resolve, FloatingIds());
                SyncColumnVisibility();
                Save();
            }

            // Rows: the toolbar strip, the main area, and the Import strip -- but the Import strip only
            // spans the left column and the viewport (columns 0-1). The right column spans both of the
            // lower rows so it reaches the status bar unbroken, matching Unreal's own Outliner/Details
            // column running the full height beside a Content Browser docked under the viewport.
            var dockArea = new Grid()
                .Rows("Auto, *, Auto")
                .Columns("Auto, *, Auto")
                .Children(
                    topPanel.GridRow(0).GridColumn(0, span: 3),
                    leftPanel.GridRow(1).GridColumn(0),
                    rightPanel.GridRow(1, span: 2).GridColumn(2),
                    bottomPanel.GridRow(2).GridColumn(0, span: 2))
                .Anchor(top: MenuBarHeight, left: 0.0f, right: 0.0f, bottom: StatusBarHeight);

            // Overlay first so the dock area draws over it; it works out the viewport from what the strips
            // cover. The right column already excludes itself via rightWidth regardless of row span, so the
            // bottom strip's height only needs to be subtracted once for the left+centre area it actually sits under.
            return new Canvas().Children(
                new ViewportOverlayLayer(
                    theme,
                    () => leftPanel.Visibility == Visibility.Collapsed ? 0.0f : leftPanel.Size,
                    () => rightPanel.Visibility == Visibility.Collapsed ? 0.0f : rightPanel.Size,
                    () => MenuBarHeight + (topPanel.Visibility == Visibility.Collapsed ? 0.0f : topPanel.Size),
                    () => StatusBarHeight + (bottomPanel.Visibility == Visibility.Collapsed ? 0.0f : bottomPanel.Size),
                    camera,
                    editor)
                    .Anchor(left: 0.0f, top: 0.0f, right: 0.0f, bottom: 0.0f),

                dockArea,

                new Border()
                    .Fill(theme.Frosted(theme.Surface))
                    .Blur(theme.BackdropBlur)
                    .Outline(theme.Border, 1.0f)
                    .Height(MenuBarHeight)
                    .Content(new Grid().Columns("*, Auto").Rows("Auto").Children(
                        BuildMenuBar(vm, state, Choose, debug, root, ChooseTheme, ChooseFont, ChooseBlur, TogglePanel, IsPanelOpen, editor)
                            .GridColumn(0),
                        BuildClock(theme).Margin(0.0f, 0.0f, 12.0f, 0.0f).GridColumn(1)))
                    .Anchor(top: 0.0f, left: 0.0f, right: 0.0f),

                new Border()
                    .Fill(theme.Frosted(theme.Surface))
                    .Blur(theme.BackdropBlur)
                    .Outline(theme.Border, 1.0f)
                    .Height(StatusBarHeight)
                    .PaddingOf(8.0f, 2.0f)
                    .Content(new Grid().Columns("*, Auto").Rows("Auto").Children(
                        ManaUiPerformanceStrip.Build(theme).GridColumn(1).CenterV()))
                    .Anchor(left: 0.0f, right: 0.0f, bottom: 0.0f));
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
            Place(bottom, resolve, "import", DockSide.Center);

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
            Place(right, resolve, "placement", DockSide.Center, outliner);
            Place(right, resolve, "physics", DockSide.Center, outliner);
            Place(right, resolve, "voxelstats", DockSide.Center, outliner);
            Place(right, resolve, "lighting", DockSide.Center, outliner);
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
            Place(right, resolve, "placement", DockSide.Center, inspector);
            Place(right, resolve, "physics", DockSide.Center, inspector);
            Place(right, resolve, "voxelstats", DockSide.Center, inspector);
            Place(right, resolve, "lighting", DockSide.Center, inspector);
            Place(right, resolve, "about", DockSide.Center, inspector);
        }

        /// <summary>Outliner as a shallow top band, details filling below it -- roughly Unreal's proportions.</summary>
        private static void BuildUnreal(DockHost left, DockHost right, Func<string, DockItem?> resolve)
        {
            var placement = Place(left, resolve, "placement", DockSide.Center);
            Place(left, resolve, "physics", DockSide.Center, placement);
            Place(left, resolve, "voxelstats", DockSide.Center, placement);
            Place(left, resolve, "lighting", DockSide.Center, placement);
            Place(left, resolve, "about", DockSide.Center, placement);
            Place(left, resolve, "showcase", DockSide.Bottom);

            var outliner = Place(right, resolve, "outliner", DockSide.Center);
            Place(right, resolve, "inspector", DockSide.Bottom, outliner);
        }

        /// <summary>Outliner far left, inspector far right, viewport clear between them.</summary>
        private static void BuildSplit(DockHost left, DockHost right, Func<string, DockItem?> resolve)
        {
            var outliner = Place(left, resolve, "outliner", DockSide.Center);
            Place(left, resolve, "placement", DockSide.Center, outliner);
            Place(left, resolve, "physics", DockSide.Center, outliner);
            Place(left, resolve, "voxelstats", DockSide.Center, outliner);
            Place(left, resolve, "lighting", DockSide.Center, outliner);
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

                if (id == "import")
                {
                    bottom.Dock(item, DockSide.Center, DockHost.FindFirstGroup(bottom.RootNode));
                    continue;
                }

                var host = right.RootNode != null ? right : left;
                host.Dock(item, DockSide.Center, DockHost.FindFirstGroup(host.RootNode));
            }
        }

        private static bool OpensByDefault(string id)
        {
            return !string.Equals(id, "level", StringComparison.Ordinal);
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

                    // Arranged size, not requested: a resized window comes back the size it was left.
                    Width = window.Bounds.Width > 0.0f ? window.Bounds.Width : FloatingFallback.X,
                    Height = window.Bounds.Height > 0.0f ? window.Bounds.Height : FloatingFallback.Y
                });
            }

            return captured;
        }

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
                if (resolve(entry.Id) is not { } item)
                {
                    continue;
                }

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
        }

        // ----- menu ---------------------------------------------------------------------------------

        /// <summary>The editor's real menu bar, in ManaUI.</summary>
        private static MenuBar BuildMenuBar(
            ManaUiShowcaseViewModel vm,
            WorkspaceState state,
            Action<WorkspacePreset> choose,
            UiDebugOverlay debug,
            UiRoot root,
            Action<UiThemeKind> chooseTheme,
            Action<string> chooseFont,
            Action<float> chooseBlur,
            Action<string> togglePanel,
            Func<string, bool> isPanelOpen,
            VoxelEditorViewModel? editor)
        {
            var bar = new MenuBar();

            // Editor menus first, so the bar reads the way the ImGui one did. Owned by the editor, not this chrome.
            if (editor != null)
            {
                foreach (var menu in VoxelEditorMainMenuManaView.BuildMenus(editor))
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
                    new MenuItem("Reset panels", () => choose(state.Preset))),

                new MenuItem("Panels").WithItems(
                    PanelIds
                        .Select(id => new MenuItem(
                                PanelTitles.TryGetValue(id, out var title) ? title : id,
                                () => togglePanel(id))
                            .Checkable(() => isPanelOpen(id)))
                        .ToArray()),

                // "Appearance" rather than "View": the editor's own View menu already sits in this bar.
                new MenuItem("Appearance").WithItems(
                    new MenuItem("Theme").WithItems(
                        new MenuItem("Dark", () => chooseTheme(UiThemeKind.Dark))
                            .Checkable(() => root.Theme.Kind == UiThemeKind.Dark),
                        new MenuItem("Light", () => chooseTheme(UiThemeKind.Light))
                            .Checkable(() => root.Theme.Kind == UiThemeKind.Light)),

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
                    new MenuItem("Wireframe", () => vm.ShowWireframe = !vm.ShowWireframe)
                        .Checkable(() => vm.ShowWireframe),
                    MenuItem.Separator(),
                    new MenuItem("Tool").WithItems(
                        vm.Tools.Select((tool, index) => new MenuItem(tool, () => vm.SelectedToolIndex = index)
                            .Checkable(() => vm.SelectedToolIndex == index)).ToArray())));
        }

        /// <summary>Powers of two: the blur is a mip chain, so anything between two levels is interpolated.</summary>
        public static readonly float[] BlurChoices = { 0.0f, 4.0f, 8.0f, 16.0f, 32.0f };

        private static string BlurLabel(float blur) => blur <= 0.0f ? "Off" : $"{blur:0} px";

        /// <summary>Fonts offered in the Font menu, filtered at build time to whichever are actually installed.</summary>
        private static readonly string[] FontChoices =
        {
            "Roboto",
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

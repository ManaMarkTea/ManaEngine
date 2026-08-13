using Mana.Code.UI.ManaUi;
using ManaCore.Graphics;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Search;

using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The editor's ManaUI workspace: two resizable columns of dockable panels, arranged from a named
    /// preset and remembered across restarts.
    /// <para>
    /// Two columns rather than one full-screen dock host, because ManaUI is still being built out alongside
    /// the existing ImGui editor -- a host that filled the window would cover both the viewport and the
    /// panels people actually use today. Columns leave the middle clear, which is also what makes the
    /// Unity- and Unreal-shaped presets possible.
    /// </para>
    /// </summary>
    public static class ManaUiDockDemo
    {
        private const float TopOffset = 60.0f;
        private const float MenuBarHeight = 32.0f;

        private const float DefaultLeftWidth = 300.0f;
        private const float DefaultRightWidth = 380.0f;

        /// <summary>
        /// Bump when a panel is added, removed or renamed. A saved workspace carrying an older version has
        /// missing panels merged into it on the next run; one carrying this version is taken as-is, so a
        /// panel the user closed stays closed. Same idea as BaseGameMode.DockLayoutVersion does for ImGui,
        /// except that this merges rather than discarding the arrangement.
        /// </summary>
        private const int LayoutVersion = 12;

        /// <summary>
        /// Layout geometry here is proportional, not pixel-based like ImGui's ini, so one file works at
        /// every UI scale and does not need the per-scale split that storage uses for ImGui.
        /// </summary>
        private static string DefaultLayoutPath =>
            ManaImGuiSettingsStorage.SharedSettingsFilePath("manaui-dock.json");

        /// <summary>
        /// What each panel is called, for the Panels menu.
        /// <para>
        /// Separate from the factory rather than read out of it, because asking the factory would mean
        /// building the panel to find out its name -- and building every panel to draw a menu is exactly
        /// the cost the factory exists to avoid. A test holds the two lists to each other.
        /// </para>
        /// </summary>
        public static readonly Dictionary<string, string> PanelTitles = new(StringComparer.Ordinal)
        {
            ["tools"] = "Voxel Tools",
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
            ["layers"] = "Layers",
            ["stats"] = "Stats",
            ["about"] = "About"
        };

        /// <summary>
        /// The asset list behind the import panel, shared rather than per-panel: scanning walks the whole
        /// asset tree, and a theme switch rebuilds every panel -- which would otherwise throw the list away
        /// and rescan for no reason.
        /// </summary>
        public static MeshAssetLibrary MeshAssets { get; } = new();

        /// <summary>
        /// The place search, shared for the same reason: a theme switch rebuilds every panel, and results
        /// someone waited on a network round trip for should survive that.
        /// </summary>
        public static PlaceSearchModel PlaceSearch { get; } = new();

        /// <summary>Every panel the workspace can show.</summary>
        /// <summary>
        /// Every panel this workspace can build, in the order a merge should add them.
        /// <para>
        /// This list and the factory below have to agree, and when they were written separately they
        /// silently stopped agreeing: six panels were added to the factory and not here, so upgrading an
        /// existing workspace found nothing new to merge and the panels only appeared after a reset. The
        /// factory asserts against this list now, so a panel that is not in both fails a test rather than
        /// going missing for exactly the people who already use the editor.
        /// </para>
        /// </summary>
        public static readonly string[] PanelIds =
        {
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
            "layers",
            "stats",
            "about"
        };

        /// <summary>
        /// Builds the workspace over a <see cref="UiRoot"/> rather than the windowing host, so the whole
        /// thing can be constructed and exercised headlessly.
        /// <para>
        /// <paramref name="editor"/> and <paramref name="world"/> are the editor's real state. Panels that
        /// need one and are not given it simply do not appear, which is what lets the same workspace be
        /// built in a test with neither.
        /// </para>
        /// </summary>
        public static UiElement Attach(
            UiRoot root,
            ManaUiShowcaseViewModel vm,
            Func<string>? liveStat = null,
            bool persist = true,
            VoxelEditorViewModel? editor = null,
            global::Mana.ManaWorld? world = null,
            string? layoutPath = null)
        {
            // Overridable so persistence can be exercised against a scratch file. Without it the only way
            // to test saving and reloading would be to write the user's real workspace.
            layoutPath ??= DefaultLayoutPath;

            // The saved theme has to be applied before anything is built, because views read colours as
            // they construct rather than at render time -- which is also why switching rebuilds rather than
            // just repainting.
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
            var left = new DockHost();
            var right = new DockHost();

            // Read each frame rather than captured: the main camera is replaced when a scene loads, and an
            // overlay still drawing the old one would silently drift from the view.
            global::Mana.CameraComponent? camera() => world?.MainCamera;

            // The factory is what makes a saved layout restorable: content is application-owned and cannot
            // come out of a file, so persistence stores ids and asks for the panel back by name.
            DockItem? Resolve(string id) => id switch
            {
                "showcase" => new DockItem("ManaUI", ManaUiShowcaseView.BuildContent(theme, vm, liveStat).Scrollable())
                {
                    Id = "showcase"
                },
                "layers" => new DockItem("Layers", BuildLayers(theme, vm).Scrollable()) { Id = "layers" },
                "stats" => new DockItem("Stats", BuildStats(theme, vm, liveStat)) { Id = "stats" },
                "tools" when editor != null => new DockItem(
                    "Voxel Tools",
                    VoxelToolPaletteManaView.Build(theme, editor).Scrollable()) { Id = "tools" },

                // One panel per subject rather than one with a tab bar: the dock already tabs anything
                // sharing a slot, so this gives the same arrangement by default and lets them be pulled
                // apart.
                "import" when editor != null => new DockItem(
                    "Import",
                    MeshImportManaView.Build(theme, editor, MeshAssets)) { Id = "import" },
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

                // The snapshot is whatever the backend reported at start-up; null before it has, which the
                // panel says rather than showing an empty frame.
                "about" => new DockItem("About", new DynamicContent()
                    .Showing<ManaGraphicsDiagnosticsSnapshot>(
                        () => ManaUiDiagnostics.Snapshot,
                        captured => AboutManaView.Build(theme, captured, ManaUiDiagnostics.BackendName))
                    .Empty(AboutManaView.Build(theme, null, ManaUiDiagnostics.BackendName)))
                {
                    Id = "about"
                },
                // world.Selection rather than a separately supplied instance: it is the same object the
                // ImGui outliner and the viewport already use, so the two outliners cannot drift apart.
                "outliner" when world != null => new DockItem(
                    "Outliner",
                    SceneOutlinerManaView.Build(theme, world, world.Selection)) { Id = "outliner" },
                "inspector" when world != null => new DockItem(
                    "Inspector",
                    SceneInspectorManaView.Build(theme, world.Selection, world.Context)) { Id = "inspector" },
                _ => null
            };

            var saved = persist ? ManaUiWorkspaceLayout.Load(layoutPath) : null;
            var state = new WorkspaceState
            {
                Preset = saved?.Preset ?? WorkspacePreset.RightColumn,
                Theme = root.Theme.Kind,
                BackdropBlur = root.Theme.BackdropBlur
            };

            // Non-short-circuiting on purpose: both columns must be restored, and && would skip the right
            // one whenever the left was empty.
            var restored = saved != null
                && (DockLayout.Apply(left, saved.Left, Resolve) | DockLayout.Apply(right, saved.Right, Resolve));

            if (!restored)
            {
                ApplyPreset(state.Preset, left, right, Resolve);
            }
            else if (saved!.Version != LayoutVersion)
            {
                // A saved workspace only knows the panels that existed when it was written, so a newly
                // added one would never appear for anyone who has run the editor before -- exactly what
                // happened when the voxel palette was added. Merging on a version change keeps the user's
                // arrangement while still surfacing what is new.
                MergeMissingPanels(left, right, Resolve);
            }

            // One controller over both columns, so a panel torn out of either can be dropped into either.
            // A controller per host could not do that: each only resolved drops inside its own tree.
            var dragController = new DockDragController(root, new[] { left, right });

            // Backgrounds matter here because the columns sit over the viewport: the handle gutter and the
            // seams between panes are part of the column but not part of any panel's content, so without a
            // backdrop the scene shows through and a solid column reads as translucent.
            var leftPanel = new ResizablePanel
            {
                Edge = Dock.Left,
                Size = saved is { LeftWidth: > 0.0f } ? saved.LeftWidth : DefaultLeftWidth,
                MinimumSize = 200.0f,

                // Translucent only when there is a blur to see through it to. An opaque column over a
                // blurred snapshot is just an opaque column that made the GPU work for nothing.
                Background = theme.Frosted(theme.Background),
                BackdropBlur = theme.BackdropBlur,
                Child = left
            };

            var rightPanel = new ResizablePanel
            {
                Edge = Dock.Right,
                Size = saved is { RightWidth: > 0.0f } ? saved.RightWidth : DefaultRightWidth,
                MinimumSize = 220.0f,
                Background = theme.Frosted(theme.Background),
                BackdropBlur = theme.BackdropBlur,
                Child = right
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
                    LeftWidth = leftPanel.Size,
                    RightWidth = rightPanel.Size,
                    Left = DockLayout.Capture(left),
                    Right = DockLayout.Capture(right)
                }.Save(layoutPath);
            }

            // An empty column would leave a resize handle floating against nothing, so it hides itself
            // until it has something to hold.
            void SyncColumnVisibility()
            {
                leftPanel.Visibility = left.RootNode == null ? Visibility.Collapsed : Visibility.Visible;
                rightPanel.Visibility = right.RootNode == null ? Visibility.Collapsed : Visibility.Visible;
            }

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

            // Committed, not every pixel of the drag: a resize would otherwise rewrite the file on every
            // frame it moved.
            leftPanel.SizeCommitted += (_, _) => Save();
            rightPanel.SizeCommitted += (_, _) => Save();

            SyncColumnVisibility();

            void ChooseTheme(UiThemeKind kind)
            {
                if (root.Theme.Kind == kind)
                {
                    return;
                }

                // Set before the save, saved before the swap: the file needs the incoming theme, and the
                // columns whose widths it also records are about to be replaced.
                state.Theme = kind;
                Save();
                root.Theme = UiTheme.Create(kind);

                // Rebuilt rather than repainted. Application views capture theme colours as they are
                // constructed -- a foreground here, a fill there -- so a swap that only re-rendered would
                // leave half the workspace in the old palette.
                shell.Child = Build(root, shell, vm, liveStat, persist, editor, world, layoutPath);
            }

            // Blurring is a whole extra capture and reduce of the frame, so it is chosen rather than always
            // on -- and rebuilds the workspace for the same reason a theme change does: the panels read
            // their fills as they are constructed.
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

                // Every measured width in the tree was computed with the old face, so this is a full
                // relayout rather than a repaint -- and the workspace is rebuilt for the same reason a
                // theme change rebuilds it.
                root.InvalidateAll();
                Save();
                shell.Child = Build(root, shell, vm, liveStat, persist, editor, world, layoutPath);
            }

            // Closing a panel used to be permanent: nothing listed what existed, so a panel dismissed by its
            // tab's close button was gone until the layout was reset. This is what makes that reversible,
            // and what makes a newly added panel findable without waiting for a version merge.
            void TogglePanel(string id)
            {
                // Floating windows count. Without them a panel torn out of the dock reads as closed, and
                // opening it again builds a second copy of something already on screen.
                if (dragController.FindFloating(id) is { } floating)
                {
                    floating.BringToFront();
                    floating.Flash();
                    return;
                }

                var docked = left.AllItems().Concat(right.AllItems())
                    .FirstOrDefault(item => item.Id == id);

                if (docked != null)
                {
                    docked.Owner?.Remove(docked);
                    return;
                }

                if (Resolve(id) is not { } item)
                {
                    return;
                }

                // Into whichever column exists, preferring the right, the same as a version merge does.
                var host = right.RootNode != null ? right : left;
                host.Dock(item, DockSide.Center, DockHost.FindFirstGroup(host.RootNode));
            }

            bool IsPanelOpen(string id) =>
                left.AllItems().Concat(right.AllItems()).Any(item => item.Id == id)
                || dragController.FindFloating(id) != null;

            void Choose(WorkspacePreset preset)
            {
                state.Preset = preset;
                ApplyPreset(preset, left, right, Resolve);
                SyncColumnVisibility();
                Save();
            }

            // The menu bar rides above the right column, both anchored clear of ImGui's own chrome. The
            // performance strip sits at the far left of that same band: it is a glance, not a panel, so it
            // wants somewhere permanent and small rather than a column of its own.
            //
            // The overlay layer goes in first, so the columns draw over it: it spans the whole window and
            // works out the viewport itself from what the columns are covering.
            return new Canvas().Children(
                new ViewportOverlayLayer(
                    theme,
                    () => leftPanel.Visibility == Visibility.Collapsed ? 0.0f : leftPanel.Size,
                    () => rightPanel.Visibility == Visibility.Collapsed ? 0.0f : rightPanel.Size,
                    TopOffset,
                    camera,
                    editor)
                    .Anchor(left: 0.0f, top: 0.0f, right: 0.0f, bottom: 0.0f),

                leftPanel.Anchor(top: TopOffset, left: 0.0f, bottom: 0.0f),

                ManaUiPerformanceStrip.Build(theme)
                    .Anchor(top: TopOffset - MenuBarHeight, left: 0.0f),

                // Sized to its own menus rather than to the right column. Pinned to the column's width, the
                // bar was cut off at whatever fitted -- so Panels and Appearance, being last, could not be
                // reached at all. It grows leftward over the viewport instead, which costs a strip of scene
                // that is already under the menu band.
                new Border()
                    .Fill(theme.Frosted(theme.Surface))
                    .Blur(theme.BackdropBlur)
                    .Outline(theme.Border, 1.0f)
                    .Rounded(theme.CornerRadius)
                    .Content(BuildMenuBar(vm, state, Choose, debug, root, ChooseTheme, ChooseFont, ChooseBlur, TogglePanel, IsPanelOpen, editor))
                    .Anchor(top: TopOffset - MenuBarHeight, right: 0.0f),

                rightPanel.Anchor(top: TopOffset, right: 0.0f, bottom: 0.0f));
        }

        /// <summary>Mutable bits the menu and the save hook both need to see.</summary>
        private sealed class WorkspaceState
        {
            public WorkspacePreset Preset { get; set; }

            /// <summary>
            /// The theme to persist, which is not always the one the root is showing.
            /// <para>
            /// A theme change has to write the file before it swaps, because the swap rebuilds the
            /// workspace and the save closure still refers to the old columns -- they are the ones holding
            /// the widths worth keeping. Reading the theme off the root at that moment gave the outgoing
            /// one, so every switch persisted the theme it was leaving and the setting was permanently one
            /// run behind.
            /// </para>
            /// </summary>
            public UiThemeKind Theme { get; set; }

            public float BackdropBlur { get; set; }
        }

        // ----- presets ------------------------------------------------------------------------------

        /// <summary>
        /// Rebuilds both columns from scratch. Presets are starting points rather than modes, so this runs
        /// only when one is chosen -- everything the user does afterwards is saved over the top of it.
        /// </summary>
        private static void ApplyPreset(
            WorkspacePreset preset,
            DockHost left,
            DockHost right,
            Func<string, DockItem?> resolve)
        {
            left.RootNode = null;
            right.RootNode = null;

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

            // Every preset names the panels it arranges, so a newly added one is in none of them and a
            // reset silently drops it -- which is how Import, Search and the blur test card went missing
            // for anyone who tried a preset. Anything a preset did not place goes in rather than nowhere.
            PlaceRemaining(left, right, resolve);
        }

        /// <summary>
        /// Docks whatever a preset did not name. Into whichever column exists, preferring the right, the
        /// same as an upgrade merge does.
        /// </summary>
        private static void PlaceRemaining(DockHost left, DockHost right, Func<string, DockItem?> resolve)
        {
            var present = left.AllItems().Concat(right.AllItems())
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var id in PanelIds)
            {
                if (present.Contains(id) || resolve(id) is not { } item)
                {
                    continue;
                }

                var host = right.RootNode != null ? right : left;
                host.Dock(item, DockSide.Center, DockHost.FindFirstGroup(host.RootNode));
            }
        }

        /// <summary>Everything in one column, which is how these panels started out.</summary>
        private static void BuildRightColumn(DockHost right, Func<string, DockItem?> resolve)
        {
            var tools = Place(right, resolve, "tools", DockSide.Center);
            var outliner = Place(right, resolve, "outliner", DockSide.Bottom, tools);

            // Tabbed with the outliner: master and detail, and side by side in one column would leave
            // neither enough width for a transform row.
            Place(right, resolve, "inspector", DockSide.Center, outliner);
            Place(right, resolve, "showcase", DockSide.Center, tools);
            Place(right, resolve, "placement", DockSide.Center, tools);
            Place(right, resolve, "physics", DockSide.Center, tools);
            Place(right, resolve, "voxelstats", DockSide.Center, tools);
            Place(right, resolve, "lighting", DockSide.Center, tools);
            Place(right, resolve, "about", DockSide.Center, tools);

            var layers = Place(right, resolve, "layers", DockSide.Bottom);
            Place(right, resolve, "stats", DockSide.Center, layers);
        }

        /// <summary>Tools on the left; hierarchy above inspector on the right, as Unity stacks them.</summary>
        private static void BuildUnity(DockHost left, DockHost right, Func<string, DockItem?> resolve)
        {
            var tools = Place(left, resolve, "tools", DockSide.Center);
            Place(left, resolve, "showcase", DockSide.Center, tools);
            Place(left, resolve, "placement", DockSide.Center, tools);
            Place(left, resolve, "physics", DockSide.Center, tools);
            Place(left, resolve, "voxelstats", DockSide.Center, tools);
            Place(left, resolve, "lighting", DockSide.Center, tools);
            Place(left, resolve, "about", DockSide.Center, tools);

            var outliner = Place(right, resolve, "outliner", DockSide.Center);
            var inspector = Place(right, resolve, "inspector", DockSide.Bottom, outliner);

            Place(right, resolve, "layers", DockSide.Center, inspector);
            Place(right, resolve, "stats", DockSide.Center, inspector);
        }

        /// <summary>
        /// Tools left; outliner as a shallow band top-right with the details panel filling the space
        /// beneath it, which is roughly the proportion Unreal gives them.
        /// </summary>
        private static void BuildUnreal(DockHost left, DockHost right, Func<string, DockItem?> resolve)
        {
            var tools = Place(left, resolve, "tools", DockSide.Center);
            Place(left, resolve, "layers", DockSide.Center, tools);
            Place(left, resolve, "placement", DockSide.Center, tools);
            Place(left, resolve, "physics", DockSide.Center, tools);
            Place(left, resolve, "voxelstats", DockSide.Center, tools);
            Place(left, resolve, "lighting", DockSide.Center, tools);
            Place(left, resolve, "about", DockSide.Center, tools);
            Place(left, resolve, "showcase", DockSide.Bottom);

            var outliner = Place(right, resolve, "outliner", DockSide.Center);
            var inspector = Place(right, resolve, "inspector", DockSide.Bottom, outliner);
            Place(right, resolve, "stats", DockSide.Center, inspector);

            // The outliner is a list and the inspector is a form, so the space is not split evenly.
            if (right.RootNode is DockSplit split && split.Children.Count == 2)
            {
                split.SetWeights(new[] { 0.35f, 0.65f });
            }
        }

        /// <summary>Outliner far left, inspector far right, viewport clear between them.</summary>
        private static void BuildSplit(DockHost left, DockHost right, Func<string, DockItem?> resolve)
        {
            var outliner = Place(left, resolve, "outliner", DockSide.Center);
            var splitTools = Place(left, resolve, "tools", DockSide.Center, outliner);
            Place(left, resolve, "placement", DockSide.Center, splitTools);
            Place(left, resolve, "physics", DockSide.Center, splitTools);
            Place(left, resolve, "voxelstats", DockSide.Center, splitTools);
            Place(left, resolve, "lighting", DockSide.Center, splitTools);
            Place(left, resolve, "about", DockSide.Center, splitTools);

            var inspector = Place(right, resolve, "inspector", DockSide.Center);
            Place(right, resolve, "showcase", DockSide.Center, inspector);
            var layers = Place(right, resolve, "layers", DockSide.Bottom);
            Place(right, resolve, "stats", DockSide.Center, layers);
        }

        /// <summary>
        /// Docks a panel by id, or does nothing when it is unavailable in this session -- the voxel palette
        /// with no editor view model, the outliner with no world. Returns the node it landed in so the next
        /// panel can be placed relative to it.
        /// </summary>
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

            // Center with no target lands in the first group rather than beside the panel it was meant to
            // join, which silently reshuffles a preset whenever an earlier panel was unavailable. Splitting
            // instead keeps the shape recognisable.
            if (target == null && host.RootNode != null && side == DockSide.Center)
            {
                side = DockSide.Bottom;
            }

            return host.Dock(item, side, target);
        }

        /// <summary>
        /// Docks any panel neither column contains. Only runs when the saved version differs from
        /// <see cref="LayoutVersion"/>, so a panel the user deliberately closed stays closed until the
        /// panel set actually changes.
        /// </summary>
        private static void MergeMissingPanels(DockHost left, DockHost right, Func<string, DockItem?> resolve)
        {
            var present = left.AllItems().Concat(right.AllItems())
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var id in PanelIds)
            {
                if (present.Contains(id))
                {
                    continue;
                }

                var item = resolve(id);
                if (item == null)
                {
                    continue;
                }

                // Into whichever column already exists, preferring the right -- that is where a
                // single-column workspace keeps everything.
                var host = right.RootNode != null ? right : left;
                host.Dock(item, DockSide.Center, DockHost.FindFirstGroup(host.RootNode));
            }
        }

        // ----- menu ---------------------------------------------------------------------------------

        /// <summary>
        /// A ManaUI menu bar running beside ImGui's own. Not a replacement yet -- it drives the demo view
        /// model rather than the editor -- but it is the shape the real one will take.
        /// </summary>
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

            // The editor's own menus come first, so the bar reads the way the ImGui one does. They are
            // added rather than built into this method because they belong to the editor, not to the
            // workspace chrome -- the workspace outlives any particular application's menus.
            if (editor != null)
            {
                foreach (var menu in VoxelEditorMainMenuManaView.BuildMenus(editor))
                {
                    bar.Add(menu);
                }
            }

            return bar.AddRange(
                new MenuItem("Debug").WithItems(
                    DebugItem("Element bounds", UiDebugFlags.ElementBounds, debug),
                    DebugItem("Clip rects", UiDebugFlags.ClipRects, debug),
                    DebugItem("Hover path", UiDebugFlags.HoverPath, debug),
                    DebugItem("Stats", UiDebugFlags.Stats, debug),
                    MenuItem.Separator(),
                    new MenuItem("All on", () => debug.Flags = UiDebugFlags.All),
                    new MenuItem("All off", () => debug.Flags = UiDebugFlags.None)),

                new MenuItem("Layout").WithItems(
                    PresetItem("Right column", WorkspacePreset.RightColumn, state, choose),
                    PresetItem("Unity", WorkspacePreset.Unity, state, choose),
                    PresetItem("Unreal", WorkspacePreset.Unreal, state, choose),
                    PresetItem("Split", WorkspacePreset.Split, state, choose),
                    MenuItem.Separator(),

                    // Reapplies the preset already chosen, which is what "reset" means here: put the
                    // panels back where this arrangement started.
                    new MenuItem("Reset panels", () => choose(state.Preset))),

                new MenuItem("Layers").WithItems(
                    new MenuItem("Add layer") { Command = vm.AddLayerCommand }.WithShortcut("Ctrl+N"),
                    new MenuItem("Remove layer") { Command = vm.RemoveLayerCommand },
                    MenuItem.Separator(),
                    new MenuItem("Clear all", () =>
                    {
                        while (vm.Layers.Count > 0)
                        {
                            vm.RemoveLayerCommand.Execute(null);
                        }
                    })),

                // Every panel the workspace can build, so none of them can be lost. Ordered as PanelIds is,
                // which groups the editor's own panels ahead of the general ones.
                new MenuItem("Panels").WithItems(
                    PanelIds
                        .Select(id => new MenuItem(
                                PanelTitles.TryGetValue(id, out var title) ? title : id,
                                () => togglePanel(id))
                            .Checkable(() => isPanelOpen(id)))
                        .ToArray()),

                // "Appearance" rather than "View": the editor's own View menu now sits in this same bar,
                // and two menus with one name is a coin toss for whoever is looking for something.
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

        /// <summary>
        /// Fonts offered in the Font menu. Filtered at build time to whichever are actually installed, so
        /// the list spans platforms without offering a Windows face on a Mac.
        /// </summary>
        /// <summary>
        /// Blur radii offered. Powers of two because the blur is a mip chain -- each level is twice the
        /// radius of the last, so anything between them is interpolated rather than distinct.
        /// </summary>
        public static readonly float[] BlurChoices = { 0.0f, 4.0f, 8.0f, 16.0f, 32.0f };

        private static string BlurLabel(float blur) => blur <= 0.0f ? "Off" : $"{blur:0} px";

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

        private static MenuItem PresetItem(
            string label,
            WorkspacePreset preset,
            WorkspaceState state,
            Action<WorkspacePreset> choose)
        {
            return new MenuItem(label, () => choose(preset))
                .Checkable(() => state.Preset == preset);
        }

        // ----- demo panels --------------------------------------------------------------------------

        private static UiElement BuildLayers(UiTheme theme, ManaUiShowcaseViewModel vm)
        {
            return new StackPanel().Vertical(theme.Spacing).Padding(12.0f).Children(
                new StackPanel().Horizontal(theme.Spacing).Children(
                    new Button().Label("Add").Command(vm.AddLayerCommand),
                    new Button().Label("Remove").Command(vm.RemoveLayerCommand)),

                new ItemsControl()
                    .Items(vm.Layers, layer => new Border()
                        .Fill(theme.SurfaceAlt)
                        .Rounded(theme.CornerRadius)
                        .PaddingOf(8.0f, 4.0f)
                        .Content(new Grid().Columns("*, Auto", 8.0f).Rows("Auto").Children(
                            new TextBlock().BindText(layer, l => l.Name).Ellipsis().GridColumn(0),
                            new TextBlock()
                                .BindText(layer, l => l.VoxelCount, n => $"{n:N0}")
                                .Foreground(theme.TextMuted)
                                .GridColumn(1))))
                    .Spacing(2.0f)
                    .WhenEmpty(new TextBlock().Text("No layers.").Foreground(theme.TextMuted)));
        }

        private static UiElement BuildStats(UiTheme theme, ManaUiShowcaseViewModel vm, Func<string>? liveStat)
        {
            var stack = new StackPanel().Vertical(theme.Spacing).Padding(12.0f);

            if (liveStat != null)
            {
                stack.Children.Add(new TextBlock().BindTextPolled(liveStat).Foreground(theme.TextMuted));
            }

            // Polled rather than bound: a collection's Count is not an observable property, and the
            // binding engine rejects the nested path outright rather than silently going stale.
            stack.Children.Add(new TextBlock()
                .BindTextPolled(() => $"{vm.Layers.Count} layer{(vm.Layers.Count == 1 ? string.Empty : "s")}")
                .Foreground(theme.TextMuted));

            stack.Children.Add(new TextBlock()
                .BindText(vm, v => v.ClickCount, n => $"{n} button press{(n == 1 ? string.Empty : "es")}")
                .Foreground(theme.TextMuted));

            stack.Children.Add(new TextBlock().Text("Drag a tab off the strip to float it.").Wrap());
            stack.Children.Add(new TextBlock().Text("Drop it near an edge to dock it back.").Wrap());

            return stack;
        }
    }
}

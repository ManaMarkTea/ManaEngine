using ManaCore.Graphics;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi
{
    // Owns layout capture, restoration and migrations; the workspace owns controls and event wiring.
    internal sealed class EditorWorkspaceLayout(
        UiRoot root, DockHost top, DockHost left, DockHost right, DockHost bottom,
        DockDragController dragController, Func<string, DockItem?> resolvePanel, bool persist, string layoutPath)
    {
        // Bump when panel membership or layout geometry changes; older layouts merge new panels.
        private const int LayoutVersion = 25;

        internal static string DefaultPath =>
            ManaUiSettingsStorage.SharedSettingsFilePath("manaui-dock.json");

        internal static ManaUiWorkspaceLayout? Load(string path, bool persist) =>
            persist ? ManaUiWorkspaceLayout.Load(path) : null;

        internal IEnumerable<DockItem> Docked => AllDocked(top, left, right, bottom);

        private HashSet<string> FloatingIds() => dragController.FloatingItems
            .Select(item => item.Id ?? string.Empty).ToHashSet(StringComparer.Ordinal);

        internal void Restore(ManaUiWorkspaceLayout? saved, WorkspacePreset preset)
        {
            // Before the columns are filled, so a floating panel doesn't also get placed in a column.
            RestoreFloating(root, dragController, saved, resolvePanel);

            // Non-short-circuiting: every host must be restored, or && would skip later ones once one came back empty.
            var restored = saved != null
                && (DockLayout.Apply(top, saved.Top, resolvePanel)
                    | DockLayout.Apply(left, saved.Left, resolvePanel)
                    | DockLayout.Apply(right, saved.Right, resolvePanel)
                    | DockLayout.Apply(bottom, saved.Bottom, resolvePanel));

            if (!restored)
            {
                ApplyPreset(preset, top, left, right, bottom, resolvePanel, FloatingIds());
            }
            else if (saved is { } previous && previous.Version != LayoutVersion)
            {
                // Merges any panel that didn't exist when this was saved, so upgrades don't lose new panels.
                MergeMissingPanels(top, left, right, bottom, resolvePanel, FloatingIds());

                // Voxel Tools and Import used to live in a column; move each to its own strip once, on upgrade.
                RelocateToStrip("tools", top, new[] { left, right, bottom }, resolvePanel, FloatingIds());
                RelocateToStrip("import", bottom, new[] { left, right, top }, resolvePanel, FloatingIds());
            }
        }

        internal void ApplyPreset(WorkspacePreset preset) =>
            ApplyPreset(preset, top, left, right, bottom, resolvePanel, FloatingIds());

        internal void Save(State state, float leftWidth, float rightWidth, float bottomHeight)
        {
            if (!persist)
            {
                return;
            }
            // Saving a theme's default font as an override would replace the next theme's own font.
            var isDefaultFont = root.Theme.FontFamily == UiTheme.Create(state.Theme).FontFamily;

            new ManaUiWorkspaceLayout
            {
                Version = LayoutVersion,
                Preset = state.Preset,
                Theme = state.Theme,
                FontFamily = isDefaultFont ? null : root.Theme.FontFamily,
                BackdropBlur = state.BackdropBlur,
                LeftWidth = leftWidth,
                RightWidth = rightWidth,
                BottomHeight = bottomHeight,
                Top = DockLayout.Capture(top),
                Left = DockLayout.Capture(left),
                Right = DockLayout.Capture(right),
                Bottom = DockLayout.Capture(bottom),
                Floating = CaptureFloating(dragController)
            }.Save(layoutPath);
        }

        private static IEnumerable<DockItem> AllDocked(DockHost top, DockHost left, DockHost right, DockHost bottom)
        {
            return top.AllItems().Concat(left.AllItems()).Concat(right.AllItems()).Concat(bottom.AllItems());
        }

        /// <summary>Mutable bits the menu and the save hook both need to see.</summary>
        internal sealed class State
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

            foreach (var id in EditorPanelRegistry.Ids)
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

            foreach (var id in EditorPanelRegistry.Ids)
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

        // Saved layouts retain closed panels; defaults apply only to new layouts and upgrades.
        private static bool OpensByDefault(string id) => EditorPanelRegistry.OpensByDefault(id);

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

    }
}

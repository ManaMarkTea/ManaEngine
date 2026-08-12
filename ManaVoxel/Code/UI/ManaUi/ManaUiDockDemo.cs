using Mana.Code.UI.ManaUi;
using ManaCore.Graphics;
using ManaUI;

using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// A working docked layout: panels that can be tabbed, split, torn out into floating windows, dropped
    /// back, and whose arrangement survives a restart.
    /// <para>
    /// Anchored to the right of the screen rather than filling it, deliberately. ManaUI is still being
    /// built out alongside the existing ImGui editor, and a full-screen dock host would cover both the
    /// viewport and the panels people actually use today.
    /// </para>
    /// </summary>
    public static class ManaUiDockDemo
    {
        private const float PanelWidth = 400.0f;
        private const float TopOffset = 60.0f;

        /// <summary>
        /// Layout geometry here is proportional, not pixel-based like ImGui's ini, so one file works at
        /// every UI scale and does not need the per-scale split that storage uses for ImGui.
        /// </summary>
        private static string LayoutPath => ManaImGuiSettingsStorage.SharedSettingsFilePath("manaui-dock.json");

        /// <summary>
        /// Takes the <see cref="UiRoot"/> rather than the windowing host, so the whole layout can be built
        /// and exercised headlessly.
        /// </summary>
        /// <summary>
        /// <paramref name="editor"/> is the editor's real view model. When supplied, the ported voxel tool
        /// palette is docked alongside the demo panels and drives the same state the ImGui palette does.
        /// </summary>
        public static UiElement Attach(
            UiRoot root,
            ManaUiShowcaseViewModel vm,
            Func<string>? liveStat = null,
            bool persist = true,
            VoxelEditorViewModel? editor = null)
        {
            var theme = root.Theme;
            var dock = new DockHost();

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
                _ => null
            };

            if (!persist || !DockLayout.Load(dock, LayoutPath, Resolve))
            {
                BuildDefaultLayout(dock, Resolve);
            }

            // Enables tear-out and drop-to-dock; the controller subscribes to groups as the tree changes.
            _ = new DockDragController(root, dock);

            if (persist)
            {
                dock.LayoutChanged += (_, _) => TrySaveLayout(dock);
            }

            // The menu bar sits above the dock host in the same column, so both stay clear of the viewport.
            return new Canvas().Children(
                new Border()
                    .Fill(theme.Surface)
                    .Outline(theme.Border, 1.0f)
                    .Rounded(theme.CornerRadius)
                    .Content(BuildMenuBar(vm))
                    .Anchor(top: TopOffset - 32.0f, right: 0.0f)
                    .Width(PanelWidth),

                dock.Anchor(top: TopOffset, right: 0.0f, bottom: 0.0f).Width(PanelWidth));
        }

        /// <summary>
        /// A ManaUI menu bar running beside ImGui's own. Not a replacement yet -- it drives the demo view
        /// model rather than the editor -- but it is the shape the real one will take.
        /// </summary>
        private static MenuBar BuildMenuBar(ManaUiShowcaseViewModel vm)
        {
            return new MenuBar().AddRange(
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

                new MenuItem("View").WithItems(
                    new MenuItem("Wireframe", () => vm.ShowWireframe = !vm.ShowWireframe)
                        .Checkable(() => vm.ShowWireframe),
                    MenuItem.Separator(),
                    new MenuItem("Tool").WithItems(
                        vm.Tools.Select((tool, index) => new MenuItem(tool, () => vm.SelectedToolIndex = index)
                            .Checkable(() => vm.SelectedToolIndex == index)).ToArray())));
        }

        private static void BuildDefaultLayout(DockHost dock, Func<string, DockItem?> resolve)
        {
            // The ported palette leads, since it is the one panel here that does real work.
            var tools = resolve("tools");
            if (tools != null)
            {
                dock.Dock(tools, DockSide.Center);
            }

            // Tabbed with the palette when there is one, otherwise it becomes the first panel itself.
            var showcase = resolve("showcase");
            if (showcase != null)
            {
                dock.Dock(showcase, DockSide.Center, tools?.Owner);
            }

            var layers = resolve("layers");
            if (layers != null)
            {
                dock.Dock(layers, DockSide.Bottom);
            }

            var stats = resolve("stats");
            if (stats != null)
            {
                // Tabbed with Layers rather than given its own pane, so the default layout is not cramped.
                dock.Dock(stats, DockSide.Center, layers?.Owner);
            }
        }

        private static void TrySaveLayout(DockHost dock)
        {
            try
            {
                DockLayout.Save(dock, LayoutPath);
            }
            catch (IOException)
            {
                // A layout that cannot be written is not worth interrupting the editor for.
            }
        }

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

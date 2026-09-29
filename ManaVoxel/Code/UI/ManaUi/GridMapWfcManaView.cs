using ManaUI;
using ManaVoxel.Code.GridMaps;
using ManaVoxel.Code.UI.ManaUi.Inspectors;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The Generator window: rules for <see cref="GridMapWfc"/>, read from and saved to .manawfc files in
    /// Generators/, and the area of the selected grid map to generate into. Every tile of the rules can be
    /// edited here -- its model, the cells it covers, how often it comes up, which turns it may take and the
    /// labels on its sides -- so a kit other than Kenney's is a new rules file, not new code.
    /// </summary>
    public static class GridMapWfcManaView
    {
        public const string PanelId = "gridmapgen";

        private static readonly string[] RotationNames = { "All turns", "No turns", "Half turns" };

        private static readonly GridMapWfcRotations[] RotationValues = { GridMapWfcRotations.All, GridMapWfcRotations.None, GridMapWfcRotations.Half };

        public static UiElement Build(UiTheme theme, global::Mana.ManaWorld world, EditorUndoHistory? history)
        {
            var context = new InspectorContext(theme, world.Context, history: history);
            return new DynamicContent()
                .Showing<ManaGridMapNode>(
                    () => Target(world),
                    gridMap => BuildFor(gridMap, context))
                .Empty(new TextBlock().Text("Select a grid map to generate into.").Foreground(theme.TextMuted).Margin(theme.Spacing))
                .Scrollable()
                .Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Disabled);
        }

        /// <summary>The map the window generates into; see <see cref="GridMapWfcEditing.MapFor"/>.</summary>
        public static ManaGridMapNode? Target(global::Mana.ManaWorld world) =>
            GridMapWfcEditing.MapFor(world.Selection.SelectedNode, map => IsInWorld(world, map));

        private static bool IsInWorld(global::Mana.ManaWorld world, ManaGridMapNode map)
        {
            global::Mana.Code.Nodes.ManaSceneNode root = map;
            while (root.Parent != null)
            {
                root = root.Parent;
            }

            lock (world.SceneLock)
            {
                return world.Scenes.Any(scene => scene.Children.Contains(root));
            }
        }

        public static UiElement BuildFor(ManaGridMapNode gridMap, InspectorContext context)
        {
            var theme = context.Theme;
            var target = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Ellipsis();
            target.BindPolled(text => text.Text, () => $"Into {gridMap.Name}");
            target.WithTooltip("The grid map selected, or the last one generated into while something else is");
            return new StackPanel().Vertical(theme.Spacing).Margin(theme.Spacing).Children(
                target,
                RulesFile(theme),
                RulesSettings(theme),
                AreaSettings(gridMap, context),
                new DynamicContent().Showing<object>(() => GridMapWfcEditing.RulesToken, _ => TileList(theme)));
        }

        // ----- the rules file --------------------------------------------------------------------------

        private static UiElement RulesFile(UiTheme theme)
        {
            var files = new DynamicContent().Showing<object>(() => GridMapWfcEditing.RulesToken, _ =>
            {
                var paths = GridMapWfcGenerator.List().ToList();
                var names = paths.Select(System.IO.Path.GetFileNameWithoutExtension).Select(name => name ?? "").ToList();
                var current = GridMapWfcEditing.Path == null ? -1 : paths.FindIndex(path => string.Equals(path, GridMapWfcEditing.Path, StringComparison.OrdinalIgnoreCase));
                if (current < 0)
                {
                    names.Insert(0, $"{GridMapWfcEditing.Rules.Name} (not saved)");
                    paths.Insert(0, string.Empty);
                    current = 0;
                }

                var combo = new ComboBox().Options(names.ToArray());
                combo.SelectedIndex = current;
                combo.SelectionChanged += (_, index) =>
                {
                    if (index >= 0 && index < paths.Count && paths[index].Length > 0 && paths[index] != GridMapWfcEditing.Path)
                    {
                        if (!GridMapWfcEditing.TryOpen(paths[index], out var error))
                        {
                            GridMapWfcEditing.Status = $"Could not open: {error}";
                        }
                    }
                };
                combo.WithTooltip($"Rules files in {GridMapWfcGenerator.FolderName}/");
                return combo;
            });

            var save = new ManaUI.Button().Label("Save").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                try
                {
                    GridMapWfcEditing.Save();
                    GridMapWfcEditing.Status = $"Saved {System.IO.Path.GetFileName(GridMapWfcEditing.Path)}";
                    GridMapWfcEditing.Changed();
                    GridMapWfcEditing.Dirty = false;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    GridMapWfcEditing.Status = $"Could not save: {exception.Message}";
                }
            });
            save.BindPolled(button => button.Text, () => GridMapWfcEditing.Dirty ? "Save *" : "Save");
            save.WithTooltip("Save the rules to their file, named after them when new.");

            var presets = new ManaUI.Button().Label("New").Variant(ButtonVariant.Subtle);
            presets.Click += (_, _) =>
            {
                if (presets.Root is not { } root)
                {
                    return;
                }

                var items = GridMapWfcPresets.All
                    .Select(preset => new MenuItem($"From {preset.Name} preset", () => GridMapWfcEditing.Replace(preset.Create(), null)))
                    .Append(new MenuItem("Empty", () => GridMapWfcEditing.Replace(new GridMapWfcRules
                    {
                        Name = "New Rules",
                        Tiles = [new GridMapWfcTile { Name = "Empty", Rotations = GridMapWfcRotations.None, Weight = 4.0f }]
                    }, null)))
                    .ToList();
                MenuFlyout.ShowBelow(root, presets, items);
            };
            presets.WithTooltip("Start new rules, from a preset or empty. Save writes them as a new file.");

            return new Grid().Columns("*, Auto, Auto", theme.Spacing).Rows("Auto").Children(
                files.GridColumn(0), save.GridColumn(1), presets.GridColumn(2));
        }

        private static UiElement RulesSettings(UiTheme theme)
        {
            var name = new TextBox();
            name.BindPolled(box => box.Text, () => GridMapWfcEditing.Rules.Name);
            name.TextCommitted += (_, text) => Edit(() => GridMapWfcEditing.Rules.Name = text.Trim());
            name.WithTooltip("The rules' name, and the file Save writes when they are new");

            var boundary = new TextBox();
            boundary.BindPolled(box => box.Text, () => GridMapWfcEditing.Rules.Boundary);
            boundary.TextCommitted += (_, text) => Edit(() => GridMapWfcEditing.Rules.Boundary = text.Trim());
            boundary.WithTooltip("The label every side facing out of the area must have; empty lets anything face out");

            var joins = new TextBox();
            joins.BindPolled(box => box.Text, () => GridMapWfcEditing.Rules.Connects);
            joins.TextCommitted += (_, text) => Edit(() => GridMapWfcEditing.Rules.Connects = text.Trim());
            joins.WithTooltip("The label that joins two tiles into one dungeon, for keeping it in one piece");

            return new StackPanel().Vertical(2.0f).Children(
                Row(theme, "Name", name),
                new Grid().Columns("*, *", theme.Spacing).Rows("Auto").Children(
                    Row(theme, "Edge", boundary).GridColumn(0),
                    Row(theme, "Joins", joins).GridColumn(1)));
        }

        // ----- where to generate ------------------------------------------------------------------------

        private static UiElement AreaSettings(ManaGridMapNode gridMap, InspectorContext context)
        {
            var theme = context.Theme;

            var origin = new VectorBox().Components(2).Step(1.0f).Places(0);
            origin.SetAxes(("X", 0), ("Z", 2));
            origin.IsInteger = true;
            origin.BindPolled(box => box.Value2, () => new Vector2(GridMapWfcEditing.X, GridMapWfcEditing.Z));
            origin.ValueChanged += (_, value) =>
            {
                if (!origin.IsApplyingBinding)
                {
                    GridMapWfcEditing.X = (int)MathF.Round(value.X);
                    GridMapWfcEditing.Z = (int)MathF.Round(value.Y);
                }
            };
            origin.WithTooltip("The area's first cell. It is outlined in the viewport while this window is up.");

            var size = new VectorBox().Components(2).Step(1.0f).Places(0);
            size.SetAxes(("W", 0), ("D", 2));
            size.IsInteger = true;
            size.Minimum = 1.0f;
            size.Maximum = 96.0f;
            size.BindPolled(box => box.Value2, () => new Vector2(GridMapWfcEditing.Width, GridMapWfcEditing.Depth));
            size.ValueChanged += (_, value) =>
            {
                if (!size.IsApplyingBinding)
                {
                    GridMapWfcEditing.Width = Math.Clamp((int)MathF.Round(value.X), 1, 96);
                    GridMapWfcEditing.Depth = Math.Clamp((int)MathF.Round(value.Y), 1, 96);
                }
            };
            size.WithTooltip("Cells across and deep");

            var seed = new NumericBox { IsInteger = true, Step = 1.0f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) };
            seed.BindPolled(box => box.Value, () => GridMapWfcEditing.Seed);
            seed.ValueChanged += (_, value) =>
            {
                if (!seed.IsApplyingBinding)
                {
                    GridMapWfcEditing.Seed = (int)MathF.Round(value);
                }
            };
            seed.WithTooltip("The same seed lays the same dungeon");

            var keep = new CheckBox().Label("Keep painted").OnChanged(value => GridMapWfcEditing.KeepPainted = value);
            keep.BindPolled(box => box.IsChecked, () => GridMapWfcEditing.KeepPainted);
            keep.WithTooltip("Build round the rules' tiles already in the area -- paint a room first, generate the rest round it.");

            var connected = new CheckBox().Label("One piece").OnChanged(value => GridMapWfcEditing.ConnectedOnly = value);
            connected.BindPolled(box => box.IsChecked, () => GridMapWfcEditing.ConnectedOnly);
            connected.WithTooltip("Keep only the largest joined part, so there are no stray corridors.");

            var generate = new ManaUI.Button().Label("Generate").Variant(ButtonVariant.Primary)
                .OnClick(() => Generate(gridMap, context, reroll: false));
            generate.WithTooltip("Fill the area on the floor being painted, on the active layer. One undo step.");

            var reroll = new ManaUI.Button().Label("Re-roll").OnClick(() => Generate(gridMap, context, reroll: true));
            reroll.WithTooltip("A new seed, and generate again");

            var status = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Wrap();
            status.BindPolled(text => text.Text, () => GridMapWfcEditing.Status);

            return new StackPanel().Vertical(2.0f).Children(
                Row(theme, "From", origin),
                Row(theme, "Size", size),
                new Grid().Columns("*, Auto, Auto", theme.Spacing).Rows("Auto").Children(
                    Row(theme, "Seed", seed).GridColumn(0), keep.CenterV().GridColumn(1), connected.CenterV().GridColumn(2)),
                new StackPanel().Horizontal(theme.Spacing).Children(generate, reroll),
                status);
        }

        private static void Generate(ManaGridMapNode gridMap, InspectorContext context, bool reroll)
        {
            if (reroll)
            {
                GridMapWfcEditing.Seed = Random.Shared.Next(1, 100000);
            }

            var area = new GridMapWfcGenerator.Area(
                GridMapWfcEditing.X, GridMapWfcEditing.Z, GridMapWfcEditing.Width, GridMapWfcEditing.Depth, GridMapEditing.Floor);
            var before = gridMap.CaptureState();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var result = GridMapWfcGenerator.Generate(
                gridMap, GridMapWfcEditing.Rules, area, GridMapWfcEditing.Seed, GridMapWfcEditing.KeepPainted, GridMapWfcEditing.ConnectedOnly);
            if (!result.Succeeded)
            {
                gridMap.RestoreState(before);
                GridMapWfcEditing.Status = result.Error ?? "Could not generate.";
                return;
            }

            var after = gridMap.CaptureState();
            context.History?.Record("Generate", () => gridMap.RestoreState(before), () => gridMap.RestoreState(after));
            var rooms = result.Placements.Count(p => GridMapWfcEditing.Rules.Tiles[p.Tile] is { Width: > 1 } or { Depth: > 1 });
            GridMapWfcEditing.Status = $"{result.Placements.Count} tiles, {rooms} rooms. Seed {GridMapWfcEditing.Seed}, {clock.ElapsedMilliseconds} ms"
                + (result.Attempts > 1 ? $", {result.Attempts} tries." : ".");
        }

        // ----- the tiles --------------------------------------------------------------------------------

        private static UiElement TileList(UiTheme theme)
        {
            var rules = GridMapWfcEditing.Rules;
            var list = new StackPanel().Vertical(2.0f);
            var heading = new TextBlock().Text($"Tiles ({rules.Tiles.Count})").FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).CenterV();
            var add = new ManaUI.Button().Label("+ Tile").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                rules.Tiles.Add(new GridMapWfcTile
                {
                    Name = "Tile",
                    Asset = ManaUiDockDemo.MeshAssets.SelectedAsset is { Length: > 0 } selected ? selected : null,
                    Sockets = new() { ["e"] = ["open"], ["n"] = ["wall"], ["w"] = ["open"], ["s"] = ["wall"] }
                });
                GridMapWfcEditing.Changed();
            });
            add.WithTooltip("Add a tile, with the model selected in the asset browser");
            list.Children.Add(new Grid().Columns("*, Auto", theme.Spacing).Rows("Auto").Children(heading.GridColumn(0), add.GridColumn(1)));

            for (var i = 0; i < rules.Tiles.Count; i++)
            {
                list.Children.Add(TileEditor(theme, rules, i));
            }

            return list;
        }

        private static UiElement TileEditor(UiTheme theme, GridMapWfcRules rules, int index)
        {
            var tile = rules.Tiles[index];
            var expander = new Expander().Header(Summary(tile));
            expander.IsExpanded = false;
            expander.ContentPadding = new Thickness(6.0f, 2.0f, 0.0f, 4.0f);

            void Changed() => GridMapWfcEditing.Dirty = true;

            var name = new TextBox { Text = tile.Name };
            name.TextCommitted += (_, text) =>
            {
                tile.Name = string.IsNullOrWhiteSpace(text) ? tile.Name : text.Trim();
                expander.Header = Summary(tile);
                Changed();
            };

            var model = new TextBlock().Ellipsis().CenterV();
            model.BindPolled(text => text.Text, () => tile.Asset == null ? "none: lays nothing" : ManaGridMapNode.TileName(tile.Asset));
            model.BindPolled(text => text.Foreground, () => tile.Asset == null ? theme.TextMuted : theme.Text);
            var use = new ManaUI.Button().Label("Use selected").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                if (ManaUiDockDemo.MeshAssets.SelectedAsset is { Length: > 0 } selected)
                {
                    tile.Asset = selected;
                    Changed();
                }
            });
            use.BindPolled(button => button.IsEnabled, () => !string.IsNullOrWhiteSpace(ManaUiDockDemo.MeshAssets.SelectedAsset));
            use.WithTooltip("Use the model selected in the asset browser");
            var clear = new ManaUI.Button().Label("x").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                tile.Asset = null;
                Changed();
            });
            clear.WithTooltip("No model: the tile is empty space");

            var size = new VectorBox().Components(2).Step(1.0f).Places(0);
            size.SetAxes(("W", 0), ("D", 2));
            size.IsInteger = true;
            size.Minimum = 1.0f;
            size.Maximum = 16.0f;
            size.Value2 = new Vector2(tile.Width, tile.Depth);
            size.ValueChanged += (_, value) =>
            {
                tile.Size = [Math.Clamp((int)MathF.Round(value.X), 1, 16), Math.Clamp((int)MathF.Round(value.Y), 1, 16)];
                expander.Header = Summary(tile);
                Changed();
            };
            size.WithTooltip("Cells covered, as modelled");

            var weight = new NumericBox { Step = 0.1f, Minimum = 0.0f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) }.Places(2).Value(tile.Weight);
            weight.ValueChanged += (_, value) =>
            {
                tile.Weight = MathF.Max(0.0f, value);
                expander.Header = Summary(tile);
                Changed();
            };
            weight.WithTooltip("How often it is picked, against the others");

            var smooth = new NumericBox { Step = 0.01f, Minimum = 0.0f, MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) }.Places(2).Value(tile.Smooth);
            smooth.ValueChanged += (_, value) =>
            {
                tile.Smooth = MathF.Max(0.0f, value);
                Changed();
            };
            smooth.WithTooltip("Collision is flat below this height, in the model's units, so rubble does not catch feet");

            var turns = new ComboBox().Options(RotationNames);
            turns.SelectedIndex = Array.IndexOf(RotationValues, tile.Rotations);
            turns.SelectionChanged += (_, choice) =>
            {
                if (choice >= 0)
                {
                    tile.Rotations = RotationValues[choice];
                    Changed();
                }
            };

            var remove = new ManaUI.Button().Label("Remove").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                rules.Tiles.Remove(tile);
                GridMapWfcEditing.Changed();
            });

            return expander.Content(new StackPanel().Vertical(2.0f).Children(
                Row(theme, "Name", name),
                Row(theme, "Model", new Grid().Columns("*, Auto, Auto", 2.0f).Rows("Auto").Children(
                    model.GridColumn(0), use.GridColumn(1), clear.GridColumn(2))),
                Row(theme, "Size", size),
                new Grid().Columns("*, *", theme.Spacing).Rows("Auto").Children(
                    Row(theme, "Weight", weight).GridColumn(0), turns.GridColumn(1)),
                Row(theme, "Smooth", smooth),
                Sockets(theme, tile, Changed),
                remove));
        }

        private static string Summary(GridMapWfcTile tile) =>
            $"{tile.Name}  {tile.Width}x{tile.Depth}  x{tile.Weight:0.##}";

        /// <summary>
        /// The labels on each side, laid out as the sides are seen from above with -Z up. A side covering several
        /// cells takes one label per cell, separated by commas, in order of rising X or Z.
        /// </summary>
        private static UiElement Sockets(UiTheme theme, GridMapWfcTile tile, Action changed)
        {
            TextBox Side(int side, string tooltip)
            {
                var key = GridMapWfc.SideKeys[side];
                var box = new TextBox { Text = string.Join(",", tile.Sockets.TryGetValue(key, out var labels) ? labels : [GridMapWfc.DefaultSocket]) };
                box.TextCommitted += (_, text) =>
                {
                    tile.Sockets[key] = GridMapWfcPresets.Split(text);
                    changed();
                };
                box.WithTooltip(tooltip);
                return box;
            }

            var middle = new TextBlock().Text("sides").FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Center();
            return new Grid().Columns("*, Auto, *", 2.0f).Rows("Auto, Auto, Auto", 2.0f).Children(
                Side(1, "-Z side: one label per cell, left to right (rising X)").GridColumn(1).GridRow(0),
                Side(2, "-X side: one label per cell, top to bottom (rising Z)").GridColumn(0).GridRow(1),
                middle.GridColumn(1).GridRow(1),
                Side(0, "+X side: one label per cell, top to bottom (rising Z)").GridColumn(2).GridRow(1),
                Side(3, "+Z side: one label per cell, left to right (rising X)").GridColumn(1).GridRow(2));
        }

        private static void Edit(Action change)
        {
            change();
            GridMapWfcEditing.Dirty = true;
        }

        private static UiElement Row(UiTheme theme, string label, UiElement editor)
        {
            var name = new TextBlock().Text(label).Foreground(theme.TextMuted).CenterV();
            name.Width = 46.0f;
            return new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Children(name.GridColumn(0), editor.GridColumn(1));
        }
    }
}

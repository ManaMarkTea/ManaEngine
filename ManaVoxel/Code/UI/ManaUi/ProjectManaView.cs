using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mana;
using Mana.Code.Nodes;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Levels;
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    public static class ProjectManaView
    {
        public static UiElement Build(UiTheme theme, ManaWorld world, VoxelEditorViewModel editor)
        {
            var model = ProjectAssetViewModel.For(world, editor);
            var search = new TextBox { Placeholder = "Search project assets", Text = model.Search };
            search.TextChanged += (_, _) => model.Search = search.Text;
            var type = new ComboBox().Options(new[] { "All", "Prefab", "Grid Map Preset" });
            type.SelectedIndex = 0;
            type.SelectionChanged += (_, index) => model.Kind = index switch { 1 => "Prefab", 2 => "Grid Map Preset", _ => "All" };
            var name = new TextBox { Text = model.AssetName, Placeholder = "New asset name" };
            name.TextChanged += (_, _) => model.AssetName = name.Text;
            var status = new TextBlock().BindTextPolled(() => model.Status).Wrap().Foreground(theme.TextMuted);
            var folders = new DynamicContent().Showing<object>(() => model.Entries, _ => BuildFolders(theme, model));
            object? previousEntries = null;
            string? previousFilter = null;
            var listRevision = new object();
            object ListRevision()
            {
                var filter = $"{model.Search}|{model.Folder}|{model.Kind}";
                if (!ReferenceEquals(previousEntries, model.Entries) || previousFilter != filter)
                {
                    previousEntries = model.Entries;
                    previousFilter = filter;
                    listRevision = new object();
                }
                return listRevision;
            }
            var list = new DynamicContent().Showing<object>(ListRevision, _ => BuildList(theme, model));
            var edit = new DynamicContent().Showing<object>(() => model.Token, _ => BuildEditing(theme, model));
            return new Grid().Columns("210, *", theme.Spacing).Rows("*, Auto", theme.Spacing).Padding(8).Children(
                folders.GridColumn(0),
                new Grid().Rows("Auto, *, Auto", theme.Spacing).Columns("*").GridColumn(1).Children(
                    new StackPanel().Horizontal(theme.Spacing).Children(search.Width(200), type.Width(130),
                        new Button().Label("Refresh").OnClick(model.Refresh)),
                    list.Scrollable().GridRow(1),
                    new StackPanel().Vertical(4).GridRow(2).Children(edit,
                        new Expander { IsExpanded = false }.Header("Save Grid Map Preset").Content(
                            new StackPanel().Vertical(4).Children(name,
                                new CheckBox().Label("Include current generator settings").Checked(model.IncludeGenerator)
                                    .OnChanged(value => model.IncludeGenerator = value),
                                new Button().Label("Save Preset").OnClick(model.SaveGridPreset))))),
                status.GridRow(1).GridColumn(1));
        }

        private sealed class ContentFolder(string name, string path)
        {
            public string Name { get; } = name;
            public string Path { get; } = path;
            public List<ContentFolder> Children { get; } = [];
        }

        private static UiElement BuildFolders(UiTheme theme, ProjectAssetViewModel model)
        {
            var root = new ContentFolder("Content", "");
            foreach (var entry in model.Entries)
            {
                var parent = root;
                foreach (var segment in (Path.GetDirectoryName(entry.RelativePath) ?? "").Split(
                    new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var child = parent.Children.FirstOrDefault(f => f.Name == segment);
                    if (child == null)
                    {
                        child = new ContentFolder(segment, Path.Combine(parent.Path, segment));
                        parent.Children.Add(child);
                    }
                    parent = child;
                }
            }
            var tree = new TreeView
            {
                ItemsSource = new[] { root },
                ChildrenSelector = item => (item as ContentFolder)?.Children,
                HasChildrenSelector = item => item is ContentFolder folder && folder.Children.Count > 0,
                IsExpandedByDefault = _ => true,
                ItemTemplate = item => new TextBlock().Text(((ContentFolder)item).Name).Ellipsis()
            };
            tree.SelectionChanged += (_, item) => model.Folder = (item as ContentFolder)?.Path ?? "";
            return new Grid().Rows("Auto, *", theme.Spacing).Columns("*").Clip().Children(
                new Button().Label("All Assets").Variant(ButtonVariant.Subtle).OnClick(() => model.Folder = ""),
                tree.Scrollable().GridRow(1));
        }

        private static UiElement BuildList(UiTheme theme, ProjectAssetViewModel model)
        {
            var entries = model.Entries.Where(e => (model.Kind == "All" || e.Kind == model.Kind)
                && (model.Folder.Length == 0 || e.RelativePath.StartsWith(model.Folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                && e.RelativePath.Contains(model.Search, StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count == 0)
            {
                return new TextBlock().Text("No matching assets. Right-click a node in the Outliner and choose Make Prefab.").Wrap().Foreground(theme.TextMuted);
            }
            var tiles = new WrapPanel { Spacing = theme.Spacing };
            foreach (var entry in entries)
            {
                var preview = new UiImage
                {
                    Fit = UiImageFit.Contain, Width = 86, Height = 86,
                    SourceSize = new Vector2(MeshThumbnailCache.ThumbnailSize, MeshThumbnailCache.ThumbnailSize)
                };
                var badge = new TextBlock().Text(entry.Kind == "Prefab" ? "PREFAB" : "GRID").Foreground(theme.Accent).Center();
                var frame = new Border().Rounded(3).Content(new Grid().Rows("*, Auto", 2).Columns("*").Children(
                    new Grid().Rows("*").Columns("*").Children(preview.Center(), badge),
                    new TextBlock().Text(entry.Name).Ellipsis().CenterH().GridRow(1)));
                frame.BindPolled(b => b.BorderColor, () => model.SelectedAsset == entry.Path ? theme.Accent : UiColor.Transparent);
                frame.BindPolled(b => b.BorderThickness, () => model.SelectedAsset == entry.Path ? 2f : 0f);
                var row = new MeshAssetRow(new MeshAssetEntry(entry.Path, entry.Name, [entry.Path]))
                {
                    Child = frame, Width = 106, Height = 120,
                    CanDrag = () => entry.Kind == "Prefab" && model.CanEdit && !model.IsEditing,
                    ContextMenuItems = () => AssetMenu(model, entry)
                };
                row.Clicked += (_, _) => model.SelectedAsset = entry.Path;
                row.Tooltip = entry.RelativePath + (entry.Kind == "Prefab" ? " · Drag into the viewport. Right-click to place, edit or delete." : " · Right-click to apply or delete.");
                var sync = MeshImportManaView.Throttled<object?>(() =>
                {
                    if (entry.Kind == "Prefab" && MeshImportManaView.IsThumbnailRequestVisible(row))
                    {
                        if (row.Root is { } root) { MeshThumbnailImages.Touch(root, entry.Path); }
                        ManaUiDockDemo.MeshThumbnails?.Request(entry.Path);
                    }
                    var hasImage = MeshThumbnailImages.HasImage(entry.Path);
                    preview.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
                    badge.Visibility = hasImage ? Visibility.Collapsed : Visibility.Visible;
                    if (hasImage) { preview.SourceUv = MeshThumbnailImages.UvFor(entry.Path); }
                    return null;
                });
                frame.BindPolled(b => b.Tag, () => sync());
                tiles.Children.Add(row);
            }
            return tiles;
        }

        public static List<MenuItem> AssetMenu(ProjectAssetViewModel model, ManaContentEntry entry)
        {
            var items = new List<MenuItem>();
            if (entry.Kind == "Prefab")
            {
                items.Add(new MenuItem("Place", () => model.RequestPlacement(entry.Path)).EnabledWhen(() => model.CanEdit && !model.IsEditing));
                items.Add(new MenuItem("Edit Prefab", () => model.OpenPrefab(entry.Path)).EnabledWhen(() => model.CanEdit && !model.IsEditing));
                items.Add(new MenuItem("Create Variant", () => model.CreateVariant(entry.Path)).EnabledWhen(() => model.CanEdit && !model.IsEditing)
                    .WithTooltip("A prefab made from this one that keeps only what you change in it -- a blue lamp from a lamp."));
                items.Add(new MenuItem("Refresh Thumbnail", () => ManaUiDockDemo.MeshThumbnails?.ForceRefresh(entry.Path)));
            }
            else
            {
                items.Add(new MenuItem("Apply to Grid Map", () => model.ApplyGridPreset(entry.Path)).EnabledWhen(() => model.CanEdit));
            }
            items.Add(MenuItem.Separator());
            items.Add(new MenuItem("Delete", () => model.DeleteAsset(entry.Path)).EnabledWhen(() => model.CanEdit));
            return items;
        }

        private static UiElement BuildEditing(UiTheme theme, ProjectAssetViewModel model)
        {
            if (!model.IsEditing)
            {
                return new TextBlock().Text("Edit a prefab to expose properties and update its instances.").Wrap().Foreground(theme.TextMuted);
            }
            return new StackPanel().Vertical(theme.Spacing).Children(
                new TextBlock().Text($"Editing: {model.EditingName}").Foreground(theme.Accent),
                new StackPanel().Horizontal(4).Children(
                    new Button().Label("Save").OnClick(() => model.SavePrefab()),
                    new Button().Label("Save & Close").OnClick(() => model.SavePrefab(true)),
                    new Button().Label("Discard").OnClick(model.DiscardEditing)),
                new TextBlock().Text("Select a node to expose its properties in the Inspector. New nodes must be children of the prefab root.").Wrap());
        }

        public static UiElement BuildInspector(UiTheme theme, ManaSceneNode node)
        {
            var model = ProjectAssetViewModel.For(node.Context.World);
            var revision = new object();
            object? lastToken = null;
            string? lastOverrides = null;
            long nextPoll = 0;
            object Poll()
            {
                if (!ReferenceEquals(lastToken, model.Token))
                {
                    lastToken = model.Token;
                    revision = new object();
                    nextPoll = 0;
                }
                if (Environment.TickCount64 >= nextPoll)
                {
                    nextPoll = Environment.TickCount64 + 300;
                    var instance = ManaPrefabRuntime.InstanceRoot(node);
                    string? snapshot;
                    try
                    {
                        snapshot = instance == null ? null : JsonSerializer.Serialize(ManaPrefabRuntime.CaptureReference(instance));
                    }
                    catch (Exception exception) { snapshot = exception.Message; }
                    if (lastOverrides != snapshot)
                    {
                        lastOverrides = snapshot;
                        revision = new object();
                    }
                }
                return revision;
            }
            return new DynamicContent().Showing<object>(Poll, _ =>
            {
                var instance = ManaPrefabRuntime.InstanceRoot(node);
                if (instance != null)
                {
                    return BuildInstance(theme, model, instance);
                }
                return model.IsEditing ? BuildExposure(theme, model) : null;
            });
        }

        private static UiElement BuildInstance(UiTheme theme, ProjectAssetViewModel model, ManaSceneNode instance)
        {
            var panel = new StackPanel().Vertical(theme.Spacing);
            try
            {
                var reference = ManaPrefabRuntime.CaptureReference(instance);
                var path = ManaContentLibrary.ResolvePrefab(reference.AssetId, reference.AssetPath);
                var asset = ManaPrefab.Load(path);
                panel.Children.Add(new TextBlock().Text($"{asset.Name} · {reference.Overrides.Count} overrides").Foreground(theme.Accent));
                panel.Children.Add(new StackPanel().Horizontal(4).Children(
                    new Button().Label("Edit Source").OnClick(() => model.OpenPrefab(path)),
                    new Button().Label("Refresh").OnClick(model.RefreshSelected),
                    new Button().Label("Unpack").OnClick(() => model.Unpack(instance))));
                panel.Children.Add(new StackPanel().Horizontal(4).Children(
                    new Button().Label("Revert All").OnClick(() => model.Revert(instance)),
                    new Button().Label("Apply to Prefab").OnClick(() => model.ApplyToSource(instance))));
                if (instance.Children.Count == 1)
                {
                    var root = ManaLevelCapture.CaptureSubtree(instance.Children[0]);
                    foreach (var property in asset.Properties)
                    {
                        var value = ProjectAssetViewModel.ReadProperty(root, property);
                        var overridden = reference.Overrides.Any(o => o.Path.SequenceEqual(ProjectAssetViewModel.PropertyPath(property)));
                        if (value == null)
                        {
                            panel.Children.Add(new TextBlock().Text($"{property.Name}: its target is missing.").Foreground(theme.Warning));
                            continue;
                        }
                        var captured = property;
                        var input = PropertyEditor(value, property, text => model.SetProperty(instance, captured, text));
                        panel.Children.Add(OverrideRow(theme, property.Name, input, overridden, () => model.Revert(instance, captured)));
                    }
                }

                // Every other change this instance makes, each with the same arrow back to the prefab's value.
                var exposed = asset.Properties.Select(ProjectAssetViewModel.PropertyPath).ToList();
                foreach (var change in reference.Overrides.Where(o => !exposed.Any(path => path.SequenceEqual(o.Path))))
                {
                    var captured = change;
                    panel.Children.Add(OverrideRow(theme, OverrideLabel(asset, change),
                        new TextBlock().Text(OverrideValue(change)).Ellipsis().Foreground(theme.TextMuted),
                        true, () => model.RevertOverride(instance, captured)));
                }
            }
            catch (Exception exception)
            {
                panel.Children.Add(new TextBlock().Text(exception.Message).Foreground(theme.Warning).Wrap());
                panel.Children.Add(new Button().Label("Retry Source").OnClick(model.RefreshSelected));
                panel.Children.Add(new Button().Label("Revert All").OnClick(() => model.Revert(instance)));
            }
            panel.Children.Add(new TextBlock().BindTextPolled(() => model.Status).Wrap().Foreground(theme.TextMuted));
            return new Expander().Header("Prefab Instance").Content(panel);
        }

        /// <summary>
        /// A value row marked the way the Inspector marks them: an accent bar and a revert arrow while it differs
        /// from the prefab, nothing extra while it does not.
        /// </summary>
        private static UiElement OverrideRow(UiTheme theme, string label, UiElement editor, bool overridden, Action revert)
        {
            var bar = new Border().Fill(overridden ? theme.Accent : UiColor.Transparent).Width(2.0f).GridColumn(0);
            var name = new TextBlock().Text(label).Ellipsis().Width(110.0f).CenterV().GridColumn(1)
                .Foreground(overridden ? theme.Text : theme.TextMuted);
            var row = new Grid().Columns("Auto, Auto, *, Auto", 4.0f).Rows("Auto").Children(bar, name, editor.GridColumn(2));
            if (overridden)
            {
                row.Children.Add(ToolbarIcons.ActionButton(theme, ToolbarIcons.DrawUndo, "Revert to the prefab's value.", revert).GridColumn(3));
            }
            return row;
        }

        private static string OverrideValue(ManaPrefabOverride change) => change.Remove ? "removed"
            : change.Value == null ? "" : Display(change.Value);

        private static string OverrideLabel(ManaPrefab asset, ManaPrefabOverride change)
        {
            var node = ManaPrefab.Walk(asset.Root).FirstOrDefault(n => n.Id == change.Path.FirstOrDefault());
            var field = change.Path.LastOrDefault() ?? "Property";
            return change.Path.Length == 1 ? (change.Remove ? "Removed node" : "Added node")
                : $"{node?.Name ?? "Missing node"} / {field}";
        }

        private static string Display(JsonNode value) => value is JsonArray array
            ? string.Join(", ", array.Select(v => v?.ToString() ?? ""))
            : value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToString();

        /// <summary>An editor for an exposed value that sets it on the instance when an edit is finished.</summary>
        private static UiElement PropertyEditor(JsonNode value, ManaPrefabProperty property, Action<string> commit)
        {
            static string Join(Vector4 v, int count) => string.Join(",", new[] { v.X, v.Y, v.Z, v.W }
                .Take(count).Select(n => n.ToString("R", CultureInfo.InvariantCulture)));

            if (value is JsonArray { Count: >= 2 and <= 4 } array)
            {
                var numbers = array.Select(v => v!.GetValue<float>()).Concat(new float[4]).Take(4).ToArray();
                var vector = new Vector4(numbers[0], numbers[1], numbers[2], numbers[3]);
                if (array.Count == 4 && property.Property.Contains("col", StringComparison.OrdinalIgnoreCase))
                {
                    var colour = new ColorField { Color = vector };
                    colour.ColorCommitted += (_, picked) => commit(Join(picked, 4));
                    return colour;
                }
                var box = new VectorBox().Components(array.Count).Step(0.1f).Places(3);
                box.Vector = vector;
                box.ValueCommitted += (_, entered) => commit(Join(entered, array.Count));
                return box;
            }
            if (value.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
            {
                var checkbox = new CheckBox().Checked(value.GetValue<bool>());
                checkbox.CheckedChanged += (_, on) => commit(on.ToString());
                return checkbox;
            }
            if (value.GetValueKind() == JsonValueKind.Number)
            {
                var numeric = new NumericBox
                {
                    Minimum = (float)(property.Minimum ?? float.NegativeInfinity),
                    Maximum = (float)(property.Maximum ?? float.PositiveInfinity),
                    Value = value.GetValue<float>(), Step = 0.1f
                };
                numeric.ValueCommitted += (_, entered) => commit(entered.ToString("R", CultureInfo.InvariantCulture));
                return numeric;
            }
            var text = new TextBox { Text = Display(value) };
            text.TextCommitted += (_, entered) => commit(entered);
            return text;
        }

        private static UiElement BuildExposure(UiTheme theme, ProjectAssetViewModel model)
        {
            var choices = model.PropertyChoices();
            var combo = new ComboBox();
            combo.SetItems(choices, c => c.Label);
            combo.SelectedIndex = choices.Count > 0 ? 0 : -1;
            var name = new TextBox { Placeholder = "Exposed name" };
            var minimum = new TextBox { Placeholder = "Minimum (optional)" };
            var maximum = new TextBox { Placeholder = "Maximum (optional)" };
            double? Bound(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : double.Parse(box.Text, CultureInfo.InvariantCulture);
            var panel = new StackPanel().Vertical(theme.Spacing).Children(combo, name, minimum, maximum,
                new Button().Label("Expose Property").OnClick(() => model.Run(() =>
                {
                    if (combo.SelectedIndex >= 0)
                    {
                        model.Expose(choices[combo.SelectedIndex], name.Text, Bound(minimum), Bound(maximum));
                    }
                })));
            foreach (var property in model.EditingAsset?.Properties ?? [])
            {
                panel.Children.Add(new StackPanel().Horizontal(4).Children(
                    new TextBlock().Text(property.Name).Width(180),
                    new Button().Label("Remove").OnClick(() => model.Run(() => model.EditingAsset?.Properties.Remove(property)))));
            }
            panel.Children.Add(new TextBlock().BindTextPolled(() => model.Status).Wrap());
            return new Expander().Header("Expose Prefab Properties").Content(panel);
        }

    }
}

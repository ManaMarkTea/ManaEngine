using System.Text.Json;
using System.Text.Json.Nodes;
using Mana;
using ManaUI;
using ManaVoxel.Code.Assets;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// Review Changes, from a prefab instance's Actions menu: every way the instance differs from its prefab,
    /// the prefab's value beside this copy's, each with a tick. Apply sends the ticked ones to the prefab, so
    /// every copy gets them; Revert puts them back on this copy. What is left unticked stays as it is.
    /// </summary>
    public static class PrefabChangesManaView
    {
        public const string PanelId = "prefab-changes";

        public static UiElement Build(UiTheme theme, ManaWorld world)
        {
            var model = ProjectAssetViewModel.For(world);
            object? lastToken = null;
            string? lastSnapshot = null;
            var revision = new object();
            long nextPoll = 0;

            // Rebuilt when the model acts, or -- checked a few times a second -- when the instance's changes do.
            object Poll()
            {
                if (!ReferenceEquals(lastToken, model.Token))
                {
                    lastToken = model.Token;
                    revision = new object();
                }
                if (Environment.TickCount64 >= nextPoll)
                {
                    nextPoll = Environment.TickCount64 + 300;
                    string? snapshot;
                    try
                    {
                        snapshot = model.CurrentReview is { } reviewed ? JsonSerializer.Serialize(ManaPrefabRuntime.CaptureReference(reviewed)) : null;
                    }
                    catch (Exception exception) when (exception is InvalidDataException or JsonException)
                    {
                        snapshot = exception.Message;
                    }
                    if (snapshot != lastSnapshot)
                    {
                        lastSnapshot = snapshot;
                        revision = new object();
                    }
                }
                return revision;
            }

            return new Grid().Rows("*").Columns("*").Padding(8.0f).Children(
                new DynamicContent().Showing<object>(Poll, _ => BuildChanges(theme, model)));
        }

        private static UiElement BuildChanges(UiTheme theme, ProjectAssetViewModel model)
        {
            var instance = model.CurrentReview;
            if (instance?.PrefabLink == null)
            {
                return Muted(theme, "Select a prefab instance and choose Review Changes from its Actions menu.");
            }

            ManaPrefabReference reference;
            ManaPrefab asset;
            try
            {
                reference = ManaPrefabRuntime.CaptureReference(instance);
                asset = ManaPrefab.Load(ManaContentLibrary.ResolvePrefab(reference.AssetId, reference.AssetPath));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
            {
                return new TextBlock().Text(exception.Message).Foreground(theme.Warning).Wrap();
            }

            var title = new TextBlock()
                .Text($"{instance.Name} · {reference.Overrides.Count} change{(reference.Overrides.Count == 1 ? "" : "s")} from {asset.Name}")
                .FontFamily(theme.FontFamilyMedium);
            if (reference.Overrides.Count == 0)
            {
                return new StackPanel().Vertical(theme.Spacing).Children(title, Muted(theme, "This copy matches its prefab."));
            }

            var source = ManaPrefabOverrides.Flatten(asset.Root);
            var ticks = new List<(CheckBox Tick, ManaPrefabOverride Change)>();
            var rows = new StackPanel().Vertical(2.0f);
            foreach (var change in reference.Overrides)
            {
                var tick = new CheckBox { IsChecked = true };
                ticks.Add((tick, change));
                rows.Children.Add(new Grid().Columns("Auto, 150, *", 6.0f).Rows("Auto").Children(
                    tick.CenterV().GridColumn(0),
                    new TextBlock().Text(ProjectManaView.OverrideLabel(asset, change)).Ellipsis().CenterV().GridColumn(1),
                    new StackPanel().Horizontal(6.0f).CenterV().GridColumn(2).Children(
                        Value(theme, ValueAt(source, change.Path), muted: true),
                        new TextBlock().Text("→").Foreground(theme.TextMuted).CenterV(),
                        change.Remove ? Muted(theme, "removed") : Value(theme, change.Value, muted: false))));
            }

            List<ManaPrefabOverride> Chosen() => ticks.Where(t => t.Tick.IsChecked).Select(t => t.Change).ToList();

            var apply = new Button().Label("Apply to Prefab").Variant(ButtonVariant.Primary)
                .WithTooltip($"Change {asset.Name} itself, so every placed copy gets these.")
                .OnClick(() => model.ApplyOverrides(instance, Chosen()));
            apply.BindPolled(b => b.IsEnabled, () => ticks.Any(t => t.Tick.IsChecked));
            var revert = new Button().Label("Revert")
                .WithTooltip($"Put these back to {asset.Name}'s values on this copy only.")
                .OnClick(() => model.RevertOverrides(instance, Chosen()));
            revert.BindPolled(b => b.IsEnabled, () => ticks.Any(t => t.Tick.IsChecked));

            var all = new Button().Label("All").Variant(ButtonVariant.Subtle).OnClick(() => ticks.ForEach(t => t.Tick.IsChecked = true));
            var none = new Button().Label("None").Variant(ButtonVariant.Subtle).OnClick(() => ticks.ForEach(t => t.Tick.IsChecked = false));

            return new Grid().Rows("Auto, *, Auto", theme.Spacing).Columns("*").Children(
                title,
                rows.Scrollable().GridRow(1),
                new Grid().Columns("Auto, Auto, *, Auto, Auto", 4.0f).Rows("Auto").GridRow(2).Children(
                    all.GridColumn(0), none.GridColumn(1), revert.GridColumn(3), apply.GridColumn(4)));
        }

        /// <summary>A value as it reads best: a colour as a swatch, anything else as text.</summary>
        private static UiElement Value(UiTheme theme, JsonNode? value, bool muted)
        {
            if (value is JsonArray { Count: 4 } array && array.All(v => v is JsonValue number && number.TryGetValue<float>(out _)))
            {
                var c = array.Select(v => v!.GetValue<float>()).ToArray();
                if (c.All(x => x is >= 0.0f and <= 1.0f))
                {
                    return new Border().Fill(new Vector4(c[0], c[1], c[2], 1.0f)).Rounded(3.0f).Width(28.0f).Height(16.0f).CenterV();
                }
            }

            var text = value == null ? "none" : ProjectManaView.Display(value);
            return new TextBlock().Text(text).Ellipsis().Foreground(muted ? theme.TextMuted : theme.Text).CenterV();
        }

        private static JsonNode? ValueAt(JsonObject root, string[] path)
        {
            JsonNode? target = root;
            foreach (var segment in path)
            {
                target = (target as JsonObject)?[segment];
            }
            return target;
        }

        private static TextBlock Muted(UiTheme theme, string text) => new TextBlock().Text(text).Foreground(theme.TextMuted).Wrap();
    }
}

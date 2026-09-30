using System.Text.Json;
using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaVoxel.Code.Assets;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// Unity's override marks: an inspector row whose value a prefab instance -- or the variant being edited --
    /// changes from its source gets an accent bar and a revert arrow that puts the source's value back.
    /// </summary>
    public static class PrefabOverrideMarks
    {
        // One subject at a time -- the selection -- re-diffed a few times a second rather than every row every frame.
        private static object? _key;
        private static long _nextRefresh;
        private static List<ManaPrefabOverride> _overrides = [];

        /// <summary>The saved path of a node field (<paramref name="component"/> null) or a script field.</summary>
        public static string[] PathOf(ManaSceneNode node, ManaComponent? component, string field) => component == null
            ? [node.AuthoredId ?? string.Empty, field]
            : [node.AuthoredId ?? string.Empty, "scripts", component.AuthoredId ?? string.Empty, "properties", field];

        public static ManaPrefabOverride? Find(ManaSceneNode node, ManaComponent? component, string field)
        {
            // An instance's own transform is where it was placed, not a change to the prefab.
            if (node.PrefabLink != null || string.IsNullOrEmpty(node.AuthoredId)
                || (component != null && string.IsNullOrEmpty(component.AuthoredId)))
            {
                return null;
            }

            var path = PathOf(node, component, field);
            return Overrides(node).FirstOrDefault(o => o.Path.Length >= path.Length && o.Path.Take(path.Length).SequenceEqual(path));
        }

        public static void Revert(ManaSceneNode node, ManaPrefabOverride change)
        {
            var model = ProjectAssetViewModel.For(node.Context.World);
            if (ManaPrefabRuntime.InstanceRoot(node) is { } instance)
            {
                model.RevertOverride(instance, change, node.AuthoredId);
            }
            else
            {
                model.RevertVariantOverride(change, node.AuthoredId);
            }

            _key = null;
        }

        /// <summary>A row, marked while its field is overridden.</summary>
        public static UiElement Mark(InspectorContext context, UiElement row, ManaSceneNode node, ManaComponent? component, string field)
        {
            var theme = context.Theme;
            var bar = new Border().Fill(theme.Accent).Width(2.0f).GridColumn(0);
            bar.BindPolled(b => b.Visibility, () => Find(node, component, field) != null ? Visibility.Visible : Visibility.Hidden);

            var revert = ToolbarIcons.ActionButton(theme, ToolbarIcons.DrawUndo, "Revert to the prefab's value.", () =>
            {
                if (Find(node, component, field) is { } change)
                {
                    Revert(node, change);
                }
            }).GridColumn(2);
            revert.BindPolled(b => b.Visibility, () => Find(node, component, field) != null ? Visibility.Visible : Visibility.Collapsed);

            return new Grid().Columns("Auto, *, Auto", 3.0f).Rows("Auto").Children(bar, row.GridColumn(1), revert);
        }

        private static List<ManaPrefabOverride> Overrides(ManaSceneNode node)
        {
            var instance = ManaPrefabRuntime.InstanceRoot(node);
            var model = ProjectAssetViewModel.For(node.Context.World);
            object? key = (object?)instance ?? (model.EditingAsset?.IsVariant == true ? model.EditingAsset : null);
            if (key == null)
            {
                return [];
            }

            if (!ReferenceEquals(key, _key) || Environment.TickCount64 >= _nextRefresh)
            {
                _key = key;
                _nextRefresh = Environment.TickCount64 + 250;
                try
                {
                    _overrides = instance != null ? ManaPrefabRuntime.CaptureReference(instance).Overrides : model.VariantOverrides();
                }
                catch (Exception exception) when (exception is InvalidDataException or JsonException or InvalidOperationException)
                {
                    _overrides = [];
                }
            }

            return _overrides;
        }
    }
}

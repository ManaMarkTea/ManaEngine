using Mana;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using ManaVoxel.Code.Levels;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>Level-file metadata and basic counts for a selected scene.</summary>
    public sealed class SceneWidget : InspectorWidget<ManaScene>
    {
        protected override UiElement Build(ManaScene scene, InspectorContext context)
        {
            var theme = context.Theme;

            return new Expander()
                .Header("Scene")
                .Section(theme.Spacing,
                    InspectorControls.Row(context, "Name", new TextBox()
                        .Value(scene.Name ?? string.Empty)
                        .Hint("Scene")
                        .OnCommitted(text => scene.Name = text)),

                    InspectorControls.Row(context, "File", InspectorControls.ReadOnly(
                        theme,
                        string.IsNullOrWhiteSpace(scene.LevelPath) ? "Not level-backed" : scene.LevelPath)),

                    InspectorControls.Row(context, "Filename", InspectorControls.ReadOnly(
                        theme,
                        string.IsNullOrWhiteSpace(scene.LevelPath) ? "None" : Path.GetFileName(scene.LevelPath))),

                    InspectorControls.Row(context, "Saved state", SavedState(theme, scene, context.Engine?.World)),

                    InspectorControls.Row(context, "Top-level nodes", InspectorControls.ReadOnly(
                        theme,
                        scene.Children.Count.ToString("N0"))),

                    InspectorControls.Row(context, "Authored objects", InspectorControls.ReadOnly(
                        theme,
                        CountAuthored(scene).ToString("N0"))));
        }

        private static UiElement SavedState(UiTheme theme, ManaScene scene, ManaWorld? world)
        {
            var text = new TextBlock()
                .Text(SceneState(scene, world))
                .Foreground(theme.Text)
                .Ellipsis()
                .CenterV();

            text.BindPolled(t => t.Text, () => SceneState(scene, world));
            return text;
        }

        private static string SceneState(ManaScene scene, ManaWorld? world)
        {
            if (string.IsNullOrWhiteSpace(scene.LevelPath))
            {
                return "Runtime scene";
            }

            if (world == null || string.IsNullOrWhiteSpace(scene.SavedLevelContentJson))
            {
                return "Level-backed";
            }

            return HasUnsavedChanges(scene, world) ? "Unsaved changes" : "Saved";
        }

        private static bool HasUnsavedChanges(ManaScene scene, ManaWorld world)
        {
            // Cached for half a second: this is polled every frame and capturing the scene is not cheap.
            return SceneDirtyCache.IsDirty(scene, () => !string.Equals(
                scene.SavedLevelContentJson,
                ManaLevelCapture.CaptureSceneContentJson(world, scene, SceneLevelName(scene)),
                StringComparison.Ordinal));
        }

        private static string SceneLevelName(ManaScene scene)
        {
            if (!string.IsNullOrWhiteSpace(scene.Name))
            {
                return scene.Name;
            }

            return string.IsNullOrWhiteSpace(scene.LevelPath)
                ? "Scene"
                : ManaLevelLibrary.DisplayName(scene.LevelPath);
        }

        private static int CountAuthored(ManaScene scene)
        {
            var count = 0;
            foreach (var child in scene.Children)
            {
                count += CountAuthored(child);
            }

            return count;
        }

        private static int CountAuthored(ManaSceneNode node)
        {
            var count = node.LevelSource != null ? 1 : 0;
            foreach (var child in node.Children)
            {
                count += CountAuthored(child);
            }

            return count;
        }
    }
}

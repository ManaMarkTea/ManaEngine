using Mana;
using ManaVoxel.Code.Levels;
using ManaUI;

using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The editor's everyday strip: level file actions, play-test actions, then the voxel tools.
    /// The old Level panel still exists for detail work, but opening and playing a level should not require
    /// finding a tab in the dock.
    /// </summary>
    public static class VoxelEditorToolbarManaView
    {
        public static UiElement Build(UiTheme theme, VoxelEditorViewModel editor, ManaWorld? world)
        {
            return new Grid()
                .Columns("Auto, Auto, Auto, Auto, Auto, Auto, Auto, Auto, *", theme.Spacing)
                .Rows("Auto")
                .Padding(6.0f, 3.0f)
                .Children(
                    BuildLevelGroup(theme, editor.Levels, world).GridColumn(0),
                    Divider(theme).GridColumn(1),
                    BuildHistoryGroup(editor).GridColumn(2),
                    Divider(theme).GridColumn(3),
                    BuildGizmoGroup(theme, editor, world).GridColumn(4),
                    Divider(theme).GridColumn(5),
                    BuildPlayGroup(theme, editor.Levels).GridColumn(6),
                    Divider(theme).GridColumn(7),
                    VoxelToolPaletteManaView.Build(theme, editor).GridColumn(8));
        }

        private static UiElement BuildLevelGroup(UiTheme theme, LevelViewModel levels, ManaWorld? world)
        {
            var name = new TextBox { Text = levels.LevelName, Width = 160.0f };
            name.TextChanged += (_, _) =>
            {
                if (!name.IsApplyingBinding)
                {
                    levels.LevelName = name.Text;
                }
            };
            name.BindPolled(box => box.Text, () => levels.LevelName);

            var save = new Button()
                .Variant(ButtonVariant.Primary)
                .WithTooltip("Save the current level. Rename the field to save as a new file.")
                .OnClick(levels.Save);
            save.BindPolled(button => button.Text, () => CurrentSceneHasChanges(levels, world) ? "Save *" : "Save");

            var open = FlyoutButton("Open", "Open a saved level.", button =>
            {
                levels.Refresh();
                return BuildOpenMenu(levels);
            });

            var current = new TextBlock()
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextMuted)
                .Ellipsis()
                .CenterV()
                .Width(170.0f);
            current.BindTextPolled(() => CurrentLevelText(levels, world));

            return new StackPanel()
                .Horizontal(theme.Spacing)
                .Children(
                    open,
                    new Button().Label("New").WithTooltip("Start a new unsaved level.").OnClick(levels.New),
                    name,
                    save,
                    new Button().Label("Reload").WithTooltip("Reload the open level from disk.").OnClick(levels.Reload)
                        .BindPolled(button => button.IsEnabled, () => levels.CurrentPath != null),
                    current);
        }

        private static UiElement BuildPlayGroup(UiTheme theme, LevelViewModel levels)
        {
            var play = new Button()
                .Variant(ButtonVariant.Primary)
                .OnClick(levels.TogglePlay)
                .WithTooltip("Play or stop the current level. F5 toggles, Esc stops.");
            play.BindPolled(button => button.Text, () => levels.PlayButtonText);
            play.BindPolled(button => button.IsEnabled, () => levels.CanPlay);

            var more = FlyoutButton("Play...", "Choose how to start play mode.", _ => BuildPlayMenu(levels));
            more.BindPolled(button => button.IsEnabled, () => levels.CanPlay);

            var status = new TextBlock()
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextMuted)
                .Ellipsis()
                .CenterV()
                .Width(210.0f);
            status.BindTextPolled(() => levels.Status);

            return new StackPanel()
                .Horizontal(theme.Spacing)
                .Children(play, more, status);
        }

        private static UiElement BuildGizmoGroup(UiTheme theme, VoxelEditorViewModel editor, ManaWorld? world)
        {
            return new StackPanel()
                .Horizontal(2.0f)
                .Children(
                    GizmoModeButton("Move", "Translate along an axis.", SceneTransformGizmoMode.Translate, editor, world),
                    GizmoModeButton("Rotate", "Rotate around an axis.", SceneTransformGizmoMode.Rotate, editor, world),
                    GizmoModeButton("Scale", "Scale along an axis.", SceneTransformGizmoMode.Scale, editor, world));
        }

        private static UiElement BuildHistoryGroup(VoxelEditorViewModel editor)
        {
            var undo = new Button()
                .Label("Undo")
                .Variant(ButtonVariant.Subtle)
                .WithTooltip("Undo the last editor action. Ctrl+Z.")
                .OnClick(() => editor.History.Undo());
            undo.BindPolled(button => button.IsEnabled, () => editor.History.CanUndo);

            var redo = new Button()
                .Label("Redo")
                .Variant(ButtonVariant.Subtle)
                .WithTooltip("Redo the last undone action. Ctrl+Y or Ctrl+Shift+Z.")
                .OnClick(() => editor.History.Redo());
            redo.BindPolled(button => button.IsEnabled, () => editor.History.CanRedo);

            return new StackPanel()
                .Horizontal(2.0f)
                .Children(undo, redo);
        }

        private static Button GizmoModeButton(
            string label,
            string tooltip,
            SceneTransformGizmoMode mode,
            VoxelEditorViewModel editor,
            ManaWorld? world)
        {
            var button = new Button()
                .Label(label)
                .WithTooltip(tooltip)
                .OnClick(() =>
                {
                    editor.TransformGizmoMode = mode;
                    editor.ShowTransformGizmo = true;
                    editor.IsSelectToolActive = true;
                });

            button.BindPolled(
                item => item.Variant,
                () => editor.ShowTransformGizmo && editor.TransformGizmoMode == mode
                    ? ButtonVariant.Primary
                    : ButtonVariant.Subtle);
            button.BindPolled(
                item => item.IsEnabled,
                () => world?.Selection.SelectedNode != null);
            return button;
        }

        private static Button FlyoutButton(
            string label,
            string tooltip,
            Func<Button, IReadOnlyList<MenuItem>> buildItems)
        {
            var button = new Button().Label(label).WithTooltip(tooltip);
            button.Click += (_, _) =>
            {
                var root = button.Root;
                if (root != null)
                {
                    MenuFlyout.ShowBelow(root, button, buildItems(button));
                }
            };

            return button;
        }

        private static IReadOnlyList<MenuItem> BuildOpenMenu(LevelViewModel levels)
        {
            var items = new List<MenuItem>
            {
                new MenuItem("New Level", levels.New),
                new MenuItem("Reload Current", levels.Reload)
                    .EnabledWhen(() => levels.CurrentPath != null),
                new MenuItem("Refresh List", levels.Refresh),
                MenuItem.Separator()
            };

            var paths = levels.Levels;
            if (paths.Count == 0)
            {
                items.Add(MenuItem.Label($"No levels in {ManaLevelLibrary.FolderName}."));
                return items;
            }

            foreach (var path in paths)
            {
                var captured = path;
                items.Add(new MenuItem(ManaLevelLibrary.DisplayName(captured), () => levels.Open(captured))
                    .WithTooltip(captured)
                    .Checkable(() => string.Equals(levels.CurrentPath, captured, StringComparison.OrdinalIgnoreCase)));
            }

            return items;
        }

        private static IReadOnlyList<MenuItem> BuildPlayMenu(LevelViewModel levels)
        {
            return new[]
            {
                new MenuItem("Play", levels.TogglePlay)
                    .WithShortcut("F5")
                    .EnabledWhen(() => levels.CanPlay && !levels.IsPlaying),
                new MenuItem("Play From Camera", levels.PlayFromCamera)
                    .WithShortcut("Shift+F5")
                    .EnabledWhen(() => levels.CanPlay && !levels.IsPlaying),
                new MenuItem("Play In New Window", levels.PlayInNewWindow)
                    .WithShortcut("Ctrl+F5")
                    .EnabledWhen(() => levels.CanPlay),
                new MenuItem("Stop", levels.TogglePlay)
                    .WithShortcut("Esc")
                    .EnabledWhen(() => levels.IsPlaying),
                MenuItem.Separator(),
                new MenuItem("Place Player Start Here", levels.PlacePlayerStart)
                    .ShortcutFrom(() => levels.HasPlayerStart ? "moves existing" : string.Empty),
                MenuItem.Label(string.Empty).ShortcutFrom(() => levels.Status)
            };
        }

        private static string CurrentLevelText(LevelViewModel levels, ManaWorld? world)
        {
            var suffix = CurrentSceneHasChanges(levels, world) ? " *" : string.Empty;
            return $"Level: {levels.CurrentLevelLabel}{suffix}";
        }

        private static bool CurrentSceneHasChanges(LevelViewModel levels, ManaWorld? world)
        {
            var path = levels.CurrentPath;
            if (string.IsNullOrWhiteSpace(path) || world == null)
            {
                return false;
            }

            lock (world.SceneLock)
            {
                foreach (var scene in world.Scenes)
                {
                    if (string.Equals(scene.LevelPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        return levels.SceneHasChanges(scene);
                    }
                }
            }

            return false;
        }

        private static UiElement Divider(UiTheme theme)
        {
            return new Border()
                .Fill(theme.Divider)
                .Width(1.0f)
                .Height(theme.ControlHeight)
                .Margin(theme.Spacing * 0.5f, 0.0f)
                .CenterV();
        }
    }
}

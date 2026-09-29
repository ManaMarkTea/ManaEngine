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
    /// <para>
    /// One horizontal scroller wraps every group rather than each group clipping on its own, and its bar is
    /// hidden rather than reserved: a visible one costs a lane of height off a 38px strip whether or not
    /// anything overflows, and the strip's height comes straight off the viewport. The wheel over the
    /// toolbar still reaches it, which is how a toolbar that occasionally overruns behaves elsewhere.
    /// </para>
    /// </summary>
    public static class VoxelEditorToolbarManaView
    {
        public static UiElement Build(UiTheme theme, VoxelEditorViewModel editor, ManaWorld? world)
        {
            var groups = new StackPanel()
                .Horizontal(theme.Spacing)
                .CenterV()
                .Children(
                    BuildLevelGroup(theme, editor.Levels, world),
                    ToolbarIcons.Divider(theme),
                    BuildHistoryGroup(theme, editor),
                    ToolbarIcons.Divider(theme),
                    BuildGizmoGroup(theme, editor, world),
                    ToolbarIcons.Divider(theme),
                    BuildPlayGroup(theme, editor.Levels),
                    ToolbarIcons.Divider(theme),
                    // The tools themselves live in the Voxel Tools window, which this brings up.
                    VoxelToolPaletteManaView.BuildToolbarToggle(theme, editor));

            var scrollable = groups
                .Scrollable()
                .Scrolls(vertical: ScrollBarVisibility.Disabled, horizontal: ScrollBarVisibility.Hidden);

            // 8 across, matching the menu bar above and the status bar below -- all three run the full width
            // of the window, and at 6 the toolbar's first and last controls sat a visible 2px closer to the
            // window edge than the rows either side of them.
            return new Grid()
                .Columns("*", theme.Spacing)
                .Rows("Auto")
                .Padding(8.0f, 3.0f)
                .Children(scrollable.GridColumn(0));
        }

        private static UiElement BuildLevelGroup(UiTheme theme, LevelViewModel levels, ManaWorld? world)
        {
            // Carries its own tooltip now that the "Level: <name>" readout beside it is gone -- the box is
            // both the name on screen and the name Save writes to, and nothing else says so.
            var name = new TextBox { Text = levels.LevelName, Width = 160.0f }
                .WithTooltip("The open level's name, and the file Save writes to. Type a new one to save a copy.");
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
                .WithTooltip(
                    "Save the current level." + NewLine
                    + "Ctrl+S. Rename the field first to save as a new file.")
                .OnClick(levels.Save);
            save.BindPolled(button => button.Text, () => CurrentSceneHasChanges(levels, world) ? "Save *" : "Save");

            var open = FlyoutButton("Level", "New, open and reload -- and which one is open.", button =>
            {
                levels.Refresh();
                return BuildOpenMenu(levels);
            });

            // New and Reload used to be buttons of their own here, and a muted "Level: <name>" readout sat
            // after them. All three are gone: the two commands are already rows in this flyout and in the
            // File menu, and the readout said what the name box immediately to its left already says. Every
            // pixel spent here is one the voxel tools further along the same strip do not get.
            return new StackPanel()
                .Horizontal(theme.Spacing)
                .Children(open, name, save);
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

            // What just happened used to be a 210px field right here. It is a status message -- read once,
            // just after an action, and never again -- so it lives in the status bar now, where there was
            // already an empty half-width going spare.
            return new StackPanel()
                .Horizontal(theme.Spacing)
                .Children(play, more);
        }

        /// <summary>
        /// Move/Rotate/Scale as icons rather than words -- the same glyphs every other 3D editor uses for
        /// them, at roughly half the width the labels cost. Disabled with nothing selected, which is what
        /// says the gizmo needs a target rather than leaving three live-looking buttons that do nothing.
        /// </summary>
        private static UiElement BuildGizmoGroup(UiTheme theme, VoxelEditorViewModel editor, ManaWorld? world)
        {
            return new StackPanel()
                .Horizontal(theme.Spacing)
                .Children(
                    GizmoModeButton(theme, ToolbarIcons.DrawMove, "Move -- drag an axis to translate.",
                        SceneTransformGizmoMode.Translate, editor, world),
                    GizmoModeButton(theme, ToolbarIcons.DrawRotate, "Rotate -- drag a ring to turn around an axis.",
                        SceneTransformGizmoMode.Rotate, editor, world),
                    GizmoModeButton(theme, ToolbarIcons.DrawScale, "Scale -- drag an axis to resize.",
                        SceneTransformGizmoMode.Scale, editor, world));
        }

        private static UiElement BuildHistoryGroup(UiTheme theme, VoxelEditorViewModel editor)
        {
            var undo = ToolbarIcons.ActionButton(
                theme,
                ToolbarIcons.DrawUndo,
                "Undo the last editor action." + NewLine + "Ctrl+Z",
                () => editor.History.Undo());
            undo.BindPolled(button => button.IsEnabled, () => editor.History.CanUndo);

            var redo = ToolbarIcons.ActionButton(
                theme,
                ToolbarIcons.DrawRedo,
                "Redo the last undone action." + NewLine + "Ctrl+Y or Ctrl+Shift+Z",
                () => editor.History.Redo());
            redo.BindPolled(button => button.IsEnabled, () => editor.History.CanRedo);

            return new StackPanel()
                .Horizontal(theme.Spacing)
                .Children(undo, redo);
        }

        private static Button GizmoModeButton(
            UiTheme theme,
            Action<ManaUiDrawList, UiRect, Vector4> drawIcon,
            string tooltip,
            SceneTransformGizmoMode mode,
            VoxelEditorViewModel editor,
            ManaWorld? world)
        {
            var button = ToolbarIcons.IconButton(
                theme,
                drawIcon,
                tooltip,
                () => editor.ShowTransformGizmo && editor.TransformGizmoMode == mode,
                () =>
                {
                    editor.TransformGizmoMode = mode;
                    editor.ShowTransformGizmo = true;
                    editor.IsSelectToolActive = true;
                });

            button.BindPolled(
                item => item.IsEnabled,
                () => world?.Selection.SelectedNode != null);
            return button;
        }

        /// <summary>A tooltip's own line break, so a shortcut sits under its description rather than beside it.</summary>
        private static string NewLine => Environment.NewLine;

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

    }
}

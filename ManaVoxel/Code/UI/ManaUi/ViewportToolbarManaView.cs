using Mana;
using Mana.Code.Components;
using ManaUI;
using Mana.Code.UI;
using Mana.Code.UI.ManaUi;
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>Viewport commands resolve the current camera when invoked, including after scene changes.</summary>
    public static class ViewportToolbarManaView
    {
        private const float SnapOptionsWidth = 330.0f;
        private static Button? _snapOptionsButton;
        private static Action? _openSnapOptions;

        /// <summary>
        /// The bar over the viewport, after Blender's header: the menus on the left; snapping (a magnet and its
        /// steps) and the view toggles on the right.
        /// </summary>
        public static UiElement BuildBar(UiTheme theme, VoxelEditorViewModel editor, Func<CameraComponent?> camera, ManaWorld? world)
        {
            var snap = editor.Snap;
            var stepText = new TextBlock()
                .BindPolled(t => t.Text, () => FormattableString.Invariant($"{snap.TranslateStep:0.##} m  {snap.RotationStepDegrees:0.#}\u00b0  \u25be"));
            var steps = new Button { Child = stepText, Variant = ButtonVariant.Subtle, Height = ToolbarIcons.ButtonSize }
                .WithTooltip("Snap steps for move, rotate and scale, and the gizmo's size.");
            _snapOptionsButton = steps;
            _openSnapOptions = () => OpenSnapOptions(theme, editor);
            steps.OnClick(_openSnapOptions);

            var tools = new StackPanel().Horizontal(2.0f).Children(
                ToolbarIcons.IconButton(theme, DrawMagnet, "Snap to grid (X).", () => snap.Enabled, () => snap.Enabled = !snap.Enabled),
                steps,
                ToolbarIcons.Divider(theme),
                ToolbarIcons.IconButton(theme, (list, box, color) => ViewportCameraGizmoManaView.DrawGizmos(list, box, color, false),
                    "Gizmos: transform handles, collider and grid map outlines.",
                    () => ManaEditorGizmos.Visible, () => ManaEditorGizmos.Visible = !ManaEditorGizmos.Visible),
                ToolbarIcons.IconButton(theme, (list, box, color) => ViewportCameraGizmoManaView.DrawRoom(list, box, color, true),
                    "The room's walls and ceiling. Off, they show only while drawing voxels.",
                    () => ManaEditorGizmos.ShowRoom, () => ManaEditorGizmos.ShowRoom = !ManaEditorGizmos.ShowRoom),
                ToolbarIcons.IconButton(theme, DrawWireframe, "Wireframe.",
                    () => editor.ShowWireframe, () => editor.ShowWireframe = !editor.ShowWireframe));

            return new Grid().Columns("Auto, *, Auto").Rows("*").Children(
                Build(editor, camera, world).GridColumn(0),
                tools.CenterV().Margin(0.0f, 0.0f, 6.0f, 0.0f).GridColumn(2));
        }

        /// <summary>Drops the snap options down from the bar built last: the open_tools command.</summary>
        public static void OpenSnapOptions() => _openSnapOptions?.Invoke();

        private static void OpenSnapOptions(UiTheme theme, VoxelEditorViewModel editor)
        {
            if (_snapOptionsButton is { Root: { } root } anchor)
            {
                // Right-aligned to the button: the bar sits at the window's right edge, where a left-aligned drop-down is cut off.
                var at = new Vector2(MathF.Max(0.0f, anchor.Bounds.Right - SnapOptionsWidth - 24.0f), anchor.Bounds.Bottom + 2.0f);
                MenuFlyout.ShowAt(root, at, new[] { MenuItem.Custom(SnapOptions(theme, editor)) });
            }
        }

        /// <summary>The snap steps and gizmo size, as the bar drops them down.</summary>
        public static UiElement SnapOptions(UiTheme theme, VoxelEditorViewModel editor)
        {
            var size = new SliderBox
            {
                Minimum = 0.25f, Maximum = 3.0f, Step = 0.05f,
                HardMinimum = 0.25f, HardMaximum = 3.0f,
                DecimalPlaces = 2, Value = editor.Snap.GizmoSize
            };
            size.ValueChanged += (_, value) =>
            {
                if (!size.IsApplyingBinding)
                {
                    editor.Snap.GizmoSize = value;
                }
            };
            size.BindPolled(s => s.Value, () => editor.Snap.GizmoSize);
            var reset = ToolbarIcons.ActionButton(theme, ToolbarIcons.DrawUndo, "Back to the default size.", () => editor.Snap.GizmoSize = 1.0f);

            return new StackPanel().Vertical(8).Width(SnapOptionsWidth).Margin(10.0f, 6.0f, 10.0f, 8.0f).Children(
                PreferencesManaView.Heading(theme, "Snap"),
                PreferencesManaView.SnapRows(theme, editor.Snap),
                PreferencesManaView.Heading(theme, "Gizmo"),
                new Grid().Columns("52, *, Auto", 4.0f).Rows("Auto").Children(
                    new TextBlock().Text("Size").Foreground(theme.TextMuted).CenterV().GridColumn(0),
                    size.GridColumn(1),
                    reset.CenterV().GridColumn(2)),
                new TextBlock().Text("Shift-drag an arrow, or drag a square, to move on a plane.")
                    .FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Wrap());
        }

        /// <summary>A horseshoe magnet, its poles tipped.</summary>
        private static void DrawMagnet(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            const float thickness = 2.2f;
            var centre = ToolbarIcons.P(box, 0.5f, 0.52f);
            var radius = box.Width * 0.28f;
            var previous = centre + new Vector2(-radius, 0.0f);
            for (var step = 1; step <= 8; step++)
            {
                var angle = MathF.PI * step / 8.0f;
                var next = centre + new Vector2(-MathF.Cos(angle), MathF.Sin(angle)) * radius;
                drawList.DrawLine(previous, next, color, thickness);
                previous = next;
            }

            var top = box.Y + box.Height * 0.16f;
            drawList.DrawLine(centre + new Vector2(-radius, 0.0f), new Vector2(centre.X - radius, top), color, thickness);
            drawList.DrawLine(centre + new Vector2(radius, 0.0f), new Vector2(centre.X + radius, top), color, thickness);
            drawList.DrawLine(new Vector2(centre.X - radius - 1.6f, top + 3.0f), new Vector2(centre.X - radius + 1.6f, top + 3.0f), color, 1.2f);
            drawList.DrawLine(new Vector2(centre.X + radius - 1.6f, top + 3.0f), new Vector2(centre.X + radius + 1.6f, top + 3.0f), color, 1.2f);
        }

        /// <summary>A wire cube.</summary>
        private static void DrawWireframe(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            const float thickness = 1.4f;
            Vector2 P(float x, float y) => ToolbarIcons.P(box, x, y);
            var front = new[] { P(0.16f, 0.36f), P(0.64f, 0.36f), P(0.64f, 0.84f), P(0.16f, 0.84f) };
            var back = new[] { P(0.36f, 0.16f), P(0.84f, 0.16f), P(0.84f, 0.64f), P(0.36f, 0.64f) };
            for (var i = 0; i < 4; i++)
            {
                drawList.DrawLine(front[i], front[(i + 1) % 4], color, thickness);
                drawList.DrawLine(back[i], back[(i + 1) % 4], color, thickness);
                drawList.DrawLine(front[i], back[i], color, thickness);
            }
        }

        public static MenuBar Build(VoxelEditorViewModel editor, Func<CameraComponent?> camera, ManaWorld? world)
        {
            MenuItem Toggle(string name, Func<bool> read, Action action) =>
                new MenuItem(name, action).Checkable(read);
            var speedControl = new SliderBox
            {
                Minimum = 0.25f, Maximum = 500f,
                HardMinimum = 0.25f, HardMaximum = 25000f,
                IsLogarithmic = true, Step = 0.25f, DecimalPlaces = 2, Width = 220f,
                Value = camera()?.MovementSpeed ?? 50f
            };
            speedControl.ValueChanged += (_, value) =>
            {
                if (!speedControl.IsApplyingBinding && camera() is { } current)
                {
                    current.MovementSpeed = value;
                }
            };
            speedControl.BindPolled(control => control.Value, () => camera()?.MovementSpeed ?? 50f);
            speedControl.BindPolled(control => control.IsEnabled, () => camera() != null);
            var speed = new MenuItem("Movement speed (units/s)") { Content = speedControl };
            var views = new MenuItem("View direction").WithItems(Enum.GetValues<CameraViewPreset>().Select(preset =>
                new MenuItem(preset.ToString(), () => camera()?.SetViewPreset(preset))
                    .EnabledWhen(() => camera() != null)).ToArray());

            return new MenuBar().AddRange(new[]
            {
                new MenuItem("Camera").WithItems(speed, views,
                    Toggle("Orthographic", () => camera()?.OrthoGraphic == true, () =>
                    {
                        if (camera() is { } current) { current.SetProjectionMode(!current.OrthoGraphic); }
                    }).EnabledWhen(() => camera() != null)),
                new MenuItem("Display").WithItems(
                    Toggle("Wireframe", () => editor.ShowWireframe, () => editor.ShowWireframe = !editor.ShowWireframe),
                    Toggle("Vertex normals", () => editor.ShowVertexNormals, () => editor.ShowVertexNormals = !editor.ShowVertexNormals),
                    Toggle("Shade by normal", () => editor.ShowNormals, () => editor.ShowNormals = !editor.ShowNormals),
                    MenuItem.Separator(),
                    Toggle("Gizmos", () => ManaEditorGizmos.Visible, () => ManaEditorGizmos.Visible = !ManaEditorGizmos.Visible),
                    Toggle("Room walls", () => ManaEditorGizmos.ShowRoom, () => ManaEditorGizmos.ShowRoom = !ManaEditorGizmos.ShowRoom)),
                new MenuItem("Focus").WithItems(
                    new MenuItem("Focus selection", () =>
                    {
                        if (world != null)
                        {
                            camera()?.TryFrameSelection(world.Selection.SelectedComponent as MeshRendererComponent, world.Selection.SelectedNode);
                        }
                    }).ShortcutFrom(() => "F").EnabledWhen(() => camera() != null
                        && (world?.Selection.SelectedNode != null || world?.Selection.SelectedComponent is MeshRendererComponent)))
            });
        }
    }
}

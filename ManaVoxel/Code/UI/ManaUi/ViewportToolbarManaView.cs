using Mana;
using Mana.Code.Components;
using ManaUI;
using Mana.Code.UI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>Viewport commands resolve the current camera when invoked, including after scene changes.</summary>
    public static class ViewportToolbarManaView
    {
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

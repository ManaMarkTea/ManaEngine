using System.Globalization;
using Mana;
using Mana.Code.UI;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi;

/// <summary>Selection bounds and live world-position readouts beside the transform arrows.</summary>
public sealed class SelectionTransformOverlay : UiElement
{
    private readonly VoxelEditorViewModel _editor;
    private readonly Func<CameraComponent?> _camera;

    public SelectionTransformOverlay(VoxelEditorViewModel editor, Func<CameraComponent?> camera)
    {
        _editor = editor;
        _camera = camera;
        IsHitTestVisible = false;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize) => Vector2.Zero;

    protected override void OnRender(ManaUiDrawList drawList)
    {
        if (Root == null || !_editor.IsSelectToolActive || !_editor.ShowTransformGizmo ||
            !ManaEditorGizmos.Visible || _camera() is not { } camera || camera.Context.World.IsPlaying)
            return;

        var selection = camera.Context.World.Selection;
        if (selection.SelectedNode is not { } node)
            return;

        // Projected onto the UI's own logical size, which is the window over the display scale.
        var window = Root.Size;
        var viewProjection = camera._ViewMatrix * camera._ProjectionMatrix;
        bool Project(Vector3 point, out Vector2 screen) =>
            VoxelPlacementDepthOverlay.TryProject(viewProjection, new Vector2(window.X, window.Y),
                point, out screen, out var depth) && depth >= 0 && depth <= 1;

        drawList.PushClip(Bounds);
        try
        {
            if (CameraComponent.TryComputeSelectionWorldBounds(
                    selection.SelectedComponent as MeshRendererComponent, node, out var min, out var max))
            {
                Span<Vector2> corners = stackalloc Vector2[8];
                Span<bool> visible = stackalloc bool[8];
                for (var i = 0; i < 8; i++)
                    visible[i] = Project(new Vector3((i & 1) == 0 ? min.X : max.X,
                        (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z), out corners[i]);

                for (var i = 0; i < 8; i++)
                for (var bit = 1; bit <= 4; bit <<= 1)
                {
                    var other = i ^ bit;
                    if (i >= other || !visible[i] || !visible[other]) continue;
                    drawList.DrawLine(corners[i], corners[other], new Vector4(0, 0, 0, 0.55f), 3);
                    drawList.DrawLine(corners[i], corners[other], new Vector4(1, 1, 1, 0.9f), 1);
                }
            }

            if (_editor.TransformGizmoMode != SceneTransformGizmoMode.Translate ||
                !Matrix4x4.Decompose(node.WorldTransform, out _, out var orientation, out var position))
                return;

            var font = Root.Fonts.Get(Root.Theme.FontFamilyMono, Root.Theme.FontSize, Root.Scale);
            for (var axis = 0; axis < 3; axis++)
            {
                var direction = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
                var tip = position + Vector3.Transform(direction, orientation) *
                    (1.23f * _editor.Snap.GizmoSize / Mana.Code.GameModes.SceneTransformGizmo.TranslationTipDistance);
                if (!Project(tip, out var screen)) continue;
                var value = axis == 0 ? position.X : axis == 1 ? position.Y : position.Z;
                var label = new TextLayout { Font = font, Text = FormatPosition(axis, value) };
                label.Update();
                var at = screen + new Vector2(6, -label.Size.Y * 0.5f);
                label.Render(drawList, new UiRect(at.X + 1, at.Y + 1, label.Size.X, label.Size.Y), new Vector4(0, 0, 0, 1));
                label.Render(drawList, new UiRect(at.X, at.Y, label.Size.X, label.Size.Y), Vector4.One);
            }
        }
        finally
        {
            drawList.PopClip();
        }
    }

    public static string FormatPosition(int axis, float value) =>
        $"{(axis == 0 ? "X" : axis == 1 ? "Y" : "Z")} {MathF.Round(value, 2).ToString("0.00", CultureInfo.InvariantCulture)}";
}

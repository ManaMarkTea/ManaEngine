using Mana;
using Mana.Code.Nodes;
using ManaUI;
using ManaEditor.Code.Terrain;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>The terrain brush's ring, laid over the ground under the pointer while sculpting.</summary>
public sealed class TerrainBrushOverlay : UiElement
{
    private const int Segments = 64;
    private static readonly Vector4 Ring = new(1.0f, 0.85f, 0.35f, 1.0f);
    private static readonly Vector4 Shadow = new(0, 0, 0, 0.6f);
    private readonly Func<CameraComponent?> _camera;

    public TerrainBrushOverlay(Func<CameraComponent?> camera)
    {
        _camera = camera;
        IsHitTestVisible = false;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize) => Vector2.Zero;

    protected override void OnRender(ManaUiDrawList drawList)
    {
        if (Root == null || _camera() is not { } camera || camera.Context.World.IsPlaying
            || TerrainEditing.Target is not { } terrain || TerrainEditing.Hover is not { } hover
            || !Matrix4x4.Invert(terrain.WorldTransform, out var toLocal))
        {
            return;
        }

        var viewProjection = camera._ViewMatrix * camera._ProjectionMatrix;
        var size = Root.Size;
        var centre = Vector3.Transform(hover, toLocal);
        drawList.PushClip(Bounds);
        try
        {
            Vector2? previous = null;
            for (var i = 0; i <= Segments; i++)
            {
                var angle = i * MathF.Tau / Segments;
                var x = centre.X + MathF.Cos(angle) * TerrainEditing.Size;
                var z = centre.Z + MathF.Sin(angle) * TerrainEditing.Size;
                // Following the ground, a little above it so it is not lost in the slope.
                var world = Vector3.Transform(new Vector3(x, terrain.HeightAt(x, z) + 0.05f, z), terrain.WorldTransform);
                Vector2? screen = null;
                if (VoxelPlacementDepthOverlay.TryProject(viewProjection, size, world, out var at, out var depth) && depth >= 0 && depth <= 1)
                {
                    screen = at;
                }

                if (previous is { } a && screen is { } b)
                {
                    drawList.DrawLine(a, b, Shadow, 3.5f);
                    drawList.DrawLine(a, b, Ring, 1.6f);
                }
                previous = screen;
            }
        }
        finally
        {
            drawList.PopClip();
        }
    }
}

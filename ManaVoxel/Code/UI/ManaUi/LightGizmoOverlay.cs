using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi;

/// <summary>
/// A bulb at every placed light, in its colour (ringed when it casts shadows), which a click selects: lights are
/// points in space, with nothing to click on in the scene itself. The selected light also shows its range.
/// </summary>
public sealed class LightGizmoOverlay : UiElement
{
    private const float IconRadius = 7.0f;
    private const int CircleSegments = 48;
    private static readonly Vector4 Outline = new(0.0f, 0.0f, 0.0f, 0.75f);
    private static readonly Vector4 Selected = new(1.0f, 0.85f, 0.3f, 1.0f);

    private readonly Func<CameraComponent?> _camera;
    private readonly List<(ManaSceneNode Node, ManaLightSourceComponent Light, Vector2 Screen)> _shown = new();

    public LightGizmoOverlay(Func<CameraComponent?> camera)
    {
        _camera = camera;
        IsHitTestVisible = true;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize) => Vector2.Zero;

    // Only the icons take the pointer; everywhere else clicks go on to the viewport.
    public override UiElement? HitTest(Vector2 point)
    {
        if (!Showing(out _) || !Bounds.Contains(point))
        {
            return null;
        }
        if (Nearest(point) == null)
        {
            return null;
        }
        return this;
    }

    protected override bool IsSelfHitTestVisible => true;

    protected override void OnPointerPressed(UiPointerEventArgs e)
    {
        if (e.Button != PointerButton.Left || !Showing(out var camera) || Nearest(e.Position) is not { } node)
        {
            return;
        }
        EditorSelectionSnapshot.SelectNode(camera.Context.World.Selection, node);
        e.Handled = true;
    }

    protected override void OnRender(ManaUiDrawList drawList)
    {
        _shown.Clear();
        if (Root == null || !Showing(out var camera))
        {
            return;
        }

        var viewProjection = camera._ViewMatrix * camera._ProjectionMatrix;
        var window = new Vector2(Root.Size.X, Root.Size.Y);
        bool Project(Vector3 point, out Vector2 screen) =>
            VoxelPlacementDepthOverlay.TryProject(viewProjection, window, point, out screen, out var depth) && depth >= 0 && depth <= 1;

        var world = camera.Context.World;
        foreach (var scene in world.Scenes)
        {
            foreach (var child in scene.Children)
            {
                Collect(child, Project);
            }
        }

        drawList.PushClip(Bounds);
        try
        {
            var selected = world.Selection.SelectedNode;
            foreach (var (node, light, screen) in _shown)
            {
                if (node == selected && light.LightType == ManaCore.Graphics.ManaLightType.Point)
                {
                    DrawRange(drawList, node.WorldTransform.Translation, light.Range, light.CurrentColor(), Project);
                }
            }
            foreach (var (node, light, screen) in _shown)
            {
                DrawIcon(drawList, screen, light, node == selected);
            }
        }
        finally
        {
            drawList.PopClip();
        }
    }

    private bool Showing(out CameraComponent camera)
    {
        camera = null!;
        if (!ManaEditorGizmos.ShowLights || !ManaEditorGizmos.Visible || _camera() is not { } found || found.Context.World.IsPlaying)
        {
            return false;
        }
        camera = found;
        return true;
    }

    private void Collect(ManaSceneNode node, ProjectPoint project)
    {
        foreach (var component in node.Components)
        {
            if (component is ManaLightSourceComponent { IsGridLight: false } light
                && project(node.WorldTransform.Translation, out var screen))
            {
                _shown.Add((node, light, screen));
            }
        }
        foreach (var child in node.Children)
        {
            Collect(child, project);
        }
    }

    private delegate bool ProjectPoint(Vector3 point, out Vector2 screen);

    // The topmost icon under the pointer: the last drawn.
    private ManaSceneNode? Nearest(Vector2 point)
    {
        for (var i = _shown.Count - 1; i >= 0; i--)
        {
            if (Vector2.Distance(_shown[i].Screen, point) <= IconRadius + 3.0f)
            {
                return _shown[i].Node;
            }
        }
        return null;
    }

    private static void DrawIcon(ManaUiDrawList drawList, Vector2 centre, ManaLightSourceComponent light, bool selected)
    {
        // The colour at full brightness, so a dim lamp still reads; a switched-off one is grey.
        var colour = light.CurrentColor();
        var peak = MathF.Max(MathF.Max(colour.X, colour.Y), MathF.Max(colour.Z, 0.001f));
        var fill = new Vector4(colour.X / peak, colour.Y / peak, colour.Z / peak, 1.0f);
        if (!light.Enabled)
        {
            fill = new Vector4(0.45f, 0.45f, 0.45f, 0.8f);
        }

        // Eight short rays, then the bulb over them.
        for (var i = 0; i < 8; i++)
        {
            var angle = i * MathF.PI / 4.0f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            drawList.DrawLine(centre + direction * (IconRadius + 2.0f), centre + direction * (IconRadius + 6.0f), Outline, 3.0f);
            drawList.DrawLine(centre + direction * (IconRadius + 2.0f), centre + direction * (IconRadius + 6.0f), fill, 1.5f);
        }
        var disc = new UiRect(centre.X - IconRadius, centre.Y - IconRadius, IconRadius * 2.0f, IconRadius * 2.0f);
        drawList.FillRect(disc, fill, new CornerRadius(IconRadius));
        drawList.DrawBorder(disc, Outline, 1.5f, new CornerRadius(IconRadius));
        if (light.CastsShadows)
        {
            // A dark core: this one casts shadows.
            var core = new UiRect(centre.X - 3.0f, centre.Y - 3.0f, 6.0f, 6.0f);
            drawList.FillRect(core, Outline, new CornerRadius(3.0f));
        }
        if (selected)
        {
            var ring = new UiRect(centre.X - IconRadius - 4.0f, centre.Y - IconRadius - 4.0f, IconRadius * 2.0f + 8.0f, IconRadius * 2.0f + 8.0f);
            drawList.DrawBorder(ring, Selected, 2.0f, new CornerRadius(IconRadius + 4.0f));
        }
    }

    // The range as three circles round the light, one in each axis plane.
    private static void DrawRange(ManaUiDrawList drawList, Vector3 centre, float range, Vector4 colour, ProjectPoint project)
    {
        var line = new Vector4(MathF.Min(colour.X, 1.0f), MathF.Min(colour.Y, 1.0f), MathF.Min(colour.Z, 1.0f), 0.7f);
        for (var plane = 0; plane < 3; plane++)
        {
            var previous = Vector2.Zero;
            var hadPrevious = false;
            for (var i = 0; i <= CircleSegments; i++)
            {
                var angle = i * MathF.Tau / CircleSegments;
                var (a, b) = (MathF.Cos(angle) * range, MathF.Sin(angle) * range);
                var offset = plane switch
                {
                    0 => new Vector3(a, b, 0.0f),
                    1 => new Vector3(a, 0.0f, b),
                    _ => new Vector3(0.0f, a, b)
                };
                var visible = project(centre + offset, out var screen);
                if (visible && hadPrevious)
                {
                    drawList.DrawLine(previous, screen, Outline, 2.5f);
                    drawList.DrawLine(previous, screen, line, 1.0f);
                }
                previous = screen;
                hadPrevious = visible;
            }
        }
    }
}

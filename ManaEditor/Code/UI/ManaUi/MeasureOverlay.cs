using Mana;
using Mana.Code.UI;
using ManaUI;
using ManaEditor.Code.Measure;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>
/// Draws the measuring tool over the scene: every measurement kept, the one being taken running to the pointer,
/// and a marker for what the pointer has snapped to. Drawn in screen space, so it is never hidden behind what it
/// measures.
/// </summary>
public sealed class MeasureOverlay : UiElement
{
    private static readonly Vector4 Kept = new(1.0f, 0.78f, 0.25f, 1.0f);
    private static readonly Vector4 Taking = new(0.35f, 0.85f, 1.0f, 1.0f);
    private static readonly Vector4 Shadow = new(0, 0, 0, 0.6f);
    private const int CircleSegments = 72;

    private readonly VoxelEditorViewModel _editor;
    private readonly Func<CameraComponent?> _camera;
    private Matrix4x4 _viewProjection;
    private Vector2 _size;

    public MeasureOverlay(VoxelEditorViewModel editor, Func<CameraComponent?> camera)
    {
        _editor = editor;
        _camera = camera;
        IsHitTestVisible = false;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize) => Vector2.Zero;

    protected override void OnRender(ManaUiDrawList drawList)
    {
        var tool = _editor.Measure;
        if (Root == null || _camera() is not { } camera || camera.Context.World.IsPlaying
            || (!tool.Active && tool.Measurements.Count == 0))
        {
            return;
        }

        _viewProjection = camera._ViewMatrix * camera._ProjectionMatrix;
        _size = Root.Size;
        drawList.PushClip(Bounds);
        try
        {
            foreach (var measurement in tool.Measurements)
            {
                DrawMeasurement(drawList, tool, measurement, Kept);
            }

            if (tool.Active && tool.Current is { } current)
            {
                // The one being taken, with the pointer as its next point.
                var preview = new Measurement(current.Mode, current.Normal);
                preview.Points.AddRange(current.Points);
                if (tool.Hover is { } hover && Vector3.Distance(hover.Position, current.Points[^1]) > 1e-4f)
                {
                    preview.Points.Add(hover.Position);
                }
                DrawMeasurement(drawList, tool, preview, Taking);
            }

            if (tool.Active && tool.RayPreview is { } ray)
            {
                if (ray.To is { } end)
                {
                    Line(drawList, ray.From, end, Taking);
                    Dot(drawList, end, Taking);
                    Label(drawList, (ray.From + end) * 0.5f, tool.Format(Vector3.Distance(ray.From, end)), "click to keep", Taking);
                }
                else
                {
                    // Nothing that way: a short stub shows which way it went.
                    DashedLine(drawList, ray.From, ray.From + ray.Direction * 0.5f, Taking);
                    SmallLabel(drawList, ray.From + ray.Direction * 0.5f, "no surface that way");
                }
            }

            if (tool.Active && tool.Hover is { } snap)
            {
                if (tool.HoverUnprojected is { } before)
                {
                    DashedLine(drawList, before, snap.Position, new Vector4(Taking.X, Taking.Y, Taking.Z, 0.6f));
                }
                DrawSnapMarker(drawList, snap);
            }
        }
        finally
        {
            drawList.PopClip();
        }
    }

    private void DrawMeasurement(ManaUiDrawList drawList, MeasureTool tool, Measurement measurement, Vector4 colour)
    {
        var points = measurement.Points;
        if (points.Count == 0)
        {
            return;
        }

        if (measurement.Mode == MeasureMode.Radius)
        {
            Dot(drawList, points[0], colour);
            if (points.Count < 2)
            {
                return;
            }
            var radius = Vector3.Distance(points[0], points[1]);
            Line(drawList, points[0], points[1], colour);
            Circle(drawList, points[0], measurement.Normal, points[1] - points[0], radius, colour);
            Dot(drawList, points[1], colour);
            Label(drawList, points[1], tool.Headline(measurement), tool.Details(measurement), colour);
            return;
        }

        for (var i = 0; i < points.Count; i++)
        {
            Dot(drawList, points[i], colour);
            if (i == 0)
            {
                continue;
            }
            Line(drawList, points[i - 1], points[i], colour);
            // Each leg's own length on a path; a plain distance has just the one label below.
            if (measurement.Mode == MeasureMode.Path && points.Count > 2)
            {
                SmallLabel(drawList, (points[i - 1] + points[i]) * 0.5f, tool.Format(Vector3.Distance(points[i - 1], points[i])));
            }
        }
        if (points.Count < 2)
        {
            return;
        }

        if (measurement.Mode == MeasureMode.Ray)
        {
            Label(drawList, (points[0] + points[1]) * 0.5f, tool.Headline(measurement), tool.Details(measurement), colour);
        }
        else if (measurement.Mode == MeasureMode.Distance)
        {
            // The right angle under the line: across the ground, then straight up, so both parts can be seen.
            var a = points[0];
            var b = points[1];
            if (measurement.Horizontal > 0.01f && measurement.Vertical > 0.01f)
            {
                var corner = a.Y < b.Y ? new Vector3(b.X, a.Y, b.Z) : new Vector3(a.X, b.Y, a.Z);
                var faint = new Vector4(colour.X, colour.Y, colour.Z, 0.45f);
                DashedLine(drawList, a.Y < b.Y ? a : b, corner, faint);
                DashedLine(drawList, corner, a.Y < b.Y ? b : a, faint);
            }
            Label(drawList, (a + b) * 0.5f, tool.Headline(measurement), tool.Details(measurement), colour);
        }
        else
        {
            Label(drawList, points[^1], tool.Headline(measurement), tool.Details(measurement), colour);
        }
    }

    private void DrawSnapMarker(ManaUiDrawList drawList, MeasureSnap snap)
    {
        if (!Project(snap.Position, out var at))
        {
            return;
        }
        const float s = 7.0f;
        var colour = Vector4.One;
        void Stroke(Vector2 from, Vector2 to)
        {
            drawList.DrawLine(from, to, Shadow, 3.5f);
            drawList.DrawLine(from, to, colour, 1.6f);
        }

        switch (snap.Kind)
        {
            case MeasureSnapKind.Vertex:
                Stroke(at + new Vector2(-s, -s), at + new Vector2(s, -s));
                Stroke(at + new Vector2(s, -s), at + new Vector2(s, s));
                Stroke(at + new Vector2(s, s), at + new Vector2(-s, s));
                Stroke(at + new Vector2(-s, s), at + new Vector2(-s, -s));
                break;
            case MeasureSnapKind.Midpoint:
                Stroke(at + new Vector2(0, -s), at + new Vector2(s, s * 0.8f));
                Stroke(at + new Vector2(s, s * 0.8f), at + new Vector2(-s, s * 0.8f));
                Stroke(at + new Vector2(-s, s * 0.8f), at + new Vector2(0, -s));
                break;
            case MeasureSnapKind.Edge:
                Stroke(at + new Vector2(-s, -s), at + new Vector2(s, s));
                Stroke(at + new Vector2(-s, s), at + new Vector2(s, -s));
                break;
            case MeasureSnapKind.Grid:
                Stroke(at + new Vector2(-s, 0), at + new Vector2(s, 0));
                Stroke(at + new Vector2(0, -s), at + new Vector2(0, s));
                break;
            default:
                drawList.FillRect(new UiRect(at.X - 4.5f, at.Y - 4.5f, 9, 9), Shadow, new CornerRadius(4.5f));
                drawList.FillRect(new UiRect(at.X - 3, at.Y - 3, 6, 6), colour, new CornerRadius(3));
                break;
        }

        if (snap.Kind is not (MeasureSnapKind.Face or MeasureSnapKind.Free))
        {
            // Left of the marker: the measurement's own label sits up and to the right of its points.
            SmallLabel(drawList, snap.Position, snap.Kind.ToString(), new Vector2(-12, 8), leftOf: true);
        }
    }

    private void Circle(ManaUiDrawList drawList, Vector3 centre, Vector3 normal, Vector3 toRim, float radius, Vector4 colour)
    {
        var n = Vector3.Normalize(normal);
        var u = toRim - n * Vector3.Dot(toRim, n);
        u = u.LengthSquared() > 1e-10f ? Vector3.Normalize(u) : Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var v = Vector3.Cross(n, u);
        var previous = centre + u * radius;
        for (var i = 1; i <= CircleSegments; i++)
        {
            var angle = i * MathF.Tau / CircleSegments;
            var next = centre + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            Line(drawList, previous, next, colour, 1.4f);
            previous = next;
        }
    }

    private void Line(ManaUiDrawList drawList, Vector3 from, Vector3 to, Vector4 colour, float thickness = 2.0f)
    {
        if (Project(from, out var a) && Project(to, out var b))
        {
            drawList.DrawLine(a, b, Shadow, thickness + 2.0f);
            drawList.DrawLine(a, b, colour, thickness);
        }
    }

    private void DashedLine(ManaUiDrawList drawList, Vector3 from, Vector3 to, Vector4 colour)
    {
        if (!Project(from, out var a) || !Project(to, out var b))
        {
            return;
        }
        var length = Vector2.Distance(a, b);
        if (length < 1)
        {
            return;
        }
        var step = 6.0f / length;
        for (var t = 0.0f; t < 1.0f; t += step * 2)
        {
            drawList.DrawLine(Vector2.Lerp(a, b, t), Vector2.Lerp(a, b, MathF.Min(1, t + step)), colour, 1.3f);
        }
    }

    private void Dot(ManaUiDrawList drawList, Vector3 point, Vector4 colour)
    {
        if (Project(point, out var at))
        {
            drawList.FillRect(new UiRect(at.X - 5, at.Y - 5, 10, 10), Shadow, new CornerRadius(5));
            drawList.FillRect(new UiRect(at.X - 3.5f, at.Y - 3.5f, 7, 7), colour, new CornerRadius(3.5f));
        }
    }

    private void Label(ManaUiDrawList drawList, Vector3 anchor, string headline, string details, Vector4 colour)
    {
        if (Root == null || !Project(anchor, out var at))
        {
            return;
        }
        var theme = Root.Theme;
        var big = new TextLayout { Font = Root.Fonts.Get(theme.FontFamilyMedium, theme.FontSize + 2, Root.Scale), Text = headline };
        big.Update();
        TextLayout? small = null;
        if (details.Length > 0)
        {
            small = new TextLayout { Font = Root.Fonts.Get(theme.FontFamilyMono, theme.FontSizeSmall, Root.Scale), Text = details };
            small.Update();
        }

        const float pad = 6.0f;
        var width = MathF.Max(big.Size.X, small?.Size.X ?? 0) + pad * 2;
        var height = big.Size.Y + (small?.Size.Y + 2 ?? 0) + pad * 2;
        var box = new UiRect(at.X + 10, at.Y - height - 6, width, height);
        drawList.FillRect(box, new Vector4(0.06f, 0.07f, 0.09f, 0.82f), new CornerRadius(6));
        drawList.FillRect(new UiRect(box.X, box.Y, 3, box.Height), colour, new CornerRadius(1.5f));
        big.Render(drawList, new UiRect(box.X + pad, box.Y + pad, big.Size.X, big.Size.Y), Vector4.One);
        small?.Render(drawList, new UiRect(box.X + pad, box.Y + pad + big.Size.Y + 2, small.Size.X, small.Size.Y),
            new Vector4(0.82f, 0.84f, 0.88f, 1));
    }

    private void SmallLabel(ManaUiDrawList drawList, Vector3 anchor, string text, Vector2 offset = default, bool leftOf = false)
    {
        if (Root == null || !Project(anchor, out var at))
        {
            return;
        }
        var layout = new TextLayout { Font = Root.Fonts.Get(Root.Theme.FontFamilyMono, Root.Theme.FontSizeSmall, Root.Scale), Text = text };
        layout.Update();
        var x = leftOf ? at.X + offset.X - layout.Size.X - 3 : at.X + offset.X - 3;
        var box = new UiRect(x, at.Y + offset.Y - 2, layout.Size.X + 6, layout.Size.Y + 4);
        drawList.FillRect(box, new Vector4(0.06f, 0.07f, 0.09f, 0.7f), new CornerRadius(4));
        layout.Render(drawList, new UiRect(box.X + 3, box.Y + 2, layout.Size.X, layout.Size.Y), Vector4.One);
    }

    private bool Project(Vector3 world, out Vector2 screen) =>
        VoxelPlacementDepthOverlay.TryProject(_viewProjection, _size, world, out screen, out var depth) && depth >= 0 && depth <= 1;
}

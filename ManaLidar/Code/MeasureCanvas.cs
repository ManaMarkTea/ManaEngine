using System.Numerics;
using ManaVoxel.Code.Measure;
using SceneKit;

namespace ManaLidar;

/// <summary>
/// Draws a MeasureTool's measurements over a SceneKit view in screen space -- lines, end dots, circles and labels --
/// the phone's counterpart of the editor's MeasureOverlay, so it is never hidden behind what it measures.
/// </summary>
public sealed class MeasureCanvas : UIView
{
    private static readonly UIColor Kept = UIColor.FromRGB(1.0f, 0.78f, 0.25f);
    private static readonly UIColor Taking = UIColor.FromRGB(0.35f, 0.85f, 1.0f);
    private static readonly UIColor Shadow = UIColor.FromWhiteAlpha(0, 0.6f);

    private readonly MeasureTool _tool;
    private readonly SCNView _view;

    public MeasureCanvas(MeasureTool tool, SCNView view)
    {
        _tool = tool;
        _view = view;
    }

    public override void Draw(CGRect rect)
    {
        foreach (var measurement in _tool.Measurements)
        {
            DrawMeasurement(measurement, Kept);
        }
        if (_tool.Current is { } current)
        {
            DrawMeasurement(current, Taking);
        }
        if (_tool.Hover is { } snap && _tool.Current != null)
        {
            if (_tool.HoverUnprojected is { } before)
            {
                Line(before, snap.Position, Taking.ColorWithAlpha(0.6f), 1.3f, dashed: true);
            }
            if (snap.Kind is not (MeasureSnapKind.Face or MeasureSnapKind.Free) && Project(snap.Position) is { } at)
            {
                SmallLabel(at + new Vector2(-12, 8), snap.Kind.ToString(), leftOf: true);
            }
        }
    }

    private void DrawMeasurement(Measurement measurement, UIColor colour)
    {
        var points = measurement.Points;
        if (points.Count == 0)
        {
            return;
        }
        if (measurement.Mode == MeasureMode.Radius && points.Count >= 2)
        {
            var radius = Vector3.Distance(points[0], points[1]);
            Line(points[0], points[1], colour, 2);
            Circle(points[0], measurement.Normal, points[1] - points[0], radius, colour);
        }
        else
        {
            for (var i = 1; i < points.Count; i++)
            {
                Line(points[i - 1], points[i], colour, 2);
                if (measurement.Mode == MeasureMode.Path && points.Count > 2 && Project((points[i - 1] + points[i]) * 0.5f) is { } middle)
                {
                    SmallLabel(middle, _tool.Format(Vector3.Distance(points[i - 1], points[i])));
                }
            }
        }
        foreach (var point in points)
        {
            Dot(point, colour);
        }
        if (points.Count < 2)
        {
            return;
        }
        var anchor = measurement.Mode is MeasureMode.Distance or MeasureMode.Ray ? (points[0] + points[1]) * 0.5f : points[^1];
        if (Project(anchor) is { } at)
        {
            Label(at, _tool.Headline(measurement), _tool.Details(measurement), colour);
        }
    }

    private void Circle(Vector3 centre, Vector3 normal, Vector3 toRim, float radius, UIColor colour)
    {
        var n = Vector3.Normalize(normal);
        var u = toRim - n * Vector3.Dot(toRim, n);
        u = u.LengthSquared() > 1e-10f ? Vector3.Normalize(u) : Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var v = Vector3.Cross(n, u);
        var previous = centre + u * radius;
        for (var i = 1; i <= 72; i++)
        {
            var angle = i * MathF.Tau / 72;
            var next = centre + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            Line(previous, next, colour, 1.4f);
            previous = next;
        }
    }

    private void Line(Vector3 from, Vector3 to, UIColor colour, float width, bool dashed = false)
    {
        if (Project(from) is not { } a || Project(to) is not { } b)
        {
            return;
        }
        var path = new UIBezierPath();
        path.MoveTo(new CGPoint(a.X, a.Y));
        path.AddLineTo(new CGPoint(b.X, b.Y));
        path.LineCapStyle = CGLineCap.Round;
        if (dashed)
        {
            path.SetLineDash(new nfloat[] { 5, 5 }, 0, 2, 0);
        }
        else
        {
            Shadow.SetStroke();
            path.LineWidth = width + 2;
            path.Stroke();
        }
        colour.SetStroke();
        path.LineWidth = width;
        path.Stroke();
    }

    private void Dot(Vector3 point, UIColor colour)
    {
        if (Project(point) is { } at)
        {
            Shadow.SetFill();
            UIBezierPath.FromOval(new CGRect(at.X - 5, at.Y - 5, 10, 10)).Fill();
            colour.SetFill();
            UIBezierPath.FromOval(new CGRect(at.X - 3.5, at.Y - 3.5, 7, 7)).Fill();
        }
    }

    private void Label(Vector2 at, string headline, string details, UIColor colour)
    {
        var big = new UIStringAttributes { Font = UIFont.SystemFontOfSize(15, UIFontWeight.Semibold), ForegroundColor = UIColor.White };
        var small = new UIStringAttributes { Font = UIFont.MonospacedDigitSystemFontOfSize(11, UIFontWeight.Regular), ForegroundColor = UIColor.FromWhiteAlpha(0.85f, 1) };
        var headlineText = new NSString(headline);
        var detailsText = new NSString(details);
        var headlineSize = headlineText.GetSizeUsingAttributes(big);
        var detailsSize = details.Length > 0 ? detailsText.GetSizeUsingAttributes(small) : CGSize.Empty;
        const float pad = 6;
        var width = Math.Max(headlineSize.Width, detailsSize.Width) + pad * 2;
        var height = headlineSize.Height + (details.Length > 0 ? detailsSize.Height + 2 : 0) + pad * 2;
        var box = new CGRect(at.X + 10, at.Y - height - 6, width, height);
        UIColor.FromRGBA(0.06f, 0.07f, 0.09f, 0.82f).SetFill();
        UIBezierPath.FromRoundedRect(box, 6).Fill();
        colour.SetFill();
        UIBezierPath.FromRoundedRect(new CGRect(box.X, box.Y, 3, box.Height), 1.5f).Fill();
        headlineText.DrawString(new CGPoint(box.X + pad, box.Y + pad), big);
        if (details.Length > 0)
        {
            detailsText.DrawString(new CGPoint(box.X + pad, box.Y + pad + headlineSize.Height + 2), small);
        }
    }

    private void SmallLabel(Vector2 at, string text, bool leftOf = false)
    {
        var attributes = new UIStringAttributes { Font = UIFont.MonospacedDigitSystemFontOfSize(11, UIFontWeight.Regular), ForegroundColor = UIColor.White };
        var label = new NSString(text);
        var size = label.GetSizeUsingAttributes(attributes);
        var x = leftOf ? at.X - size.Width - 3 : at.X - size.Width / 2 - 3;
        var box = new CGRect(x, at.Y - 2, size.Width + 6, size.Height + 4);
        UIColor.FromRGBA(0.06f, 0.07f, 0.09f, 0.7f).SetFill();
        UIBezierPath.FromRoundedRect(box, 4).Fill();
        label.DrawString(new CGPoint(box.X + 3, box.Y + 2), attributes);
    }

    private Vector2? Project(Vector3 world)
    {
        var p = _view.ProjectPoint(new SCNVector3(world.X, world.Y, world.Z));
        return p.Z is >= 0 and <= 1 ? new Vector2(p.X, p.Y) : null;
    }
}

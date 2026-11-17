using ManaUI;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>
/// A right-pointing arrow drawn as lines, between a flight's two airports. Not the "→" character: Roboto has none, and
/// the browser has no system font to fall back to, so it showed as a missing-glyph box there.
/// </summary>
public sealed class RouteArrow : UiElement
{
    private const float ArrowWidth = 22.0f;
    private const float ArrowHeight = 14.0f;
    private const float Thickness = 2.0f;

    private readonly Func<Vector4> _color;

    public RouteArrow(Func<Vector4> color)
    {
        _color = color;
        IsHitTestVisible = false;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize) => new(ArrowWidth, ArrowHeight);

    protected override void OnRender(ManaUiDrawList drawList)
    {
        var color = _color();
        var middle = Bounds.Y + Bounds.Height * 0.5f;
        var left = Bounds.X + (Bounds.Width - ArrowWidth) * 0.5f;
        var tip = new Vector2(left + ArrowWidth, middle);
        var head = ArrowHeight * 0.5f;
        drawList.DrawLine(new Vector2(left, middle), tip, color, Thickness);
        drawList.DrawLine(new Vector2(tip.X - head, middle - head), tip, color, Thickness);
        drawList.DrawLine(new Vector2(tip.X - head, middle + head), tip, color, Thickness);
    }
}

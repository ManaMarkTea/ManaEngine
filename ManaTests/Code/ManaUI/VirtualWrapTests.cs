using System.Numerics;
using ManaUI;

namespace ManaTests;

/// <summary>
/// The Import panel lists thousands of asset tiles; a plain WrapPanel laid out and drew every one each frame
/// (14 ms). A VirtualWrapPanel builds only the rows its scroll viewer shows.
/// </summary>
public static class VirtualWrapChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var root = new UiRoot(fonts);
        var made = 0;
        var panel = new VirtualWrapPanel
        {
            Spacing = 4,
            Items = Enumerable.Range(0, 2000).Cast<object>().ToList(),
            CreateItem = index => { made++; return new Border(); },
            ItemSize = () => new Vector2(60, 80)
        };
        var scroller = (ScrollViewer)panel.Scrollable().Size(400.0f, 300.0f);
        root.Content = scroller;
        root.UpdateLayout(new Vector2(500, 400));

        check("two thousand items build only a screenful", panel.RealizedCount is > 0 and < 60, $"{panel.RealizedCount} built");
        check("but the extent covers every row", panel.DesiredSize.Y > 2000 / 6 * 80, $"{panel.DesiredSize.Y:F0}px");

        scroller.Offset = new Vector2(0, 84 * 100);
        root.UpdateLayout(new Vector2(500, 400));
        var onScreen = panel.Children.Count(child => child.Bounds.IntersectsWith(scroller.Bounds));
        check("scrolled far down, the rows in view are the ones built", onScreen > 6 && panel.RealizedCount < 60,
            $"{onScreen} on screen of {panel.RealizedCount} built");

        var before = made;
        root.UpdateLayout(new Vector2(500, 400));
        check("an unchanged frame builds nothing", made == before, $"{made - before} built");
    }
}

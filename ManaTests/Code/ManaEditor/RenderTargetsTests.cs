using ManaCore.Graphics;
using ManaUI;
using ManaEditor.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// The Render Targets panel: it draws its tiles from the preview texture (which is what makes the backend fill it), a
/// click on a tile shows that one large, and a second click goes back to all of them.
/// </summary>
public static class RenderTargetsChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var root = new UiRoot(fonts) { Content = RenderTargetsManaView.Build(UiTheme.Dark()) };
        var size = new Vector2(600, 900);
        root.UpdateLayout(size);
        var drawList = root.Render();
        check("the panel draws from the preview texture", drawList.DrawsTexture(ManaRenderTargetPreview.TextureId), "");
        var tiles = Tiles(root).ToList();
        check("one tile for each target", tiles.Count == ManaRenderTargetPreview.Names.Length, $"{tiles.Count}");

        var first = tiles[3].Bounds;
        root.Input.InjectPointerMoved(first.Center);
        root.Input.InjectPointerPressed(first.Center, PointerButton.Left, 0.0);
        root.Input.InjectPointerReleased(first.Center, PointerButton.Left);
        root.UpdateLayout(size);
        tiles = Tiles(root).ToList();
        check("a click shows that target alone, larger", tiles.Count == 1 && tiles[0].Bounds.Width > first.Width * 1.5f,
            $"{tiles.Count} tiles, {tiles.FirstOrDefault()?.Bounds.Width:0} wide");

        root.UpdateLayout(size);
        check("and stays that way frame to frame", Tiles(root).Count() == 1 && ReferenceEquals(Tiles(root).First(), tiles[0]), "");

        var large = tiles[0].Bounds;
        root.Input.InjectPointerPressed(large.Center, PointerButton.Left, 1.0);
        root.Input.InjectPointerReleased(large.Center, PointerButton.Left);
        root.UpdateLayout(size);
        check("a second click goes back to all of them", Tiles(root).Count() == ManaRenderTargetPreview.Names.Length, "");
        root.Render();
        check("nothing else draws from the preview texture", !new UiRoot(fonts).Render().DrawsTexture(ManaRenderTargetPreview.TextureId), "");
    }

    private static IEnumerable<UiElement> Tiles(UiRoot root) => Walk(root.Content!).Where(element => element.GetType().Name == "PreviewTile");

    private static IEnumerable<UiElement> Walk(UiElement element)
    {
        yield return element;
        for (var i = 0; i < element.VisualChildCount; i++)
        {
            foreach (var below in Walk(element.GetVisualChild(i)))
            {
                yield return below;
            }
        }
    }
}

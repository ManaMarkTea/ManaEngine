using ManaCore.Graphics;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi;

/// <summary>
/// The Render Targets panel (Panels > Debugging): the frame's intermediate targets live -- HDR scene colour, depth,
/// the three sun shadow cascades and the placed lights per screen tile. Click one to see it large, again for all.
/// The backend draws them (ManaWebGpuTargetPreview) only while this panel is on screen.
/// </summary>
public static class RenderTargetsManaView
{
    public const string PanelId = "rendertargets";

    public static UiElement Build(UiTheme theme)
    {
        var focused = -1;
        // DynamicContent rebuilds when its key is a different object: one fixed key per state (all, or one tile).
        var keys = new object[ManaRenderTargetPreview.Names.Length + 1];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = new object();
        }
        var content = new DynamicContent().Showing<object>(() => keys[focused + 1], _ =>
        {
            if (focused >= 0)
            {
                return Tile(theme, focused, () => focused = -1);
            }

            var grid = new Grid().Columns("*, *", 8.0f).Rows("Auto, Auto, Auto", 8.0f);
            for (var i = 0; i < ManaRenderTargetPreview.Names.Length; i++)
            {
                var tile = i;
                grid.Children.Add(Tile(theme, tile, () => focused = tile).GridColumn(tile % 2).GridRow(tile / 2));
            }
            return grid;
        });

        var body = new StackPanel().Vertical(8.0f).Margin(theme.Spacing).Children(
            content,
            new TextBlock()
                .Text("Live from the last frame. Click a target to see it large. Depth and lights follow the main camera; "
                    + "shadow maps show light depth, darker nearer the sun, blue where nothing casts. See also Scene > PBR debug for per-pixel views.")
                .FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Wrap());
        return body.Scrollable().Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Disabled);
    }

    private static UiElement Tile(UiTheme theme, int tile, Action click) =>
        new StackPanel().Vertical(4.0f).Children(
            new TextBlock().Text(ManaRenderTargetPreview.Names[tile]).FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted),
            new PreviewTile(tile, click));

    /// <summary>One tile of the preview texture, 16:9 across the width it is given; a click calls back.</summary>
    private sealed class PreviewTile(int tile, Action click) : UiElement
    {
        protected override bool IsSelfHitTestVisible => true;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var width = availableSize.X;
            if (float.IsInfinity(width))
            {
                width = 320.0f;
            }
            return new Vector2(width, width * ManaRenderTargetPreview.TileHeight / ManaRenderTargetPreview.TileWidth);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            drawList.SetTexture(ManaRenderTargetPreview.TextureId);
            drawList.DrawImage(Bounds, ManaRenderTargetPreview.Uv(tile), Vector4.One, CornerRadius.Zero);
            drawList.SetTexture(0);
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button == PointerButton.Left)
            {
                click();
                e.Handled = true;
            }
        }
    }
}

using ManaUI;

namespace ManaTests;

/// <summary>Themes dressed in pictures: a nine-slice is nine quads that tile the rectangle exactly, and the Kenney theme exists.</summary>
public static class SkinChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var slice = new UiSkinSlice(new Vector4(0.25f, 0.25f, 0.5f, 0.5f), new Vector2(64, 64), new Thickness(16));
        var list = new ManaUiDrawList();
        list.Reset();
        list.DrawNineSlice(new UiRect(10, 20, 200, 100), slice, UiColor.White);
        list.Finish();
        check("a nine-slice draws nine quads", list.InstanceCount == 9, $"{list.InstanceCount}");

        var area = 0.0f;
        foreach (var instance in list.Instances)
        {
            area += (instance.Rect.Z - instance.Rect.X) * (instance.Rect.W - instance.Rect.Y);
        }
        check("and they cover the rectangle, overlapping neighbours by a pixel so no seam shows", area >= 200 * 100 && area < 200 * 100 + 800, $"{area}");

        var tiny = new ManaUiDrawList();
        tiny.Reset();
        tiny.DrawNineSlice(new UiRect(0, 0, 10, 8), slice, UiColor.White);
        tiny.Finish();
        var tinyArea = 0.0f;
        foreach (var instance in tiny.Instances)
        {
            tinyArea += (instance.Rect.Z - instance.Rect.X) * (instance.Rect.W - instance.Rect.Y);
        }
        check("a rectangle smaller than the corners is still covered", tinyArea >= 80 && tinyArea < 80 + 120, $"{tinyArea}");

        // On a display scaled 150%, a seam on a whole logical pixel is still half a device pixel off; they must land on device ones.
        var scaled = new ManaUiDrawList { PixelScale = 1.5f };
        scaled.Reset();
        scaled.PixelScale = 1.5f;
        scaled.DrawNineSlice(new UiRect(10.3f, 20.2f, 200.4f, 100.1f), slice, UiColor.White, 0.4f);
        scaled.Finish();
        var onPixels = true;
        var covered = 0.0f;
        foreach (var instance in scaled.Instances)
        {
            foreach (var edge in new[] { instance.Rect.X, instance.Rect.Y, instance.Rect.Z, instance.Rect.W })
            {
                onPixels &= MathF.Abs(edge * 1.5f - MathF.Round(edge * 1.5f)) < 0.001f;
            }
            covered += (instance.Rect.Z - instance.Rect.X) * (instance.Rect.W - instance.Rect.Y);
        }
        check("at 150% scale every seam and edge lies on a device pixel, and the rectangle is fully covered", onPixels && covered >= 200.4f * 100.1f, $"{covered}");

        var theme = UiTheme.Create(UiThemeKind.Kenney);
        check("the Kenney theme is its own kind, with no skin until a host gives it one", theme.Kind == UiThemeKind.Kenney && theme.Skin == null, "");
    }
}

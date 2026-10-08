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
        check("and they cover the rectangle, overlapping neighbours by half a pixel so no seam shows", area >= 200 * 100 && area < 200 * 100 + 400, $"{area}");

        var tiny = new ManaUiDrawList();
        tiny.Reset();
        tiny.DrawNineSlice(new UiRect(0, 0, 10, 8), slice, UiColor.White);
        tiny.Finish();
        var tinyArea = 0.0f;
        foreach (var instance in tiny.Instances)
        {
            tinyArea += (instance.Rect.Z - instance.Rect.X) * (instance.Rect.W - instance.Rect.Y);
        }
        check("a rectangle smaller than the corners is still covered", tinyArea >= 80 && tinyArea < 80 + 60, $"{tinyArea}");

        var theme = UiTheme.Create(UiThemeKind.Kenney);
        check("the Kenney theme is its own kind, with no skin until a host gives it one", theme.Kind == UiThemeKind.Kenney && theme.Skin == null, "");
    }
}

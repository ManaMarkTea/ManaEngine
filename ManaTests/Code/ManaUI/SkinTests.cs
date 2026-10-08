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
        check("and they tile the rectangle without blending overlapping artwork", area == 200 * 100, $"{area}");

        var tiny = new ManaUiDrawList();
        tiny.Reset();
        tiny.DrawNineSlice(new UiRect(0, 0, 10, 8), slice, UiColor.White);
        tiny.Finish();
        var tinyArea = 0.0f;
        foreach (var instance in tiny.Instances)
        {
            tinyArea += (instance.Rect.Z - instance.Rect.X) * (instance.Rect.W - instance.Rect.Y);
        }
        check("a rectangle smaller than the corners is still covered", tinyArea == 80, $"{tinyArea}");

        var inSource = true;
        foreach (var instance in list.Instances)
        {
            inSource &= instance.Uv.X >= slice.Uv.X && instance.Uv.Y >= slice.Uv.Y
                && instance.Uv.Z <= slice.Uv.Z && instance.Uv.W <= slice.Uv.W;
        }
        check("slice UVs never extrapolate beyond the source artwork", inSource, "");
        check("outer texel boundaries are preserved", list.Instances[0].Uv.X == slice.Uv.X
            && list.Instances[0].Uv.Y == slice.Uv.Y
            && list.Instances[8].Uv.Z == slice.Uv.Z && list.Instances[8].Uv.W == slice.Uv.W, "");

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

        // Another user of the atlas resetting it must not leave the theme drawing someone else's pixels.
        var atlas = new ManaImageAtlas(64, 64);
        var rebuilt = 0;
        UiSkin Make() => new UiSkin { Atlas = atlas, Generation = atlas.Generation, Refresh = () => { rebuilt++; return Make(); } };
        var skinned = UiTheme.Create(UiThemeKind.Kenney);
        skinned.Skin = Make();
        _ = skinned.Skin;
        atlas.Reset();
        var after = skinned.Skin;
        check("a skin is rebuilt once its atlas has been reset, and not before", rebuilt == 1 && after != null && !after.IsStale, $"{rebuilt}");

        var lcarsAtlas = new ManaImageAtlas(256, 256);
        var lcarsSkin = LcarsUiSkin.For(lcarsAtlas);
        var frame = default(UiSkinSlice);
        var capsule = default(UiSkinSlice);
        var lcarsParts = lcarsSkin != null && lcarsSkin.TryGet("panel", out frame)
            && lcarsSkin.TryGet("primary", out capsule);
        check("LCARS builds its frame and capsule artwork into the shared atlas", lcarsParts, "");
        if (lcarsParts)
        {
            byte Channel(UiSkinSlice part, int x, int y, int channel)
            {
                var ax = (int)MathF.Round(part.Uv.X * lcarsAtlas.Width) + x;
                var ay = (int)MathF.Round(part.Uv.Y * lcarsAtlas.Height) + y;
                return lcarsAtlas.Pixels[(ay * lcarsAtlas.Width + ax) * 4 + channel];
            }
            check("LCARS capsules keep transparent corners and an opaque centre",
                Channel(capsule, 0, 0, 3) == 0 && Channel(capsule, 32, 32, 3) == 255, "");
            var lcars = UiTheme.Lcars();
            check("LCARS frame colours remain distinct from its working surface",
                Channel(frame, 2, 32, 0) == (byte)MathF.Round(lcars.Border.X * 255)
                && Channel(frame, 32, 32, 0) == (byte)MathF.Round(lcars.Surface.X * 255)
                && frame.PreserveColors && frame.CornerScale == 0.6f, "");
            lcars.Skin = lcarsSkin;
            lcarsAtlas.Reset();
            check("LCARS artwork repacks after an atlas reset",
                lcars.Skin is { IsStale: false } refreshed && !ReferenceEquals(refreshed, lcarsSkin), "");
        }

        var standalone = new UiRoot(new ManaFontManager());
        var themeEvents = 0;
        var skinReadyDuringEvent = false;
        standalone.ThemeChanged += (_, changed) =>
        {
            themeEvents++;
            skinReadyDuringEvent = changed.Skin != null;
        };
        standalone.RequestTheme(UiThemeKind.Lcars);
        check("a standalone ManaUI root switches theme with artwork ready before notification",
            standalone.Theme.Kind == UiThemeKind.Lcars && skinReadyDuringEvent && themeEvents == 1, "");
        standalone.RequestTheme(UiThemeKind.Lcars);
        check("requesting the current theme does not rebuild views", themeEvents == 1, "");
        var customBuilds = 0;
        standalone.RegisterSkinFactory(UiThemeKind.Kenney, imageAtlas =>
        {
            customBuilds++;
            return new UiSkin { Atlas = imageAtlas, Generation = imageAtlas.Generation };
        });
        standalone.RequestTheme(UiThemeKind.Kenney);
        check("host artwork participates in ManaUI theme switching",
            customBuilds == 1 && standalone.Theme.Skin?.Atlas == standalone.Images && themeEvents == 2, "");
        var otherRoot = new UiRoot(new ManaFontManager(), UiTheme.Lcars());
        check("new ManaUI roots initialize LCARS artwork without an editor or host",
            otherRoot.Theme.Skin?.Atlas == otherRoot.Images, "");
        otherRoot.RequestTheme(UiThemeKind.Kenney);
        check("host skin registrations are isolated to their UI root",
            otherRoot.Theme.Skin == null && customBuilds == 1, "");
        standalone.Theme = UiTheme.Lcars();
        check("direct theme assignments also initialize artwork", standalone.Theme.Skin != null, "");

        var theme = UiTheme.Create(UiThemeKind.Kenney);
        check("the Kenney theme is its own kind, with no skin until a host gives it one", theme.Kind == UiThemeKind.Kenney && theme.Skin == null, "");
    }
}

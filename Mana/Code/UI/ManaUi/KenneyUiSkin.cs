using System.Runtime.CompilerServices;
using ManaCore.Imaging;
using ManaUI;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Builds the Kenney theme's skin from the Space UI pack (CC0): its pictures are decoded once and packed into
    /// the UI's colour atlas, where controls draw them as nine-slices. Null when the pack is not there (or, on the
    /// web, not yet downloaded), which leaves the theme flat rather than broken.
    /// </summary>
    public static class KenneyUiSkin
    {
        private static readonly ConditionalWeakTable<ManaImageAtlas, UiSkin?> Built = new();

        public static UiSkin? For(ManaImageAtlas atlas)
        {
            if (Built.TryGetValue(atlas, out var existing))
            {
                return existing;
            }

            var skin = Build(atlas);
            Built.Add(atlas, skin);
            return skin;
        }

        /// <summary>Puts the skin on a theme if it is the Kenney one.</summary>
        public static void Apply(UiTheme theme, ManaImageAtlas atlas)
        {
            if (theme.Kind == UiThemeKind.Kenney)
            {
                theme.Skin = For(atlas);
            }
        }

        private static UiSkin? Build(ManaImageAtlas atlas)
        {
            var skin = new UiSkin();
            // Insets are in the picture's own pixels: its rounded corner and border.
            // Pictures from the Space expansion and from the plain UI pack, which has what the first lacks: check boxes,
            // arrows, knobs and inputs. Insets are in the picture's own pixels (its rounded corner and border); 0 draws
            // the picture whole.
            const string Space = "zip:kenney_ui-pack-space-expansion.zip/PNG/";
            const string Plain = "zip:kenney_ui-pack.zip/PNG/";
            var parts = new (string Name, string File, float Inset)[]
            {
                ("button", Space + "Extra/Default/button_rectangle_depth.png", 12.0f),
                ("buttonPressed", Space + "Extra/Default/button_rectangle.png", 12.0f),
                ("panel", Space + "Extra/Default/button_rectangle.png", 14.0f),
                ("titleBar", Space + "Blue/Default/bar_square_large.png", 5.0f),
                ("primary", Space + "Blue/Default/bar_round_large.png", 11.0f),
                ("track", Space + "Extra/Default/bar_shadow_round_large.png", 11.0f),
                ("fill", Space + "Blue/Default/bar_round_large.png", 11.0f),
                ("fillSuccess", Space + "Green/Default/bar_round_large.png", 11.0f),
                ("fillWarning", Space + "Yellow/Default/bar_round_large.png", 11.0f),
                ("fillDanger", Space + "Red/Default/bar_round_large.png", 11.0f),
                ("primaryDanger", Space + "Red/Default/bar_round_large.png", 11.0f),
                ("glass", Space + "Extra/Default/panel_glass.png", 16.0f),
                ("octagon", Space + "Extra/Default/panel_glass_notches.png", 22.0f),
                ("input", Plain + "Extra/Default/input_rectangle.png", 12.0f),
                ("thumb", Plain + "Blue/Default/button_round_border.png", 0.0f),
                ("checkOff", Plain + "Blue/Default/check_square_grey.png", 0.0f),
                ("checkOn", Plain + "Blue/Default/check_square_color_checkmark.png", 0.0f),
                ("arrowDown", Plain + "Blue/Default/arrow_basic_s_small.png", 0.0f),
                ("arrowUp", Plain + "Blue/Default/arrow_basic_n_small.png", 0.0f),
                ("arrowRight", Plain + "Blue/Default/arrow_basic_e_small.png", 0.0f)
            };

            var uvs = new Dictionary<string, (Vector4 Uv, Vector2 Size)>();
            foreach (var (name, file, inset) in parts)
            {
                if (!uvs.TryGetValue(file, out var packed))
                {
                    var path = ManaSpritePictures.Resolve(file);
                    if (path == null)
                    {
                        // The Space pack is the essential one; a missing plain pack only costs its extras.
                        if (file.StartsWith(Plain, StringComparison.Ordinal))
                        {
                            continue;
                        }
                        return null;
                    }

                    ManaImage image;
                    try
                    {
                        image = ManaImageCodec.Load(path);
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException)
                    {
                        return null;
                    }

                    if (atlas.Add(image.Pixels, image.Width, image.Height) is not { } uv)
                    {
                        return null;
                    }

                    packed = (uv, new Vector2(image.Width, image.Height));
                    uvs[file] = packed;
                }

                skin.Add(name, new UiSkinSlice(packed.Uv, packed.Size, new Thickness(inset)));
            }

            return skin;
        }
    }
}

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
        private const string Pack = "zip:kenney_ui-pack-space-expansion.zip/PNG/";
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
            var parts = new (string Name, string File, float Inset)[]
            {
                ("button", "Extra/Default/button_rectangle.png", 12.0f),
                ("panel", "Extra/Default/panel_rectangle.png", 14.0f),
                ("titleBar", "Blue/Default/bar_square_large.png", 5.0f),
                ("primary", "Blue/Default/bar_round_large.png", 11.0f),
                ("track", "Extra/Default/bar_shadow_round_large.png", 11.0f),
                ("fill", "Blue/Default/bar_round_large.png", 11.0f)
            };

            var uvs = new Dictionary<string, (Vector4 Uv, Vector2 Size)>();
            foreach (var (name, file, inset) in parts)
            {
                if (!uvs.TryGetValue(file, out var packed))
                {
                    var path = ManaSpritePictures.Resolve(Pack + file);
                    if (path == null)
                    {
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

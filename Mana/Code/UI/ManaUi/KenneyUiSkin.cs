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
        private static readonly ConditionalWeakTable<ManaImageAtlas, Holder> Built = new();

        private sealed class Holder
        {
            public int Generation = -1;
            public UiSkin? Skin;
        }

        public static UiSkin? For(ManaImageAtlas atlas)
        {
            var holder = Built.GetOrCreateValue(atlas);
            if (holder.Generation != atlas.Generation)
            {
                holder.Generation = atlas.Generation;
                holder.Skin = Build(atlas);
            }

            return holder.Skin;
        }

        /// <summary>Puts the skin on a theme if it is the Kenney one.</summary>
        public static void Apply(UiTheme theme, ManaImageAtlas atlas)
        {
            if (theme.Kind == UiThemeKind.Kenney)
            {
                theme.Skin = For(atlas);
            }
        }

        private const int Gutter = 4;

        private static readonly Dictionary<string, ManaImage> Decoded = new(StringComparer.Ordinal);

        private static ManaImage Padded(ManaImage image, int border)
        {
            var width = image.Width + border * 2;
            var height = image.Height + border * 2;
            var result = new ManaImage(width, height);
            for (var y = 0; y < height; y++)
            {
                var sy = Math.Clamp(y - border, 0, image.Height - 1);
                for (var x = 0; x < width; x++)
                {
                    var sx = Math.Clamp(x - border, 0, image.Width - 1);
                    Array.Copy(image.Pixels, (sy * image.Width + sx) * 4, result.Pixels, (y * width + x) * 4, 4);
                }
            }

            return result;
        }

        private static UiSkin? Build(ManaImageAtlas atlas)
        {
            var skin = new UiSkin { Atlas = atlas, Generation = atlas.Generation, Refresh = () => For(atlas) };
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
                ("scrollTrack", Space + "Extra/Default/bar_shadow_square_small.png", 4.0f),
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

            // Every progress fill the pack draws: four colours, round or square, plain or glossy.
            var fills = new List<(string Name, string File, float Inset)>();
            foreach (var (tone, colour) in new[] { ("Accent", "Blue"), ("Success", "Green"), ("Warning", "Yellow"), ("Danger", "Red") })
            {
                foreach (var shape in new[] { "round", "square" })
                {
                    foreach (var gloss in new[] { false, true })
                    {
                        fills.Add(($"fill.{tone}.{shape}{(gloss ? ".gloss" : string.Empty)}",
                            Space + $"{colour}/Default/bar_{shape}{(gloss ? "_gloss" : string.Empty)}_large.png", shape == "round" ? 11.0f : 4.0f));
                    }
                }
            }
            parts = parts.Concat(fills).Append(("track.square", Space + "Extra/Default/bar_shadow_square_large.png", 4.0f)).ToArray();

            var uvs = new Dictionary<string, (Vector4 Uv, Vector2 Size)>();
            foreach (var (name, file, inset) in parts)
            {
                if (!uvs.TryGetValue(file, out var packed))
                {
                    // Decoded once per run: an atlas reset (the Import panel filling it) rebuilds the skin, and that
                    // should be a repack, not a reread of thirty pictures.
                    if (!Decoded.TryGetValue(file, out var padded))
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

                        try
                        {
                            // Packed with a border of its own edge pixels, and the picture's UVs taken from inside it.
                            // The atlas filters and mips its contents, so a picture touching transparent padding fades
                            // at its rim; repeating the edge keeps every neighbour the same colour.
                            padded = Padded(ManaImageCodec.Load(path), Gutter);
                        }
                        catch (Exception exception) when (exception is IOException or InvalidDataException)
                        {
                            return null;
                        }

                        Decoded[file] = padded;
                    }

                    var image = (Width: padded.Width - Gutter * 2, Height: padded.Height - Gutter * 2);
                    if (atlas.Add(padded.Pixels, padded.Width, padded.Height) is not { } outer)
                    {
                        return null;
                    }

                    var uv = new Vector4(
                        outer.X + Gutter / (float)atlas.Width, outer.Y + Gutter / (float)atlas.Height,
                        outer.Z - Gutter / (float)atlas.Width, outer.W - Gutter / (float)atlas.Height);
                    packed = (uv, new Vector2(image.Width, image.Height));
                    uvs[file] = packed;
                }

                skin.Add(name, new UiSkinSlice(packed.Uv, packed.Size, new Thickness(inset)));
            }

            return skin;
        }
    }
}

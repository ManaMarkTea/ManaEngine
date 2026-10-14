using System.Runtime.CompilerServices;

namespace ManaUI;

/// <summary>Classic square bevels, generated into the shared atlas without platform artwork.</summary>
public static class Windows31UiSkin
{
    private sealed class Holder
    {
        public UiSkin? Skin;
    }

    private static readonly ConditionalWeakTable<ManaImageAtlas, Holder> Built = new();

    public static UiSkin? For(ManaImageAtlas atlas)
    {
        var holder = Built.GetOrCreateValue(atlas);
        if (holder.Skin == null || holder.Skin.IsStale)
        {
            holder.Skin = Build(atlas);
        }
        return holder.Skin;
    }

    private static UiSkin? Build(ManaImageAtlas atlas)
    {
        var skin = new UiSkin { Atlas = atlas, Generation = atlas.Generation, Refresh = () => For(atlas) };
        const int size = 16;
        const int gutter = 2;
        const int outer = size + gutter * 2;

        bool Add(string name, string fill, bool recessed = false, bool bevel = true)
        {
            var pixels = new byte[outer * outer * 4];
            for (var y = 0; y < outer; y++)
            {
                for (var x = 0; x < outer; x++)
                {
                    var px = Math.Clamp(x - gutter, 0, size - 1);
                    var py = Math.Clamp(y - gutter, 0, size - 1);
                    var color = UiColor.FromHex(fill);
                    if (bevel)
                    {
                        if (px == 0 || py == 0)
                        {
                            color = UiColor.FromHex(recessed ? "#808080" : "#FFFFFF");
                        }
                        else if (px == size - 1 || py == size - 1)
                        {
                            color = UiColor.FromHex(recessed ? "#FFFFFF" : "#000000");
                        }
                        else if (px == 1 || py == 1)
                        {
                            color = UiColor.FromHex(recessed ? "#000000" : "#DFDFDF");
                        }
                        else if (px == size - 2 || py == size - 2)
                        {
                            color = UiColor.FromHex(recessed ? "#DFDFDF" : "#808080");
                        }
                    }
                    var index = (y * outer + x) * 4;
                    pixels[index] = (byte)MathF.Round(color.X * 255);
                    pixels[index + 1] = (byte)MathF.Round(color.Y * 255);
                    pixels[index + 2] = (byte)MathF.Round(color.Z * 255);
                    pixels[index + 3] = 255;
                }
            }
            if (atlas.Add(pixels, outer, outer) is not { } uv)
            {
                return false;
            }
            uv += new Vector4(gutter / (float)atlas.Width, gutter / (float)atlas.Height,
                -gutter / (float)atlas.Width, -gutter / (float)atlas.Height);
            skin.Add(name, new UiSkinSlice(uv, new Vector2(size), new Thickness(3))
            {
                PreserveColors = true,
                CornerScale = 1
            });
            return true;
        }

        return Add("panel", "#C0C0C0") && Add("button", "#C0C0C0")
            && Add("buttonPressed", "#C0C0C0", recessed: true)
            && Add("input", "#FFFFFF", recessed: true)
            && Add("primaryPressed", "#000080", recessed: true) && Add("primary", "#000080") && Add("titleBar", "#000080", bevel: false)
            && Add("track", "#C0C0C0", recessed: true) && Add("fill", "#000080", bevel: false)
            && Add("scrollbarThumb", "#C0C0C0") && Add("thumb", "#C0C0C0") ? skin : null;
    }
}

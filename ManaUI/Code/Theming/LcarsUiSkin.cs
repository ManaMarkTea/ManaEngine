using System.Runtime.CompilerServices;

namespace ManaUI;

/// <summary>LCARS frames and capsule controls, rasterized once into the shared nine-slice atlas.</summary>
public static class LcarsUiSkin
{
    private sealed class Holder
    {
        public int Generation = -1;
        public UiSkin? Skin;
    }

    private static readonly ConditionalWeakTable<ManaImageAtlas, Holder> Built = new();

    public static UiSkin? For(ManaImageAtlas atlas)
    {
        var holder = Built.GetOrCreateValue(atlas);
        if (holder.Generation != atlas.Generation)
        {
            holder.Skin = Build(atlas);
            holder.Generation = atlas.Generation;
        }
        return holder.Skin;
    }

    private static UiSkin? Build(ManaImageAtlas atlas)
    {
        var theme = UiTheme.Lcars();
        var skin = new UiSkin { Atlas = atlas, Generation = atlas.Generation, Refresh = () => For(atlas) };
        const int size = 64;
        const int gutter = 4;
        var outerSize = size + gutter * 2;

        bool Add(string name, bool frame, Vector4 color)
        {
            var pixels = new byte[outerSize * outerSize * 4];
            for (var y = 0; y < outerSize; y++)
            {
                for (var x = 0; x < outerSize; x++)
                {
                    var sample = Vector4.Zero;
                    // Supersample the code-authored silhouettes so curves stay clean at different DPI scales.
                    for (var sy = 0; sy < 4; sy++)
                    {
                        for (var sx = 0; sx < 4; sx++)
                        {
                            var px = Math.Clamp(x - gutter, 0, size - 1) + (sx + 0.5f) / 4.0f;
                            var py = Math.Clamp(y - gutter, 0, size - 1) + (sy + 0.5f) / 4.0f;
                            if (frame)
                            {
                                var outer = px >= 16 || py >= 16
                                    || Vector2.Distance(new Vector2(px, py), new Vector2(16)) <= 16;
                                var inner = px >= 12 && py >= 6 && (px >= 28 || py >= 22
                                    || Vector2.Distance(new Vector2(px, py), new Vector2(28, 22)) <= 16);
                                if (outer)
                                {
                                    sample += inner ? theme.Surface : color;
                                }
                            }
                            else
                            {
                                var centre = new Vector2(32);
                                if (Vector2.Distance(new Vector2(px, py), centre) <= 32)
                                {
                                    sample += color;
                                }
                            }
                        }
                    }
                    sample /= 16;
                    var index = (y * outerSize + x) * 4;
                    // Keep straight-alpha colours at the transparent edge to avoid a dark filtering halo.
                    if (sample.W > 0)
                    {
                        pixels[index] = (byte)Math.Clamp(MathF.Round(sample.X / sample.W * 255), 0, 255);
                        pixels[index + 1] = (byte)Math.Clamp(MathF.Round(sample.Y / sample.W * 255), 0, 255);
                        pixels[index + 2] = (byte)Math.Clamp(MathF.Round(sample.Z / sample.W * 255), 0, 255);
                    }
                    pixels[index + 3] = (byte)Math.Clamp(MathF.Round(sample.W * 255), 0, 255);
                }
            }
            if (atlas.Add(pixels, outerSize, outerSize) is not { } uv)
            {
                return false;
            }
            uv += new Vector4(gutter / (float)atlas.Width, gutter / (float)atlas.Height,
                -gutter / (float)atlas.Width, -gutter / (float)atlas.Height);
            skin.Add(name, new UiSkinSlice(uv, new Vector2(size), new Thickness(frame ? 28 : 32))
            {
                PreserveColors = frame,
                CornerScale = frame ? 0.6f : null
            });
            return true;
        }

        return Add("panel", true, theme.Border) && Add("primary", false, theme.Accent)
            && Add("primaryDanger", false, theme.Danger) ? skin : null;
    }
}

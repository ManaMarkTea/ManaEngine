namespace ManaUI
{
    /// <summary>
    /// Colours travel as Vector4 (repo convention, straight-alpha, sRGB) and are packed to RGBA8 only at
    /// the moment they enter the draw list.
    /// </summary>
    public static class UiColor
    {
        public static readonly Vector4 Transparent = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
        public static readonly Vector4 White = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
        public static readonly Vector4 Black = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);

        public static uint Pack(Vector4 color)
        {
            var r = (uint)(Math.Clamp(color.X, 0.0f, 1.0f) * 255.0f + 0.5f);
            var g = (uint)(Math.Clamp(color.Y, 0.0f, 1.0f) * 255.0f + 0.5f);
            var b = (uint)(Math.Clamp(color.Z, 0.0f, 1.0f) * 255.0f + 0.5f);
            var a = (uint)(Math.Clamp(color.W, 0.0f, 1.0f) * 255.0f + 0.5f);
            return r | (g << 8) | (b << 16) | (a << 24);
        }

        public static Vector4 Unpack(uint packed)
        {
            return new Vector4(
                (packed & 0xFF) / 255.0f,
                ((packed >> 8) & 0xFF) / 255.0f,
                ((packed >> 16) & 0xFF) / 255.0f,
                ((packed >> 24) & 0xFF) / 255.0f);
        }

        /// <summary>Parses "#RGB", "#RRGGBB" or "#RRGGBBAA" (leading '#' optional).</summary>
        public static Vector4 FromHex(string hex)
        {
            var span = hex.AsSpan().TrimStart('#');

            static float Channel(ReadOnlySpan<char> text)
            {
                return Convert.ToInt32(text.ToString(), 16) / 255.0f;
            }

            switch (span.Length)
            {
                case 3:
                    return new Vector4(
                        Convert.ToInt32(new string(span[0], 2), 16) / 255.0f,
                        Convert.ToInt32(new string(span[1], 2), 16) / 255.0f,
                        Convert.ToInt32(new string(span[2], 2), 16) / 255.0f,
                        1.0f);
                case 6:
                    return new Vector4(Channel(span[..2]), Channel(span[2..4]), Channel(span[4..6]), 1.0f);
                case 8:
                    return new Vector4(Channel(span[..2]), Channel(span[2..4]), Channel(span[4..6]), Channel(span[6..8]));
                default:
                    throw new FormatException($"'{hex}' is not a 3, 6 or 8 digit hex colour.");
            }
        }

        public static Vector4 WithAlpha(this Vector4 color, float alpha)
        {
            return new Vector4(color.X, color.Y, color.Z, alpha);
        }

        /// <summary>
        /// Hue in degrees (0..360), saturation and value in 0..1. Hue is undefined for a grey, and is
        /// returned as zero -- callers editing a colour must keep their own hue rather than round-tripping
        /// through this, or dragging saturation to zero would lose which hue was being edited.
        /// </summary>
        public static Vector3 ToHsv(Vector4 color)
        {
            var max = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
            var min = MathF.Min(color.X, MathF.Min(color.Y, color.Z));
            var chroma = max - min;

            var hue = 0.0f;
            if (chroma > 0.00001f)
            {
                if (max == color.X)
                {
                    hue = (color.Y - color.Z) / chroma % 6.0f;
                }
                else if (max == color.Y)
                {
                    hue = (color.Z - color.X) / chroma + 2.0f;
                }
                else
                {
                    hue = (color.X - color.Y) / chroma + 4.0f;
                }

                hue *= 60.0f;
                if (hue < 0.0f)
                {
                    hue += 360.0f;
                }
            }

            return new Vector3(hue, max <= 0.0f ? 0.0f : chroma / max, max);
        }

        /// <summary>Hue in degrees, saturation and value in 0..1, back to RGB.</summary>
        public static Vector4 FromHsv(float hue, float saturation, float value, float alpha = 1.0f)
        {
            hue = ((hue % 360.0f) + 360.0f) % 360.0f;
            saturation = Math.Clamp(saturation, 0.0f, 1.0f);
            value = Math.Clamp(value, 0.0f, 1.0f);

            var chroma = value * saturation;
            var sector = hue / 60.0f;
            var second = chroma * (1.0f - MathF.Abs(sector % 2.0f - 1.0f));
            var match = value - chroma;

            var rgb = (int)sector switch
            {
                0 => new Vector3(chroma, second, 0.0f),
                1 => new Vector3(second, chroma, 0.0f),
                2 => new Vector3(0.0f, chroma, second),
                3 => new Vector3(0.0f, second, chroma),
                4 => new Vector3(second, 0.0f, chroma),
                _ => new Vector3(chroma, 0.0f, second)
            };

            return new Vector4(rgb.X + match, rgb.Y + match, rgb.Z + match, alpha);
        }

        /// <summary>
        /// "#RRGGBB", or "#RRGGBBAA" when the colour is not fully opaque -- a trailing "FF" on every value
        /// is noise in a field people read at a glance.
        /// </summary>
        public static string ToHex(Vector4 color, bool includeAlpha = false)
        {
            static int Channel(float value) => (int)(Math.Clamp(value, 0.0f, 1.0f) * 255.0f + 0.5f);

            var hex = $"#{Channel(color.X):X2}{Channel(color.Y):X2}{Channel(color.Z):X2}";
            return includeAlpha || color.W < 1.0f ? hex + $"{Channel(color.W):X2}" : hex;
        }

        /// <summary>
        /// Toward white by <paramref name="amount"/>, alpha untouched. Used for the top of a gradient,
        /// where mixing toward white keeps the hue rather than washing it out the way raising every
        /// channel equally would.
        /// </summary>
        public static Vector4 Lighten(Vector4 color, float amount)
        {
            var t = Math.Clamp(amount, 0.0f, 1.0f);
            return new Vector4(
                color.X + (1.0f - color.X) * t,
                color.Y + (1.0f - color.Y) * t,
                color.Z + (1.0f - color.Z) * t,
                color.W);
        }

        /// <summary>Toward black by <paramref name="amount"/>, alpha untouched.</summary>
        public static Vector4 Darken(Vector4 color, float amount)
        {
            var t = 1.0f - Math.Clamp(amount, 0.0f, 1.0f);
            return new Vector4(color.X * t, color.Y * t, color.Z * t, color.W);
        }

        public static Vector4 Lerp(Vector4 from, Vector4 to, float amount)
        {
            return Vector4.Lerp(from, to, Math.Clamp(amount, 0.0f, 1.0f));
        }
    }
}

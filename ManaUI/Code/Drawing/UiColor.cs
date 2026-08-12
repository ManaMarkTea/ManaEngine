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

        public static Vector4 Lerp(Vector4 from, Vector4 to, float amount)
        {
            return Vector4.Lerp(from, to, Math.Clamp(amount, 0.0f, 1.0f));
        }
    }
}

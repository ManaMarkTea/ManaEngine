namespace ManaUI
{
    /// <summary>
    /// How a control dresses itself in the theme's skin when it has one: <c>if (!drawList.DrawSkin(theme, "button", rect, fill))</c>
    /// then draw the flat version. The skin's art is pale, so the control's own fill colour tints it to the theme.
    /// </summary>
    public static class UiSkinDrawing
    {
        /// <summary>
        /// A whole skin picture (no stretching) drawn into <paramref name="rect"/>: an arrow, a check box. False when the
        /// theme has no such picture.
        /// </summary>
        public static bool DrawSkinIcon(this ManaUiDrawList drawList, UiTheme theme, string name, UiRect rect, Vector4 tint)
        {
            if (theme.Skin is not { } skin || !skin.TryGet(name, out var slice))
            {
                return false;
            }

            // Half a texel in from the edge, so bilinear sampling never reads the atlas beside the picture.
            var halfU = 0.5f * (slice.Uv.Z - slice.Uv.X) / slice.Size.X;
            var halfV = 0.5f * (slice.Uv.W - slice.Uv.Y) / slice.Size.Y;
            // On whole device pixels, outwards, like a nine-slice's edges.
            var pixel = MathF.Max(drawList.PixelScale, 1.0f);
            var left = MathF.Floor(rect.X * pixel) / pixel;
            var top = MathF.Floor(rect.Y * pixel) / pixel;
            rect = new UiRect(left, top, MathF.Ceiling(rect.Right * pixel) / pixel - left, MathF.Ceiling(rect.Bottom * pixel) / pixel - top);
            drawList.DrawImage(rect, new Vector4(slice.Uv.X + halfU, slice.Uv.Y + halfV, slice.Uv.Z - halfU, slice.Uv.W - halfV), tint, CornerRadius.Zero);
            return true;
        }

        /// <summary>Whether the theme's skin has a slice of this name.</summary>
        public static bool HasSkin(this UiTheme theme, string name) => theme.Skin != null && theme.Skin.TryGet(name, out _);

        /// <summary>
        /// Draws the named slice over <paramref name="rect"/>, tinted by <paramref name="tint"/> (alpha kept). Corners
        /// are sized to the rectangle: a control shorter than the picture shrinks them to fit, a big panel keeps small ones.
        /// False when the theme has no skin or no such slice, and nothing was drawn.
        /// </summary>
        public static bool DrawSkin(this ManaUiDrawList drawList, UiTheme theme, string name, UiRect rect, Vector4 tint, bool lift = true)
        {
            if (theme.Skin is not { } skin || !skin.TryGet(name, out var slice) || tint.W <= 0.0f)
            {
                return false;
            }

            var shortSide = MathF.Min(rect.Width, rect.Height);
            var scale = shortSide < slice.Size.Y * 1.5f ? MathF.Min(1.0f, shortSide / slice.Size.Y) : 0.4f;
            if (lift)
            {
                // Pale art times a dark tint goes muddy; lift the tint so the result lands on the colour asked for.
                tint = new Vector4(MathF.Min(tint.X * 1.18f, 1.0f), MathF.Min(tint.Y * 1.18f, 1.0f), MathF.Min(tint.Z * 1.18f, 1.0f), tint.W);
            }

            drawList.DrawNineSlice(rect, slice, tint, scale);
            return true;
        }
    }
}

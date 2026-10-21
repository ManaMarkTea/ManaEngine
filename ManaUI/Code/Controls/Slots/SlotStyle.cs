namespace ManaUI
{
    /// <summary>
    /// The look shared by slots, slot grids, hotbars and panel frames: a dark, bevelled industrial palette by default
    /// (an inventory in a factory game), or one taken from a theme.
    /// </summary>
    public sealed record SlotStyle
    {
        public static SlotStyle Industrial { get; } = new();

        public Vector4 Slot { get; init; } = new(0.17f, 0.17f, 0.18f, 1);
        public Vector4 SlotHover { get; init; } = new(0.27f, 0.27f, 0.28f, 1);
        public Vector4 SlotLight { get; init; } = new(0.36f, 0.36f, 0.37f, 1);
        public Vector4 SlotShade { get; init; } = new(0.06f, 0.06f, 0.07f, 1);
        public Vector4 Unavailable { get; init; } = new(0.42f, 0.11f, 0.09f, 1);
        public Vector4 Selected { get; init; } = new(1, 0.63f, 0.16f, 1);
        public Vector4 Progress { get; init; } = new(0.36f, 0.8f, 0.3f, 1);
        public Vector4 Count { get; init; } = new(1, 1, 1, 1);
        public Vector4 CountShadow { get; init; } = new(0, 0, 0, 0.9f);
        public Vector4 Hotkey { get; init; } = new(0.82f, 0.82f, 0.82f, 0.9f);
        public Vector4 Frame { get; init; } = new(0.21f, 0.21f, 0.22f, 0.97f);
        public Vector4 FrameEdge { get; init; } = new(0.05f, 0.05f, 0.05f, 1);
        public Vector4 Inset { get; init; } = new(0.12f, 0.12f, 0.13f, 1);
        public Vector4 Title { get; init; } = new(1, 0.9f, 0.75f, 1);
        public Vector4 Groove { get; init; } = new(0.15f, 0.15f, 0.16f, 1);
        public float CountSize { get; init; } = 13;
        public float HotkeySize { get; init; } = 10;
        public float TitleSize { get; init; } = 17;

        /// <summary>The slot look in a theme's own colours.</summary>
        public static SlotStyle FromTheme(UiTheme theme) => new()
        {
            Slot = theme.SurfaceAlt,
            SlotHover = Mix(theme.SurfaceAlt, theme.Hover),
            SlotLight = theme.BorderStrong,
            SlotShade = theme.Background,
            Selected = theme.Accent,
            Progress = theme.Success,
            Unavailable = Mix(theme.SurfaceAlt, theme.Danger.WithAlpha(0.45f)),
            Count = theme.Text,
            Hotkey = theme.TextMuted,
            Frame = theme.Surface,
            FrameEdge = theme.Border,
            Inset = theme.Background,
            Title = theme.Text,
            Groove = theme.SurfaceAlt
        };

        private static Vector4 Mix(Vector4 under, Vector4 over) =>
            new(under.X + (over.X - under.X) * over.W, under.Y + (over.Y - under.Y) * over.W, under.Z + (over.Z - under.Z) * over.W, under.W);

        /// <summary>A count as a slot shows it: 950, 1.2k, 45k, 3.1M.</summary>
        public static string FormatCount(long count) => count switch
        {
            < 1000 => count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            < 10_000 => (Math.Floor(count / 100.0) / 10).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "k",
            < 1_000_000 => (count / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture) + "k",
            _ => (Math.Floor(count / 100_000.0) / 10).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "M"
        };

        /// <summary>The empty, bevelled square every slot is drawn on: lit on its top-left edge, shaded on the bottom-right.</summary>
        public void DrawWell(ManaUiDrawList drawList, UiRect rect, Vector4 fill)
        {
            drawList.FillRect(rect, SlotShade);
            drawList.FillRect(new UiRect(rect.X, rect.Y, rect.Width - 1, rect.Height - 1), SlotLight);
            drawList.FillRect(new UiRect(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2), fill);
        }

        /// <summary>A short line of text with its top-left (or, right-aligned, its top-right) at a point; returns its width.</summary>
        public static float DrawText(ManaUiDrawList drawList, UiRoot root, string text, Vector2 at, float size, Vector4 color, bool alignRight = false, Vector4? shadow = null)
        {
            var font = root.Fonts.Get(root.Theme.FontFamily, size, root.Scale);
            var width = font.MeasureWidth(text);
            var x = alignRight ? at.X - width : at.X;
            if (shadow is { } dark)
            {
                Glyphs(drawList, font, text, new Vector2(x + 1, at.Y + 1), dark);
            }
            Glyphs(drawList, font, text, new Vector2(x, at.Y), color);
            return width;
        }

        private static void Glyphs(ManaUiDrawList drawList, ManaFont font, string text, Vector2 at, Vector4 color)
        {
            var pen = at.X;
            foreach (var ch in text)
            {
                var glyph = font.GetGlyph(ch);
                if (glyph.HasPixels)
                {
                    drawList.DrawGlyph(new UiRect(pen + glyph.Offset.X, font.SnapToPixel(at.Y + font.Ascent + glyph.Offset.Y), glyph.Size.X, glyph.Size.Y), glyph.AtlasUv, color);
                }
                pen += glyph.Advance;
            }
        }
    }
}

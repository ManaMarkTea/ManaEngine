namespace ManaUI
{
    public enum UiIconDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    /// <summary>
    /// The small chrome icons -- arrows, ticks, crosses -- drawn from the box primitive rather than taken
    /// from a font.
    /// <para>
    /// Fonts were the obvious source and the wrong one. Roboto has no U+25B6, U+2713 or any of the
    /// geometric-shape arrows, so every one of them rendered as .notdef: the tofu box people saw in the
    /// tree and on the window close buttons. Shipping a font that has them would trade a visible bug for a
    /// latent one, since the next theme font would bring it straight back.
    /// </para>
    /// <para>
    /// A triangle is not expressible as one axis-aligned rounded box, so these are drawn as a short
    /// staircase of rows. At icon size the rows are a pixel or two each and the antialiasing on every row's
    /// rounded edges does the rest -- it reads as a triangle, and it reads the same in every font.
    /// </para>
    /// </summary>
    public static class UiIcons
    {
        /// <summary>Rows in a triangle. More is smoother and costs one instance each.</summary>
        private const int TriangleSteps = 7;

        /// <summary>The character each icon prefers, when a font in the chain can actually draw it.</summary>
        private static int TriangleCodepoint(UiIconDirection direction)
        {
            return direction switch
            {
                UiIconDirection.Up => 0x25B2,
                UiIconDirection.Down => 0x25BC,
                UiIconDirection.Left => 0x25C0,
                _ => 0x25B6
            };
        }

        /// <summary>
        /// A triangle, using the font's own glyph when one is reachable and falling back to the drawn
        /// staircase when it is not.
        /// <para>
        /// A real glyph is hinted and properly antialiased, so it looks better than seven stacked rows ever
        /// will. The staircase stays as the guarantee: on a machine with no symbol font the arrow is still
        /// an arrow rather than a tofu box, which is what it used to be.
        /// </para>
        /// </summary>
        public static void Triangle(
            ManaUiDrawList drawList,
            UiRect box,
            UiIconDirection direction,
            Vector4 color,
            ManaFont? font)
        {
            if (font != null && TryGlyph(drawList, box, TriangleCodepoint(direction), color, font))
            {
                return;
            }

            Triangle(drawList, box, direction, color);
        }

        /// <summary>A check mark, preferring U+2713 when the font chain has it.</summary>
        public static void Check(
            ManaUiDrawList drawList,
            UiRect box,
            Vector4 color,
            ManaFont? font,
            float thickness = 2.0f)
        {
            if (font != null && TryGlyph(drawList, box, 0x2713, color, font))
            {
                return;
            }

            Check(drawList, box, color, thickness);
        }

        /// <summary>
        /// Draws a codepoint centred in the box, or reports false when nothing in the chain has it.
        /// Scaled down if the glyph is larger than the box, so an icon never overruns its slot.
        /// </summary>
        private static bool TryGlyph(
            ManaUiDrawList drawList,
            UiRect box,
            int codepoint,
            Vector4 color,
            ManaFont font)
        {
            var glyph = font.GetGlyph(codepoint);
            if (!glyph.IsDrawable || glyph.Size.X <= 0.0f || glyph.Size.Y <= 0.0f)
            {
                return false;
            }

            var scale = MathF.Min(1.0f, MathF.Min(box.Width / glyph.Size.X, box.Height / glyph.Size.Y));
            var size = glyph.Size * scale;

            drawList.DrawGlyph(
                new UiRect(
                    font.SnapToPixel(box.Center.X - size.X * 0.5f),
                    font.SnapToPixel(box.Center.Y - size.Y * 0.5f),
                    size.X,
                    size.Y),
                glyph.AtlasUv,
                color);

            return true;
        }

        /// <summary>
        /// A solid triangle filling <paramref name="box"/>, pointing whichever way is asked. Sized from the
        /// box rather than centred in it, so callers control the icon's weight by the rect they pass.
        /// </summary>
        public static void Triangle(
            ManaUiDrawList drawList,
            UiRect box,
            UiIconDirection direction,
            Vector4 color)
        {
            if (box.Width <= 0.0f || box.Height <= 0.0f)
            {
                return;
            }

            var vertical = direction is UiIconDirection.Up or UiIconDirection.Down;
            var steps = TriangleSteps;

            for (var i = 0; i < steps; i++)
            {
                // The row's distance along the axis the triangle points, and how wide it is across.
                var along = (i + 0.5f) / steps;
                var across = 1.0f - along;

                if (vertical)
                {
                    var rowHeight = box.Height / steps;
                    var rowWidth = box.Width * across;
                    var y = direction == UiIconDirection.Down
                        ? box.Y + i * rowHeight
                        : box.Bottom - (i + 1) * rowHeight;

                    // Overlapped by half a pixel so consecutive rows leave no seam between them.
                    drawList.FillRect(
                        new UiRect(box.Center.X - rowWidth * 0.5f, y, rowWidth, rowHeight + 0.5f),
                        color);
                }
                else
                {
                    var columnWidth = box.Width / steps;
                    var columnHeight = box.Height * across;
                    var x = direction == UiIconDirection.Right
                        ? box.X + i * columnWidth
                        : box.Right - (i + 1) * columnWidth;

                    drawList.FillRect(
                        new UiRect(x, box.Center.Y - columnHeight * 0.5f, columnWidth + 0.5f, columnHeight),
                        color);
                }
            }
        }

        /// <summary>A check mark: a short stroke down-right meeting a long stroke up-right.</summary>
        public static void Check(ManaUiDrawList drawList, UiRect box, Vector4 color, float thickness = 2.0f)
        {
            if (box.Width <= 0.0f || box.Height <= 0.0f)
            {
                return;
            }

            // Proportions of a check mark within its box, as fractions: the elbow sits low and left of
            // centre, and the long arm rises past the top-right.
            var start = new Vector2(box.X + box.Width * 0.16f, box.Y + box.Height * 0.52f);
            var elbow = new Vector2(box.X + box.Width * 0.42f, box.Y + box.Height * 0.78f);
            var end = new Vector2(box.X + box.Width * 0.84f, box.Y + box.Height * 0.22f);

            Stroke(drawList, start, elbow, color, thickness);
            Stroke(drawList, elbow, end, color, thickness);
        }

        /// <summary>A cross, for a close button in a font with no multiplication sign to borrow.</summary>
        public static void Cross(ManaUiDrawList drawList, UiRect box, Vector4 color, float thickness = 1.5f)
        {
            if (box.Width <= 0.0f || box.Height <= 0.0f)
            {
                return;
            }

            Stroke(drawList, box.Position, new Vector2(box.Right, box.Bottom), color, thickness);
            Stroke(drawList, new Vector2(box.Right, box.Y), new Vector2(box.X, box.Bottom), color, thickness);
        }

        /// <summary>
        /// A diagonal line, as one rotated box.
        /// <para>
        /// This used to be a run of overlapping round dots, because the pipeline could not express a box at
        /// an angle. A check mark cost about forty instances that way, every checkbox, every frame -- and
        /// the dots were only antialiased against each other, so the edge was subtly ragged. It is two
        /// instances now, with the SDF doing the edge properly.
        /// </para>
        /// </summary>
        private static void Stroke(
            ManaUiDrawList drawList,
            Vector2 from,
            Vector2 to,
            Vector4 color,
            float thickness)
        {
            drawList.DrawLine(from, to, color, thickness);
        }
    }
}

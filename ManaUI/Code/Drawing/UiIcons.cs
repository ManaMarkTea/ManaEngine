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

        /// <summary>
        /// A check mark: a short stroke down-right meeting a long stroke up-right. Both are diagonal, so
        /// each is a run of small squares along its line -- the same staircase the triangles use.
        /// </summary>
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
        /// A diagonal line as a run of overlapping round dots. Spacing is half the thickness so the dots
        /// merge into a continuous stroke rather than reading as a dotted one.
        /// </summary>
        private static void Stroke(
            ManaUiDrawList drawList,
            Vector2 from,
            Vector2 to,
            Vector4 color,
            float thickness)
        {
            var delta = to - from;
            var length = delta.Length();
            if (length <= 0.0f)
            {
                return;
            }

            var steps = Math.Max(2, (int)MathF.Ceiling(length / MathF.Max(0.5f, thickness * 0.5f)));
            var corners = new CornerRadius(thickness * 0.5f);

            for (var i = 0; i <= steps; i++)
            {
                var point = from + delta * (i / (float)steps);
                drawList.FillRect(
                    new UiRect(
                        point.X - thickness * 0.5f,
                        point.Y - thickness * 0.5f,
                        thickness,
                        thickness),
                    color,
                    corners);
            }
        }
    }
}

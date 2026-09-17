using ManaUI;

using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The toolbar's icon vocabulary, shared by the voxel tool palette and the editor toolbar beside it.
    /// <para>
    /// Icons rather than word buttons for the things every 3D editor already draws as icons -- transform
    /// modes and undo/redo. The strip is the one row of chrome always on screen and always competing with
    /// the viewport for width, and "Move"/"Rotate"/"Scale" spelled out cost roughly twice what the glyphs
    /// do for no added clarity. The full name still lives one hover away.
    /// </para>
    /// <para>
    /// Drawn from the same three primitives the palette's own icons use -- rounded rects, lines and a
    /// stroked border -- because the draw list has no filled polygon and no arc.
    /// </para>
    /// </summary>
    public static class ToolbarIcons
    {
        /// <summary>Matches the theme's control height, so an icon button is the same size as everything beside it.</summary>
        public const float ButtonSize = 24.0f;

        /// <summary>
        /// An icon button whose fill and icon colour follow whatever <paramref name="isSelected"/> reports,
        /// polled the same way every other control in the toolbar reads the view model.
        /// </summary>
        public static Button IconButton(
            UiTheme theme,
            Action<ManaUiDrawList, UiRect, Vector4> drawIcon,
            string tooltip,
            Func<bool> isSelected,
            Action onClick)
        {
            var button = new Button
            {
                IgnoreThemePadding = true,
                Width = ButtonSize,
                Height = ButtonSize,
                CornerRadius = theme.CornerRadiusCompact
            };

            // Disabled has to win over selected: a mode button for a mode that cannot be entered right now
            // (no selection) still reports itself selected, and a Subtle button does not dim its own child.
            button.Child = new ToolbarIcon(drawIcon)
                .BindPolled(i => i.Color, () => !button.IsEnabled ? theme.TextDisabled
                    : isSelected() ? theme.TextOnAccent
                    : theme.Text);

            return button
                .WithTooltip(tooltip)
                .OnClick(onClick)
                .BindPolled(b => b.Variant, () => isSelected() ? ButtonVariant.Primary : ButtonVariant.Subtle);
        }

        /// <summary>An icon button with no selected state -- an action rather than a mode.</summary>
        public static Button ActionButton(
            UiTheme theme,
            Action<ManaUiDrawList, UiRect, Vector4> drawIcon,
            string tooltip,
            Action onClick)
        {
            return IconButton(theme, drawIcon, tooltip, () => false, onClick);
        }

        /// <summary>
        /// The rule between two groups on the strip.
        /// <para>
        /// One implementation, because there were two: the editor toolbar's was a fixed-height rule centred
        /// in the row, the voxel palette's stretched to whatever height the row happened to be, and they sat
        /// side by side on the same strip at visibly different heights. The margins differed too, which is
        /// how the same "group boundary" came out as 13px in one place, 20 in another and 21 in a third.
        /// </para>
        /// </summary>
        public static UiElement Divider(UiTheme theme)
        {
            return new Border()
                .Fill(theme.Divider)
                .Width(1.0f)
                .Height(ButtonSize)
                .Margin(theme.Spacing, 0.0f)
                .CenterV();
        }

        /// <summary>
        /// Draws into the box a delegate is given, so each icon is a small function rather than a control
        /// of its own -- there is only ever one of these on screen per icon, and the geometry does not need
        /// a life of its own beyond drawing.
        /// </summary>
        public sealed class ToolbarIcon : UiElement
        {
            private readonly Action<ManaUiDrawList, UiRect, Vector4> _draw;
            private Vector4 _color;

            public ToolbarIcon(Action<ManaUiDrawList, UiRect, Vector4> draw)
            {
                _draw = draw;
            }

            public Vector4 Color
            {
                get => _color;
                set => SetVisualProperty(ref _color, value);
            }

            protected override Vector2 MeasureOverride(Vector2 availableSize) => new(18.0f, 18.0f);

            protected override void OnRender(ManaUiDrawList drawList)
            {
                _draw(drawList, Bounds, _color);
            }
        }

        // ----- drawing helpers ------------------------------------------------------------------------

        /// <summary>A point at fractional coordinates inside the icon's box.</summary>
        public static Vector2 P(UiRect box, float tx, float ty) =>
            new(box.X + box.Width * tx, box.Y + box.Height * ty);

        public static void FillDot(ManaUiDrawList drawList, Vector2 center, float radius, Vector4 color)
        {
            drawList.FillRect(
                new UiRect(center.X - radius, center.Y - radius, radius * 2.0f, radius * 2.0f),
                color,
                new CornerRadius(radius));
        }

        /// <summary>A ring -- or a flattened one when the radii differ, there being no ellipse primitive.</summary>
        public static void DrawRing(
            ManaUiDrawList drawList, Vector2 center, float radiusX, float radiusY, float thickness, Vector4 color)
        {
            var rect = new UiRect(center.X - radiusX, center.Y - radiusY, radiusX * 2.0f, radiusY * 2.0f);
            drawList.DrawBorder(rect, color, thickness, new CornerRadius(MathF.Min(radiusX, radiusY)));
        }

        /// <summary>
        /// An arrowhead at <paramref name="tip"/>, opening back along <paramref name="direction"/>. Two
        /// strokes rather than a filled triangle, which the draw list cannot express.
        /// </summary>
        public static void Arrowhead(
            ManaUiDrawList drawList, Vector2 tip, Vector2 direction, float length, Vector4 color, float thickness)
        {
            var forward = Vector2.Normalize(direction);
            var normal = new Vector2(-forward.Y, forward.X);
            var back = tip - forward * length;

            drawList.DrawLine(tip, back + normal * length * 0.6f, color, thickness);
            drawList.DrawLine(tip, back - normal * length * 0.6f, color, thickness);
        }

        // ----- transform modes ------------------------------------------------------------------------

        /// <summary>Move: the four-way arrow every editor draws for translate.</summary>
        public static void DrawMove(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            const float thickness = 1.6f;
            var centre = P(box, 0.5f, 0.5f);
            var reach = box.Width * 0.38f;
            var head = box.Width * 0.18f;

            var left = new Vector2(centre.X - reach, centre.Y);
            var right = new Vector2(centre.X + reach, centre.Y);
            var up = new Vector2(centre.X, centre.Y - reach);
            var down = new Vector2(centre.X, centre.Y + reach);

            drawList.DrawLine(left, right, color, thickness);
            drawList.DrawLine(up, down, color, thickness);

            Arrowhead(drawList, left, new Vector2(-1.0f, 0.0f), head, color, thickness);
            Arrowhead(drawList, right, new Vector2(1.0f, 0.0f), head, color, thickness);
            Arrowhead(drawList, up, new Vector2(0.0f, -1.0f), head, color, thickness);
            Arrowhead(drawList, down, new Vector2(0.0f, 1.0f), head, color, thickness);
        }

        /// <summary>Rotate: a ring with an arrowhead riding it, so it reads as turning rather than as a circle.</summary>
        public static void DrawRotate(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            const float thickness = 1.6f;
            var centre = P(box, 0.5f, 0.54f);
            var radius = box.Width * 0.34f;

            DrawRing(drawList, centre, radius, radius, thickness, color);

            // Tangent to the ring rather than radial, or it reads as a pin stuck through the circle
            // instead of as motion around it.
            var tip = new Vector2(centre.X + radius * 0.25f, centre.Y - radius);
            Arrowhead(drawList, tip, new Vector2(1.0f, -0.5f), box.Width * 0.22f, color, thickness);
        }

        /// <summary>Scale: a small box and a large one on one diagonal -- the same thing, two sizes.</summary>
        public static void DrawScale(ManaUiDrawList drawList, UiRect box, Vector4 color)
        {
            const float thickness = 1.5f;
            drawList.DrawLine(P(box, 0.26f, 0.74f), P(box, 0.70f, 0.30f), color, thickness);

            var small = box.Width * 0.18f;
            var smallCentre = P(box, 0.22f, 0.78f);
            drawList.FillRect(
                new UiRect(smallCentre.X - small * 0.5f, smallCentre.Y - small * 0.5f, small, small),
                color,
                new CornerRadius(1.0f));

            var large = box.Width * 0.34f;
            var largeCentre = P(box, 0.74f, 0.26f);
            drawList.DrawBorder(
                new UiRect(largeCentre.X - large * 0.5f, largeCentre.Y - large * 0.5f, large, large),
                color,
                thickness,
                new CornerRadius(1.5f));
        }

        // ----- history --------------------------------------------------------------------------------

        /// <summary>Undo: an arrow curling back on itself to the left.</summary>
        public static void DrawUndo(ManaUiDrawList drawList, UiRect box, Vector4 color) =>
            DrawHistoryArrow(drawList, box, color, mirrored: false);

        /// <summary>Redo: the same arrow, mirrored.</summary>
        public static void DrawRedo(ManaUiDrawList drawList, UiRect box, Vector4 color) =>
            DrawHistoryArrow(drawList, box, color, mirrored: true);

        /// <summary>
        /// The shared curl, faceted out of straight segments since there is no arc primitive -- at icon
        /// size four segments read as a curve. Authored left-handed and flipped for redo, so the two are
        /// guaranteed to be mirror images rather than two hand-tuned near-matches.
        /// </summary>
        private static void DrawHistoryArrow(ManaUiDrawList drawList, UiRect box, Vector4 color, bool mirrored)
        {
            const float thickness = 1.7f;

            Vector2 At(float tx, float ty) => P(box, mirrored ? 1.0f - tx : tx, ty);

            var start = At(0.16f, 0.46f);
            var a = At(0.28f, 0.28f);
            var b = At(0.54f, 0.24f);
            var c = At(0.76f, 0.42f);
            var end = At(0.80f, 0.74f);

            drawList.DrawLine(start, a, color, thickness);
            drawList.DrawLine(a, b, color, thickness);
            drawList.DrawLine(b, c, color, thickness);
            drawList.DrawLine(c, end, color, thickness);

            // The head sits where the curl begins, pointing back the way the stroke came in.
            Arrowhead(
                drawList,
                start,
                new Vector2(mirrored ? 1.0f : -1.0f, 0.5f),
                box.Width * 0.24f,
                color,
                thickness);
        }
    }
}

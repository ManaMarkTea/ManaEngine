namespace ManaUI
{
    /// <summary>In a <see cref="WrapPanel"/>, ends the line here, like a line break in text. Takes no room itself.</summary>
    public sealed class WrapBreak : UiElement
    {
        protected override Vector2 MeasureOverride(Vector2 availableSize) => Vector2.Zero;
    }

    /// <summary>
    /// Lays children out in a line, starting a new one when the current one runs out of room.
    /// <para>
    /// A <see cref="StackPanel"/> leaves the stacking axis unconstrained, which is right for a list but wrong
    /// for a row of small controls: seven format toggles in a 300px panel simply run off the edge. This is
    /// the same layout with a width to respect, so the row becomes as many rows as it needs.
    /// </para>
    /// </summary>
    public class WrapPanel : Panel
    {
        private Orientation _orientation = Orientation.Horizontal;
        private float _horizontalSpacing;
        private float _verticalSpacing;

        /// <summary>The direction lines run in. Horizontal fills across and wraps down.</summary>
        public Orientation Orientation
        {
            get => _orientation;
            set => SetLayoutProperty(ref _orientation, value);
        }

        public float HorizontalSpacing
        {
            get => _horizontalSpacing;
            set => SetLayoutProperty(ref _horizontalSpacing, value);
        }

        public float VerticalSpacing
        {
            get => _verticalSpacing;
            set => SetLayoutProperty(ref _verticalSpacing, value);
        }

        /// <summary>Sets both gaps at once, which is what almost every caller wants.</summary>
        public float Spacing
        {
            set
            {
                HorizontalSpacing = value;
                VerticalSpacing = value;
            }
        }

        /// <summary>
        /// Lines the children currently occupy. One until they stop fitting -- which is the only thing about
        /// this panel worth asserting from outside it.
        /// </summary>
        public int LineCount { get; private set; } = 1;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var inner = LayoutHelper.Deflate(availableSize, Padding);
            var horizontal = _orientation == Orientation.Horizontal;

            // The wrapping axis is bounded and the other is not: a child gets the full line to ask for, and
            // the line grows as tall as whatever it ends up holding.
            var limit = horizontal ? inner.X : inner.Y;
            var childConstraint = horizontal
                ? new Vector2(inner.X, LayoutHelper.Infinity)
                : new Vector2(LayoutHelper.Infinity, inner.Y);

            var lineMain = 0.0f;
            var lineCross = 0.0f;
            var totalMain = 0.0f;
            var totalCross = 0.0f;
            var lineEmpty = true;
            var lines = 1;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                if (child is WrapBreak)
                {
                    totalMain = MathF.Max(totalMain, lineMain);
                    totalCross += lineCross + CrossSpacing;
                    lines++;
                    lineMain = 0.0f;
                    lineCross = 0.0f;
                    lineEmpty = true;
                    continue;
                }

                child.Measure(childConstraint);

                var main = horizontal ? child.DesiredSize.X : child.DesiredSize.Y;
                var cross = horizontal ? child.DesiredSize.Y : child.DesiredSize.X;
                var gap = lineEmpty ? 0.0f : MainSpacing;

                // A child wider than the whole line still gets its own line rather than being wrapped onto
                // one it cannot fit either -- there is nowhere better for it to go.
                if (!lineEmpty && lineMain + gap + main > limit)
                {
                    totalMain = MathF.Max(totalMain, lineMain);
                    totalCross += lineCross + CrossSpacing;
                    lines++;

                    lineMain = main;
                    lineCross = cross;
                    continue;
                }

                lineMain += gap + main;
                lineCross = MathF.Max(lineCross, cross);
                lineEmpty = false;
            }

            totalMain = MathF.Max(totalMain, lineMain);
            totalCross += lineCross;
            LineCount = lines;

            var content = horizontal
                ? new Vector2(totalMain, totalCross)
                : new Vector2(totalCross, totalMain);

            return LayoutHelper.Inflate(content, Padding);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var horizontal = _orientation == Orientation.Horizontal;
            var origin = Padding.TopLeft;
            var inner = LayoutHelper.Deflate(finalSize, Padding);
            var limit = horizontal ? inner.X : inner.Y;

            var mainOffset = 0.0f;
            var crossOffset = 0.0f;
            var lineCross = 0.0f;
            var lineEmpty = true;
            var lines = 1;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                if (child is WrapBreak)
                {
                    crossOffset += lineCross + CrossSpacing;
                    mainOffset = 0.0f;
                    lineCross = 0.0f;
                    lineEmpty = true;
                    lines++;
                    continue;
                }

                var main = horizontal ? child.DesiredSize.X : child.DesiredSize.Y;
                var cross = horizontal ? child.DesiredSize.Y : child.DesiredSize.X;
                var gap = lineEmpty ? 0.0f : MainSpacing;

                // Wrapped the same way it was measured, or a child lands somewhere the measure never
                // accounted for and the panel is the wrong size for what is in it.
                if (!lineEmpty && mainOffset + gap + main > limit)
                {
                    crossOffset += lineCross + CrossSpacing;
                    mainOffset = 0.0f;
                    lineCross = 0.0f;
                    lineEmpty = true;
                    gap = 0.0f;
                    lines++;
                }

                mainOffset += gap;

                // Children take exactly what they asked for; their own alignment positions them inside it.
                var slot = horizontal
                    ? new UiRect(Bounds.X + origin.X + mainOffset, Bounds.Y + origin.Y + crossOffset, main, cross)
                    : new UiRect(Bounds.X + origin.X + crossOffset, Bounds.Y + origin.Y + mainOffset, cross, main);

                child.Arrange(slot);

                mainOffset += main;
                lineCross = MathF.Max(lineCross, cross);
                lineEmpty = false;
            }

            LineCount = lines;
            return finalSize;
        }

        private float MainSpacing => _orientation == Orientation.Horizontal ? _horizontalSpacing : _verticalSpacing;

        private float CrossSpacing => _orientation == Orientation.Horizontal ? _verticalSpacing : _horizontalSpacing;
    }
}

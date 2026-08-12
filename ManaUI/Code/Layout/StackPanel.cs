namespace ManaUI
{
    /// <summary>
    /// Lays children out in a single line. Children are unconstrained along the stacking axis, which is what
    /// lets a StackPanel inside a ScrollViewer grow past the viewport instead of squashing its contents.
    /// </summary>
    public class StackPanel : Panel
    {
        private Orientation _orientation = Orientation.Vertical;
        private float _spacing;

        public Orientation Orientation
        {
            get => _orientation;
            set => SetLayoutProperty(ref _orientation, value);
        }

        /// <summary>Gap between adjacent visible children. Cheaper and tidier than per-child margins.</summary>
        public float Spacing
        {
            get => _spacing;
            set => SetLayoutProperty(ref _spacing, value);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var inner = LayoutHelper.Deflate(availableSize, Padding);
            var isVertical = _orientation == Orientation.Vertical;

            var childConstraint = isVertical
                ? new Vector2(inner.X, LayoutHelper.Infinity)
                : new Vector2(LayoutHelper.Infinity, inner.Y);

            var stacked = 0.0f;
            var crossMax = 0.0f;
            var visibleCount = 0;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                child.Measure(childConstraint);
                var desired = child.DesiredSize;

                if (isVertical)
                {
                    stacked += desired.Y;
                    crossMax = MathF.Max(crossMax, desired.X);
                }
                else
                {
                    stacked += desired.X;
                    crossMax = MathF.Max(crossMax, desired.Y);
                }

                visibleCount++;
            }

            if (visibleCount > 1)
            {
                stacked += _spacing * (visibleCount - 1);
            }

            var content = isVertical ? new Vector2(crossMax, stacked) : new Vector2(stacked, crossMax);
            return LayoutHelper.Inflate(content, Padding);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var isVertical = _orientation == Orientation.Vertical;
            var origin = Padding.TopLeft;
            var inner = LayoutHelper.Deflate(finalSize, Padding);
            var offset = 0.0f;

            foreach (var child in Children)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                var desired = child.DesiredSize;

                // Children fill the cross axis and take exactly what they asked for on the stacking axis;
                // their own alignment then positions them inside that slot.
                var slot = isVertical
                    ? new UiRect(Bounds.X + origin.X, Bounds.Y + origin.Y + offset, inner.X, desired.Y)
                    : new UiRect(Bounds.X + origin.X + offset, Bounds.Y + origin.Y, desired.X, inner.Y);

                child.Arrange(slot);
                offset += (isVertical ? desired.Y : desired.X) + _spacing;
            }

            return finalSize;
        }
    }
}

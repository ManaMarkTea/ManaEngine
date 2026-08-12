namespace ManaUI
{
    /// <summary>
    /// Children claim an edge of the remaining space in declaration order; the last child optionally fills
    /// what is left. This is the natural shape for editor chrome -- menu bar on top, status bar on the
    /// bottom, tool palette left, viewport filling the middle.
    /// </summary>
    public class DockPanel : Panel
    {
        private bool _lastChildFill = true;

        public bool LastChildFill
        {
            get => _lastChildFill;
            set => SetLayoutProperty(ref _lastChildFill, value);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var inner = LayoutHelper.Deflate(availableSize, Padding);

            // Space already claimed by docked edges, and the largest fill-direction demand seen so far.
            var usedWidth = 0.0f;
            var usedHeight = 0.0f;
            var maxWidth = 0.0f;
            var maxHeight = 0.0f;

            var children = Children;
            var lastIndex = children.Count - 1;

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                var remaining = new Vector2(
                    MathF.Max(0.0f, inner.X - usedWidth),
                    MathF.Max(0.0f, inner.Y - usedHeight));

                child.Measure(remaining);
                var desired = child.DesiredSize;

                var isFill = _lastChildFill && i == lastIndex;
                if (isFill)
                {
                    maxWidth = MathF.Max(maxWidth, usedWidth + desired.X);
                    maxHeight = MathF.Max(maxHeight, usedHeight + desired.Y);
                    continue;
                }

                switch (child.LayoutSlotOrNull?.Dock ?? Dock.Left)
                {
                    case Dock.Left:
                    case Dock.Right:
                        maxHeight = MathF.Max(maxHeight, usedHeight + desired.Y);
                        usedWidth += desired.X;
                        break;
                    default:
                        maxWidth = MathF.Max(maxWidth, usedWidth + desired.X);
                        usedHeight += desired.Y;
                        break;
                }
            }

            var content = new Vector2(MathF.Max(maxWidth, usedWidth), MathF.Max(maxHeight, usedHeight));
            return LayoutHelper.Inflate(content, Padding);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var content = ContentBounds;
            var left = content.Left;
            var top = content.Top;
            var right = content.Right;
            var bottom = content.Bottom;

            var children = Children;
            var lastIndex = children.Count - 1;

            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child.Visibility == Visibility.Collapsed)
                {
                    continue;
                }

                if (_lastChildFill && i == lastIndex)
                {
                    child.Arrange(UiRect.FromEdges(left, top, right, bottom));
                    break;
                }

                var desired = child.DesiredSize;

                switch (child.LayoutSlotOrNull?.Dock ?? Dock.Left)
                {
                    case Dock.Left:
                    {
                        var width = MathF.Min(desired.X, MathF.Max(0.0f, right - left));
                        child.Arrange(UiRect.FromEdges(left, top, left + width, bottom));
                        left += width;
                        break;
                    }
                    case Dock.Right:
                    {
                        var width = MathF.Min(desired.X, MathF.Max(0.0f, right - left));
                        child.Arrange(UiRect.FromEdges(right - width, top, right, bottom));
                        right -= width;
                        break;
                    }
                    case Dock.Top:
                    {
                        var height = MathF.Min(desired.Y, MathF.Max(0.0f, bottom - top));
                        child.Arrange(UiRect.FromEdges(left, top, right, top + height));
                        top += height;
                        break;
                    }
                    default:
                    {
                        var height = MathF.Min(desired.Y, MathF.Max(0.0f, bottom - top));
                        child.Arrange(UiRect.FromEdges(left, bottom - height, right, bottom));
                        bottom -= height;
                        break;
                    }
                }
            }

            return finalSize;
        }
    }
}

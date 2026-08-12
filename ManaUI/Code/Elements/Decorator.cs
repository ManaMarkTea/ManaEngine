namespace ManaUI
{
    /// <summary>
    /// Base for elements wrapping exactly one child. Separate from Panel because a single-child parent needs
    /// no collection, no paint ordering and no span arithmetic -- and most of the widget library is built
    /// out of these.
    /// </summary>
    public abstract class Decorator : UiElement
    {
        private UiElement? _child;
        private Thickness _padding;

        public UiElement? Child
        {
            get => _child;
            set
            {
                if (ReferenceEquals(_child, value))
                {
                    return;
                }

                if (_child != null && ReferenceEquals(_child.Parent, this))
                {
                    _child.Parent = null;

                    // Same reason as UiElementCollection.Detach: bindings must not outlive the tree.
                    _child.ClearBindings();
                }

                if (value != null)
                {
                    if (value.Parent != null && !ReferenceEquals(value.Parent, this))
                    {
                        throw new InvalidOperationException(
                            $"{value} already has a parent ({value.Parent}). Remove it before re-parenting.");
                    }

                    value.Parent = this;
                }

                _child = value;
                InvalidateMeasure();
            }
        }

        public Thickness Padding
        {
            get => _padding;
            set => SetLayoutProperty(ref _padding, value);
        }

        /// <summary>Space the child sits inside: padding, plus whatever a subclass adds (a border, say).</summary>
        protected virtual Thickness ContentInset => _padding;

        public override UiRect ContentBounds => Bounds.Deflate(ContentInset);

        public override int VisualChildCount => _child != null ? 1 : 0;

        public override UiElement GetVisualChild(int index)
        {
            if (index != 0 || _child == null)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _child;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var inset = ContentInset;

            if (_child == null || _child.Visibility == Visibility.Collapsed)
            {
                return inset.Size;
            }

            _child.Measure(LayoutHelper.Deflate(availableSize, inset));
            return LayoutHelper.Inflate(_child.DesiredSize, inset);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            _child?.Arrange(ContentBounds);
            return finalSize;
        }
    }
}

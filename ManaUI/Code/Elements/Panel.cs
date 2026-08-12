namespace ManaUI
{
    /// <summary>
    /// Base for elements that arrange many children. Subclasses supply the placement policy by overriding
    /// MeasureOverride/ArrangeOverride; everything else -- the children collection, padding, a background,
    /// paint order and hit testing -- lives here.
    /// </summary>
    public abstract class Panel : UiElement
    {
        private Thickness _padding;
        private Vector4 _background = UiColor.Transparent;

        protected Panel()
        {
            Children = new UiElementCollection(this);
        }

        public UiElementCollection Children { get; }

        public Thickness Padding
        {
            get => _padding;
            set => SetLayoutProperty(ref _padding, value);
        }

        public Vector4 Background
        {
            get => _background;
            set => SetVisualProperty(ref _background, value);
        }

        public CornerRadius CornerRadius { get; set; } = CornerRadius.Zero;

        public override UiRect ContentBounds => Bounds.Deflate(_padding);

        public override int VisualChildCount => Children.Count;

        public override UiElement GetVisualChild(int index)
        {
            return Children.PaintOrdered[index];
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            if (Background.W > 0.0f)
            {
                drawList.FillRect(Bounds, Background, CornerRadius);
            }
        }

        /// <summary>
        /// A panel is only itself a hit target when it has a visible background -- otherwise clicks fall
        /// through transparent layout scaffolding to whatever is behind it, which is nearly always what the
        /// author meant.
        /// </summary>
        public override UiElement? HitTest(Vector2 point)
        {
            if (Visibility != Visibility.Visible || !IsHitTestVisible || !Bounds.Contains(point))
            {
                return null;
            }

            var ordered = Children.PaintOrdered;
            for (var i = ordered.Count - 1; i >= 0; i--)
            {
                var hit = ordered[i].HitTest(point);
                if (hit != null)
                {
                    return hit;
                }
            }

            return Background.W > 0.0f ? this : null;
        }
    }
}

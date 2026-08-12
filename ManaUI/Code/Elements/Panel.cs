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

        /// <summary>A panel is only a hit target once it actually paints something.</summary>
        protected override bool IsSelfHitTestVisible => Background.W > 0.0f;
    }
}

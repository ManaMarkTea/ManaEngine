namespace ManaUI
{
    /// <summary>
    /// A filled, rounded, optionally-bordered box around one child. Fill and border go out as a single
    /// instance, so a border costs no more to draw than a plain rectangle.
    /// </summary>
    public class Border : Decorator
    {
        private float _borderThickness;
        private Vector4 _background = UiColor.Transparent;
        private Vector4 _borderColor = UiColor.Transparent;
        private CornerRadius _cornerRadius = CornerRadius.Zero;

        public Vector4 Background
        {
            get => _background;
            set => SetVisualProperty(ref _background, value);
        }

        public Vector4 BorderColor
        {
            get => _borderColor;
            set => SetVisualProperty(ref _borderColor, value);
        }

        /// <summary>Inset border width. Affects layout, since the child is pushed in by it.</summary>
        public float BorderThickness
        {
            get => _borderThickness;
            set => SetLayoutProperty(ref _borderThickness, value);
        }

        public CornerRadius CornerRadius
        {
            get => _cornerRadius;
            set => SetVisualProperty(ref _cornerRadius, value);
        }

        /// <summary>Optional drop shadow blur radius; 0 disables it.</summary>
        public float ShadowBlur { get; set; }

        public Vector4 ShadowColor { get; set; } = new Vector4(0.0f, 0.0f, 0.0f, 0.35f);

        protected override Thickness ContentInset => Padding + new Thickness(_borderThickness);

        protected override void OnRender(ManaUiDrawList drawList)
        {
            if (ShadowBlur > 0.0f)
            {
                drawList.DrawShadow(Bounds, ShadowColor, ShadowBlur, _cornerRadius);
            }

            drawList.DrawBox(Bounds, _background, _borderColor, _borderThickness, _cornerRadius);
        }

        /// <summary>Same rule as Panel: an invisible border is layout scaffolding, not a click target.</summary>
        protected override bool IsSelfHitTestVisible =>
            _background.W > 0.0f || (_borderThickness > 0.0f && _borderColor.W > 0.0f);
    }
}

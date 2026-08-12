namespace ManaUI
{
    /// <summary>
    /// Displays a run of text. The layout object is kept across passes, so measure, arrange and render all
    /// share one line break rather than computing it three times a frame.
    /// </summary>
    public class TextBlock : UiElement
    {
        private readonly TextLayout _layout = new();

        private string _text = string.Empty;
        private string? _fontFamily;
        private float _fontSize = float.NaN;
        private TextWrapping _wrapping = TextWrapping.NoWrap;
        private TextTrimming _trimming = TextTrimming.None;
        private float _lineSpacing = float.NaN;
        private Vector4? _foreground;

        public TextBlock()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
            IsHitTestVisible = false;
        }

        public TextBlock(string text)
            : this()
        {
            _text = text;
        }

        public string Text
        {
            get => _text;
            set => SetLayoutProperty(ref _text, value ?? string.Empty);
        }

        /// <summary>Null uses the theme's default family.</summary>
        public string? FontFamily
        {
            get => _fontFamily;
            set => SetLayoutProperty(ref _fontFamily, value);
        }

        /// <summary>NaN uses the theme's default size.</summary>
        public float FontSize
        {
            get => _fontSize;
            set => SetLayoutProperty(ref _fontSize, value);
        }

        public TextWrapping TextWrapping
        {
            get => _wrapping;
            set => SetLayoutProperty(ref _wrapping, value);
        }

        public TextTrimming TextTrimming
        {
            get => _trimming;
            set => SetLayoutProperty(ref _trimming, value);
        }

        /// <summary>Null uses the theme's Text colour, or TextDisabled when the element is disabled.</summary>
        public Vector4? Foreground
        {
            get => _foreground;
            set => SetVisualProperty(ref _foreground, value);
        }

        public TextAlignment TextAlignment { get; set; } = TextAlignment.Left;

        /// <summary>Multiplier on the font's natural line height. NaN uses the theme's value.</summary>
        public float LineSpacing
        {
            get => _lineSpacing;
            set => SetLayoutProperty(ref _lineSpacing, value);
        }

        /// <summary>The resolved font, or null while the element is detached from a root.</summary>
        public ManaFont? Font => ResolveFont();

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var font = ResolveFont();
            if (font == null)
            {
                return Vector2.Zero;
            }

            _layout.Font = font;
            _layout.Text = _text;
            _layout.Wrapping = _wrapping;
            _layout.Trimming = _trimming;
            _layout.LineSpacing = LayoutHelper.IsAuto(_lineSpacing)
                ? Root?.Theme.LineSpacing ?? 1.0f
                : _lineSpacing;

            // Trimming needs a finite width to trim against; an unconstrained measure has none, so the
            // element reports its full natural width and trims later against the arranged bounds.
            _layout.MaxWidth = _wrapping == TextWrapping.Wrap ? availableSize.X : float.PositiveInfinity;

            _layout.Update();
            return _layout.Size;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            if (_trimming == TextTrimming.CharacterEllipsis && _wrapping == TextWrapping.NoWrap)
            {
                _layout.MaxWidth = finalSize.X;
                _layout.Update();
            }

            return finalSize;
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var font = ResolveFont();
            if (font == null || _text.Length == 0)
            {
                return;
            }

            var theme = Root?.Theme;
            var color = _foreground
                ?? (IsEffectivelyEnabled ? theme?.Text : theme?.TextDisabled)
                ?? UiColor.White;

            _layout.Font = font;
            _layout.Alignment = TextAlignment;
            _layout.Render(drawList, Bounds, color, VerticalAlignment.Top);
        }

        private ManaFont? ResolveFont()
        {
            var root = Root;
            if (root == null)
            {
                return null;
            }

            var family = _fontFamily ?? root.Theme.FontFamily;
            var size = LayoutHelper.IsAuto(_fontSize) ? root.Theme.FontSize : _fontSize;
            return root.Fonts.Get(family, size, root.Scale);
        }
    }
}

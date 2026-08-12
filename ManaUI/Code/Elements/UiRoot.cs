namespace ManaUI
{
    /// <summary>
    /// Top of the UI tree: owns the layer stack, the theme and fonts, and drives layout and rendering.
    /// <para>
    /// The four fixed layers exist so that docking, menus and drag adornments never have to fight the main
    /// content tree for z-order. Content is the docked workspace; Floating holds undocked windows; Popup
    /// holds menus, dropdowns and tooltips; Overlay sits above everything for drag previews and dock
    /// target indicators, which must be visible over a menu that is still open.
    /// </para>
    /// </summary>
    public sealed class UiRoot : Panel
    {
        private readonly ManaUiDrawList _drawList = new();
        private Vector2 _size;
        private bool _layoutDirty = true;
        private bool _visualDirty = true;

        public UiRoot(ManaFontManager fonts, UiTheme? theme = null)
        {
            ArgumentNullException.ThrowIfNull(fonts);

            Fonts = fonts;
            Theme = theme ?? UiTheme.Dark();

            ContentLayer = new ContentHost();
            FloatingLayer = new Canvas();
            PopupLayer = new Canvas();
            OverlayLayer = new Canvas { IsHitTestVisible = false };

            Children.Add(ContentLayer);
            Children.Add(FloatingLayer);
            Children.Add(PopupLayer);
            Children.Add(OverlayLayer);
        }

        public ManaFontManager Fonts { get; }
        public ManaGlyphAtlas Atlas => Fonts.Atlas;
        public UiTheme Theme { get; set; }

        /// <summary>The docked workspace. Everything an application builds normally lives here.</summary>
        public Decorator ContentLayer { get; }

        /// <summary>Undocked windows, positioned absolutely and ordered by ZIndex.</summary>
        public Canvas FloatingLayer { get; }

        /// <summary>Menus, dropdowns and tooltips.</summary>
        public Canvas PopupLayer { get; }

        /// <summary>Drag previews and dock indicators; never hit-testable, always on top.</summary>
        public Canvas OverlayLayer { get; }

        /// <summary>Shorthand for <see cref="ContentLayer"/>'s child.</summary>
        public UiElement? Content
        {
            get => ContentLayer.Child;
            set => ContentLayer.Child = value;
        }

        /// <summary>Logical (DPI-independent) size of the UI surface.</summary>
        public Vector2 Size => _size;

        /// <summary>
        /// Logical-to-physical pixel ratio. The UI lays out entirely in logical units; the backend applies
        /// this once in its projection matrix, and fonts are rasterised at the scaled size so text stays
        /// crisp rather than being a magnified bitmap.
        /// </summary>
        public float Scale { get; private set; } = 1.0f;

        public bool IsLayoutValid => !_layoutDirty;

        /// <summary>
        /// True when nothing has changed since the last frame's draw list was built, so the backend can
        /// re-submit the previous buffer instead of re-recording. A static editor UI hits this every frame.
        /// </summary>
        public bool IsVisualValid => !_visualDirty && !_layoutDirty;

        internal void NotifyLayoutInvalidated()
        {
            _layoutDirty = true;
            _visualDirty = true;
        }

        internal void NotifyVisualInvalidated()
        {
            _visualDirty = true;
        }

        /// <summary>Forces the next frame to re-record, e.g. after a theme swap.</summary>
        public void InvalidateAll()
        {
            _layoutDirty = true;
            _visualDirty = true;
            InvalidateMeasure();
        }

        public void SetScale(float scale)
        {
            scale = MathF.Max(0.1f, scale);
            if (LayoutHelper.AreClose(Scale, scale))
            {
                return;
            }

            Scale = scale;

            // Glyphs are baked at a physical size, so every one of them is now the wrong size. Dropping the
            // atlas is cheaper than tracking which entries survive, and a scale change is a rare event.
            Atlas.Reset();
            InvalidateAll();
        }

        /// <summary>
        /// Runs measure and arrange for the whole tree if anything is stale. Call once per frame before
        /// <see cref="Render"/>.
        /// </summary>
        public void UpdateLayout(Vector2 availableSize)
        {
            availableSize = new Vector2(MathF.Max(0.0f, availableSize.X), MathF.Max(0.0f, availableSize.Y));

            if (!LayoutHelper.AreClose(_size, availableSize))
            {
                _size = availableSize;
                _layoutDirty = true;
                InvalidateMeasure();
            }

            if (!_layoutDirty)
            {
                return;
            }

            Measure(_size);
            Arrange(new UiRect(Vector2.Zero, _size));
            _layoutDirty = false;
        }

        /// <summary>
        /// Records the frame. The returned list is owned and reused by the root -- consume it before the
        /// next call rather than holding on to it.
        /// </summary>
        public ManaUiDrawList Render()
        {
            _drawList.Reset();

            // No implicit full-screen fill: ManaUI draws over a live 3D viewport, so the root is
            // transparent unless an application deliberately sets Background (a standalone tool window
            // would; an in-engine overlay must not).
            base.Render(_drawList);

            _drawList.Finish();
            _visualDirty = false;
            return _drawList;
        }

        /// <summary>The draw list from the most recent <see cref="Render"/>.</summary>
        public ManaUiDrawList DrawList => _drawList;

        /// <summary>Topmost hit-testable element under a point in logical coordinates.</summary>
        public UiElement? HitTest(float x, float y)
        {
            return HitTest(new Vector2(x, y));
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            foreach (var layer in Children)
            {
                layer.Measure(availableSize);
            }

            return availableSize;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var full = new UiRect(Bounds.Position, finalSize);
            foreach (var layer in Children)
            {
                layer.Arrange(full);
            }

            return finalSize;
        }
    }
}

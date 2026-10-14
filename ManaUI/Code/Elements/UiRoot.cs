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
        private readonly List<(UiElement Element, string? Profile)> _polledBindingElements = new();
        private Vector2 _size;
        private bool _layoutDirty = true;
        private bool _polledBindingsDirty = true;
        private bool _visualDirty = true;
        private readonly Dictionary<UiThemeKind, Func<ManaImageAtlas, UiSkin?>> _skinFactories = new()
        {
            [UiThemeKind.Lcars] = LcarsUiSkin.For,
            [UiThemeKind.Windows31] = Windows31UiSkin.For
        };

        public UiRoot(ManaFontManager fonts, UiTheme? theme = null)
        {
            ArgumentNullException.ThrowIfNull(fonts);

            Fonts = fonts;
            _theme = theme ?? UiTheme.Dark();
            ApplyThemeSkin(_theme);
            FocusManager = new UiFocusManager(this);
            Input = new UiInputRouter(this);
            Popups = new UiPopupManager(this);
            Debug = new UiDebugOverlay(this);

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
        /// <summary>Desktop host hook; null on platforms without native panel windows.</summary>
        public Func<string, IUiDesktopWindow>? CreateDesktopWindow { get; set; }

        /// <summary>
        /// Screen position of this root's top-left, so a drag can be followed from one window into another.
        /// Null reads as the origin, which is right for anything that is not a real window.
        /// </summary>
        public Func<Vector2>? ScreenOrigin { get; set; }

        /// <summary>
        /// Window (screen) pixels per logical pixel: the OS display scale on Windows -- 1.5 at 150% -- which the
        /// layout is divided by, so everything is drawn that much bigger. Pointer positions arrive in window pixels
        /// and are divided by it on the way in; anything mapping UI positions back to the screen multiplies.
        /// </summary>
        public float ScreenScale { get; set; } = 1.0f;
        public ManaGlyphAtlas Atlas => Fonts.Atlas;

        /// <summary>
        /// The colour atlas, for anything that brings its own colours rather than being tinted: thumbnails,
        /// colour emoji, multi-colour icons. Bound alongside the glyph atlas, so mixing the two costs no
        /// extra draw call.
        /// <para>
        /// The font manager's, not one of its own -- colour glyphs are packed by fonts, and a root holding
        /// a second atlas would hand the renderer an empty one while the emoji sat in the other.
        /// </para>
        /// </summary>
        public ManaImageAtlas Images => Fonts.Images;
        /// <summary>
        /// The palette every control reads. Swapping it re-renders the tree, but note that a view which
        /// captured a colour when it was built keeps the old one -- most controls read the theme at render
        /// time, application views often do not. A host switching themes at runtime should rebuild its
        /// content as well, which is what <see cref="ThemeChanged"/> is for.
        /// </summary>
        public UiTheme Theme
        {
            get => _theme;
            set
            {
                ArgumentNullException.ThrowIfNull(value);

                if (ReferenceEquals(_theme, value))
                {
                    return;
                }

                ApplyThemeSkin(value);
                _theme = value;
                InvalidateAll();
                ThemeChanged?.Invoke(this, value);
            }
        }

        private UiTheme _theme;

        /// <summary>Raised after the theme is swapped, so hosts can rebuild views that captured colours.</summary>
        public event EventHandler<UiTheme>? ThemeChanged;

        /// <summary>Switches this UI's theme and notifies views that need to rebuild captured styles.</summary>
        public void RequestTheme(UiThemeKind kind)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (Theme.Kind != kind)
            {
                Theme = UiTheme.Create(kind);
            }
        }

        /// <summary>Registers optional artwork supplied by a host, without adding asset dependencies to ManaUI.</summary>
        public void RegisterSkinFactory(UiThemeKind kind, Func<ManaImageAtlas, UiSkin?> factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _skinFactories[kind] = factory;
            if (Theme.Kind == kind)
            {
                ApplyThemeSkin(Theme);
                InvalidateAll();
            }
        }

        private void ApplyThemeSkin(UiTheme theme)
        {
            if (_skinFactories.TryGetValue(theme.Kind, out var factory))
            {
                theme.Skin = factory(Images);
            }
        }

        /// <summary>Routes platform input into the tree. The host feeds it; widgets never see a windowing API.</summary>
        public UiInputRouter Input { get; }

        public UiFocusManager FocusManager { get; }

        /// <summary>Open dropdowns, menus and tooltips.</summary>
        public UiPopupManager Popups { get; }

        /// <summary>
        /// Wireframe and statistics drawn over the finished UI. Off until something turns it on, and it
        /// costs nothing while it is off.
        /// </summary>
        public UiDebugOverlay Debug { get; }

        /// <summary>The cursor shape the UI wants right now; the host applies it to the real window.</summary>
        public UiCursor RequestedCursor => Input.RequestedCursor;

        /// <summary>Element currently receiving all pointer input, or null.</summary>
        public UiElement? PointerCaptureTarget => Input.PointerCaptureTarget;

        public void CapturePointer(UiElement element)
        {
            Input.CapturePointer(element);
        }

        /// <summary>
        /// Releases whichever element currently holds the pointer. Named apart from
        /// <see cref="UiElement.ReleasePointerCapture"/>, which releases only the caller's own capture --
        /// the two read alike but mean different things, and one silently hid the other.
        /// </summary>
        public void ReleaseAnyPointerCapture()
        {
            Input.ReleasePointerCapture();
        }

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
            InvalidateSubtreeLayout();
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

            // Polled bindings feed layout inputs (a label's text changes its width), so they are applied
            // before measure rather than after. The tree is only rescanned when it actually changed --
            // any structural edit dirties layout, and a binding added to an already-attached element
            // raises its own flag.
            // Settled rather than refreshed once. A refresh can build a subtree -- DynamicContent rebuilds
            // its content when the thing it is showing changes -- and the elements it creates are not in
            // the list currently being iterated. Stopping after one pass means their bindings first
            // evaluate on the following frame, so a panel shows one frame of blank rows every time the
            // selection changes, and anything reading it in between sees a half-built tree.
            // Re-reading a binding is cheap and writes only on a real change, so the repeat costs little.
            for (var pass = 0; pass < MaxPolledBindingPasses; pass++)
            {
                if (_layoutDirty || _polledBindingsDirty)
                {
                    RescanPolledBindings();
                }

                // By index: a binding may open a popup, adding elements mid-walk; the rescan next pass settles them.
                for (var i = 0; i < _polledBindingElements.Count; i++)
                {
                    var (element, name) = _polledBindingElements[i];
                    using var profile = name == null ? default : ManaCore.Diagnostics.ManaFrameProfiler.Measure(name);
                    element.RefreshPolledBindings();
                }

                // Nothing new appeared, so the tree has settled.
                if (!_polledBindingsDirty)
                {
                    break;
                }
            }

            if (!_layoutDirty)
            {
                return;
            }

            Measure(_size);

            // Between the passes on purpose: placement needs the popup's measured size (to know whether it
            // fits, and which way to flip) and its anchor's bounds, and it writes canvas offsets that the
            // arrange pass then consumes. Doing it after arrange would need a second arrange to apply.
            Popups.UpdatePlacements(_size);

            Arrange(new UiRect(Vector2.Zero, _size));
            _layoutDirty = false;

            // The tree may have changed shape since the last frame; anything removed, hidden or disabled
            // must not still be holding focus and silently eating keystrokes.
            FocusManager.ValidateFocus();
        }

        /// <summary>
        /// Records the frame. The returned list is owned and reused by the root -- consume it before the
        /// next call rather than holding on to it.
        /// </summary>
        public ManaUiDrawList Render()
        {
            _drawList.Reset();
            _drawList.PixelScale = Scale;

            // No implicit full-screen fill: ManaUI draws over a live 3D viewport, so the root is
            // transparent unless an application deliberately sets Background (a standalone tool window
            // would; an in-engine overlay must not).
            base.Render(_drawList);

            // Batches are only closed by Finish, so the content's own batch count is one more than however
            // many boundaries it opened. Captured before the overlay draws, so the readout describes the UI
            // rather than the overlay measuring itself.
            var contentInstances = _drawList.InstanceCount;
            var contentBatches = _drawList.Batches.Count + (contentInstances > 0 ? 1 : 0);

            Debug.Render(_drawList, contentInstances, contentBatches);

            _drawList.Finish();
            _visualDirty = false;
            return _drawList;
        }

        /// <summary>The draw list from the most recent <see cref="Render"/>.</summary>
        public ManaUiDrawList DrawList => _drawList;

        /// <summary>
        /// Adds a floating window and raises it. Placement defaults to a cascade from the top-left so two
        /// windows opened in a row do not land exactly on top of each other.
        /// </summary>
        public UiWindow ShowWindow(UiWindow window, Vector2? position = null)
        {
            ArgumentNullException.ThrowIfNull(window);

            window.Position = position ?? new Vector2(
                40.0f + FloatingLayer.Children.Count * 24.0f,
                40.0f + FloatingLayer.Children.Count * 24.0f);

            FloatingLayer.Children.Add(window);
            window.BringToFront();
            if (Theme.Kind == UiThemeKind.MacOS)
            {
                window.Focus();
            }
            return window;
        }

        /// <summary>Topmost hit-testable element under a point in logical coordinates.</summary>
        public UiElement? HitTest(float x, float y)
        {
            return HitTest(new Vector2(x, y));
        }

        /// <summary>
        /// Rebuilds the flat list of elements carrying polled bindings, so the per-frame refresh iterates a
        /// short list instead of walking the whole tree.
        /// </summary>
        /// <summary>
        /// How many times a frame will re-scan for subtrees built by a polled refresh before giving up.
        /// A cap rather than a loop, so a binding that rebuilds its own content every time cannot hang the
        /// frame -- it just settles a frame late, as it did before.
        /// </summary>
        private const int MaxPolledBindingPasses = 4;

        internal void NotifyPolledBindingsChanged()
        {
            _polledBindingsDirty = true;
        }

        private void RescanPolledBindings()
        {
            _polledBindingsDirty = false;
            _polledBindingElements.Clear();
            Collect(this, null);

            void Collect(UiElement element, string? profile)
            {
                profile = element.BindingProfile ?? profile;
                if (element.HasPolledBindings)
                {
                    _polledBindingElements.Add((element, profile));
                }

                // A collapsed element's own bindings still run -- that is how it notices it should become
                // visible again -- but its children do not. Without this, a hidden subtree costs exactly
                // as much as a visible one: every dock panel behind the loading screen, every unselected
                // inspector, every closed popup would go on reading whatever live state its bindings poll,
                // for a picture nobody can see. Worse than wasted work, some of that state is being written
                // concurrently on another thread -- the loading screen exists because a background load is
                // still mutating the scene graph, and a hidden outliner walking it at the same time is a
                // race, not merely a waste.
                if (element.Visibility == Visibility.Collapsed)
                {
                    return;
                }

                var count = element.VisualChildCount;
                for (var i = 0; i < count; i++)
                {
                    Collect(element.GetVisualChild(i), profile);
                }
            }
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

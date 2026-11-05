namespace ManaUI
{
    /// <summary>
    /// Shows a <see cref="ManaUiSurface"/>. Takes whatever room it is given; the application watches
    /// <see cref="DevicePixelSize"/> and re-renders the surface at that size to keep it sharp (a zoomable canvas does).
    /// <para>
    /// It receives the pointer, so a host drives pan, zoom and picking through the ordinary
    /// <see cref="UiElement.PointerPressed"/>, <see cref="UiElement.PointerMoved"/> and
    /// <see cref="UiElement.PointerWheel"/> events. A press captures the pointer, so a drag keeps arriving after it leaves.
    /// </para>
    /// </summary>
    public class UiSurfaceImage : UiElement
    {
        private ManaUiSurface? _surface;
        private UiImageFit _fit = UiImageFit.Stretch;
        private Vector4 _tint = UiColor.White;
        private bool _captured;
        private bool _hasDrawn;

        // The draw list is retained: new pixels need no re-record once the picture is in it, but the first ones (or a
        // new size) do, or nothing would ask for the batch.
        private void OnSurfaceChanged()
        {
            if (!_hasDrawn || _surface is not { HasPixels: true })
            {
                InvalidateVisual();
                return;
            }

            if (_surface.Width != _drawnWidth || _surface.Height != _drawnHeight)
            {
                InvalidateVisual();
            }
        }

        private int _drawnWidth;
        private int _drawnHeight;

        public ManaUiSurface? Surface
        {
            get => _surface;
            set
            {
                if (ReferenceEquals(_surface, value))
                {
                    return;
                }

                if (_surface != null)
                {
                    _surface.Changed -= OnSurfaceChanged;
                }
                _surface = value;
                _hasDrawn = false;
                if (_surface != null)
                {
                    _surface.Changed += OnSurfaceChanged;
                }
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        public UiImageFit Fit
        {
            get => _fit;
            set => SetVisualProperty(ref _fit, value);
        }

        public Vector4 Tint
        {
            get => _tint;
            set => SetVisualProperty(ref _tint, value);
        }

        /// <summary>The element's size in device pixels: what to render the surface at for one texel per pixel.</summary>
        public Vector2 DevicePixelSize
        {
            get
            {
                var scale = 1.0f;
                if (Root != null)
                {
                    scale = Root.Scale;
                }
                return new Vector2(MathF.Round(Bounds.Width * scale), MathF.Round(Bounds.Height * scale));
            }
        }

        protected override bool IsSelfHitTestVisible => true;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            // Fills a finite offer; an unbounded one gets the picture's own size.
            var width = availableSize.X;
            var height = availableSize.Y;
            if (float.IsInfinity(width))
            {
                width = 0.0f;
                if (_surface is { HasPixels: true })
                {
                    width = _surface.Width;
                }
            }
            if (float.IsInfinity(height))
            {
                height = 0.0f;
                if (_surface is { HasPixels: true })
                {
                    height = _surface.Height;
                }
            }
            return new Vector2(width, height);
        }

        /// <summary>Where the picture lands inside the element after <see cref="Fit"/>.</summary>
        public UiRect DestinationRect()
        {
            if (_surface is not { HasPixels: true } || Bounds.IsEmpty || _fit == UiImageFit.Stretch)
            {
                return Bounds;
            }

            var scaleX = Bounds.Width / _surface.Width;
            var scaleY = Bounds.Height / _surface.Height;
            var scale = MathF.Min(scaleX, scaleY);
            if (_fit == UiImageFit.Cover)
            {
                scale = MathF.Max(scaleX, scaleY);
            }

            var size = new Vector2(_surface.Width, _surface.Height) * scale;
            return new UiRect(Bounds.Center.X - size.X * 0.5f, Bounds.Center.Y - size.Y * 0.5f, size.X, size.Y);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            if (_surface is not { HasPixels: true, IsDisposed: false } surface || Root == null)
            {
                return;
            }

            // The backend finds the pixels through the root, and only uploads pictures a batch draws from.
            Root.Surfaces[surface.Id] = surface;
            _hasDrawn = true;
            _drawnWidth = surface.Width;
            _drawnHeight = surface.Height;

            var clipped = _fit == UiImageFit.Cover;
            if (clipped)
            {
                drawList.PushClip(Bounds);
            }

            drawList.SetTexture(surface.Id);
            drawList.DrawImage(DestinationRect(), new Vector4(0.0f, 0.0f, 1.0f, 1.0f), _tint, CornerRadius.Zero);
            drawList.SetTexture(0);

            if (clipped)
            {
                drawList.PopClip();
            }
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            CapturePointer();
            _captured = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (_captured)
            {
                _captured = false;
                ReleasePointerCapture();
            }
        }
    }
}

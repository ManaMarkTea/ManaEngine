namespace ManaUI
{
    public enum UiImageFit
    {
        /// <summary>Fill the element, ignoring the image's own proportions.</summary>
        Stretch,

        /// <summary>Fit inside the element, keeping proportions. Leaves empty space on one axis.</summary>
        Contain,

        /// <summary>Cover the element, keeping proportions. Crops on one axis.</summary>
        Cover
    }

    /// <summary>
    /// Draws a region of the shared colour atlas -- a thumbnail, an icon, a texture preview.
    /// <para>
    /// It holds a UV rather than pixels, because the atlas owns the pixels and hands out coordinates. That
    /// is what keeps an image in the same draw call as the text beside it: same bound texture, same batch.
    /// An image too large for the atlas belongs in its own texture and a batch of its own, which is a
    /// different control.
    /// </para>
    /// </summary>
    public class UiImage : UiElement
    {
        private Vector4 _sourceUv;
        private Vector2 _sourceSize;
        private UiImageFit _fit = UiImageFit.Contain;
        private Vector4 _tint = UiColor.White;
        private CornerRadius _corners;
        private string? _picture;
        private float _rotation;

        /// <summary>A picture by path, as a layout file names one; found through <see cref="UiPictures"/>.</summary>
        public string? Picture
        {
            get => _picture;
            set
            {
                if (_picture == value)
                {
                    return;
                }

                _picture = value;
                SourceSize = Vector2.Zero;
                InvalidateVisual();
            }
        }

        /// <summary>Turned about its centre, in radians, clockwise.</summary>
        public float Rotation
        {
            get => _rotation;
            set => SetVisualProperty(ref _rotation, value);
        }

        /// <summary>Region of the atlas to draw, as u0, v0, u1, v1.</summary>
        public Vector4 SourceUv
        {
            get => _sourceUv;
            set => SetVisualProperty(ref _sourceUv, value);
        }

        /// <summary>
        /// The image's size in pixels. Used for the desired size and for the aspect ratio -- UVs alone
        /// cannot supply either, since they are normalised against the atlas rather than the image.
        /// </summary>
        public Vector2 SourceSize
        {
            get => _sourceSize;
            set
            {
                if (_sourceSize == value)
                {
                    return;
                }

                _sourceSize = value;
                InvalidateMeasure();
            }
        }

        public UiImageFit Fit
        {
            get => _fit;
            set => SetVisualProperty(ref _fit, value);
        }

        /// <summary>Multiplied into the image. White leaves it alone; use it to fade or recolour.</summary>
        public Vector4 Tint
        {
            get => _tint;
            set => SetVisualProperty(ref _tint, value);
        }

        public CornerRadius Corners
        {
            get => _corners;
            set => SetVisualProperty(ref _corners, value);
        }

        /// <summary>True once something has actually assigned a region to draw.</summary>
        public bool HasSource => _sourceSize.X > 0.0f && _sourceSize.Y > 0.0f;

        /// <summary>Points the image at a region of an atlas, taking both the UV and the pixel size at once.</summary>
        public UiImage SetSource(Vector4 uv, Vector2 sizeInPixels)
        {
            SourceUv = uv;
            SourceSize = sizeInPixels;
            return this;
        }

        // Looked for whenever it is needed: the atlas can be reset, and a picture may not be ready the first time.
        private void FindPicture()
        {
            if (UiPictures.Get(this, _picture) is { } found)
            {
                SetSource(found.Uv, found.Size);
            }
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            FindPicture();
            if (!HasSource)
            {
                return Vector2.Zero;
            }

            // Its natural size when unconstrained, shrunk to fit when not. A parent that offers infinity
            // gets the image's own pixels, which is what makes an unsized image appear at 1:1.
            var width = float.IsInfinity(availableSize.X) ? _sourceSize.X : MathF.Min(_sourceSize.X, availableSize.X);
            var height = float.IsInfinity(availableSize.Y) ? _sourceSize.Y : MathF.Min(_sourceSize.Y, availableSize.Y);
            return new Vector2(width, height);
        }

        /// <summary>The rect the image actually occupies, after fitting. Public for hit testing and tests.</summary>
        public UiRect DestinationRect()
        {
            if (!HasSource || Bounds.IsEmpty || _fit == UiImageFit.Stretch)
            {
                return Bounds;
            }

            var scaleX = Bounds.Width / _sourceSize.X;
            var scaleY = Bounds.Height / _sourceSize.Y;
            var scale = _fit == UiImageFit.Cover
                ? MathF.Max(scaleX, scaleY)
                : MathF.Min(scaleX, scaleY);

            var size = _sourceSize * scale;
            return new UiRect(
                Bounds.Center.X - size.X * 0.5f,
                Bounds.Center.Y - size.Y * 0.5f,
                size.X,
                size.Y);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            FindPicture();
            if (!HasSource)
            {
                return;
            }

            // Cover overflows its bounds by design, so it clips; the others already fit inside.
            var clipped = _fit == UiImageFit.Cover;
            if (clipped)
            {
                drawList.PushClip(Bounds);
            }

            drawList.DrawImage(DestinationRect(), _sourceUv, _tint, _corners, _rotation);

            if (clipped)
            {
                drawList.PopClip();
            }
        }
    }
}

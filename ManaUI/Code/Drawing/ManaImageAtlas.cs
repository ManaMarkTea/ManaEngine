namespace ManaUI
{
    /// <summary>
    /// A four-channel atlas for anything the UI draws in colour: icons, thumbnails, colour emoji.
    /// <para>
    /// The sibling of <see cref="ManaGlyphAtlas"/>, which is single-channel coverage tinted by the
    /// instance's fill colour. That works for text and for monochrome icons and is a quarter of the memory,
    /// but it cannot carry an image that brings its own colours -- which is every case this atlas exists
    /// for.
    /// </para>
    /// <para>
    /// Both atlases are bound at once by the shader and chosen per instance by a flag, so a frame mixing
    /// text and images is still one draw call. A separate batch is only needed for a texture this atlas
    /// does not own, such as a large render target.
    /// </para>
    /// The backend uploads <see cref="Pixels"/> when <see cref="IsDirty"/>, then calls <see cref="ClearDirty"/>.
    /// </summary>
    public sealed class ManaImageAtlas
    {
        private const int Padding = 1;
        private const int BytesPerPixel = 4;

        private int _shelfX;
        private int _shelfY;
        private int _shelfHeight;

        private int _dirtyMinX;
        private int _dirtyMinY;
        private int _dirtyMaxX;
        private int _dirtyMaxY;

        /// <summary>
        /// 2048 square by default, which is 16MB of RAM.
        /// <para>
        /// 1024 was sized for icons. Mesh thumbnails are the first caller to want the atlas by the hundred --
        /// a 64px preview per asset, and an asset library runs to thousands -- and this is a shelf packer, so
        /// a region cannot be freed once handed out. Four times the area is four times as many previews before
        /// the grid falls back to format badges, for a rounding error of memory next to the meshes themselves.
        /// </para>
        /// </summary>
        public ManaImageAtlas(int width = 2048, int height = 2048)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * BytesPerPixel];

            // An opaque white block at the origin, so an instance can sample this atlas and get its own
            // fill colour back unchanged -- the same trick the glyph atlas uses.
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    var offset = (y * width + x) * BytesPerPixel;
                    Pixels[offset] = 0xFF;
                    Pixels[offset + 1] = 0xFF;
                    Pixels[offset + 2] = 0xFF;
                    Pixels[offset + 3] = 0xFF;
                }
            }

            _shelfX = 4;
            _shelfHeight = 4;
            MarkDirty(0, 0, 4, 4);
        }

        public int Width { get; }
        public int Height { get; }

        /// <summary>Tightly packed RGBA, row-major. Straight (not premultiplied) alpha.</summary>
        public byte[] Pixels { get; }

        public bool IsDirty { get; private set; }
        /// <summary>Tracks changes independently for renderers in multiple desktop windows.</summary>
        public long Revision { get; private set; }
        public long DirtyBaseRevision { get; private set; }

        /// <summary>The region changed since the last upload, as (minX, minY, maxX, maxY) exclusive-max.</summary>
        public (int MinX, int MinY, int MaxX, int MaxY) DirtyRegion => (_dirtyMinX, _dirtyMinY, _dirtyMaxX, _dirtyMaxY);

        public Vector4 WhitePixelUv => new Vector4(0.5f / Width, 0.5f / Height, 0.5f / Width, 0.5f / Height);

        /// <summary>Bumped on reset, so anything holding a UV knows it is stale.</summary>
        public int Generation { get; private set; }

        /// <summary>
        /// Reserves a padded slot. Returns false when the atlas is full -- callers should treat that as
        /// "draw nothing" rather than throwing, so too many thumbnails degrades instead of crashing.
        /// </summary>
        public bool TryPack(int width, int height, out int x, out int y)
        {
            x = 0;
            y = 0;

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            var paddedWidth = width + Padding;
            var paddedHeight = height + Padding;

            if (paddedWidth > Width)
            {
                return false;
            }

            if (_shelfX + paddedWidth > Width)
            {
                _shelfY += _shelfHeight;
                _shelfX = 0;
                _shelfHeight = 0;
            }

            if (_shelfY + paddedHeight > Height)
            {
                return false;
            }

            x = _shelfX;
            y = _shelfY;

            _shelfX += paddedWidth;
            _shelfHeight = Math.Max(_shelfHeight, paddedHeight);

            MarkDirty(x, y, x + width, y + height);
            return true;
        }

        /// <summary>Copies tightly-packed RGBA into a previously reserved slot.</summary>
        public void Blit(ReadOnlySpan<byte> source, int sourceStrideBytes, int x, int y, int width, int height)
        {
            var rowBytes = width * BytesPerPixel;

            for (var row = 0; row < height; row++)
            {
                var sourceOffset = row * sourceStrideBytes;
                var destinationOffset = ((y + row) * Width + x) * BytesPerPixel;
                source.Slice(sourceOffset, rowBytes).CopyTo(Pixels.AsSpan(destinationOffset, rowBytes));
            }

            MarkDirty(x, y, x + width, y + height);
        }

        /// <summary>
        /// Packs an image and returns its UV, or null when it does not fit. The one call most users need:
        /// reserving and blitting separately only matters for something written incrementally.
        /// </summary>
        public Vector4? Add(ReadOnlySpan<byte> rgba, int width, int height)
        {
            if (!TryPack(width, height, out var x, out var y))
            {
                return null;
            }

            Blit(rgba, width * BytesPerPixel, x, y, width, height);
            return ToUv(x, y, width, height);
        }

        public Vector4 ToUv(int x, int y, int width, int height)
        {
            return new Vector4(
                x / (float)Width,
                y / (float)Height,
                (x + width) / (float)Width,
                (y + height) / (float)Height);
        }

        public void ClearDirty()
        {
            IsDirty = false;
            _dirtyMinX = int.MaxValue;
            _dirtyMinY = int.MaxValue;
            _dirtyMaxX = 0;
            _dirtyMaxY = 0;
        }

        public void Reset()
        {
            Array.Clear(Pixels);

            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    var offset = (y * Width + x) * BytesPerPixel;
                    Pixels[offset] = 0xFF;
                    Pixels[offset + 1] = 0xFF;
                    Pixels[offset + 2] = 0xFF;
                    Pixels[offset + 3] = 0xFF;
                }
            }

            _shelfX = 4;
            _shelfY = 0;
            _shelfHeight = 4;
            Generation++;
            MarkDirty(0, 0, Width, Height);
        }

        private void MarkDirty(int minX, int minY, int maxX, int maxY)
        {
            if (!IsDirty)
            {
                DirtyBaseRevision = Revision;
                _dirtyMinX = int.MaxValue;
                _dirtyMinY = int.MaxValue;
                _dirtyMaxX = 0;
                _dirtyMaxY = 0;
                IsDirty = true;
            }
            Revision++;

            _dirtyMinX = Math.Min(_dirtyMinX, minX);
            _dirtyMinY = Math.Min(_dirtyMinY, minY);
            _dirtyMaxX = Math.Max(_dirtyMaxX, maxX);
            _dirtyMaxY = Math.Max(_dirtyMaxY, maxY);
        }
    }
}

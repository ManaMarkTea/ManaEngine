namespace ManaUI
{
    /// <summary>
    /// A single-channel coverage atlas shared by every font and size in the UI.
    /// <para>
    /// One atlas for everything is deliberate: it is what allows an entire frame -- panels, borders and all
    /// text -- to go out as one instanced draw call. Packing is a shelf (row) allocator, which is a good fit
    /// because glyphs arrive in similar heights and are never freed.
    /// </para>
    /// The backend uploads <see cref="Pixels"/> when <see cref="IsDirty"/>, then calls <see cref="ClearDirty"/>.
    /// </summary>
    public sealed class ManaGlyphAtlas
    {
        private const int Padding = 1;

        private int _shelfX;
        private int _shelfY;
        private int _shelfHeight;

        private int _dirtyMinX;
        private int _dirtyMinY;
        private int _dirtyMaxX;
        private int _dirtyMaxY;

        public ManaGlyphAtlas(int width = 1024, int height = 1024)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height];

            // A 2x2 fully-opaque block at the origin. Untextured instances that still want to sample the
            // atlas (and any future solid-fill-through-the-text-path case) point here.
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    Pixels[y * width + x] = 0xFF;
                }
            }

            _shelfX = 4;
            _shelfHeight = 4;
            MarkDirty(0, 0, 4, 4);
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }

        public bool IsDirty { get; private set; }

        /// <summary>The region changed since the last upload, as (minX, minY, maxX, maxY) exclusive-max.</summary>
        public (int MinX, int MinY, int MaxX, int MaxY) DirtyRegion => (_dirtyMinX, _dirtyMinY, _dirtyMaxX, _dirtyMaxY);

        /// <summary>UV of the solid white texel, for instances that sample the atlas but want no shape.</summary>
        public Vector4 WhitePixelUv => new Vector4(0.5f / Width, 0.5f / Height, 0.5f / Width, 0.5f / Height);

        /// <summary>Bumped whenever the atlas is reset, so fonts know their cached glyphs are stale.</summary>
        public int Generation { get; private set; }

        /// <summary>
        /// Reserves a padded slot. Returns false when the atlas is full -- callers should treat that as
        /// "draw nothing" rather than throwing, so a runaway glyph set degrades instead of crashing.
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
                // Close the current shelf and open a new one below it.
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

        /// <summary>Copies a tightly-packed coverage bitmap into a previously reserved slot.</summary>
        public void Blit(ReadOnlySpan<byte> source, int sourceStride, int x, int y, int width, int height)
        {
            for (var row = 0; row < height; row++)
            {
                var sourceOffset = row * sourceStride;
                var destinationOffset = (y + row) * Width + x;
                source.Slice(sourceOffset, width).CopyTo(Pixels.AsSpan(destinationOffset, width));
            }

            MarkDirty(x, y, x + width, y + height);
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

        /// <summary>
        /// Empties the atlas. Every cached glyph is invalidated via <see cref="Generation"/>; fonts re-raster
        /// on demand. Used when the UI scale changes and the old sizes will never be asked for again.
        /// </summary>
        public void Reset()
        {
            Array.Clear(Pixels);
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 2; x++)
                {
                    Pixels[y * Width + x] = 0xFF;
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
                _dirtyMinX = int.MaxValue;
                _dirtyMinY = int.MaxValue;
                _dirtyMaxX = 0;
                _dirtyMaxY = 0;
                IsDirty = true;
            }

            _dirtyMinX = Math.Min(_dirtyMinX, minX);
            _dirtyMinY = Math.Min(_dirtyMinY, minY);
            _dirtyMaxX = Math.Max(_dirtyMaxX, maxX);
            _dirtyMaxY = Math.Max(_dirtyMaxY, maxY);
        }
    }
}

namespace ManaUI
{
    /// <summary>
    /// A picture the application owns and redraws as often as it likes -- a canvas, a plot, a software-rendered
    /// view. Unlike <see cref="ManaImageAtlas"/> it is one image of any size in a texture of its own, so a big
    /// picture does not crowd out glyphs and thumbnails.
    /// <para>
    /// ManaUI only holds the pixels (straight-alpha RGBA, sRGB-encoded as a PNG's are); the graphics backend
    /// uploads them when <see cref="Revision"/> moves and the picture is on screen. Update it from the thread that
    /// lays out the UI.
    /// </para>
    /// </summary>
    public sealed class ManaUiSurface
    {
        // Far above the engine's own texture ids (ManaRenderTargetPreview), so the two never meet.
        private static int _nextId = 10_000;

        private byte[] _pixels = Array.Empty<byte>();

        public ManaUiSurface()
        {
            Id = Interlocked.Increment(ref _nextId);
        }

        /// <summary>What a draw batch names this picture by (<see cref="ManaUiDrawList.SetTexture"/>).</summary>
        public int Id { get; }

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>Bumped by every <see cref="Update"/>; the backend re-uploads when it differs from what it holds.</summary>
        public long Revision { get; private set; }

        public bool HasPixels => Width > 0 && Height > 0;

        /// <summary>Gone for good: the backend frees its texture and forgets the id.</summary>
        public bool IsDisposed { get; private set; }

        public ReadOnlySpan<byte> Pixels => _pixels.AsSpan(0, Width * Height * 4);

        /// <summary>Raised after every <see cref="Update"/> and on <see cref="Dispose"/>.</summary>
        public event Action? Changed;

        /// <summary>Replaces the picture. <paramref name="rgba"/> is <paramref name="width"/> x <paramref name="height"/> x 4 bytes, rows top to bottom.</summary>
        public void Update(int width, int height, ReadOnlySpan<byte> rgba)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "A surface needs a positive size.");
            }
            if (rgba.Length < width * height * 4)
            {
                throw new ArgumentException("Not enough pixels for that size.", nameof(rgba));
            }

            if (_pixels.Length < width * height * 4)
            {
                _pixels = new byte[width * height * 4];
            }

            rgba.Slice(0, width * height * 4).CopyTo(_pixels);
            Width = width;
            Height = height;
            Revision++;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            IsDisposed = true;
            _pixels = Array.Empty<byte>();
            Width = 0;
            Height = 0;
            Changed?.Invoke();
        }
    }
}

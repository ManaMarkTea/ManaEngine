namespace ManaCore.Graphics
{
    /// <summary>
    /// A picture made on the CPU and changed as the game runs -- a live pixel effect's frame. Whoever draws it writes
    /// <see cref="Pixels"/> (RGBA, bottom row first, as textures are uploaded) under <see cref="Sync"/> and calls
    /// <see cref="Publish"/>; the backend uploads it again whenever <see cref="Revision"/> has moved.
    /// </summary>
    public sealed class ManaDynamicTexture
    {
        private static int _nextId;
        private int _revision;

        public ManaDynamicTexture(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public int Id { get; } = Interlocked.Increment(ref _nextId);
        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }

        /// <summary>Held while the pixels are written and while they are uploaded, so a frame never goes up half drawn.</summary>
        public object Sync { get; } = new();

        public int Revision => Volatile.Read(ref _revision);

        /// <summary>Says the pixels changed: the next frame uploads them.</summary>
        public void Publish() => Interlocked.Increment(ref _revision);
    }
}

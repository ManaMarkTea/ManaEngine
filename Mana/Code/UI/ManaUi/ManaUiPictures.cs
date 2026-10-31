using ManaCore.Imaging;
using ManaUI;
using System.Runtime.CompilerServices;

namespace Mana.Code.UI.ManaUi
{
    /// <summary>
    /// Finds the pictures layout files name, at full size (<see cref="ManaUiTexturePreviews"/> makes small thumbnails).
    /// Paths are what sprites take: a repo path, or a pack picture.
    /// </summary>
    public static class ManaUiPictures
    {
        // A picture that is not ready (a pack being read, the browser still decoding) is asked for every frame.
        private const int RetryAfterAsks = 60;
        private const int MaximumTries = 5;

        private static readonly ConditionalWeakTable<ManaImageAtlas, Cache> Caches = new();

        private sealed class Entry
        {
            public UiPicture? Found;
            public int Tries;
            public int Asks;
        }

        private sealed class Cache
        {
            public int Generation = -1;
            public readonly Dictionary<string, Entry> Entries = new();
        }

        public static void Install() => UiPictures.Resolver = Find;

        private static UiPicture? Find(ManaImageAtlas atlas, string path)
        {
            var cache = Caches.GetValue(atlas, _ => new Cache());
            if (cache.Generation != atlas.Generation)
            {
                cache.Entries.Clear();
                cache.Generation = atlas.Generation;
            }

            if (!cache.Entries.TryGetValue(path, out var entry))
            {
                entry = new Entry();
                cache.Entries[path] = entry;
            }

            if (entry.Found != null)
            {
                return entry.Found;
            }

            if (entry.Tries >= MaximumTries || entry.Asks-- > 0)
            {
                return null;
            }

            entry.Tries++;
            entry.Asks = RetryAfterAsks;
            entry.Found = Load(atlas, path);
            return entry.Found;
        }

        private static UiPicture? Load(ManaImageAtlas atlas, string path)
        {
            try
            {
                if (ManaSpritePictures.TryGetResolved(path) is not { } file)
                {
                    return null;
                }

                var image = ManaImageCodec.Load(file);
                if (atlas.Add(image.Pixels, image.Width, image.Height) is not { } uv)
                {
                    return null;
                }

                return new UiPicture(uv, new Vector2(image.Width, image.Height));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}

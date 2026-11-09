using ManaUI;
using ManaEditor.Code.Assets;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>
    /// Where generated thumbnails become drawable: the bridge between the background cache and ManaUI's
    /// colour atlas.
    /// <para>
    /// Separate from <see cref="MeshThumbnailCache"/> because the two halves have opposite constraints. The
    /// cache runs on a worker and knows nothing about the UI; the atlas is not thread-safe, belongs to the
    /// frame, and is a fixed size. So the cache produces pixels whenever it can, and this hands a few of them
    /// to the atlas per frame, on the main thread.
    /// </para>
    /// </summary>
    public sealed class MeshThumbnailImages
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ManaImageAtlas, MeshThumbnailImages> Atlases = new();

        private MeshThumbnailImages() { }

        // Native pop-outs sharing an atlas must also share its thumbnail coordinates.
        public static MeshThumbnailImages For(UiRoot root) => Atlases.GetValue(root.Images, _ => new MeshThumbnailImages());

        /// <summary>
        /// How long a visible tile stays eligible for the resident atlas when a rebuild is needed. This is
        /// deliberately wall-clock rather than frame-counted: the thumbnail bridge is called from UI bindings
        /// and from the editor pump, not from one tidy owner that can bracket a frame.
        /// </summary>
        private const long RecentVisibilityMilliseconds = 2500;

        /// <summary>
        /// Uploads allowed per frame. Each insertion dirties a region of the atlas that the backend re-uploads
        /// afterwards, so letting a whole folder land in one frame would produce exactly the hitch the
        /// background worker exists to avoid.
        /// </summary>
        private const int UploadsPerFrame = 4;

        private static readonly int ExpectedByteCount =
            MeshThumbnailCache.ThumbnailSize * MeshThumbnailCache.ThumbnailSize * 4;

        private readonly Dictionary<string, Vector4> Uvs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> Pixels = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> LastVisibleAt = new(StringComparer.Ordinal);
        private readonly HashSet<string> Failed = new(StringComparer.Ordinal);
        private int _atlasGeneration = -1;

        /// <summary>Thumbnails resident in the atlas.</summary>
        public int ResidentCount => Uvs.Count;

        /// <summary>Decoded thumbnails retained outside the atlas, so residents can be rebuilt after scrolling.</summary>
        public int StoredCount => Pixels.Count;

        /// <summary>True only when the current visible set cannot fit even after rebuilding the atlas.</summary>
        public bool IsAtlasFull { get; private set; }

        public bool HasImage(string assetPath) => Uvs.ContainsKey(assetPath);

        public Vector4 UvFor(string assetPath) =>
            Uvs.TryGetValue(assetPath, out var uv) ? uv : Vector4.Zero;

        /// <summary>
        /// Marks a tile as visible and makes its thumbnail resident if the cache has already produced pixels.
        /// This is called from the tile's polled bindings, so newly visible assets can repopulate the atlas
        /// without asking the thumbnail worker to regenerate anything.
        /// </summary>
        public void Touch(UiRoot root, string assetPath)
        {
            if (root == null || string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            TrackAtlasGeneration(root.Images);
            LastVisibleAt[assetPath] = Environment.TickCount64;
            EnsureResident(root, assetPath);
        }

        /// <summary>
        /// Moves finished thumbnails into the atlas. Main thread, once per frame, from wherever the workspace
        /// already pumps per-frame work.
        /// </summary>
        public void Pump(UiRoot root, MeshThumbnailCache cache)
        {
            foreach (var (path, pixels) in cache.TakeFinished(UploadsPerFrame))
            {
                AcceptFinished(root, path, pixels);
            }
        }

        /// <summary>
        /// Accepts one completed thumbnail. <see cref="Pump"/> is the normal caller; tests and alternate hosts
        /// can use this directly when they already own a finished-pixel queue.
        /// </summary>
        public void AcceptFinished(UiRoot root, string path, byte[]? pixels)
        {
            if (root == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            TrackAtlasGeneration(root.Images);

            if (pixels == null || pixels.Length != ExpectedByteCount)
            {
                // Remembered so a failed asset is not re-requested every frame for the rest of the session.
                Failed.Add(path);
                Pixels.Remove(path);
                Uvs.Remove(path);
                return;
            }

            Failed.Remove(path);
            Pixels[path] = pixels;

            if (WasRecentlyVisible(path, Environment.TickCount64))
            {
                // A manual Refresh Thumbnail can produce new pixels for a thumbnail already resident in the
                // atlas. Drop the old UV so the new pixels get packed and the tile visibly updates.
                Uvs.Remove(path);
                EnsureResident(root, path);
            }
        }

        /// <summary>Forgets every mapping. For a test, or a library pointed somewhere new.</summary>
        public void Reset()
        {
            Uvs.Clear();
            Pixels.Clear();
            LastVisibleAt.Clear();
            Failed.Clear();
            IsAtlasFull = false;
            _atlasGeneration = -1;
        }

        private void TrackAtlasGeneration(ManaImageAtlas atlas)
        {
            if (_atlasGeneration == atlas.Generation)
            {
                return;
            }

            // Another image-atlas user may have reset it. UVs are atlas-generation scoped; decoded pixels are
            // not, so they survive and can be repacked the next time a tile becomes visible.
            Uvs.Clear();
            IsAtlasFull = false;
            _atlasGeneration = atlas.Generation;
        }

        private bool EnsureResident(UiRoot root, string path)
        {
            if (Uvs.ContainsKey(path) || !Pixels.TryGetValue(path, out var pixels))
            {
                return Uvs.ContainsKey(path);
            }

            if (TryAddResident(root.Images, path, pixels))
            {
                return true;
            }

            RebuildResidents(root, path);
            return Uvs.ContainsKey(path);
        }

        private bool TryAddResident(ManaImageAtlas atlas, string path, byte[] pixels)
        {
            var uv = atlas.Add(pixels, MeshThumbnailCache.ThumbnailSize, MeshThumbnailCache.ThumbnailSize);
            if (uv == null)
            {
                return false;
            }

            Uvs[path] = uv.Value;
            IsAtlasFull = false;
            return true;
        }

        private void RebuildResidents(UiRoot root, string priorityPath)
        {
            root.Images.Reset();
            Uvs.Clear();
            _atlasGeneration = root.Images.Generation;
            IsAtlasFull = false;

            var now = Environment.TickCount64;
            var paths = LastVisibleAt
                .Where(pair => string.Equals(pair.Key, priorityPath, StringComparison.Ordinal)
                    || WasRecentlyVisible(pair.Key, now))
                .OrderByDescending(pair => string.Equals(pair.Key, priorityPath, StringComparison.Ordinal))
                .ThenByDescending(pair => pair.Value)
                .Select(pair => pair.Key);

            foreach (var path in paths)
            {
                if (!Pixels.TryGetValue(path, out var pixels))
                {
                    continue;
                }

                if (TryAddResident(root.Images, path, pixels))
                {
                    continue;
                }

                IsAtlasFull = true;
                break;
            }
        }

        private bool WasRecentlyVisible(string path, long now)
        {
            return LastVisibleAt.TryGetValue(path, out var then)
                && now - then <= RecentVisibilityMilliseconds;
        }
    }
}

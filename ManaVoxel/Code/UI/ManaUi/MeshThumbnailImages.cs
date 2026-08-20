using ManaUI;
using ManaVoxel.Code.Assets;

namespace ManaVoxel.Code.UI.ManaUi
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
    /// <para>
    /// Static because there is one atlas and one asset library. A second workspace would want an instance,
    /// which is a change of shape rather than of behaviour.
    /// </para>
    /// </summary>
    public static class MeshThumbnailImages
    {
        /// <summary>
        /// Thumbnails allowed into the atlas. The atlas is shared with icons and any other colour image, it
        /// cannot free a region once packed (it is a shelf packer, not a heap), and a library can hold
        /// thousands of assets -- so this is the point where the grid stops gaining previews and keeps format
        /// badges instead. At 64px in a 2048 atlas the ceiling is around a thousand; this leaves room.
        /// </summary>
        private const int MaximumResident = 900;

        /// <summary>
        /// Uploads allowed per frame. Each insertion dirties a region of the atlas that the backend re-uploads
        /// afterwards, so letting a whole folder land in one frame would produce exactly the hitch the
        /// background worker exists to avoid.
        /// </summary>
        private const int UploadsPerFrame = 4;

        private static readonly Dictionary<string, Vector4> Uvs = new(StringComparer.Ordinal);
        private static readonly HashSet<string> Failed = new(StringComparer.Ordinal);

        /// <summary>Thumbnails resident in the atlas.</summary>
        public static int ResidentCount => Uvs.Count;

        /// <summary>True once the atlas is full, so the remaining tiles will keep their format badges.</summary>
        public static bool IsAtlasFull { get; private set; }

        public static bool HasImage(string assetPath) => Uvs.ContainsKey(assetPath);

        public static Vector4 UvFor(string assetPath) =>
            Uvs.TryGetValue(assetPath, out var uv) ? uv : Vector4.Zero;

        /// <summary>
        /// Moves finished thumbnails into the atlas. Main thread, once per frame, from wherever the workspace
        /// already pumps per-frame work.
        /// </summary>
        public static void Pump(UiRoot root, MeshThumbnailCache cache)
        {
            foreach (var (path, pixels) in cache.TakeFinished(UploadsPerFrame))
            {
                if (pixels == null)
                {
                    // Remembered so a failed asset is not re-requested every frame for the rest of the
                    // session -- the cache only collapses repeats of requests it has not finished.
                    Failed.Add(path);
                    continue;
                }

                if (Uvs.Count >= MaximumResident)
                {
                    IsAtlasFull = true;
                    continue;
                }

                var uv = root.Images.Add(pixels, MeshThumbnailCache.ThumbnailSize, MeshThumbnailCache.ThumbnailSize);
                if (uv == null)
                {
                    // The atlas said no: it is out of shelf space, which is permanent for this session.
                    IsAtlasFull = true;
                    continue;
                }

                Uvs[path] = uv.Value;
            }
        }

        /// <summary>Forgets every mapping. For a test, or a library pointed somewhere new.</summary>
        public static void Reset()
        {
            Uvs.Clear();
            Failed.Clear();
            IsAtlasFull = false;
        }
    }
}

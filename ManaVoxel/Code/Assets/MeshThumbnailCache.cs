using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Mana.Code;
using Mana.Code.Shapes.Loaders;
using ManaCore.Diagnostics;

namespace ManaVoxel.Code.Assets
{
    /// <summary>
    /// Thumbnails for mesh assets: generated on a worker, cached on disk, handed to the UI a few at a time.
    /// <para>
    /// Three tiers, because generating one costs an Assimp import and the answer never changes. In memory for
    /// this session; on disk keyed by the file's path, size and write time, so a second run of the editor
    /// shows a full grid immediately; and generated only for assets somebody actually looked at, since a
    /// library of a thousand models would otherwise spend minutes rendering previews nobody scrolled to.
    /// </para>
    /// <para>
    /// One worker, not a pool. Assimp is being asked to import arbitrary files off the main thread and the
    /// point here is to keep the frame moving, not to finish the whole library as fast as possible -- a
    /// single worker does that with none of the questions a pool would raise.
    /// </para>
    /// </summary>
    public sealed class MeshThumbnailCache : IDisposable
    {
        /// <summary>Edge length of a thumbnail. 64 is what the grid draws at 1x, and the atlas holds ~1000.</summary>
        public const int ThumbnailSize = 64;

        /// <summary>
        /// Bump to invalidate every cached file on disk. The stored bytes are raw pixels with no header
        /// describing how they were made, so a change to the rasterizer needs this or old previews persist.
        /// </summary>
        private const int CacheFormatVersion = 1;

        private const int BytesPerPixel = 4;
        private static readonly int ExpectedByteCount = ThumbnailSize * ThumbnailSize * BytesPerPixel;

        private readonly ManaContext _context;
        private readonly string _cacheDirectory;

        private readonly ConcurrentQueue<string> _requests = new();
        private readonly ConcurrentQueue<(string Path, byte[]? Pixels)> _finished = new();

        // Every path ever requested, so a repeat request -- which a scrolling grid makes every frame -- does
        // not queue the same import again.
        private readonly ConcurrentDictionary<string, byte> _requested = new();

        private readonly SemaphoreSlim _work = new(0);
        private readonly CancellationTokenSource _shutdown = new();
        private Task? _worker;

        public MeshThumbnailCache(ManaContext context, string? cacheDirectory = null)
        {
            _context = context;
            _cacheDirectory = cacheDirectory ?? DefaultCacheDirectory();
        }

        /// <summary>Thumbnails generated this session, for a diagnostic readout.</summary>
        public int GeneratedCount { get; private set; }

        /// <summary>Thumbnails that came from disk rather than being generated.</summary>
        public int DiskHitCount { get; private set; }

        /// <summary>Assets whose preview could not be produced, so the UI stops asking.</summary>
        public int FailedCount { get; private set; }

        /// <summary>Requests queued and not yet finished.</summary>
        public int PendingCount => _requests.Count;

        private static string DefaultCacheDirectory()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "ManaRenderer", "Thumbnails");
        }

        /// <summary>
        /// Asks for a thumbnail if this is the first time anyone has. Cheap and safe to call every frame for
        /// every visible tile, which is exactly how the grid uses it.
        /// </summary>
        public void Request(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !_requested.TryAdd(assetPath, 0))
            {
                return;
            }

            _requests.Enqueue(assetPath);
            EnsureWorker();
            _work.Release();
        }

        /// <summary>
        /// Hands over up to <paramref name="maximum"/> finished thumbnails. Main thread: the caller uploads
        /// them into the UI atlas, which is not thread-safe and belongs to the frame.
        /// <para>
        /// Bounded because atlas insertion dirties a region the backend then re-uploads, and a folder's worth
        /// arriving in one frame would be a visible hitch -- the exact thing the background worker is for.
        /// </para>
        /// </summary>
        public IEnumerable<(string Path, byte[]? Pixels)> TakeFinished(int maximum = 4)
        {
            for (var taken = 0; taken < maximum && _finished.TryDequeue(out var finished); taken++)
            {
                if (finished.Pixels == null)
                {
                    FailedCount++;
                }

                yield return finished;
            }
        }

        private void EnsureWorker()
        {
            if (_worker != null)
            {
                return;
            }

            _worker = Task.Run(WorkerLoop);
        }

        private async Task WorkerLoop()
        {
            if (Thread.CurrentThread.Name == null)
            {
                Thread.CurrentThread.Name = "Mesh thumbnails";
            }

            while (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    await _work.WaitAsync(_shutdown.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!_requests.TryDequeue(out var path))
                {
                    continue;
                }

                byte[]? pixels = null;
                try
                {
                    pixels = LoadFromDisk(path);
                    if (pixels != null)
                    {
                        DiskHitCount++;
                    }
                    else
                    {
                        pixels = Generate(path);
                        if (pixels != null)
                        {
                            GeneratedCount++;
                            SaveToDisk(path, pixels);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // A library scanned off disk contains whatever is on disk, including files Assimp will
                    // refuse. One unreadable asset must not take the worker down with it.
                    ManaLog.WriteDiagnostic($"[Thumbnails] '{Path.GetFileName(path)}' failed: {ex.Message}");
                }

                _finished.Enqueue((path, pixels));
            }
        }

        private byte[]? Generate(string assetPath)
        {
            if (!File.Exists(assetPath))
            {
                return null;
            }

            // CPU-only: this loads geometry and nothing else, so it touches no graphics device and is safe
            // away from the render thread. A full LoadFile would build materials and upload buffers.
            var loader = new ManaGltfLoader(_context);
            var mesh = loader.LoadFirstMeshCpuOnly(assetPath);
            var cpuData = mesh.CpuData;

            return cpuData == null
                ? null
                : MeshThumbnailRasterizer.Render(cpuData, ThumbnailSize, new Vector4(0.82f, 0.84f, 0.88f, 1.0f));
        }

        // ----- disk tier ---------------------------------------------------------------------------------

        /// <summary>
        /// Cache file name. Includes the asset's size and write time, so an edited model regenerates rather
        /// than showing a preview of what it used to look like -- a hash of the path alone would not notice.
        /// </summary>
        private string CacheFileFor(string assetPath)
        {
            var stamp = string.Empty;
            try
            {
                var info = new FileInfo(assetPath);
                if (info.Exists)
                {
                    stamp = $"{info.Length}-{info.LastWriteTimeUtc.Ticks}";
                }
            }
            catch (IOException)
            {
                // An unreadable stamp only costs a regenerate, so it is not worth failing over.
            }

            var key = $"{CacheFormatVersion}|{ThumbnailSize}|{assetPath}|{stamp}";
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
            return Path.Combine(_cacheDirectory, $"{hash[..32]}.thumb");
        }

        private byte[]? LoadFromDisk(string assetPath)
        {
            try
            {
                var file = CacheFileFor(assetPath);
                if (!File.Exists(file))
                {
                    return null;
                }

                var bytes = File.ReadAllBytes(file);

                // A truncated file is a half-written one from a previous run; regenerating is the fix, and
                // handing short buffers to the atlas would be a buffer overrun rather than a bad picture.
                return bytes.Length == ExpectedByteCount ? bytes : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private void SaveToDisk(string assetPath, byte[] pixels)
        {
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
                var file = CacheFileFor(assetPath);

                // Written beside the target and moved into place, so a crash mid-write cannot leave a
                // half-file that later reads as a valid cache entry.
                var temporary = file + ".tmp";
                File.WriteAllBytes(temporary, pixels);
                File.Move(temporary, file, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ManaLog.WriteDiagnostic($"[Thumbnails] could not cache '{Path.GetFileName(assetPath)}': {ex.Message}");
            }
        }

        public void Dispose()
        {
            _shutdown.Cancel();
            _work.Release();
            _shutdown.Dispose();
            _work.Dispose();
        }
    }
}

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Mana.Code;
using Mana.Code.Shapes.Loaders;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaVoxel.Code.Services;

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
        // 2: real material colour (texture / vertex colour / base colour factor) replaced the flat tint,
        // and zip-packed assets generate at all now instead of silently failing.
        // 3: a textured fragment's own alpha now cuts it out instead of painting transparent regions black
        // and wrongly winning the depth test; a multi-part character (one mesh per limb) now combines every
        // part instead of showing whichever one happened to be first.
        private const int CacheFormatVersion = 3;

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

        /// <summary>
        /// Project-local rather than per-user AppData: the cache key already includes the asset's full path,
        /// so a global store bought no real sharing between checkouts anyway (a different checkout is a
        /// different path, hence a different key). Project-local means it is easy to find, easy to clear,
        /// and travels naturally with per-checkout regeneration -- the same "Library holds derived data" idea
        /// Unity uses, and gitignored the same way.
        /// </summary>
        private static string DefaultCacheDirectory() =>
            Path.Combine(ManaProjectFolder.Resolve("Library"), "Thumbnails");

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
            // CPU-only either way: this loads geometry and material colour, nothing else, so it touches no
            // graphics device and is safe away from the render thread. A full LoadFile would build materials
            // and upload buffers.
            var cpuData = MeshAssetLibrary.TryDecodeZip(assetPath, out var zipPath, out var entryPath)
                ? GenerateFromZip(zipPath, entryPath)
                : GenerateFromFile(assetPath);

            return cpuData == null
                ? null
                : MeshThumbnailRasterizer.Render(cpuData, ThumbnailSize, new Vector4(0.82f, 0.84f, 0.88f, 1.0f));
        }

        private ManaMeshCpuData? GenerateFromFile(string assetPath)
        {
            if (!File.Exists(assetPath))
            {
                return null;
            }

            var loader = new ManaGltfLoader(_context);
            return loader.LoadFirstMeshCpuOnly(assetPath).CpuData;
        }

        /// <summary>
        /// A zip-packed asset (most of the character and prop library ships this way) has no directory on
        /// disk a relative texture reference could resolve against, so its bytes are read straight out of
        /// the archive instead -- same resolver <see cref="VoxelFileImportService"/> uses to import one for
        /// real, just without the GPU-hooked half of that pipeline.
        /// </summary>
        private ManaMeshCpuData? GenerateFromZip(string zipPath, string entryPath)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(entryPath);
            if (entry == null)
            {
                return null;
            }

            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);

            var resolver = VoxelFileImportService.CreateZipSidecarResolver(archive, entryPath);
            var loader = new ManaGltfLoader(_context);
            return loader.LoadFirstMeshCpuOnlyFromMemory(memory.ToArray(), entry.Name, resolver).CpuData;
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

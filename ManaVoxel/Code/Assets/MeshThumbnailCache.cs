using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes.Loaders;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaGraphics.Code.Graphics;
using ManaVoxel.Code.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

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
        /// <summary>Edge length of a generated thumbnail, matching the import picker's largest display size.</summary>
        public const int ThumbnailSize = 112;

        /// <summary>
        /// Bump to invalidate every cached file on disk. A version bump is not strictly required for a
        /// format change any more -- a PNG decode of an old raw-pixel file just fails and regenerates -- but
        /// it still means old entries do not linger as silent decode failures on the first load after an
        /// update, and it is the one place documenting why the format moved.
        /// </summary>
        // 2: real material colour (texture / vertex colour / base colour factor) replaced the flat tint,
        // and zip-packed assets generate at all now instead of silently failing.
        // 3: a textured fragment's own alpha now cuts it out instead of painting transparent regions black
        // and wrongly winning the depth test; a multi-part character (one mesh per limb) now combines every
        // part instead of showing whichever one happened to be first.
        // 4: WebGPU builds now render through the real renderer (ManaWebGpuContext.RenderThumbnailAsync)
        // instead of the CPU rasterizer, which also fixes multi-part assets rendering with each part's real
        // composed node transform instead of the CPU path's flat local-space concatenation. OpenGL builds,
        // and any WebGPU asset the GPU render fails on, still fall back to MeshThumbnailRasterizer unchanged.
        // 5: cached files are PNG-encoded instead of raw RGBA -- an order of magnitude smaller on disk for
        // images that are mostly flat colour and transparent background. Still named .thumb, so a PNG
        // viewer needs the extension renamed or pointed at the bytes directly to open one.
        // 6: WebGPU thumbnails queue correctly after the UI cache is created late, render from a top-down
        // isometric camera, and reject/sanitize empty or translucent readbacks before they reach disk.
        // 7: thumbnails generate at 112px so the import picker's largest preview size draws 1:1 instead of
        // upscaling a 64px source and exposing filtering artifacts.
        // 8: CPU fallback thumbnails bake glTF node transforms and use the same top-down isometric view as
        // WebGPU, so cached sideways fallback renders regenerate.
        // 9: imported node rotations now preserve their authored axis when glTF quaternions are decomposed
        // into Mana's X=pitch, Y=yaw, Z=roll Euler convention.
        private const int CacheFormatVersion = 9;

        private const int BytesPerPixel = 4;
        private static readonly int ExpectedByteCount = ThumbnailSize * ThumbnailSize * BytesPerPixel;

        private readonly ManaContext _context;
        private readonly ManaWebGpuContext? _webGpuContext;
        private readonly string _cacheDirectory;

        private readonly ConcurrentQueue<string> _requests = new();
        private readonly ConcurrentQueue<(string Path, byte[]? Pixels)> _finished = new();

        // Every path ever requested, so a repeat request -- which a scrolling grid makes every frame -- does
        // not queue the same import again.
        private readonly ConcurrentDictionary<string, byte> _requested = new();

        private readonly SemaphoreSlim _work = new(0);
        private readonly CancellationTokenSource _shutdown = new();
        private Task? _worker;

        // A throwaway world used only to load-and-discard one asset at a time for a GPU render -- see
        // GetOrCreateThumbnailWorld. Never the live editor's own Context.World.
        private ManaWorld? _thumbnailWorld;

        public MeshThumbnailCache(ManaContext context, ManaWebGpuContext? webGpuContext = null, string? cacheDirectory = null)
        {
            _context = context;
            _webGpuContext = webGpuContext;
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

        /// <summary>Unique assets this cache has accepted this session, including requests already finished.</summary>
        public int RequestedCount => _requested.Count;

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
        /// Forces one asset to regenerate, bypassing both the in-flight dedup and the disk cache -- for
        /// when a generated thumbnail is judged wrong, or the source file has changed in a way the cache
        /// key's size/write-time stamp does not catch (a zip-packed asset's stamp never reflects the zip's
        /// own mtime, only the plain-file case does -- see <see cref="CacheFileFor"/>).
        /// <para>
        /// The stale disk entry is deleted outright rather than merely re-requested: the worker checks disk
        /// before generating, so leaving the old file in place would make this a no-op.
        /// </para>
        /// </summary>
        public void ForceRefresh(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            try
            {
                File.Delete(CacheFileFor(assetPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left in place, the stale file just means SaveToDisk's own overwrite-on-move handles it
                // once regeneration succeeds -- see its own comment on why that move is safe.
            }

            _requested.TryRemove(assetPath, out _);
            Request(assetPath);
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
            if (_webGpuContext != null)
            {
                try
                {
                    var gpuPixels = GenerateViaGpu(assetPath);
                    if (gpuPixels != null)
                    {
                        return gpuPixels;
                    }
                }
                catch (Exception ex)
                {
                    ManaLog.WriteDiagnostic(
                        $"[Thumbnails] GPU render failed for '{Path.GetFileName(assetPath)}', falling back to CPU rasterizer: {ex.Message}");
                }
            }

            return GenerateViaCpu(assetPath);
        }

        /// <summary>
        /// The real renderer, through a throwaway world loaded via the full glTF pipeline -- correct
        /// multi-part assembly, composed node transforms, real materials and textures, real alpha handling.
        /// WebGPU only; see <see cref="GenerateViaCpu"/> for the OpenGL/fallback path.
        /// </summary>
        private byte[]? GenerateViaGpu(string assetPath)
        {
            var world = GetOrCreateThumbnailWorld();
            List<ManaRenderMeshInstance> instances;

            lock (world.SceneLock)
            {
                try
                {
                    var rootNode = MeshAssetLibrary.TryDecodeZip(assetPath, out var zipPath, out var entryPath)
                        ? LoadGpuPreviewFromZip(world, zipPath, entryPath)
                        : world.LoadFromGltf(assetPath);

                    // LoadFromGltf(Memory) never calls this itself -- WorldTransform is only composed with
                    // its parent inside UpdateTransform, so skipping this would silently reproduce the exact
                    // "parts don't line up" bug this whole GPU path exists to fix, just less visibly.
                    rootNode.UpdateTransform();

                    instances = new List<ManaRenderMeshInstance>();
                    world.Scenes[^1].CollectRenderMeshInstances(instances);
                }
                finally
                {
                    ResetThumbnailWorld(world);
                }
            }

            if (instances.Count == 0)
            {
                return null;
            }

            return _webGpuContext!.RenderThumbnailAsync(instances, ThumbnailSize).GetAwaiter().GetResult();
        }

        private static ManaSceneNode LoadGpuPreviewFromZip(ManaWorld world, string zipPath, string entryPath)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(entryPath)
                ?? throw new FileNotFoundException($"Entry '{entryPath}' not found in zip '{zipPath}'.", zipPath);

            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);

            var resolver = VoxelFileImportService.CreateZipSidecarResolver(archive, entryPath);
            return world.LoadFromGltfMemory(memory.ToArray(), entryPath, resolver);
        }

        /// <summary>
        /// One world, reused for every request rather than one per request -- ManaWorld.Initialize
        /// subscribes to Context.Window.Resize with no teardown path, so a fresh world per thumbnail would
        /// leak one subscription (and the whole world) per asset ever generated. createRenderer: false
        /// skips building a renderer this world never needs.
        /// </summary>
        private ManaWorld GetOrCreateThumbnailWorld()
        {
            if (_thumbnailWorld != null)
            {
                return _thumbnailWorld;
            }

            var world = new ManaWorld();
            world.Initialize(_context, createRenderer: false);
            _thumbnailWorld = world;
            return world;
        }

        /// <summary>Trims the world back to what Initialize left it with, so the next request starts clean.</summary>
        private static void ResetThumbnailWorld(ManaWorld world)
        {
            if (world.Scenes.Count > 1)
            {
                world.Scenes.RemoveRange(1, world.Scenes.Count - 1);
            }

            world.Meshes.Clear();
            world.Materials.Clear();
        }

        private byte[]? GenerateViaCpu(string assetPath)
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
            return loader.LoadFirstMeshCpuOnlyFromMemory(memory.ToArray(), entryPath, resolver).CpuData;
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

        /// <summary>
        /// PNG on disk rather than the raw RGBA the rest of the pipeline works in -- a thumbnail is mostly
        /// flat colour and transparent background, which PNG's filtering and deflate shrink by an order of
        /// magnitude, and a library of a thousand cached previews is disk space and I/O either way.
        /// </summary>
        private byte[]? LoadFromDisk(string assetPath)
        {
            try
            {
                var file = CacheFileFor(assetPath);
                if (!File.Exists(file))
                {
                    return null;
                }

                using var image = Image.Load<Rgba32>(file);

                // A size mismatch is a half-written or otherwise unusable file from a previous run;
                // regenerating is the fix, and handing a wrongly-sized buffer to the atlas would be a
                // buffer overrun rather than a bad picture.
                if (image.Width != ThumbnailSize || image.Height != ThumbnailSize)
                {
                    return null;
                }

                var pixels = new byte[ExpectedByteCount];
                image.CopyPixelDataTo(pixels);
                return pixels;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageFormatException)
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
                using (var image = Image.LoadPixelData<Rgba32>(pixels, ThumbnailSize, ThumbnailSize))
                {
                    image.SaveAsPng(temporary);
                }

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

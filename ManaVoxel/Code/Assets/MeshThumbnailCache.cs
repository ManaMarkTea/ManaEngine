using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
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
    /// Three tiers, because generating one costs a glTF import and the answer never changes. In memory for
    /// this session; on disk keyed by the file's path, size and write time, so a second run of the editor
    /// shows a full grid immediately; and generated only for assets somebody actually looked at, since a
    /// library of a thousand models would otherwise spend minutes rendering previews nobody scrolled to.
    /// </para>
    /// <para>
    /// One worker, not a pool. SharpGLTF is being asked to import arbitrary files off the main thread and the
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
        // not queue the same import again. Never shrinks: RequestedCount below promises "unique assets this
        // cache has ever accepted", not "still outstanding".
        private readonly ConcurrentDictionary<string, byte> _requested = new();

        // Paths the worker could not answer last time (neither a pre-built preview nor the model itself was
        // available) and that a later Request() should retry rather than treat as already handled. Kept
        // separate from _requested specifically so a retry never shrinks the public "ever requested" count.
        private readonly ConcurrentDictionary<string, byte> _awaitingRetry = new();

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

        /// <summary>Thumbnails fetched pre-built from the catalog rather than rendered locally.</summary>
        public int PrebuiltHitCount { get; private set; }

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
        /// <para>
        /// Blocked on the model itself being ready -- generating one means loading it -- unless the catalog
        /// already has a pre-built preview staged for it (see Tools/ThumbnailBaker), in which case there is
        /// something to show well before the full model finishes downloading.
        /// </para>
        /// </summary>
        public void Request(string assetPath)
        {
            var catalog = ManaCore.Assets.ManaAssetCatalog.Current;
            if (Path.GetExtension(assetPath) != ManaPrefab.Extension && catalog != null && !catalog.IsReady(assetPath) && catalog.ThumbnailPathOf(assetPath) == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            // A path the worker previously could not answer (see the "neither available yet" branch in
            // WorkerLoop) gets one more try instead of being treated as already handled -- otherwise a
            // scrolling grid's every-frame re-request would never notice the model or its preview finally
            // showing up. _requested itself is left untouched either way; only this retry flag moves.
            var isRetry = _awaitingRetry.TryRemove(assetPath, out _);
            if (!_requested.TryAdd(assetPath, 0) && !isRetry)
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
                var hasAnswer = true;
                try
                {
                    pixels = LoadFromDisk(path);
                    if (pixels != null)
                    {
                        DiskHitCount++;
                    }
                    else
                    {
                        pixels = await TryLoadPrebuilt(path);
                        if (pixels != null)
                        {
                            PrebuiltHitCount++;
                            SaveToDisk(path, pixels);
                        }
                        else if (IsModelLocallyAvailable(path))
                        {
                            pixels = Generate(path);
                            if (pixels != null)
                            {
                                GeneratedCount++;
                                SaveToDisk(path, pixels);
                            }
                        }
                        else
                        {
                            // Neither a pre-built preview nor the model itself is available yet -- not a
                            // failure, just too early to say. Marked for retry rather than removed from
                            // _requested (see Request()'s own comment on why that set must never shrink);
                            // a future frame's Request() call for the same path sees the retry flag and
                            // queues again, instead of this tile showing a permanent failure that never
                            // asks again.
                            hasAnswer = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // A library scanned off disk contains whatever is on disk, including files Assimp will
                    // refuse. One unreadable asset must not take the worker down with it.
                    ManaLog.WriteDiagnostic($"[Thumbnails] '{Path.GetFileName(path)}' failed: {ex.Message}");
                }

                if (!hasAnswer)
                {
                    _awaitingRetry[path] = 0;
                    continue;
                }

                _finished.Enqueue((path, pixels));
            }
        }

        private static bool IsModelLocallyAvailable(string assetPath) =>
            MeshAssetLibrary.TryDecodeZip(assetPath, out var zipPath, out _) ? File.Exists(zipPath) : File.Exists(assetPath);

        /// <summary>
        /// A pre-built preview staged by Tools/ThumbnailBaker, if the catalog has one for this exact model
        /// reference -- fetched (verified, cached, progress-tracked, all through the ordinary catalog
        /// download path) and decoded, without needing the model itself locally at all. Null on desktop
        /// (no catalog), for a local user import (no catalog entry), for an older catalog with no baker run
        /// over it, or if the fetch or decode simply fails -- any of which falls back to Generate as before.
        /// </summary>
        private static async Task<byte[]?> TryLoadPrebuilt(string assetPath)
        {
            if (ManaCore.Assets.ManaAssetCatalog.Current is not { } catalog)
            {
                return null;
            }

            var thumbnailPath = catalog.ThumbnailPathOf(assetPath);
            if (thumbnailPath == null)
            {
                return null;
            }

            await catalog.EnsureAsync(thumbnailPath);
            return DecodePngFile(catalog.LocalFilePath(thumbnailPath));
        }

        private byte[]? Generate(string assetPath)
        {
            if (Path.GetExtension(assetPath) == ManaPrefab.Extension) { return GenerateViaCpuStandalone(_context, assetPath); }
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

        private byte[]? GenerateViaCpu(string assetPath) => GenerateViaCpuStandalone(_context, assetPath);

        /// <summary>
        /// The same CPU rasterization <see cref="GenerateViaCpu"/> uses on the live worker, exposed standalone
        /// for Tools/ThumbnailBaker -- a build-time console tool with no live MeshThumbnailCache instance to
        /// call into, just a throwaway ManaContext (the same minimal one MeshThumbnailTests constructs
        /// headlessly: <c>new ManaContext(null!, null!, new ManaWorld())</c>). Reused rather than duplicated
        /// so a pre-built preview and an on-demand fallback are guaranteed to render identically.
        /// <para>
        /// <paramref name="externalAssetResolver"/> is for the baker only: the live cache always loads a
        /// loose glTF from its real directory on disk (or the browser's downloaded copy, placed at its
        /// original relative path), where a sibling buffer/texture's own relative URI already resolves
        /// correctly. The baker instead reads straight out of a published AssetLibrary/, where every file
        /// has been renamed to its content hash and flattened into one directory -- nothing a relative URI
        /// could resolve against -- so it supplies a resolver the way a zip-packed asset already needs one.
        /// </para>
        /// </summary>
        public static byte[]? GenerateViaCpuStandalone(
            ManaContext context, string assetPath, Func<string, byte[]?>? externalAssetResolver = null)
        {
            if (Path.GetExtension(assetPath) == ManaPrefab.Extension)
            {
                return GeneratePrefabThumbnail(assetPath);
            }
            // CPU-only either way: this loads geometry and material colour, nothing else, so it touches no
            // graphics device and is safe away from the render thread. A full LoadFile would build materials
            // and upload buffers.
            var cpuData = MeshAssetLibrary.TryDecodeZip(assetPath, out var zipPath, out var entryPath)
                ? GenerateFromZipStandalone(context, zipPath, entryPath)
                : GenerateFromFileStandalone(context, assetPath, externalAssetResolver);

            return cpuData == null
                ? null
                : MeshThumbnailRasterizer.Render(cpuData, ThumbnailSize, new Vector4(0.82f, 0.84f, 0.88f, 1.0f));
        }

        private static byte[]? GeneratePrefabThumbnail(string path)
        {
            // A separate CPU-only context prevents thumbnail loading from touching the live scene,
            // renderer, physics or script state on the worker thread.
            var world = new ManaWorld();
            var context = new ManaContext(null!, null!, world) { DeferGpuUploads = true };
            world.Context = context;
            world.DefaultScene = new ManaScene(context);
            world.Scenes.Add(world.DefaultScene);
            var prefab = ManaPrefab.Load(path);
            void StripRuntime(Levels.ManaLevelNode node)
            {
                node.Collision = null;
                node.ChildColliders = null;
                node.Scripts = null;
                if (node.GridMap != null) { node.GridMap.Collision = false; }
                foreach (var child in node.Children ?? []) { StripRuntime(child); }
            }
            StripRuntime(prefab.Root);
            Levels.ManaLevelLoader.Load(new Levels.ManaLevel { Nodes = [prefab.Root] }, context, world.DefaultScene, false);
            var root = new ManaSceneNode(context, Matrix4x4.Identity);
            foreach (var node in world.DefaultScene.Children) { root.Children.Add(node); }
            var data = FlattenSharpGltfSceneToCpuMesh(root);
            return data == null ? null : MeshThumbnailRasterizer.Render(data, ThumbnailSize, Vector4.One);
        }

        private static ManaMeshCpuData? GenerateFromFileStandalone(
            ManaContext context, string assetPath, Func<string, byte[]?>? externalAssetResolver)
        {
            if (!File.Exists(assetPath))
            {
                return null;
            }

            // ManaSharpGltfLoader.LoadFile already builds real per-mesh ManaMeshCpuData -- LoadMesh()
            // calls SetCpuData() unconditionally, before its GPU-upload block runs, so no device or scene
            // ever needs to exist for this. The one thing it doesn't do on its own is concatenate every
            // part of a multi-mesh scene with its node transform baked in; skipping that would show one
            // bone's mesh (a leg, an arm) for a rigged Kenney character instead of the whole silhouette --
            // see FlattenSharpGltfSceneToCpuMesh's own comment.
            var loader = new ManaSharpGltfLoader(context);
            var root = externalAssetResolver == null
                ? loader.LoadFile(assetPath)
                : loader.LoadFileFromMemory(File.ReadAllBytes(assetPath), Path.GetFileName(assetPath), externalAssetResolver);
            return FlattenSharpGltfSceneToCpuMesh(root);
        }

        /// <summary>
        /// A zip-packed asset (most of the character and prop library ships this way) has no directory on
        /// disk a relative texture reference could resolve against, so its bytes are read straight out of
        /// the archive instead -- same resolver <see cref="VoxelFileImportService"/> uses to import one for
        /// real, just without the GPU-hooked half of that pipeline.
        /// </summary>
        private static ManaMeshCpuData? GenerateFromZipStandalone(ManaContext context, string zipPath, string entryPath)
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
            var loader = new ManaSharpGltfLoader(context);
            var root = loader.LoadFileFromMemory(memory.ToArray(), entryPath, resolver);
            return FlattenSharpGltfSceneToCpuMesh(root);
        }

        /// <summary>
        /// Concatenates every mesh in a SharpGLTF-loaded scene into one preview mesh, with each part's node
        /// transform baked into its vertices.
        /// <para>
        /// Mirrors <c>ManaGltfLoader.BuildCpuOnlyMesh</c>'s reasoning exactly (see its own comment): a rigged
        /// Kenney character -- most of what's actually bundled for this target -- is several separate rigid
        /// meshes (<c>leg-left</c>, <c>arm-right</c>, <c>head</c>, <c>torso</c>...) each parented to its own
        /// joint, not one skinned mesh. Taking only the scene's first mesh would preview whichever part
        /// happened to load first, typically a single small box.
        /// </para>
        /// <para>
        /// Transforms are baked here rather than read off <see cref="ManaSceneNode.WorldTransform"/> because
        /// that property is only correct after a node has gone through at least one
        /// <see cref="ManaSceneNode.UpdateTransform"/> pass (normally driven by the world's per-frame walk) --
        /// these nodes are freshly constructed by the loader and never attached to a live scene, so it would
        /// still read back as each node's own <c>LocalTransform</c>, parent offset not included.
        /// </para>
        /// </summary>
        // Public: MeshThumbnailTests exercises this directly to verify node-transform composition,
        // the same way it used to exercise ManaGltfLoader.LoadFirstMeshCpuOnlyFromMemory.
        public static ManaMeshCpuData? FlattenSharpGltfSceneToCpuMesh(ManaSceneNode root)
        {
            var placements = new List<(ManaMesh Mesh, System.Numerics.Matrix4x4 Transform)>();
            CollectMeshPlacements(root, System.Numerics.Matrix4x4.Identity, placements);
            if (placements.Count == 0)
            {
                return null;
            }

            var totalVertices = 0;
            var totalIndices = 0;
            foreach (var (mesh, _) in placements)
            {
                var part = mesh.CpuData ?? mesh.GetOrCreateCpuData();
                totalVertices += part.Positions.Length;
                totalIndices += part.Indexes.Length;
            }

            var positions = new Vector3[totalVertices];
            var normals = new Vector3[totalVertices];
            var colors = new Vector4[totalVertices];
            var uv0 = new Vector2[totalVertices];
            var uv1 = new Vector2[totalVertices];
            var indexes = new List<uint>(totalIndices);

            var minSize = Vector3.One * float.MaxValue;
            var maxSize = Vector3.One * float.MinValue;
            var vertexOffset = 0u;
            byte[]? albedoTextureBytes = null;
            Vector4? albedoFactor = null;

            foreach (var (mesh, transform) in placements)
            {
                var part = mesh.CpuData ?? mesh.GetOrCreateCpuData();

                // Same normal-matrix reasoning as the Assimp path: a non-uniform scale needs the inverse
                // transpose, not the transform itself, or normals shear away from perpendicular.
                var normalTransform = transform;
                if (System.Numerics.Matrix4x4.Invert(transform, out var inverse))
                {
                    normalTransform = System.Numerics.Matrix4x4.Transpose(inverse);
                }

                for (var i = 0; i < part.Positions.Length; i++)
                {
                    var position = Vector3.Transform(part.Positions[i], transform);
                    positions[vertexOffset + i] = position;
                    minSize = Vector3.Min(minSize, position);
                    maxSize = Vector3.Max(maxSize, position);

                    normals[vertexOffset + i] = i < part.Normals.Length
                        ? Vector3.TransformNormal(part.Normals[i], normalTransform).Normalized()
                        : Vector3.UnitY;
                    colors[vertexOffset + i] = i < part.Colors.Length ? part.Colors[i] : Vector4.One;
                    uv0[vertexOffset + i] = i < part.TextureCoordinates0.Length ? part.TextureCoordinates0[i] : Vector2.Zero;
                    uv1[vertexOffset + i] = i < part.TextureCoordinates1.Length ? part.TextureCoordinates1[i] : Vector2.Zero;
                }

                foreach (var index in part.Indexes)
                {
                    indexes.Add(index + vertexOffset);
                }

                vertexOffset += (uint)part.Positions.Length;

                // The first part's material stands in for the whole preview, same trade the Assimp path
                // makes: ManaMeshCpuData carries one albedo, and a multi-part character has no single
                // correct answer for which of its several materials that should be.
                if (albedoTextureBytes == null && albedoFactor == null)
                {
                    albedoTextureBytes = part.AlbedoTextureBytes;
                    albedoFactor = part.AlbedoFactor;
                }
            }

            return new ManaMeshCpuData
            {
                Indexes = indexes.ToArray(),
                Positions = positions,
                Normals = normals,
                Colors = colors,
                TextureCoordinates0 = uv0,
                TextureCoordinates1 = uv1,
                AlbedoTexturePath = null,
                AlbedoTextureBytes = albedoTextureBytes,
                AlbedoFactor = albedoFactor
            };
        }

        private static void CollectMeshPlacements(
            ManaSceneNode node,
            System.Numerics.Matrix4x4 parentTransform,
            List<(ManaMesh Mesh, System.Numerics.Matrix4x4 Transform)> placements)
        {
            var transform = node.LocalTransform * parentTransform;

            foreach (var component in node.Components)
            {
                if (component is MeshRendererComponent renderer)
                {
                    placements.Add((renderer.Mesh, transform));
                }
            }

            foreach (var child in node.Children)
            {
                CollectMeshPlacements(child, transform, placements);
            }
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
            var file = CacheFileFor(assetPath);
            return File.Exists(file) ? DecodePngFile(file) : null;
        }

        /// <summary>Shared by the on-disk cache and a fetched pre-built preview -- both are just a PNG file
        /// somewhere on the (real or wasm virtual) filesystem that needs to become raw RGBA of the right size.</summary>
        private static byte[]? DecodePngFile(string file)
        {
            try
            {
                using var image = Image.Load<Rgba32>(file);

                // A size mismatch is a half-written or otherwise unusable file; regenerating (or, for a
                // pre-built preview, falling back to Generate) is the fix, and handing a wrongly-sized
                // buffer to the atlas would be a buffer overrun rather than a bad picture.
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

            // Waited for, not just signalled: a caller that goes on to build a second cache sharing this
            // one's ManaContext/ManaWorld (a throwaway world used only long enough to load one asset at a
            // time -- see GetOrCreateThumbnailWorld) needs the worker actually stopped touching it first,
            // or the two race on the same mutable world from two threads. Bounded so a stuck worker cannot
            // hang shutdown forever.
            try { _worker?.Wait(TimeSpan.FromSeconds(2)); }
            catch (AggregateException) { /* Cancellation, or a fault WorkerLoop's own try/catch already logged. */ }

            _shutdown.Dispose();
            _work.Dispose();
        }
    }
}

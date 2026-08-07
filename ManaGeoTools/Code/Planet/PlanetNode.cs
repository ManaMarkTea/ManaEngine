using System.Collections.Concurrent;
using Mana.Code;
using Mana.Code.Nodes;
using ManaGeoTools.Code.GeoTiff;
using ManaGeoTools.Code.GeoTiff.Terrarium;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// A planet built from a 6-face x (N x N) grid of cube-sphere PlanetPatchMesh tiles (see
    /// CubeSphere.WarpCubeToSphere) — cube-face subdivision has no polar coordinate singularity, so
    /// patches stay uniformly sized everywhere instead of degenerating into thin wedges near the poles
    /// the way a lat/long-degree patch grid would. Builds instantly as a smooth placeholder sphere (no
    /// GeoTIFF decode on construction); call LoadHeightDataAsync to kick off background decoding that
    /// progressively swaps each patch to real elevation/color as its tile finishes — the caller isn't
    /// blocked, and patches already loaded keep rendering while others are still pending. Also owns
    /// HeightExaggeration as a live, reboundable setting (e.g. for an Inspector slider): changing it
    /// rebuilds every patch's geometry immediately.
    /// </summary>
    /// <summary>Where a patch's on-demand Terrarium up-res (see PlanetNode.TryBeginPatchUpres) stands.</summary>
    public enum PatchUpresStatus
    {
        NotRequested,
        InProgress,
        Done,
        Failed,
    }

    public readonly record struct PatchDetailInfo(
        int? Zoom,
        int MeshResolution,
        int? SourceResolution,
        double? SourceMetersPerPixel);

    public sealed class PlanetNode : ManaSceneNode
    {
        public float Radius { get; }
        public int Resolution { get; }
        public int PatchesPerFaceEdge { get; }
        public GeoTiffLibrary? HeightLibrary { get; private set; }

        // Serializes patch/ocean rebuilds: a background tile finishing and a main-thread
        // HeightExaggeration edit could otherwise race on the same mesh's DynamicGeometryContainer/CpuData.
        private readonly object _rebuildLock = new();

        // A "leaf" is one currently-rendered patch of the planet's quadtree -- either an original grid
        // square, or the result of one or more splits (see SplitPatch). Non-leaf (already-split)
        // patches don't exist as entries here or in the scene graph at all; splitting removes the
        // parent and adds its refined children in one step.
        private sealed class PatchLeaf
        {
            public required PlanetPatchMesh Mesh { get; init; }
            public required ManaMeshSceneNode Node { get; init; }

            // The Terrarium zoom level currently backing this patch's elevation data, once it's been
            // up-res'd at least once (null for a patch still on the whole-globe ETOPO baseline) --
            // baked into the Node's display Name (see PatchName) so it's visible directly in the
            // Outliner instead of needing to cross-reference the log.
            public int? Zoom { get; set; }

            // Approximate number of source-data samples across this patch at Zoom. Null for the
            // whole-globe ETOPO baseline before an on-demand Terrarium up-res.
            public int? SourceResolution { get; set; }

            // Approximate source-data ground resolution at the patch center latitude. Web Mercator
            // pixels vary by latitude, so this is intentionally a patch-local estimate.
            public double? SourceMetersPerPixel { get; set; }
        }

        private readonly Dictionary<PatchKey, PatchLeaf> _leaves = new();
        private readonly Dictionary<ManaSceneNode, PatchKey> _nodeToPatchKey = new();
        private readonly ConcurrentDictionary<PatchKey, PatchDetailInfo> _patchDetails = new();
        // Read every frame from the main thread (see VoxelEditorGameMode's selection-outline logic),
        // so status lookups deliberately stay off _rebuildLock; that lock can be held while rebuilding
        // large patch meshes after a cached Terrarium fetch completes.
        private readonly ConcurrentDictionary<PatchKey, PatchUpresStatus> _upresStatus = new();

        // Tracks a split/refinement that's fetching its children in the background -- see
        // BeginSplitLocked's doc comment for why this exists (avoiding a visible "downrez then refill"
        // flash).
        private sealed class PendingSplit
        {
            public required PatchKey ParentKey { get; init; }
            public required PatchLeaf ParentLeaf { get; init; }
            public required PatchKey[] ChildKeys { get; init; }
            public required PlanetPatchMesh[] ChildMeshes { get; init; }
            public required PatchUpresPlan[] ChildPlans { get; init; }
            public required bool[] ChildReady { get; init; }
            public required bool[] ChildSucceeded { get; init; }
            public required int?[] ChildZoom { get; init; }
        }

        private sealed class PatchUpresPlan
        {
            public required int Zoom { get; init; }
            public required int MeshResolution { get; init; }
            public required int SourceResolution { get; init; }
            public required double SourceMetersPerPixel { get; init; }
            public required IReadOnlyList<(int X, int Y)> TileIndices { get; init; }
        }

        private readonly record struct TerrariumTileKey(int Zoom, int X, int Y);

        private sealed class PatchSplitPlan
        {
            public required IReadOnlyList<PlannedPatch> Children { get; init; }
        }

        private readonly record struct PlannedPatch(
            PatchKey Key,
            float MinU,
            float MaxU,
            float MinV,
            float MaxV,
            PatchUpresPlan Plan);

        // Parent keys with a split currently in flight -- guards against double-triggering a second
        // split for the same parent before the first one's children finish (see TrySplitPatch).
        private readonly ConcurrentDictionary<PatchKey, byte> _splitPending = new();

        // Splits whose children have all finished fetching, waiting for ProcessPendingSplits to
        // actually swap them into the scene graph on the main thread -- see UpresPendingChildrenAsync.
        private readonly Queue<PendingSplit> _readyToFinalize = new();

        private readonly PlanetOceanMesh _oceanMesh;

        // Caps how many Terrarium tiles a single patch up-res will fetch, and how fine a zoom it'll
        // reach for -- see SlippyMapMath.PickZoom. Tile count roughly quadruples per zoom level for a
        // fixed geographic area, and these patches are genuinely large (PatchesPerFaceEdge divides a
        // cube face into a fairly coarse grid by default) -- an earlier, much lower cap (16) forced the
        // zoom picker down to zoom 5 (~4.9km/px) for a real patch, which is actually COARSER than the
        // 60-arcsecond ETOPO baseline it's supposed to be improving on (~1.85km/px), making the whole
        // fetch a no-op visually. 1024 tiles (a few hundred KB-to-low-MB of PNGs, fetched with bounded
        // concurrency -- see UpresPatchAsync) reaches a genuinely finer zoom for patches this size.
        // 13 is the highest zoom Terrarium serves directly (14/15 need oversampling from 13, out of
        // scope here).
        private const int UpresMaxTiles = 1024;
        private const int UpresMaxZoom = 13;
        private const int UpresMaxConcurrentFetches = 24;

        // SourceResolution records the approximate Terrarium sample span, which can be thousands of
        // pixels for a high-zoom cached patch. This is a per-leaf mesh ceiling, not a final density
        // ceiling: the refinement planner subdivides until each leaf's source span fits under it.
        private const int InitialTerrariumUpresZoom = 7;
        public const int DefaultBaselineMaxPatchMeshResolution = 97;

        private const int FallbackPatchResolution = 160;
        private const int MaxPatchMeshResolution = 1025;
        private const int MaxDirectSourceMatchDepth = 3;
        private const int PatchNameSplitPathTailDigits = 4;

        private static readonly SemaphoreSlim UpresFetchThrottle = new(UpresMaxConcurrentFetches);

        private float _heightExaggeration;

        public float HeightExaggeration
        {
            get => _heightExaggeration;
            set
            {
                _heightExaggeration = value;
                RebuildAllPatches();
            }
        }

        public PlanetNode(
            ManaContext context,
            int patchesPerFaceEdge,
            float radius,
            int resolution,
            float heightExaggeration,
            int oceanResolution)
            : base(context, Matrix4x4.Identity)
        {
            Radius = radius;
            Resolution = resolution;
            PatchesPerFaceEdge = Math.Max(1, patchesPerFaceEdge);
            _heightExaggeration = heightExaggeration;
            Name = "Planet";

            var heightScale = ComputeHeightScale();
            var step = 2f / PatchesPerFaceEdge;
            for (var face = 0; face < CubeSphere.FaceNormals.Length; face++)
            {
                var normal = CubeSphere.FaceNormals[face];
                for (var row = 0; row < PatchesPerFaceEdge; row++)
                {
                    var minV = -1f + (row * step);
                    for (var col = 0; col < PatchesPerFaceEdge; col++)
                    {
                        var minU = -1f + (col * step);
                        var mesh = new PlanetPatchMesh(
                            context, normal, minU, minU + step, minV, minV + step, radius, resolution, heightScale, heightLibrary: null);
                        var key = PatchKey.Root(face, row, col);
                        var node = new ManaMeshSceneNode(context, mesh) { Name = PatchName(key, meshResolution: mesh.Resolution) };
                        AddChild(node);
                        _leaves[key] = new PatchLeaf { Mesh = mesh, Node = node };
                        _nodeToPatchKey[node] = key;
                        _patchDetails[key] = new PatchDetailInfo(null, mesh.Resolution, null, null);
                    }
                }
            }

            // Always present, the same way a real planet just has oceans -- not a separate opt-in
            // layer like the KML overlay or the flat demo water pond. A single un-tiled shell (not
            // one per terrain patch), masked to real ocean coverage once height data arrives -- see
            // PlanetOceanMesh's doc comment. Its own resolution is independent of (and typically
            // higher than) the terrain patches': waves need enough vertex density to actually resolve
            // whatever wave frequency the shader is asked for without aliasing, which the terrain's
            // patch resolution was never tuned for (that's sized for elevation detail, not wave
            // detail, and terrain patches are also much smaller than the whole-globe ocean shell to
            // begin with).
            _oceanMesh = new PlanetOceanMesh(context, radius, oceanResolution, heightLibrary: null);
            var oceanNode = new ManaMeshSceneNode(context, _oceanMesh) { Name = "Ocean" };
            AddChild(oceanNode);
        }

        /// <summary>
        /// Kicks off background decoding of every tile in heightLibrary; returns immediately without
        /// waiting on it. Each patch swaps from smooth placeholder to real elevation/color as soon as
        /// its own tile finishes, independently of the others.
        /// </summary>
        public void LoadHeightDataAsync(GeoTiffLibrary heightLibrary)
        {
            HeightLibrary = heightLibrary;
            Task.Run(() => heightLibrary.LoadAllImageData(ApplyTileToOverlappingPatches));
        }

        private void ApplyTileToOverlappingPatches(ManaGeoTools.Code.GeoTiff.GeoTiff tile)
        {
            lock (_rebuildLock)
            {
                foreach (var (key, leaf) in _leaves)
                {
                    if (TileOverlapsPatch(tile, leaf.Mesh))
                    {
                        if (leaf.Zoom.HasValue)
                        {
                            continue;
                        }

                        var sourceResolution = EstimateGeoTiffSourceResolutionForPatch(tile, leaf.Mesh);
                        var sourceMetersPerPixel = EstimateGeoTiffMetersPerPixel(tile, leaf.Mesh);
                        var meshResolution = Math.Max(
                            leaf.Mesh.Resolution,
                            Math.Min(sourceResolution, DefaultBaselineMaxPatchMeshResolution));

                        leaf.Mesh.ApplyHeightDataWithResolution(HeightLibrary!, meshResolution);
                        leaf.SourceResolution = sourceResolution;
                        leaf.SourceMetersPerPixel = sourceMetersPerPixel;
                        leaf.Node.Name = PatchName(key, leaf.Zoom, leaf.Mesh.Resolution, leaf.SourceResolution, leaf.SourceMetersPerPixel);
                        _patchDetails[key] = new PatchDetailInfo(leaf.Zoom, leaf.Mesh.Resolution, leaf.SourceResolution, leaf.SourceMetersPerPixel);
                    }
                }

                // Unlike a patch, the ocean shell spans the whole globe, so every tile is potentially
                // relevant to it -- no overlap test, just re-mask against whatever height data is
                // loaded so far every time any tile finishes.
                _oceanMesh.ApplyHeightData(HeightLibrary!);
            }
        }

        public bool TryGetPatchKey(ManaSceneNode node, out PatchKey key)
        {
            return _nodeToPatchKey.TryGetValue(node, out key);
        }

        /// <summary>
        /// Kicks off a background fetch of higher-resolution Terrarium elevation tiles covering just
        /// this one patch, then rebuilds only that patch's mesh at a higher (but still bounded)
        /// resolution once they're in (see UpresPatchAsync). Safe -- and idempotent -- to call
        /// repeatedly for the same key, e.g. every time it's (re)selected: only the very first call per
        /// key does anything; once a patch is Done, InProgress, or Failed, further calls are a no-op.
        /// The source-data resolution is tracked separately from the mesh resolution, which is capped
        /// to keep an on-demand cache hit from generating an enormous mesh in one rebuild. Reaching
        /// finer source data requires the separate, deliberate TrySplitPatch path so a smaller region
        /// can qualify for a higher Terrarium zoom.
        /// </summary>
        public void TryBeginPatchUpres(PatchKey key)
        {
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                if (HeightLibrary == null || !_leaves.TryGetValue(key, out var leaf))
                {
                    return;
                }

                if (_upresStatus.TryGetValue(key, out var status)
                    && status is PatchUpresStatus.InProgress or PatchUpresStatus.Done)
                {
                    return;
                }

                BeginPatchUpresLocked(key, leaf);
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        /// <summary>
        /// Deliberately splits an already up-res'd (Done) patch into 4 smaller patches, each of which
        /// immediately starts its own up-res fetch -- since each child covers 1/4 the area, it may be
        /// able to use a higher Terrarium zoom while staying inside the tile budget. Splitting is
        /// rejected when it would only subdivide already-represented source data.
        /// Unlike TryBeginPatchUpres,
        /// this is intentionally NEVER triggered by plain reselection -- only by an explicit user action
        /// (the Inspector's "Increase source zoom" button, shown once a patch reaches Done) --
        /// otherwise a couple of casual re-clicks near the same spot as it splits and its children
        /// finish (fast, since each child fetches far less than the original) can cascade several
        /// levels deep before you notice. No-ops if the patch isn't currently Done (already split away,
        /// still fetching, its up-res failed, or a split for it is already in flight -- see
        /// IsSplitPending).
        /// </summary>
        public void TrySplitPatch(PatchKey key)
        {
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                if (!_leaves.TryGetValue(key, out var leaf) || _splitPending.ContainsKey(key))
                {
                    return;
                }

                var status = _upresStatus.TryGetValue(key, out var existing) ? existing : PatchUpresStatus.NotRequested;
                if (status != PatchUpresStatus.Done)
                {
                    return;
                }

                if (!TryCreateSplitPlanLocked(key, leaf, includeTileIndices: true, out var splitPlan))
                {
                    Console.WriteLine($"[Terrarium] Split {PatchName(key, leaf.Zoom, leaf.Mesh.Resolution, leaf.SourceResolution, leaf.SourceMetersPerPixel)} skipped: could not increase source zoom by one without undersampling.");
                    return;
                }

                if (splitPlan.Children.Count == 1 && splitPlan.Children[0].Key.Equals(key))
                {
                    _upresStatus[key] = PatchUpresStatus.InProgress;
                    Task.Run(() => UpresPatchAsync(key, leaf.Mesh, splitPlan.Children[0].Plan));
                    return;
                }

                if (!_splitPending.TryAdd(key, 0))
                {
                    return;
                }

                BeginSplitLocked(key, leaf, splitPlan);
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        /// <summary>
        /// True when one explicit action can increase key's Terrarium source zoom by one level without
        /// undersampling the resulting source data.
        /// </summary>
        public bool CanSplitPatch(PatchKey key)
        {
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return false;
            }

            try
            {
                if (!_leaves.TryGetValue(key, out var leaf) || _splitPending.ContainsKey(key))
                {
                    return false;
                }

                var status = _upresStatus.TryGetValue(key, out var existing) ? existing : PatchUpresStatus.NotRequested;
                return status == PatchUpresStatus.Done && TryCreateSplitPlanLocked(key, leaf, includeTileIndices: false, out _);
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        /// <summary>Whether key's split/refinement is currently fetching child patches.</summary>
        public bool IsSplitPending(PatchKey key)
        {
            return _splitPending.ContainsKey(key);
        }

        private static int? GetNextSourceZoom(PatchLeaf leaf)
        {
            var currentZoom = leaf.Zoom ?? (InitialTerrariumUpresZoom - 1);
            return currentZoom >= UpresMaxZoom ? null : currentZoom + 1;
        }

        // Caller must already hold _rebuildLock.
        private void BeginPatchUpresLocked(PatchKey key, PatchLeaf leaf)
        {
            var targetZoom = GetNextSourceZoom(leaf);
            if (!targetZoom.HasValue)
            {
                _upresStatus[key] = PatchUpresStatus.Done;
                return;
            }

            var plannedPatches = CreateSourceMatchedPatchPlan(key, leaf.Mesh, targetZoom.Value, includeTileIndices: true);
            if (plannedPatches == null)
            {
                Console.WriteLine($"[Terrarium] Patch {PatchName(key, meshResolution: leaf.Mesh.Resolution)} could not increase source zoom to z{targetZoom.Value} without undersampling -- skipping up-res.");
                _upresStatus[key] = PatchUpresStatus.Failed;
                return;
            }

            _upresStatus[key] = PatchUpresStatus.InProgress;
            if (plannedPatches.Count == 1 && plannedPatches[0].Key.Equals(key))
            {
                Task.Run(() => UpresPatchAsync(key, leaf.Mesh, plannedPatches[0].Plan));
                return;
            }

            if (!_splitPending.TryAdd(key, 0))
            {
                return;
            }

            BeginSplitLocked(key, leaf, new PatchSplitPlan { Children = plannedPatches });
        }

        // Caller must already hold _rebuildLock. Creates cheap, invisible child mesh placeholders
        // (quadrant = a quarter of the parent's U/V range each) and kicks off each one's own up-res
        // fetch, but touches neither the scene graph nor _leaves yet -- the parent keeps rendering,
        // unchanged, until every child has finished. FinalizeSplit swaps the parent out only if all
        // children fetched usable source data; otherwise the parent remains visible. Each child is
        // rebuilt from fetched/cached Terrarium data before it is added to the scene, so no placeholder
        // is ever visible.
        //
        // ComputePatchLonLatBounds/TileOverlapsPatch/PlanetPatchMesh's own skirt geometry all read only
        // a mesh's own FaceNormal/MinU/MaxU/MinV/MaxV, never grid coordinates or how the sub-rectangle
        // was derived, so a split child needs no special-casing anywhere else -- it's indistinguishable
        // from an original grid patch to every other part of this class.
        private void BeginSplitLocked(PatchKey parentKey, PatchLeaf parentLeaf, PatchSplitPlan splitPlan)
        {
            var parentMesh = parentLeaf.Mesh;
            var heightScale = ComputeHeightScale();
            var children = splitPlan.Children;

            var pending = new PendingSplit
            {
                ParentKey = parentKey,
                ParentLeaf = parentLeaf,
                ChildKeys = new PatchKey[children.Count],
                ChildMeshes = new PlanetPatchMesh[children.Count],
                ChildPlans = children.Select(child => child.Plan).ToArray(),
                ChildReady = new bool[children.Count],
                ChildSucceeded = new bool[children.Count],
                ChildZoom = new int?[children.Count],
            };

            var targetZooms = string.Join("/", children.Select(child => $"z{child.Plan.Zoom}").Distinct().OrderBy(zoom => zoom));
            Console.WriteLine($"[Terrarium] Increasing {PatchName(parentKey, parentLeaf.Zoom, parentLeaf.Mesh.Resolution, parentLeaf.SourceResolution, parentLeaf.SourceMetersPerPixel)} to {targetZooms}: building {children.Count} child patch(es) before swapping in (parent stays visible until then).");

            for (var index = 0; index < children.Count; index++)
            {
                var child = children[index];
                var childMesh = new PlanetPatchMesh(
                    Context, parentMesh.FaceNormal, child.MinU, child.MaxU, child.MinV, child.MaxV, Radius, resolution: 2, heightScale, heightLibrary: null);

                pending.ChildKeys[index] = child.Key;
                pending.ChildMeshes[index] = childMesh;
            }

            Task.Run(() => UpresPendingChildrenAsync(pending));
        }

        private async Task UpresPendingChildrenAsync(PendingSplit pending)
        {
            var tileKeys = pending.ChildPlans
                .SelectMany(plan => plan.TileIndices.Select(index => new TerrariumTileKey(plan.Zoom, index.X, index.Y)))
                .Distinct()
                .ToList();
            Console.WriteLine($"[Terrarium] Split {PatchName(pending.ParentKey, pending.ParentLeaf.Zoom, pending.ParentLeaf.Mesh.Resolution, pending.ParentLeaf.SourceResolution, pending.ParentLeaf.SourceMetersPerPixel)}: fetching {tileKeys.Count} unique tile(s) for {pending.ChildPlans.Length} child patch(es).");

            var fetched = await FetchTilesAsync(tileKeys);
            var fetchedKeys = fetched
                .Select(tile => new TerrariumTileKey(tile.Zoom, tile.TileX, tile.TileY))
                .ToHashSet();

            lock (_rebuildLock)
            {
                foreach (var tile in fetched)
                {
                    HeightLibrary!.AddTile(tile);
                }

                for (var childIndex = 0; childIndex < pending.ChildPlans.Length; childIndex++)
                {
                    var plan = pending.ChildPlans[childIndex];
                    var childFetchedAnyTile = plan.TileIndices
                        .Any(index => fetchedKeys.Contains(new TerrariumTileKey(plan.Zoom, index.X, index.Y)));
                    pending.ChildReady[childIndex] = true;
                    if (!childFetchedAnyTile)
                    {
                        continue;
                    }

                    pending.ChildMeshes[childIndex].ApplyHeightDataWithResolution(HeightLibrary!, plan.MeshResolution);
                    pending.ChildZoom[childIndex] = plan.Zoom;
                    pending.ChildSucceeded[childIndex] = true;
                }

                // NOT finalized here -- this runs on a background thread pool thread, and FinalizeSplit
                // mutates the scene graph (AddChild/RemoveChild on Children, a plain List<T>) -- doing
                // that from a background thread raced with ManaWorld.CreateRenderScene()'s every-frame
                // main-thread traversal of that same list ("Collection was modified; enumeration
                // operation may not execute"). Queue it instead; ProcessPendingSplits (called once per
                // frame from VoxelEditorGameMode, main thread) drains and finalizes it there.
                _readyToFinalize.Enqueue(pending);
            }

            var succeededCount = pending.ChildSucceeded.Count(succeeded => succeeded);
            Console.WriteLine($"[Terrarium] Split {PatchName(pending.ParentKey, pending.ParentLeaf.Zoom, pending.ParentLeaf.Mesh.Resolution, pending.ParentLeaf.SourceResolution, pending.ParentLeaf.SourceMetersPerPixel)}: applied {fetched.Count}/{tileKeys.Count} unique tile(s) to {succeededCount}/{pending.ChildPlans.Length} child patch(es) (pending swap-in).");
        }

        /// <summary>
        /// Finalizes any splits whose children have all finished fetching. MUST be called from the
        /// main thread once per frame (e.g. alongside other per-frame PlanetNode calls in
        /// VoxelEditorGameMode) -- see UpresPendingChildrenAsync's comment for why finalizing can't safely
        /// happen directly from the background fetch task that detects readiness.
        /// </summary>
        public void ProcessPendingSplits()
        {
            List<PendingSplit>? ready = null;
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                if (_readyToFinalize.Count > 0)
                {
                    ready = new List<PendingSplit>(_readyToFinalize);
                    _readyToFinalize.Clear();
                }

                if (ready == null)
                {
                    return;
                }

                foreach (var pending in ready)
                {
                    FinalizeSplit(pending);
                }
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        // Caller must already hold _rebuildLock.
        private void FinalizeSplit(PendingSplit pending)
        {
            if (!Array.TrueForAll(pending.ChildSucceeded, succeeded => succeeded))
            {
                _splitPending.TryRemove(pending.ParentKey, out _);
                if (_upresStatus.TryGetValue(pending.ParentKey, out var status) && status == PatchUpresStatus.InProgress)
                {
                    _upresStatus[pending.ParentKey] = PatchUpresStatus.Failed;
                }
                Console.WriteLine($"[Terrarium] Split {PatchName(pending.ParentKey, pending.ParentLeaf.Zoom, pending.ParentLeaf.Mesh.Resolution, pending.ParentLeaf.SourceResolution, pending.ParentLeaf.SourceMetersPerPixel)} aborted: one or more children failed to fetch finer source data. Parent patch remains visible.");
                return;
            }

            RemoveChild(pending.ParentLeaf.Node);
            _leaves.Remove(pending.ParentKey);
            _nodeToPatchKey.Remove(pending.ParentLeaf.Node);
            _patchDetails.TryRemove(pending.ParentKey, out _);
            _upresStatus.TryRemove(pending.ParentKey, out _);
            _splitPending.TryRemove(pending.ParentKey, out _);

            for (var quadrant = 0; quadrant < pending.ChildKeys.Length; quadrant++)
            {
                var childKey = pending.ChildKeys[quadrant];
                var childMesh = pending.ChildMeshes[quadrant];
                var childZoom = pending.ChildZoom[quadrant];
                var childPlan = pending.ChildPlans[quadrant];
                var childSourceResolution = childPlan.SourceResolution;
                var childMetersPerPixel = childPlan.SourceMetersPerPixel;
                var childNode = new ManaMeshSceneNode(Context, childMesh) { Name = PatchName(childKey, childZoom, childMesh.Resolution, childSourceResolution, childMetersPerPixel) };
                AddChild(childNode);

                var childLeaf = new PatchLeaf
                {
                    Mesh = childMesh,
                    Node = childNode,
                    Zoom = childZoom,
                    SourceResolution = childSourceResolution,
                    SourceMetersPerPixel = childMetersPerPixel,
                };
                _leaves[childKey] = childLeaf;
                _nodeToPatchKey[childNode] = childKey;
                _patchDetails[childKey] = new PatchDetailInfo(childZoom, childMesh.Resolution, childSourceResolution, childMetersPerPixel);
                _upresStatus[childKey] = PatchUpresStatus.Done;
            }

            Console.WriteLine($"[Terrarium] Split {PatchName(pending.ParentKey, pending.ParentLeaf.Zoom, pending.ParentLeaf.Mesh.Resolution, pending.ParentLeaf.SourceResolution, pending.ParentLeaf.SourceMetersPerPixel)} into {pending.ChildKeys.Length} child patches (all pre-fetched, swapped in together). Total leaves now: {_leaves.Count}.");
        }

        public PatchUpresStatus GetPatchUpresStatus(PatchKey key)
        {
            return _upresStatus.TryGetValue(key, out var status) ? status : PatchUpresStatus.NotRequested;
        }

        public PatchDetailInfo? GetPatchDetailInfo(PatchKey key)
        {
            return _patchDetails.TryGetValue(key, out var detail) ? detail : null;
        }

        private async Task UpresPatchAsync(PatchKey key, PlanetPatchMesh patch, PatchUpresPlan plan)
        {
            var fetched = await FetchTilesForPatchAsync(key, plan);
            if (fetched is not { Count: > 0 })
            {
                Console.WriteLine($"[Terrarium] Up-res {PatchName(key, plan.Zoom, plan.MeshResolution, plan.SourceResolution, plan.SourceMetersPerPixel)}: no tiles fetched successfully, leaving patch as-is.");
                lock (_rebuildLock) { _upresStatus[key] = PatchUpresStatus.Failed; }
                return;
            }

            lock (_rebuildLock)
            {
                foreach (var tile in fetched)
                {
                    HeightLibrary!.AddTile(tile);
                }
                patch.ApplyHeightDataWithResolution(HeightLibrary!, plan.MeshResolution);
                _upresStatus[key] = PatchUpresStatus.Done;
                if (_leaves.TryGetValue(key, out var leaf))
                {
                    leaf.Zoom = plan.Zoom;
                    leaf.SourceResolution = plan.SourceResolution;
                    leaf.SourceMetersPerPixel = plan.SourceMetersPerPixel;
                    leaf.Node.Name = PatchName(key, plan.Zoom, patch.Resolution, plan.SourceResolution, plan.SourceMetersPerPixel);
                    _patchDetails[key] = new PatchDetailInfo(plan.Zoom, patch.Resolution, plan.SourceResolution, plan.SourceMetersPerPixel);
                }
            }

            Console.WriteLine($"[Terrarium] Up-res {PatchName(key, plan.Zoom, plan.MeshResolution, plan.SourceResolution, plan.SourceMetersPerPixel)}: applied {fetched.Count} tile(s).");
        }

        // Shared by standalone up-res and split/refine up-res. Fetches every unique Terrarium tile with
        // bounded global concurrency. The returned list may be empty if every individual fetch failed.
        private static async Task<List<TerrariumHeightTile>> FetchTilesForPatchAsync(PatchKey key, PatchUpresPlan plan)
        {
            Console.WriteLine($"[Terrarium] Up-res {PatchName(key, plan.Zoom, plan.MeshResolution, plan.SourceResolution, plan.SourceMetersPerPixel)}: fetching {plan.TileIndices.Count} tile(s).");
            var tileKeys = plan.TileIndices
                .Select(index => new TerrariumTileKey(plan.Zoom, index.X, index.Y))
                .ToList();
            return await FetchTilesAsync(tileKeys);
        }

        private static async Task<List<TerrariumHeightTile>> FetchTilesAsync(IReadOnlyList<TerrariumTileKey> tileKeys)
        {
            var fetchTasks = tileKeys.Select(async tileKey =>
            {
                await UpresFetchThrottle.WaitAsync();
                try
                {
                    return await TerrariumTileFetcher.FetchTileAsync(tileKey.Zoom, tileKey.X, tileKey.Y);
                }
                finally
                {
                    UpresFetchThrottle.Release();
                }
            });
            return (await Task.WhenAll(fetchTasks)).Where(tile => tile != null).Select(tile => tile!).ToList();
        }

        private static IReadOnlyList<PlannedPatch>? CreateSourceMatchedPatchPlan(PatchKey key, PlanetPatchMesh patch, int targetZoom, bool includeTileIndices)
        {
            var plannedPatches = new List<PlannedPatch>();
            return TryAddSourceMatchedPatchPlans(
                key,
                patch.FaceNormal,
                patch.MinU,
                patch.MaxU,
                patch.MinV,
                patch.MaxV,
                targetZoom,
                includeTileIndices,
                plannedPatches)
                    ? plannedPatches
                    : null;
        }

        private static bool TryAddSourceMatchedPatchPlans(
            PatchKey key,
            Vector3 faceNormal,
            float minU,
            float maxU,
            float minV,
            float maxV,
            int maxZoom,
            bool includeTileIndices,
            List<PlannedPatch> plannedPatches,
            int depth = 0)
        {
            var plan = CreateUpresPlan(faceNormal, minU, maxU, minV, maxV, maxZoom, includeTileIndices);
            if (plan != null && plan.SourceResolution <= MaxPatchMeshResolution)
            {
                plannedPatches.Add(new PlannedPatch(key, minU, maxU, minV, maxV, plan));
                return true;
            }

            if (depth >= MaxDirectSourceMatchDepth)
            {
                return false;
            }

            var midU = (minU + maxU) * 0.5f;
            var midV = (minV + maxV) * 0.5f;
            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var quadMinU = (quadrant & 1) == 0 ? minU : midU;
                var quadMaxU = (quadrant & 1) == 0 ? midU : maxU;
                var quadMinV = (quadrant & 2) == 0 ? minV : midV;
                var quadMaxV = (quadrant & 2) == 0 ? midV : maxV;

                if (!TryAddSourceMatchedPatchPlans(
                    key.Child(quadrant),
                    faceNormal,
                    quadMinU,
                    quadMaxU,
                    quadMinV,
                    quadMaxV,
                    maxZoom,
                    includeTileIndices,
                    plannedPatches,
                    depth + 1))
                {
                    return false;
                }
            }

            return true;
        }

        private static PatchUpresPlan? CreateUpresPlan(
            Vector3 faceNormal,
            float minU,
            float maxU,
            float minV,
            float maxV,
            int maxZoom,
            bool includeTileIndices)
        {
            var (lonMin, lonMax, latMin, latMax) = ComputePatchLonLatBounds(faceNormal, minU, maxU, minV, maxV);
            var zoom = SlippyMapMath.PickZoom(lonMin, lonMax, latMin, latMax, UpresMaxTiles, maxZoom);
            if (!zoom.HasValue)
            {
                return null;
            }
            if (zoom.Value != maxZoom)
            {
                return null;
            }

            IReadOnlyList<(int X, int Y)> tileIndices = includeTileIndices
                ? SlippyMapMath.EnumerateTiles(lonMin, lonMax, latMin, latMax, zoom.Value).ToList()
                : Array.Empty<(int X, int Y)>();
            var sourceResolution = EstimateSourceResolutionForZoom(lonMin, lonMax, latMin, latMax, zoom.Value);
            return new PatchUpresPlan
            {
                Zoom = zoom.Value,
                MeshResolution = Math.Min(sourceResolution, MaxPatchMeshResolution),
                SourceResolution = sourceResolution,
                SourceMetersPerPixel = EstimateSourceMetersPerPixel(latMin, latMax, zoom.Value),
                TileIndices = tileIndices
            };
        }

        // Converts the streamed tile zoom into the approximate number of source samples available
        // across this patch. Terrarium tiles are 256x256 samples in Web Mercator tile space.
        private static int EstimateSourceResolutionForZoom(float lonMin, float lonMax, float latMin, float latMax, int zoom)
        {
            var (xMin, yMin) = SlippyMapMath.LatLongToTileCoords(lonMin, latMax, zoom);
            var (xMax, yMax) = SlippyMapMath.LatLongToTileCoords(lonMax, latMin, zoom);
            var pixelSpanX = Math.Abs(xMax - xMin) * TerrariumHeightTile.TileSize;
            var pixelSpanY = Math.Abs(yMax - yMin) * TerrariumHeightTile.TileSize;
            var sourceSamplesAcrossPatch = Math.Max(pixelSpanX, pixelSpanY);
            if (double.IsNaN(sourceSamplesAcrossPatch) || double.IsInfinity(sourceSamplesAcrossPatch))
            {
                return FallbackPatchResolution;
            }

            return Math.Max(2, (int)Math.Ceiling(sourceSamplesAcrossPatch) + 1);
        }

        private static double EstimateSourceMetersPerPixel(float latMin, float latMax, int zoom)
        {
            var centerLatitude = Math.Clamp((latMin + latMax) * 0.5, -SlippyMapMath.MaxLatitudeDegrees, SlippyMapMath.MaxLatitudeDegrees);
            var metersPerPixelAtEquator = 2.0 * Math.PI * PlanetLoader.EarthRadiusMeters
                / (TerrariumHeightTile.TileSize * Math.Pow(2.0, zoom));
            return Math.Max(0.0, metersPerPixelAtEquator * Math.Cos(centerLatitude * Math.PI / 180.0));
        }

        private static int EstimateGeoTiffSourceResolutionForPatch(ManaGeoTools.Code.GeoTiff.GeoTiff tile, PlanetPatchMesh patch)
        {
            var pixelSize = tile.Area.PixelSize;
            if (pixelSize.X <= 0f || pixelSize.Y <= 0f)
            {
                return FallbackPatchResolution;
            }

            var (lonMin, lonMax, latMin, latMax) = ComputePatchLonLatBounds(patch);
            var lonSpanPixels = Math.Abs(lonMax - lonMin) / pixelSize.X;
            var latSpanPixels = Math.Abs(latMax - latMin) / pixelSize.Y;
            var sourceSamplesAcrossPatch = Math.Max(lonSpanPixels, latSpanPixels);
            if (float.IsNaN(sourceSamplesAcrossPatch) || float.IsInfinity(sourceSamplesAcrossPatch))
            {
                return FallbackPatchResolution;
            }

            return Math.Max(2, (int)MathF.Ceiling(sourceSamplesAcrossPatch) + 1);
        }

        private static double EstimateGeoTiffMetersPerPixel(ManaGeoTools.Code.GeoTiff.GeoTiff tile, PlanetPatchMesh patch)
        {
            var pixelSize = tile.Area.PixelSize;
            if (pixelSize.X <= 0f || pixelSize.Y <= 0f)
            {
                return 0.0;
            }

            var (_, _, latMin, latMax) = ComputePatchLonLatBounds(patch);
            var centerLatitude = Math.Clamp((latMin + latMax) * 0.5, -90.0f, 90.0f);
            var metersPerDegree = 2.0 * Math.PI * PlanetLoader.EarthRadiusMeters / 360.0;
            var latMetersPerPixel = metersPerDegree * pixelSize.Y;
            var lonMetersPerPixel = metersPerDegree * pixelSize.X * Math.Cos(centerLatitude * Math.PI / 180.0);
            return Math.Max(0.0, Math.Max(latMetersPerPixel, lonMetersPerPixel));
        }

        // Caller must already hold _rebuildLock.
        private static bool TryCreateSplitPlanLocked(PatchKey parentKey, PatchLeaf parentLeaf, bool includeTileIndices, out PatchSplitPlan splitPlan)
        {
            splitPlan = null!;
            var parentMesh = parentLeaf.Mesh;
            var targetZoom = GetNextSourceZoom(parentLeaf);
            if (!targetZoom.HasValue)
            {
                return false;
            }

            var children = new List<PlannedPatch>();
            if (!TryAddSourceMatchedPatchPlans(
                parentKey,
                parentMesh.FaceNormal,
                parentMesh.MinU,
                parentMesh.MaxU,
                parentMesh.MinV,
                parentMesh.MaxV,
                targetZoom.Value,
                includeTileIndices,
                children))
            {
                return false;
            }

            splitPlan = new PatchSplitPlan { Children = children };
            return true;
        }

        // Same corner+center sampling pattern TileOverlapsPatch uses below, plus edge midpoints for a
        // bit more margin -- this drives which Terrarium tiles get fetched, so erring slightly generous
        // (a few extra tiles, still bounded by UpresMaxTiles) is preferable to missing coverage at a
        // patch's edge.
        private static (float LonMin, float LonMax, float LatMin, float LatMax) ComputePatchLonLatBounds(PlanetPatchMesh patch)
        {
            return ComputePatchLonLatBounds(patch.FaceNormal, patch.MinU, patch.MaxU, patch.MinV, patch.MaxV);
        }

        private static (float LonMin, float LonMax, float LatMin, float LatMax) ComputePatchLonLatBounds(
            Vector3 faceNormal,
            float minU,
            float maxU,
            float minV,
            float maxV)
        {
            var axisA = new Vector3(faceNormal.Y, faceNormal.Z, faceNormal.X);
            var axisB = Vector3.Cross(faceNormal, axisA);
            var midU = (minU + maxU) * 0.5f;
            var midV = (minV + maxV) * 0.5f;

            var sampleUVs = new[]
            {
                (minU, minV), (maxU, minV),
                (minU, maxV), (maxU, maxV),
                (midU, minV), (midU, maxV),
                (minU, midV), (maxU, midV),
                (midU, midV),
            };

            var lonMin = float.MaxValue;
            var lonMax = float.MinValue;
            var latMin = float.MaxValue;
            var latMax = float.MinValue;

            foreach (var (u, v) in sampleUVs)
            {
                var pointOnUnitCube = faceNormal + (u * axisA) + (v * axisB);
                var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(pointOnUnitCube);
                var latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);

                lonMin = Math.Min(lonMin, latLong.Longitude);
                lonMax = Math.Max(lonMax, latLong.Longitude);
                latMin = Math.Min(latMin, latLong.Latitude);
                latMax = Math.Max(latMax, latLong.Latitude);
            }

            return (lonMin, lonMax, latMin, latMax);
        }

        // Samples a handful of representative points on the patch (4 corners + center) through the
        // exact same FaceNormal/u/v -> CubeSphere.WarpCubeToSphere -> LatLong formula used for real
        // vertex generation, and checks each against the tile's already-correct lat/long bounding box.
        // A cube-face patch maps to a *curved* quadrilateral in lat/long space, so building a lat/long
        // bounding box FOR the patch (the way the old lat/long-degree patch version could) breaks at
        // the antimeridian seam and near the poles (a small polar patch's true longitude range can be
        // the full 360 degrees) — sampling points through the real formula and testing them against
        // the tile's simple, correct Area.Contains() sidesteps both problems entirely by never
        // constructing a lat/long bounding box for the curved patch at all. A missed partial overlap
        // just skips an unnecessary rebuild trigger this round, not a correctness bug — each vertex
        // independently re-samples its own tile at generation time regardless.
        private static bool TileOverlapsPatch(ManaGeoTools.Code.GeoTiff.GeoTiff tile, PlanetPatchMesh patch)
        {
            var axisA = new Vector3(patch.FaceNormal.Y, patch.FaceNormal.Z, patch.FaceNormal.X);
            var axisB = Vector3.Cross(patch.FaceNormal, axisA);
            var midU = (patch.MinU + patch.MaxU) * 0.5f;
            var midV = (patch.MinV + patch.MaxV) * 0.5f;

            var sampleUVs = new[]
            {
                (patch.MinU, patch.MinV), (patch.MaxU, patch.MinV),
                (patch.MinU, patch.MaxV), (patch.MaxU, patch.MaxV),
                (midU, midV),
            };

            foreach (var (u, v) in sampleUVs)
            {
                var pointOnUnitCube = patch.FaceNormal + (u * axisA) + (v * axisB);
                var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(pointOnUnitCube);
                var latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);
                if (tile.Area.Contains(latLong))
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildAllPatches()
        {
            lock (_rebuildLock)
            {
                var heightScale = ComputeHeightScale();
                foreach (var leaf in _leaves.Values)
                {
                    leaf.Mesh.Rebuild(heightScale);
                }
            }
        }

        private float ComputeHeightScale()
        {
            return (Radius / PlanetLoader.EarthRadiusMeters) * _heightExaggeration;
        }

        // zoom is the Terrarium zoom level currently backing this patch's elevation data (see
        // PatchLeaf.Zoom) -- null for a patch still on the whole-globe ETOPO baseline, never up-res'd.
        // meshResolution is the current mesh grid size. sourceResolution is intentionally not shown in
        // the Outliner: it can be a huge source span (e.g. 5395) that is useful in the Inspector but too
        // noisy for node names.
        private static string PatchName(
            PatchKey key,
            int? zoom = null,
            int? meshResolution = null,
            int? sourceResolution = null,
            double? sourceMetersPerPixel = null)
        {
            var name = "Terrain";
            if (zoom.HasValue)
            {
                name += $" z{zoom.Value}";
            }
            if (sourceMetersPerPixel.HasValue)
            {
                name += $" {FormatMetersPerPixel(sourceMetersPerPixel.Value)}";
            }
            if (meshResolution.HasValue)
            {
                name += $" {meshResolution.Value}v";
            }
            name += $" F{key.Face}/R{key.Row}/C{key.Col}";
            if (!key.SplitPath.IsEmpty)
            {
                name += $" L{key.SplitPath.Length}";
                name += $" q{FormatSplitPathTail(key)}";
            }
            return name;
        }

        private static string FormatSplitPathTail(PatchKey key)
        {
            var splitPath = key.SplitPath;
            var start = Math.Max(0, splitPath.Length - PatchNameSplitPathTailDigits);
            return string.Concat(splitPath.Skip(start));
        }

        private static string FormatMetersPerPixel(double metersPerPixel)
        {
            if (metersPerPixel >= 1000.0)
            {
                return $"{metersPerPixel / 1000.0:0.##}km/px";
            }

            if (metersPerPixel >= 100.0)
            {
                return $"{metersPerPixel:0}m/px";
            }

            if (metersPerPixel >= 10.0)
            {
                return $"{metersPerPixel:0.#}m/px";
            }

            return metersPerPixel >= 1.0
                ? $"{metersPerPixel:0.##}m/px"
                : $"{metersPerPixel * 100.0:0.#}cm/px";
        }
    }
}

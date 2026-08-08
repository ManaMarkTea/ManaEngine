using System.Collections.Concurrent;
using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
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

            // Cached at leaf-creation time (see ComputePatchLocalBoundsSphere) -- invariant across an
            // in-place up-res (MinU/MaxU/MinV/MaxV never change until split/merge replaces the leaf
            // entirely), so auto-LOD's per-frame coverage scan never needs to re-sample corners.
            public required PatchBoundsSphere LocalBoundsSphere { get; init; }
        }

        // A patch's approximate bounding sphere in PlanetNode's own LOCAL space, derived from just its
        // FaceNormal/MinU/MaxU/MinV/MaxV (see ComputePatchLocalBoundsSphere).
        private readonly record struct PatchBoundsSphere(Vector3 LocalCenter, float LocalRadius);

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
            public required bool[] ChildSucceeded { get; init; }
            public required bool[] ChildFinalized { get; init; }
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

        private sealed class PendingSplitChildReady
        {
            public required PendingSplit Pending { get; init; }
            public required int ChildIndex { get; init; }
        }

        // Child splits that have finished building, waiting for ProcessPendingSplits to swap each one
        // into the scene graph on the main thread as it becomes ready -- see UpresPendingChildrenAsync.
        private readonly Queue<PendingSplitChildReady> _readyToFinalize = new();

        // Child keys with a merge (of their sibling group) currently in flight -- one entry per child,
        // not per synthesized parent key, since the children (not the not-yet-existing merged patch)
        // are what TryBeginPatchUpres/TrySplitPatch/CanSplitPatch need to see as "busy" (see
        // TryMergeSiblingGroup and the guard checks added to those three methods).
        private readonly ConcurrentDictionary<PatchKey, byte> _mergePending = new();

        // A sibling-group merge whose off-thread mesh rebuild is in flight -- see BeginMergeLocked.
        private sealed class PendingMerge
        {
            public required PatchKey MergedKey { get; init; }
            public required PatchKey[] ChildKeys { get; init; }
            public required PatchLeaf[] ChildLeaves { get; init; }
            public required PlanetPatchMesh MergedMesh { get; init; }
            public required int MergedMeshResolution { get; init; }
            public required int? MergedZoom { get; init; }
            public required int? MergedSourceResolution { get; init; }
            public required double? MergedSourceMetersPerPixel { get; init; }
        }

        // Merges whose off-thread rebuild has finished, waiting for ProcessPendingMerges to actually
        // swap them into the scene graph on the main thread -- mirrors _readyToFinalize.
        private readonly Queue<PendingMerge> _readyToFinalizeMerge = new();

        private readonly PlanetOceanMesh _oceanMesh;

        // Caps how many Terrarium tiles a single patch up-res will fetch (see SlippyMapMath.PickZoom).
        // A lower cap (16) used to force the zoom picker down to z5 (~4.9km/px), coarser than the
        // baseline it's meant to improve on -- 1024 tiles reaches a genuinely finer zoom. 13 is the
        // highest zoom Terrarium serves directly (14/15 need oversampling, out of scope).
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
        private const int MaxPendingSplitFinalizationsPerFrame = 1;

        private static readonly SemaphoreSlim UpresFetchThrottle = new(UpresMaxConcurrentFetches);

        // Caps concurrent GPU height-sample dispatches (see TrySampleHeightsOnGpuAsync), mirroring
        // UpresFetchThrottle's role for network fetches above. Lower than the network throttle -- GPU
        // submission from many threads still serializes at the driver/queue level, so higher concurrency
        // here mostly adds contention rather than throughput.
        private const int HeightSampleMaxConcurrentDispatches = 8;
        private static readonly TimeSpan HeightSampleTimeout = TimeSpan.FromSeconds(5);
        private static readonly SemaphoreSlim HeightSampleReadbackThrottle = new(HeightSampleMaxConcurrentDispatches);

        // UpdateAutoLod's activation/deactivation radius is great-circle distance from the sub-camera
        // point, scaled by altitude expressed as a fraction of Radius (not raw world units) so these
        // multipliers mean the same thing at any planet scale. Deactivation > activation gives real
        // hysteresis so a patch that just got more detail doesn't immediately qualify to lose it.
        private const float AutoLodActivationRadiusMultiplier = 3.0f;
        private const float AutoLodDeactivationRadiusMultiplier = 5.0f;

        // Floor under the altitude fraction so a camera sitting on the surface doesn't compute a
        // near-zero radius and trigger nothing.
        private const float AutoLodMinAltitudeFraction = 0.025f;

        // Hard cap on the altitude-scaled radius -- otherwise it grows unbounded with distance, and
        // viewing from far away (including the engine's default starting camera) would make every patch
        // on the globe register as "close" and stream in at once.
        private const float AutoLodMaxActivationRadiusFraction = 0.5f;
        private const float AutoLodMaxDeactivationRadiusFraction = 0.75f;

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

        /// <summary>
        /// Live, reboundable toggle for UpdateAutoLod (see its doc comment) -- mirrors HeightExaggeration's
        /// own pattern of a PlanetNode-owned setting an Inspector checkbox flips directly.
        /// </summary>
        public bool AutoLodEnabled { get; set; } = false;

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
                        var localBounds = ComputePatchLocalBoundsSphere(normal, minU, minU + step, minV, minV + step, radius);
                        _leaves[key] = new PatchLeaf { Mesh = mesh, Node = node, LocalBoundsSphere = localBounds };
                        _nodeToPatchKey[node] = key;
                        _patchDetails[key] = new PatchDetailInfo(null, mesh.Resolution, null, null);
                    }
                }
            }

            // A single un-tiled shell covering the whole globe (not one per terrain patch), masked to
            // real ocean coverage once height data arrives -- see PlanetOceanMesh. Resolution is
            // independent of the terrain patches' since waves need their own vertex density.
            _oceanMesh = new PlanetOceanMesh(context, radius, oceanResolution, heightLibrary: null);
            var oceanNode = new ManaMeshSceneNode(context, _oceanMesh) { Name = "Ocean" };
            AddChild(oceanNode);
        }

        /// <summary>
        /// Kicks off background decoding of every tile in heightLibrary; returns immediately without
        /// waiting on it. Each patch swaps from smooth placeholder to real elevation/color as soon as
        /// its own tile finishes, independently of the others.
        /// </summary>
        private static void SetCurrentThreadName(string name)
        {
            if (string.IsNullOrEmpty(System.Threading.Thread.CurrentThread.Name))
            {
                System.Threading.Thread.CurrentThread.Name = name;
            }
        }

        public void LoadHeightDataAsync(GeoTiffLibrary heightLibrary)
        {
            HeightLibrary = heightLibrary;
            Task.Run(() =>
            {
                SetCurrentThreadName("Planet height decode");
                heightLibrary.LoadAllImageData(ApplyTileToOverlappingPatches);
            });
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
                if (HeightLibrary == null || !_leaves.TryGetValue(key, out var leaf) || _mergePending.ContainsKey(key))
                {
                    return;
                }

                if (_upresStatus.TryGetValue(key, out var status)
                    && status is PatchUpresStatus.InProgress or PatchUpresStatus.Done or PatchUpresStatus.Failed)
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
        /// Splits an already up-res'd (Done) patch into 4 smaller patches, each starting its own up-res
        /// fetch at a higher Terrarium zoom (each child covers 1/4 the area, so it fits a finer zoom in
        /// the same tile budget). Unlike TryBeginPatchUpres, only ever triggered by an explicit user
        /// action (the Inspector's "Increase source zoom" button) -- never by plain reselection, or a
        /// couple of casual re-clicks could cascade several split levels deep. No-op if the patch isn't
        /// currently Done.
        /// </summary>
        public void TrySplitPatch(PatchKey key)
        {
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                if (!_leaves.TryGetValue(key, out var leaf) || _splitPending.ContainsKey(key) || _mergePending.ContainsKey(key))
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
                    Task.Run(() =>
                {
                    SetCurrentThreadName("Planet patch up-res");
                    return UpresPatchAsync(key, leaf.Mesh, splitPlan.Children[0].Plan);
                });
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
                if (!_leaves.TryGetValue(key, out var leaf) || _splitPending.ContainsKey(key) || _mergePending.ContainsKey(key))
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
                Task.Run(() =>
                {
                    SetCurrentThreadName("Planet patch up-res");
                    return UpresPatchAsync(key, leaf.Mesh, plannedPatches[0].Plan);
                });
                return;
            }

            if (!_splitPending.TryAdd(key, 0))
            {
                return;
            }

            BeginSplitLocked(key, leaf, new PatchSplitPlan { Children = plannedPatches });
        }

        // Caller must already hold _rebuildLock. Each child starts as a crop of the parent's own
        // already-loaded HeightLibrary data at the parent's resolution (no fetch needed), so the swap
        // happens synchronously here with no visible gap. UpresPendingChildrenAsync then fetches each
        // child's finer-zoom data in the background and updates that same live mesh in place.
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
                ChildSucceeded = new bool[children.Count],
                ChildFinalized = new bool[children.Count],
                ChildZoom = new int?[children.Count],
            };

            var targetZooms = string.Join("/", children.Select(child => $"z{child.Plan.Zoom}").Distinct().OrderBy(zoom => zoom));
            Console.WriteLine($"[Terrarium] Increasing {PatchName(parentKey, parentLeaf.Zoom, parentLeaf.Mesh.Resolution, parentLeaf.SourceResolution, parentLeaf.SourceMetersPerPixel)} to {targetZooms}: swapping in {children.Count} child patch(es) at current resolution, refining each as finer source data arrives.");

            RemoveChild(parentLeaf.Node);
            _leaves.Remove(parentKey);
            _nodeToPatchKey.Remove(parentLeaf.Node);
            _patchDetails.TryRemove(parentKey, out _);
            _upresStatus.TryRemove(parentKey, out _);

            for (var index = 0; index < children.Count; index++)
            {
                var child = children[index];
                var childMesh = new PlanetPatchMesh(
                    Context, parentMesh.FaceNormal, child.MinU, child.MaxU, child.MinV, child.MaxV, Radius,
                    parentLeaf.Mesh.Resolution, heightScale, HeightLibrary);

                pending.ChildKeys[index] = child.Key;
                pending.ChildMeshes[index] = childMesh;

                var childNode = new ManaMeshSceneNode(Context, childMesh)
                {
                    Name = PatchName(child.Key, parentLeaf.Zoom, childMesh.Resolution, parentLeaf.SourceResolution, parentLeaf.SourceMetersPerPixel)
                };
                AddChild(childNode);

                _leaves[child.Key] = new PatchLeaf
                {
                    Mesh = childMesh,
                    Node = childNode,
                    Zoom = parentLeaf.Zoom,
                    SourceResolution = parentLeaf.SourceResolution,
                    SourceMetersPerPixel = parentLeaf.SourceMetersPerPixel,
                    LocalBoundsSphere = ComputePatchLocalBoundsSphere(childMesh.FaceNormal, child.MinU, child.MaxU, child.MinV, child.MaxV, Radius),
                };
                _nodeToPatchKey[childNode] = child.Key;
                _patchDetails[child.Key] = new PatchDetailInfo(parentLeaf.Zoom, childMesh.Resolution, parentLeaf.SourceResolution, parentLeaf.SourceMetersPerPixel);
                _upresStatus[child.Key] = PatchUpresStatus.InProgress;
            }

            Task.Run(() =>
            {
                SetCurrentThreadName("Planet split child build");
                return UpresPendingChildrenAsync(pending);
            });
        }

        private async Task UpresPendingChildrenAsync(PendingSplit pending)
        {
            var tileKeys = pending.ChildPlans
                .SelectMany(plan => plan.TileIndices.Select(index => new TerrariumTileKey(plan.Zoom, index.X, index.Y)))
                .Distinct()
                .ToList();
            Console.WriteLine($"[Terrarium] Split {PatchName(pending.ParentKey, pending.ParentLeaf.Zoom, pending.ParentLeaf.Mesh.Resolution, pending.ParentLeaf.SourceResolution, pending.ParentLeaf.SourceMetersPerPixel)}: fetching {tileKeys.Count} unique tile(s) for {pending.ChildPlans.Length} child patch(es).");

            // ManaLog.TimeVerbose's scope is a ref struct and can't be held alive across an await, so
            // spans that need to cover an awaited call use the GetTimestamp()/WriteVerboseElapsed() pair
            // instead (see ManaLog.TimeVerbose's doc comment).
            var fetchStart = ManaLog.GetTimestamp();
            var fetched = await FetchTilesAsync(tileKeys);
            ManaLog.WriteVerboseElapsed($"[SplitTiming] fetch {tileKeys.Count} tile(s)", fetchStart);
            var fetchedByKey = fetched.ToDictionary(tile => new TerrariumTileKey(tile.Zoom, tile.TileX, tile.TileY));

            // GPU height-sampling path (see TrySampleHeightsOnGpuAsync), attempted per child -- outside
            // the lock below since it involves awaiting a GPU dispatch+readback, which lock (a
            // synchronous primitive) can't contain. Each child is independent, so run them concurrently
            // rather than one at a time (HeightSampleReadbackThrottle still bounds how many actually
            // dispatch to the GPU at once).
            var precomputedHeightsTasks = new Task<float[,]?>[pending.ChildPlans.Length];
            for (var childIndex = 0; childIndex < pending.ChildPlans.Length; childIndex++)
            {
                var plan = pending.ChildPlans[childIndex];
                var childMesh = pending.ChildMeshes[childIndex];
                var childFetchedTiles = plan.TileIndices
                    .Select(index => fetchedByKey.GetValueOrDefault(new TerrariumTileKey(plan.Zoom, index.X, index.Y)))
                    .Where(tile => tile != null)
                    .Select(tile => tile!)
                    .ToList();
                precomputedHeightsTasks[childIndex] = TrySampleHeightsOnGpuAsync(
                    Context, childMesh.FaceNormal, childMesh.MinU, childMesh.MaxU, childMesh.MinV, childMesh.MaxV,
                    plan.MeshResolution, childFetchedTiles, plan.TileIndices.Count);
            }
            var gpuSampleStart = ManaLog.GetTimestamp();
            var precomputedHeightsResults = await Task.WhenAll(precomputedHeightsTasks);
            ManaLog.WriteVerboseElapsed($"[SplitTiming] GPU sample {pending.ChildPlans.Length} child(ren)", gpuSampleStart);

            lock (_rebuildLock)
            {
                foreach (var tile in fetched)
                {
                    HeightLibrary!.AddTile(tile);
                }

                // Sequential across children, deliberately -- an earlier attempt to parallelize this loop
                // with Parallel.For (nesting with each child's own internal Parallel.For inside
                // GenerateData()'s ForEachRow) caused severe thread-pool oversubscription, regressing from
                // ~40s total to ~30s PER CHILD, and a further attempt to fix that via a [ThreadStatic]
                // suppression flag produced an outright deadlock under real use. Reverted to the simple,
                // correct, known-quantity sequential loop -- see PlanetPatchMesh.GenerateData's own
                // internal Parallel.For (gated on resolution) for the only parallelism active here.
                for (var childIndex = 0; childIndex < pending.ChildPlans.Length; childIndex++)
                {
                    var plan = pending.ChildPlans[childIndex];
                    var childFetchedAnyTile = plan.TileIndices
                        .Any(index => fetchedByKey.ContainsKey(new TerrariumTileKey(plan.Zoom, index.X, index.Y)));
                    if (!childFetchedAnyTile)
                    {
                        pending.ChildZoom[childIndex] = plan.Zoom;
                        pending.ChildSucceeded[childIndex] = false;
                        _readyToFinalize.Enqueue(new PendingSplitChildReady
                        {
                            Pending = pending,
                            ChildIndex = childIndex,
                        });
                        continue;
                    }

                    var precomputedHeights = precomputedHeightsResults[childIndex];
                    using (ManaLog.TimeVerbose($"[SplitTiming] child={childIndex} meshResolution={plan.MeshResolution} usedGpu={precomputedHeights != null}"))
                    {
                        if (precomputedHeights != null)
                        {
                            pending.ChildMeshes[childIndex].ApplyHeightDataWithResolution(HeightLibrary!, plan.MeshResolution, precomputedHeights);
                        }
                        else
                        {
                            pending.ChildMeshes[childIndex].ApplyHeightDataWithResolution(HeightLibrary!, plan.MeshResolution);
                        }
                    }

                    pending.ChildZoom[childIndex] = plan.Zoom;
                    pending.ChildSucceeded[childIndex] = true;
                    _readyToFinalize.Enqueue(new PendingSplitChildReady
                    {
                        Pending = pending,
                        ChildIndex = childIndex,
                    });
                }

                // Metadata (zoom/resolution/status) is finalized on the main thread, not here -- _leaves
                // is read every frame by UpdateAutoLod/the Outliner, so mutating it off-thread risks a
                // "collection modified" race. Queue each child instead; ProcessPendingSplits drains it.
            }

            var succeededCount = pending.ChildSucceeded.Count(succeeded => succeeded);
            Console.WriteLine($"[Terrarium] Split {PatchName(pending.ParentKey, pending.ParentLeaf.Zoom, pending.ParentLeaf.Mesh.Resolution, pending.ParentLeaf.SourceResolution, pending.ParentLeaf.SourceMetersPerPixel)}: refined {succeededCount}/{pending.ChildPlans.Length} child patch(es) with {fetched.Count}/{tileKeys.Count} unique tile(s).");
        }

        /// <summary>
        /// Finalizes pending split children on the main thread, one per frame, so the scene graph
        /// updates progressively as each child becomes ready rather than all at once.
        /// </summary>
        public void ProcessPendingSplits()
        {
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                var processedThisFrame = 0;
                while (processedThisFrame < MaxPendingSplitFinalizationsPerFrame && _readyToFinalize.Count > 0)
                {
                    var readySplit = _readyToFinalize.Dequeue();
                    FinalizeSplitChild(readySplit.Pending, readySplit.ChildIndex);
                    processedThisFrame++;
                }
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        // Caller must already hold _rebuildLock. Children already exist in the scene (added up front by
        // BeginSplitLocked) -- this just records the refined zoom/resolution metadata once a child's
        // finer-zoom fetch completes. A failed fetch leaves that child visible at its parent-inherited
        // resolution rather than deleting it.
        private void FinalizeSplitChild(PendingSplit pending, int childIndex)
        {
            if (pending.ChildFinalized[childIndex])
            {
                return;
            }

            pending.ChildFinalized[childIndex] = true;
            var childKey = pending.ChildKeys[childIndex];

            if (pending.ChildSucceeded[childIndex])
            {
                var childMesh = pending.ChildMeshes[childIndex];
                var childZoom = pending.ChildZoom[childIndex];
                var childPlan = pending.ChildPlans[childIndex];
                var childSourceResolution = childPlan.SourceResolution;
                var childMetersPerPixel = childPlan.SourceMetersPerPixel;

                if (_leaves.TryGetValue(childKey, out var childLeaf))
                {
                    childLeaf.Zoom = childZoom;
                    childLeaf.SourceResolution = childSourceResolution;
                    childLeaf.SourceMetersPerPixel = childMetersPerPixel;
                    childLeaf.Node.Name = PatchName(childKey, childZoom, childMesh.Resolution, childSourceResolution, childMetersPerPixel);
                }
                _patchDetails[childKey] = new PatchDetailInfo(childZoom, childMesh.Resolution, childSourceResolution, childMetersPerPixel);
                _upresStatus[childKey] = PatchUpresStatus.Done;
            }
            else if (_upresStatus.TryGetValue(childKey, out var status) && status == PatchUpresStatus.InProgress)
            {
                _upresStatus[childKey] = PatchUpresStatus.Failed;
            }

            if (pending.ChildFinalized.All(finalized => finalized))
            {
                _splitPending.TryRemove(pending.ParentKey, out _);
                var succeededCount = pending.ChildSucceeded.Count(succeeded => succeeded);
                Console.WriteLine($"[Terrarium] Split {PatchName(pending.ParentKey, pending.ParentLeaf.Zoom, pending.ParentLeaf.Mesh.Resolution, pending.ParentLeaf.SourceResolution, pending.ParentLeaf.SourceMetersPerPixel)} complete: {succeededCount}/{pending.ChildKeys.Length} child patch(es) refined to finer source data. Total leaves now: {_leaves.Count}.");
            }
        }

        /// <summary>
        /// Merges parentKey's 4 sibling children (quadrants 0-3, all currently Done, none already
        /// splitting/merging) back into one coarser patch. Purely structural -- doesn't consider screen
        /// coverage, that's UpdateAutoLod's job. Returns false without doing anything if the group isn't
        /// a clean 4-sibling set or any member is busy.
        /// </summary>
        public bool TryMergeSiblingGroup(PatchKey parentKey)
        {
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return false;
            }

            try
            {
                if (!TryFindMergeableSiblingGroupLocked(parentKey, out var childKeys, out var childLeaves))
                {
                    return false;
                }

                foreach (var childKey in childKeys)
                {
                    _mergePending.TryAdd(childKey, 0);
                }

                BeginMergeLocked(parentKey, childKeys, childLeaves);
                return true;
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        // Caller must already hold _rebuildLock. Finds parentKey's 4 sibling children (quadrants 0-3,
        // each a current, Done, not-already-busy leaf). Doesn't match the rarer uneven-SplitPath-depth
        // case TryAddSourceMatchedPatchPlans can produce -- those subtrees just never auto-merge.
        private bool TryFindMergeableSiblingGroupLocked(PatchKey parentKey, out PatchKey[] childKeys, out PatchLeaf[] childLeaves)
        {
            childKeys = new PatchKey[4];
            childLeaves = new PatchLeaf[4];

            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var childKey = parentKey.Child(quadrant);
                if (!_leaves.TryGetValue(childKey, out var leaf) ||
                    _splitPending.ContainsKey(childKey) ||
                    _mergePending.ContainsKey(childKey))
                {
                    return false;
                }

                var status = _upresStatus.TryGetValue(childKey, out var existing) ? existing : PatchUpresStatus.NotRequested;
                if (status != PatchUpresStatus.Done)
                {
                    return false;
                }

                childKeys[quadrant] = childKey;
                childLeaves[quadrant] = leaf;
            }

            return true;
        }

        // Caller must already hold _rebuildLock (reads childLeaves' Zoom, set only under that lock).
        // Merging targets one zoom coarser than the children's own (min(childZooms) - 1), reverting to
        // Zoom=null (whole-globe ETOPO baseline) if that would go below the lowest Terrarium zoom ever
        // assigned -- every child is guaranteed a non-null Zoom by the time it's a leaf (FinalizeSplit
        // aborts the whole split if any child failed to fetch), so this is always well-defined.
        private static (int? Zoom, int MeshResolution, int? SourceResolution, double? SourceMetersPerPixel) ComputeMergedPatchTarget(
            Vector3 faceNormal, float minU, float maxU, float minV, float maxV, PatchLeaf[] childLeaves)
        {
            var minChildZoom = childLeaves.Min(leaf => leaf.Zoom ?? (InitialTerrariumUpresZoom - 1));
            var targetZoom = minChildZoom - 1;
            if (targetZoom < InitialTerrariumUpresZoom)
            {
                return (null, DefaultBaselineMaxPatchMeshResolution, null, null);
            }

            var (lonMin, lonMax, latMin, latMax) = ComputePatchLonLatBounds(faceNormal, minU, maxU, minV, maxV);
            var sourceResolution = EstimateSourceResolutionForZoom(lonMin, lonMax, latMin, latMax, targetZoom);
            var sourceMetersPerPixel = EstimateSourceMetersPerPixel(latMin, latMax, targetZoom);
            var meshResolution = Math.Min(sourceResolution, MaxPatchMeshResolution);
            return (targetZoom, meshResolution, sourceResolution, sourceMetersPerPixel);
        }

        // Caller must already hold _rebuildLock. Builds one placeholder merged mesh at the union of the
        // 4 children's UV rects and kicks off its off-thread rebuild, touching neither the scene graph
        // nor _leaves yet -- all 4 children keep rendering until ProcessPendingMerges swaps them out.
        private void BeginMergeLocked(PatchKey parentKey, PatchKey[] childKeys, PatchLeaf[] childLeaves)
        {
            var faceNormal = childLeaves[0].Mesh.FaceNormal;
            var mergedMinU = childLeaves.Min(leaf => leaf.Mesh.MinU);
            var mergedMaxU = childLeaves.Max(leaf => leaf.Mesh.MaxU);
            var mergedMinV = childLeaves.Min(leaf => leaf.Mesh.MinV);
            var mergedMaxV = childLeaves.Max(leaf => leaf.Mesh.MaxV);

            var (mergedZoom, mergedMeshResolution, mergedSourceResolution, mergedSourceMetersPerPixel) =
                ComputeMergedPatchTarget(faceNormal, mergedMinU, mergedMaxU, mergedMinV, mergedMaxV, childLeaves);

            var heightScale = ComputeHeightScale();
            var mergedMesh = new PlanetPatchMesh(
                Context, faceNormal, mergedMinU, mergedMaxU, mergedMinV, mergedMaxV, Radius, resolution: 2, heightScale, heightLibrary: null);

            var pending = new PendingMerge
            {
                MergedKey = parentKey,
                ChildKeys = childKeys,
                ChildLeaves = childLeaves,
                MergedMesh = mergedMesh,
                MergedMeshResolution = mergedMeshResolution,
                MergedZoom = mergedZoom,
                MergedSourceResolution = mergedSourceResolution,
                MergedSourceMetersPerPixel = mergedSourceMetersPerPixel,
            };

            Console.WriteLine($"[Terrarium] Merging {childKeys.Length} sibling patches under {PatchName(parentKey, meshResolution: mergedMeshResolution)} back into one (children stay visible until ready).");

            Task.Run(() =>
            {
                SetCurrentThreadName("Planet merge rebuild");
                RebuildMergedPatchOffThread(pending);
            });
        }

        // Off-main-thread (Task.Run), but unlike UpresPendingChildrenAsync there is no async I/O to
        // await -- GeoTiffLibrary never removes tiles once added, so the merged mesh's rebuild can freely
        // reuse whatever finer Terrarium tiles the 4 children already fetched (or fall back to baseline
        // ETOPO data), no network fetch needed at all. Still run via Task.Run purely to keep
        // GenerateData()'s real CPU cost off the main thread -- the same lesson that motivated split's
        // off-graph-then-swap pattern applies just as much here.
        private void RebuildMergedPatchOffThread(PendingMerge pending)
        {
            lock (_rebuildLock)
            {
                pending.MergedMesh.ApplyHeightDataWithResolution(HeightLibrary!, pending.MergedMeshResolution);
                _readyToFinalizeMerge.Enqueue(pending);
            }

            Console.WriteLine($"[Terrarium] Merge {PatchName(pending.MergedKey, pending.MergedZoom, pending.MergedMesh.Resolution, pending.MergedSourceResolution, pending.MergedSourceMetersPerPixel)}: rebuilt at {pending.MergedMesh.Resolution}v (pending swap-in).");
        }

        /// <summary>
        /// Finalizes any merges whose off-thread rebuild has finished. MUST be called from the main
        /// thread once per frame (e.g. alongside ProcessPendingSplits in VoxelEditorGameMode) -- see
        /// RebuildMergedPatchOffThread's comment for why finalizing can't safely happen directly from the
        /// background task that detects readiness.
        /// </summary>
        public void ProcessPendingMerges()
        {
            List<PendingMerge>? ready = null;
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                if (_readyToFinalizeMerge.Count > 0)
                {
                    ready = new List<PendingMerge>(_readyToFinalizeMerge);
                    _readyToFinalizeMerge.Clear();
                }

                if (ready == null)
                {
                    return;
                }

                foreach (var pending in ready)
                {
                    FinalizeMerge(pending);
                }
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        // Caller must already hold _rebuildLock. Removes the 4 children, adds the 1 merged patch --
        // mirrors FinalizeSplit's atomic remove/add swap, cardinality inverted (4 -> 1 instead of 1 -> N).
        private void FinalizeMerge(PendingMerge pending)
        {
            foreach (var childKey in pending.ChildKeys)
            {
                if (!_leaves.TryGetValue(childKey, out var childLeaf))
                {
                    continue;
                }

                RemoveChild(childLeaf.Node);
                _leaves.Remove(childKey);
                _nodeToPatchKey.Remove(childLeaf.Node);
                _patchDetails.TryRemove(childKey, out _);
                _upresStatus.TryRemove(childKey, out _);
                _mergePending.TryRemove(childKey, out _);
            }

            var mergedMesh = pending.MergedMesh;
            var mergedNode = new ManaMeshSceneNode(Context, mergedMesh)
            {
                Name = PatchName(pending.MergedKey, pending.MergedZoom, mergedMesh.Resolution, pending.MergedSourceResolution, pending.MergedSourceMetersPerPixel),
            };
            AddChild(mergedNode);

            var localBounds = ComputePatchLocalBoundsSphere(
                mergedMesh.FaceNormal, mergedMesh.MinU, mergedMesh.MaxU, mergedMesh.MinV, mergedMesh.MaxV, Radius);
            var mergedLeaf = new PatchLeaf
            {
                Mesh = mergedMesh,
                Node = mergedNode,
                Zoom = pending.MergedZoom,
                SourceResolution = pending.MergedSourceResolution,
                SourceMetersPerPixel = pending.MergedSourceMetersPerPixel,
                LocalBoundsSphere = localBounds,
            };
            _leaves[pending.MergedKey] = mergedLeaf;
            _nodeToPatchKey[mergedNode] = pending.MergedKey;
            _patchDetails[pending.MergedKey] = new PatchDetailInfo(pending.MergedZoom, mergedMesh.Resolution, pending.MergedSourceResolution, pending.MergedSourceMetersPerPixel);
            _upresStatus[pending.MergedKey] = PatchUpresStatus.Done;

            Console.WriteLine($"[Terrarium] Merge {PatchName(pending.MergedKey, pending.MergedZoom, mergedMesh.Resolution, pending.MergedSourceResolution, pending.MergedSourceMetersPerPixel)} complete: {pending.ChildKeys.Length} children folded into 1 patch. Total leaves now: {_leaves.Count}.");
        }

        /// <summary>
        /// Camera-driven auto-LOD: splits/up-reses patches within AutoLodActivationRadiusMultiplier
        /// altitude-lengths of the sub-camera point, and merges clean 4-sibling groups that have drifted
        /// beyond AutoLodDeactivationRadiusMultiplier back into one coarser patch. Call once per frame
        /// alongside ProcessPendingSplits/ProcessPendingMerges. No-op if AutoLodEnabled is false. Reuses
        /// TryBeginPatchUpres/TrySplitPatch/TryMergeSiblingGroup's own guards, so this can never conflict
        /// with a manual trigger (double-click, Inspector button, reselection).
        /// </summary>
        public void UpdateAutoLod(CameraComponent camera)
        {
            if (!AutoLodEnabled || !Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                if (!Matrix4x4.Invert(WorldTransform, out var worldToLocal))
                {
                    return;
                }

                // Everything below works in PlanetNode's own LOCAL space (planet center = local origin)
                // -- a pure direction-from-center comparison, so PlanetNode's own world position/
                // rotation never needs to be reapplied per patch, only once here for the camera.
                var cameraLocalPos = Vector3.Transform(camera.Owner.Position, worldToLocal);
                var cameraDistanceFromCenter = cameraLocalPos.Length();
                if (cameraDistanceFromCenter < 0.001f)
                {
                    return;
                }

                var cameraDirection = cameraLocalPos / cameraDistanceFromCenter;
                // "Planet scale": altitude expressed as a fraction of the planet's own Radius (a
                // dimensionless number, e.g. 0.1 = one tenth of a planet-radius above the surface)
                // rather than a raw world-unit distance -- see the constants' own doc comments for why.
                var altitude = cameraDistanceFromCenter - Radius;
                var altitudeFraction = MathF.Max(AutoLodMinAltitudeFraction, altitude / Radius);
                var activationRadius = Radius * MathF.Min(altitudeFraction * AutoLodActivationRadiusMultiplier, AutoLodMaxActivationRadiusFraction);
                var deactivationRadius = Radius * MathF.Min(altitudeFraction * AutoLodDeactivationRadiusMultiplier, AutoLodMaxDeactivationRadiusFraction);

                // Ground-sample-distance ceiling: the finest zoom worth requesting resolves to about one
                // screen pixel at the camera's altitude -- without this a patch inside the activation
                // radius keeps re-qualifying for TrySplitPatch every frame with no ceiling short of
                // UpresMaxZoom, even once the camera stops moving.
                var targetMetersPerPixel = ComputeTargetMetersPerPixel(camera, MathF.Max(0.001f, altitude));

                var mergeCandidateParents = new HashSet<PatchKey>();

                foreach (var (key, leaf) in _leaves)
                {
                    if (_splitPending.ContainsKey(key) || _mergePending.ContainsKey(key))
                    {
                        continue;
                    }

                    var status = GetPatchUpresStatus(key);
                    if (status == PatchUpresStatus.InProgress)
                    {
                        continue;
                    }

                    var groundDistance = GreatCircleDistanceFromCameraDirection(leaf.LocalBoundsSphere.LocalCenter, cameraDirection);

                    if (groundDistance < activationRadius)
                    {
                        // Proximity alone isn't enough to escalate once the patch's current source data
                        // already resolves finer than the screen can show at this altitude.
                        if (IsSourceResolutionAlreadySufficientForAltitude(leaf.SourceMetersPerPixel, targetMetersPerPixel))
                        {
                            continue;
                        }

                        if (status != PatchUpresStatus.Done)
                        {
                            TryBeginPatchUpres(key);
                        }
                        else if (CanSplitPatch(key))
                        {
                            TrySplitPatch(key);
                        }
                    }
                    else if (groundDistance > deactivationRadius && !key.IsRoot && status == PatchUpresStatus.Done)
                    {
                        mergeCandidateParents.Add(key.Parent());
                    }
                }

                foreach (var parentKey in mergeCandidateParents)
                {
                    if (!TryFindMergeableSiblingGroupLocked(parentKey, out _, out var childLeaves))
                    {
                        continue;
                    }

                    // Re-check the MERGED (parent-level) center, not just one member's own distance -- a
                    // group only merges if the union is ALSO beyond the deactivation radius, not just
                    // one sibling.
                    var faceNormal = childLeaves[0].Mesh.FaceNormal;
                    var mergedMinU = childLeaves.Min(l => l.Mesh.MinU);
                    var mergedMaxU = childLeaves.Max(l => l.Mesh.MaxU);
                    var mergedMinV = childLeaves.Min(l => l.Mesh.MinV);
                    var mergedMaxV = childLeaves.Max(l => l.Mesh.MaxV);
                    var unionLocalBounds = ComputePatchLocalBoundsSphere(faceNormal, mergedMinU, mergedMaxU, mergedMinV, mergedMaxV, Radius);
                    var unionGroundDistance = GreatCircleDistanceFromCameraDirection(unionLocalBounds.LocalCenter, cameraDirection);
                    if (unionGroundDistance > deactivationRadius)
                    {
                        TryMergeSiblingGroup(parentKey);
                    }
                }
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        // Arc-length distance (in world units) between a patch's center and the point directly below
        // the camera, both expressed purely as directions from the planet's local origin -- this only
        // needs the ANGLE between them (great-circle geometry), never an actual lat/long conversion, so
        // it sidesteps pole/antimeridian degeneracies entirely.
        private float GreatCircleDistanceFromCameraDirection(Vector3 patchLocalCenter, Vector3 cameraDirection)
        {
            var patchDirection = Vector3.Normalize(patchLocalCenter);
            var cosAngle = Math.Clamp(Vector3.Dot(patchDirection, cameraDirection), -1f, 1f);
            return MathF.Acos(cosAngle) * Radius;
        }

        public readonly record struct PlanetStreamingStatus(
            int TotalLeaves,
            int PatchesUpresInProgress,
            int PatchesSplitting,
            int PatchesMerging,
            int ActiveTileFetches,
            int MaxConcurrentTileFetches);

        /// <summary>
        /// Cheap live snapshot of current streaming activity (pushed into the World Perf panel's
        /// "Streaming" section every frame). Safe to call from the main thread without _rebuildLock --
        /// the backing collections are all ConcurrentDictionary/semaphore/main-thread-only-mutated.
        /// </summary>
        public PlanetStreamingStatus GetStreamingStatus()
        {
            var upresInProgress = _upresStatus.Values.Count(status => status == PatchUpresStatus.InProgress);
            return new PlanetStreamingStatus(
                TotalLeaves: _leaves.Count,
                PatchesUpresInProgress: upresInProgress,
                PatchesSplitting: _splitPending.Count,
                PatchesMerging: _mergePending.Count / 4,
                ActiveTileFetches: UpresMaxConcurrentFetches - UpresFetchThrottle.CurrentCount,
                MaxConcurrentTileFetches: UpresMaxConcurrentFetches);
        }

        /// <summary>
        /// Scene nodes for every leaf currently doing background streaming work -- in-place up-res or
        /// splitting (both InProgress in _upresStatus), or merging (still-visible children in
        /// _mergePending) -- for a caller-side "highlight what's actively working" overlay. Safe to call
        /// without _rebuildLock, same as GetStreamingStatus.
        /// </summary>
        public IReadOnlyList<ManaSceneNode> GetStreamingActivityNodes()
        {
            var nodes = new List<ManaSceneNode>();

            foreach (var (key, status) in _upresStatus)
            {
                if (status == PatchUpresStatus.InProgress && _leaves.TryGetValue(key, out var leaf))
                {
                    nodes.Add(leaf.Node);
                }
            }

            foreach (var childKey in _mergePending.Keys)
            {
                if (_leaves.TryGetValue(childKey, out var leaf))
                {
                    nodes.Add(leaf.Node);
                }
            }

            return nodes;
        }

        public PatchUpresStatus GetPatchUpresStatus(PatchKey key)
        {
            return _upresStatus.TryGetValue(key, out var status) ? status : PatchUpresStatus.NotRequested;
        }

        public PatchDetailInfo? GetPatchDetailInfo(PatchKey key)
        {
            return _patchDetails.TryGetValue(key, out var detail) ? detail : null;
        }

        /// <summary>
        /// On-disk cache paths (see TerrariumTileFetcher.CacheRoot) for the Terrarium tiles currently
        /// backing key's elevation data -- for an Inspector "open source tile" button, so terrain
        /// anomalies can be checked against the actual cached PNG. Empty if the patch is still on the
        /// whole-globe ETOPO baseline (Zoom null) rather than a Terrarium up-res. Paths aren't guaranteed
        /// to exist on disk (a tile can be part of the plan but have failed to fetch).
        /// </summary>
        public IReadOnlyList<string> GetPatchTileCacheFilePaths(PatchKey key)
        {
            if (!_leaves.TryGetValue(key, out var leaf) || !leaf.Zoom.HasValue)
            {
                return Array.Empty<string>();
            }

            var zoom = leaf.Zoom.Value;
            var (lonMin, lonMax, latMin, latMax) = ComputePatchLonLatBounds(leaf.Mesh);
            return SlippyMapMath.EnumerateTiles(lonMin, lonMax, latMin, latMax, zoom)
                .Select(tile => Path.Combine(TerrariumTileFetcher.CacheRoot, zoom.ToString(), tile.X.ToString(), $"{tile.Y}.png"))
                .ToList();
        }

        // GPU height-sampling path for one patch's up-res (see IHeightSamplingComputeDevice) -- one
        // batched compute dispatch instead of GenerateData()'s per-vertex CPU FindCurrentTile loop.
        // Returns null (CPU fallback) if no compute device is available, a requested tile failed to
        // fetch, or the dispatch throws for any reason (e.g. context tearing down mid-flight).
        private static async Task<float[,]?> TrySampleHeightsOnGpuAsync(
            ManaContext context,
            Vector3 faceNormal,
            float minU,
            float maxU,
            float minV,
            float maxV,
            int meshResolution,
            IReadOnlyList<TerrariumHeightTile> fetchedTiles,
            int requestedTileCount)
        {
            if (context.GraphicsDevice is not IHeightSamplingComputeDevice computeDevice)
            {
                return null;
            }

            if (fetchedTiles.Count != requestedTileCount)
            {
                return null;
            }

            try
            {
                var lonLatGrid = PlanetPatchMesh.ComputeLonLatGrid(faceNormal, minU, maxU, minV, maxV, meshResolution);
                var resolution = lonLatGrid.GetLength(0);
                var points = new Vector2[resolution * resolution];
                for (var y = 0; y < resolution; y++)
                {
                    for (var x = 0; x < resolution; x++)
                    {
                        points[(y * resolution) + x] = lonLatGrid[x, y];
                    }
                }

                var tileDescriptors = new HeightSampleTileDescriptor[fetchedTiles.Count];
                for (var i = 0; i < fetchedTiles.Count; i++)
                {
                    var tile = fetchedTiles[i];
                    tileDescriptors[i] = new HeightSampleTileDescriptor
                    {
                        LonMin = tile.Area.UpperLeft.Longitude,
                        LonMax = tile.Area.LowerRight.Longitude,
                        LatMin = tile.Area.LowerLeft.Latitude,
                        LatMax = tile.Area.UpperRight.Latitude,
                        Width = TerrariumHeightTile.TileSize,
                        Height = TerrariumHeightTile.TileSize,
                        HeightData = tile.HeightData,
                    };
                }

                float[] heights;
                await HeightSampleReadbackThrottle.WaitAsync();
                try
                {
                    var sampleTask = computeDevice.SampleHeightsAsync(points, tileDescriptors);
                    var completedTask = await Task.WhenAny(sampleTask, Task.Delay(HeightSampleTimeout));
                    if (completedTask != sampleTask)
                    {
                        Console.WriteLine($"[Terrarium] GPU height sampling timed out after {HeightSampleTimeout.TotalSeconds:0.#}s, falling back to CPU.");
                        return null;
                    }

                    heights = await sampleTask;
                }
                finally
                {
                    HeightSampleReadbackThrottle.Release();
                }

                var result = new float[resolution, resolution];
                for (var y = 0; y < resolution; y++)
                {
                    for (var x = 0; x < resolution; x++)
                    {
                        result[x, y] = heights[(y * resolution) + x];
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Terrarium] GPU height sampling failed, falling back to CPU: {ex.Message}");
                return null;
            }
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

            var precomputedHeights = await TrySampleHeightsOnGpuAsync(
                Context, patch.FaceNormal, patch.MinU, patch.MaxU, patch.MinV, patch.MaxV,
                plan.MeshResolution, fetched, plan.TileIndices.Count);

            lock (_rebuildLock)
            {
                foreach (var tile in fetched)
                {
                    HeightLibrary!.AddTile(tile);
                }

                if (precomputedHeights != null)
                {
                    patch.ApplyHeightDataWithResolution(HeightLibrary!, plan.MeshResolution, precomputedHeights);
                }
                else
                {
                    patch.ApplyHeightDataWithResolution(HeightLibrary!, plan.MeshResolution);
                }

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

            Console.WriteLine($"[Terrarium] Up-res {PatchName(key, plan.Zoom, plan.MeshResolution, plan.SourceResolution, plan.SourceMetersPerPixel)}: applied {fetched.Count} tile(s){(precomputedHeights != null ? " (GPU)" : "")}.");
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

        // Real-world ground distance one screen pixel represents at the camera's current altitude and
        // FOV -- the same "screen-space error"/ground-sample-distance concept full terrain-LOD engines
        // use to decide how much source-data resolution is actually worth requesting: past this point,
        // finer Terrarium tiles would resolve detail no pixel could show anyway.
        private float ComputeTargetMetersPerPixel(CameraComponent camera, float sceneLocalAltitude)
        {
            // Altitude is a pure geometric (position) quantity -- converts through the planet's plain
            // Radius-to-EarthRadiusMeters ratio, NOT ComputeHeightScale() (which also folds in
            // HeightExaggeration, an artistic multiplier applied only to terrain DISPLACEMENT, never to
            // camera/patch positions).
            var metersPerSceneUnit = PlanetLoader.EarthRadiusMeters / Radius;
            var realWorldAltitudeMeters = sceneLocalAltitude * metersPerSceneUnit;

            var viewportHeightPixels = MathF.Max(1f, Context.Window.Size.Y);
            var visibleHeightMeters = 2f * realWorldAltitudeMeters * MathF.Tan(camera.FieldOfViewDegrees.ToRadians() * 0.5f);
            return visibleHeightMeters / viewportHeightPixels;
        }

        // True once a patch's current source data already resolves at or finer than one screen pixel at
        // the camera's altitude -- the gate UpdateAutoLod uses to stop escalating a close patch once
        // more detail wouldn't be visible. A never-up-res'd baseline patch still has a real known
        // resolution (~1.85km/px for 60-arcsec ETOPO), so it isn't treated as automatically insufficient.
        // Null (baseline hasn't loaded for this patch yet) is also treated as sufficient/"leave it alone"
        // rather than "go ahead" -- jumping to a Terrarium fetch before baseline even arrives is wrong,
        // and every leaf reads null for the first few seconds after launch.
        private static bool IsSourceResolutionAlreadySufficientForAltitude(double? currentMetersPerPixel, float targetMetersPerPixel)
        {
            return !currentMetersPerPixel.HasValue || currentMetersPerPixel.Value <= targetMetersPerPixel;
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

        // Samples 4 corners + center through the real vertex-generation formula and tests each against
        // the tile's lat/long bounding box, rather than building a bounding box FOR the patch -- a
        // cube-face patch is a *curved* quadrilateral in lat/long space, so its own bounding box breaks
        // at the antimeridian and near the poles. A missed partial overlap just skips a rebuild trigger
        // this round, not a correctness bug -- each vertex re-samples its own tile at generation time.
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

        // Same corner+center sampling idiom as ComputePatchLonLatBounds/TileOverlapsPatch above, just
        // projected through Radius instead of converted to lat/long -- gives a cheap, approximate
        // bounding sphere for a patch (center = the actual mid-UV point, radius = the farthest corner
        // from it) good enough for a screen-coverage LOD trigger without needing per-vertex data. Only
        // recomputed at leaf creation (root construction, FinalizeSplit, FinalizeMerge); invariant across
        // an in-place up-res since MinU/MaxU/MinV/MaxV never change until the leaf itself is replaced.
        private static PatchBoundsSphere ComputePatchLocalBoundsSphere(
            Vector3 faceNormal, float minU, float maxU, float minV, float maxV, float radius)
        {
            var axisA = new Vector3(faceNormal.Y, faceNormal.Z, faceNormal.X);
            var axisB = Vector3.Cross(faceNormal, axisA);
            var midU = (minU + maxU) * 0.5f;
            var midV = (minV + maxV) * 0.5f;

            var centerOnUnitCube = faceNormal + (midU * axisA) + (midV * axisB);
            var center = CubeSphere.WarpCubeToSphere(centerOnUnitCube) * radius;

            var cornerUVs = new[] { (minU, minV), (maxU, minV), (minU, maxV), (maxU, maxV) };
            var boundsRadius = 0f;
            foreach (var (u, v) in cornerUVs)
            {
                var cornerOnUnitCube = faceNormal + (u * axisA) + (v * axisB);
                var corner = CubeSphere.WarpCubeToSphere(cornerOnUnitCube) * radius;
                boundsRadius = MathF.Max(boundsRadius, Vector3.Distance(center, corner));
            }

            return new PatchBoundsSphere(center, boundsRadius);
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

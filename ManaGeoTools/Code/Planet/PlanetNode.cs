using static ManaGeoTools.Code.Planet.PlanetPatchGeometry;
using static ManaGeoTools.Code.Planet.PlanetPatchPlanner;
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
        private volatile bool _retired;
        public bool IsRetired => _retired;

        // Permanent removal only: ordinary scene reparenting must leave streaming alive.
        public void Retire()
        {
            lock (_rebuildLock)
            {
                if (_retired)
                {
                    return;
                }
                _retired = true;
                _pendingDetailChase = null;
                _rebuilds.Retire();
                _splitPending.Clear();
                _mergePending.Clear();
                _upresStatus.Clear();
                DiscardChangedPatches();
                Console.WriteLine($"[Planet] Retired {Name}; pending terrain results will be discarded.");
            }
        }

        public float Radius { get; }
        public int Resolution { get; }
        public int PatchesPerFaceEdge { get; }
        public int OceanResolution { get; }
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

            // The MeshRendererComponent that actually renders this patch, and the shared scene node it
            // lives on (one persistent node per root (face,row,col) grid cell -- every patch anywhere in
            // that root's split subtree is a component on the SAME node, however deep the split goes, so
            // the scene graph never grows past its initial node count). Inherited unchanged from parent
            // to children on split, and from siblings to the merged patch on merge.
            public required MeshRendererComponent Renderer { get; init; }
            public required ManaSceneNode RootNode { get; init; }

            // The Terrarium zoom level currently backing this patch's elevation data, once it's been
            // up-res'd at least once (null for a patch still on the whole-globe baseline) --
            // baked into the Node's display Name (see PatchName) so it's visible directly in the
            // Outliner instead of needing to cross-reference the log.
            public int? Zoom { get; set; }

            // Approximate number of source-data samples across this patch at Zoom. Null for the
            // whole-globe baseline before an on-demand Terrarium up-res.
            public int? SourceResolution { get; set; }

            // Approximate source-data ground resolution at the patch center latitude. Web Mercator
            // pixels vary by latitude, so this is intentionally a patch-local estimate.
            public double? SourceMetersPerPixel { get; set; }

            // Cached at leaf-creation time (see ComputePatchLocalBoundsSphere) -- invariant across an
            // in-place up-res (MinU/MaxU/MinV/MaxV never change until split/merge replaces the leaf
            // entirely), so auto-LOD's per-frame coverage scan never needs to re-sample corners.
            public required PatchBoundsSphere LocalBoundsSphere { get; init; }
        }

        private readonly Dictionary<PatchKey, PatchLeaf> _leaves = new();
        private readonly Dictionary<MeshRendererComponent, PatchKey> _componentToPatchKey = new();
        private readonly ConcurrentDictionary<PatchKey, PatchDetailInfo> _patchDetails = new();
        // Read every frame from the main thread (see VoxelEditorGameMode's selection-outline logic),
        // so status lookups deliberately stay off _rebuildLock; that lock can be held while rebuilding
        // large patch meshes after a cached Terrarium fetch completes.
        private readonly ConcurrentDictionary<PatchKey, PatchUpresStatus> _upresStatus = new();

        // Patches whose geometry changed, as immutable snapshots. Consumers (draped overlays) read
        // these with NO locking at all: everything they need is captured here, at the moment the
        // producer already holds _rebuildLock. ManaMeshCpuData is replaced wholesale by SetCpuData
        // rather than mutated, so a captured reference stays a valid snapshot forever.
        private readonly ConcurrentQueue<PatchGeometrySnapshot> _changedPatches = new();

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

        private sealed class PatchSplitPlan
        {
            public required IReadOnlyList<PlannedPatch> Children { get; init; }
        }

        // Parent keys with a split currently in flight -- guards against double-triggering a second
        // split for the same parent before the first one's children finish (see TrySplitPatch).
        private readonly ConcurrentDictionary<PatchKey, byte> _splitPending = new();

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

        private readonly PlanetOceanController _ocean;

        // Set once LoadHeightDataAsync's decode has applied every baseline tile. Volatile: written on the
        // decode task, read on the main thread every frame.
        private volatile bool _baselineHeightDataLoaded;

        // Terrarium serves native detail through z13; finer requests would oversample.
        private const int UpresMaxZoom = 13;

        // The first Terrarium step above the baseline. Zoom 7 (~1.2 km) followed a 1.85 km ETOPO baseline; over today's
        // 28 km one it was six times finer than 2,000 km up needs, in dozens of 257-vertex children per root -- 525
        // patches, 100 million indices. Zoom 4 (~10 km at the equator) is one step finer than the baseline.
        private const int InitialTerrariumUpresZoom = 4;
        public const int DefaultBaselineMaxPatchMeshResolution = 97;

        private const int PatchNameSplitPathTailDigits = 4;
        private const int MaxPendingSplitFinalizationsPerFrame = 1;

        // Merge finalization costs about what a split child's does -- scene-graph swap plus a snapshot
        // enqueue whose consumers then re-drape -- so it gets the same per-frame ceiling. Auto-LOD puts
        // no cap on how many merges it LAUNCHES, so without this a burst of them lands in one frame.
        private const int MaxPendingMergeFinalizationsPerFrame = 1;

        // Ceiling on snapshots handed to consumers per DrainChangedPatches call. Consumers budget their
        // own per-point work, but their intake (bucket scan per snapshot) is paid up front and scales
        // with snapshot count, so the drain itself has to be bounded too.
        public const int DefaultMaxChangedPatchesPerDrain = 4;

        private readonly PlanetRebuildScheduler _rebuilds = new();

        // UpdateAutoLod's activation/deactivation radius is great-circle distance from the sub-camera
        // point, scaled by altitude expressed as a fraction of Radius (not raw world units) so these
        // multipliers mean the same thing at any planet scale. Deactivation > activation gives real
        // hysteresis so a patch that just got more detail doesn't immediately qualify to lose it.
        // Refinements (an up-res or a split) auto-LOD starts in one frame; the nearest go first.
        private const int AutoLodStartsPerFrame = 1;

        // Merges started a frame; the rest are picked up next frame.
        private const int AutoLodMergesPerFrame = 2;

        // Vertices across a split's child until its own data arrives.
        private const int SplitPlaceholderResolution = 33;

        // Screen pixels between terrain samples that still read as full detail; each step of two is a quarter the vertices.
        private const float AutoLodPixelsPerSample = 4.0f;

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

        /// <summary>
        /// Live toggle for UpdateNearOcean, mirroring AutoLodEnabled. On by default: unlike auto-LOD it
        /// streams nothing and costs nothing until the camera is already near the surface.
        /// </summary>
        public bool NearOceanEnabled { get => _ocean.Enabled; set => _ocean.Enabled = value; }

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
            OceanResolution = oceanResolution;
            _heightExaggeration = heightExaggeration;
            Name = "Planet";

            var heightScale = ComputeHeightScale();
            float RootEdge(int index) => (2 * index - PatchesPerFaceEdge) / (float)PatchesPerFaceEdge;
            // The starting patches are built side by side (each one's colours and coastline raster are a few
            // milliseconds; one after another they froze the level's opening for 1.4 s). Only the scene graph below
            // waits for the main thread.
            var perFace = PatchesPerFaceEdge * PatchesPerFaceEdge;
            var meshes = new PlanetPatchMesh[CubeSphere.FaceNormals.Length * perFace];
            Parallel.For(0, meshes.Length, index =>
            {
                var normal = CubeSphere.FaceNormals[index / perFace];
                var row = (index % perFace) / PatchesPerFaceEdge;
                var col = index % PatchesPerFaceEdge;
                meshes[index] = new PlanetPatchMesh(
                    context, normal, RootEdge(col), RootEdge(col + 1), RootEdge(row), RootEdge(row + 1),
                    radius, resolution, heightScale, heightLibrary: null, _imageryPath);
            });

            for (var face = 0; face < CubeSphere.FaceNormals.Length; face++)
            {
                var normal = CubeSphere.FaceNormals[face];
                for (var row = 0; row < PatchesPerFaceEdge; row++)
                {
                    for (var col = 0; col < PatchesPerFaceEdge; col++)
                    {
                        var mesh = meshes[face * perFace + row * PatchesPerFaceEdge + col];
                        var key = PatchKey.Root(face, row, col);
                        // A permanent, shared container -- every patch this root's subtree ever splits
                        // into lives as a MeshRendererComponent on this SAME node, so the scene graph's
                        // node count never grows past what's created here.
                        var node = new ManaMeshSceneNode(context, mesh) { Name = $"Terrain Root F{face}/R{row}/C{col}" };
                        node.MeshRenderer.Name = PatchName(key, meshResolution: mesh.Resolution);
                        // Every patch of one root hands over to the next without a gap while the GPU uploads it.
                        node.MeshRenderer.HandoverGroup = $"planet:{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this):x}:{node.Name}";
                        AddChild(node);
                        var localBounds = ComputePatchLocalBoundsSphere(normal, mesh.MinU, mesh.MaxU, mesh.MinV, mesh.MaxV, radius);
                        _leaves[key] = new PatchLeaf { Mesh = mesh, Renderer = node.MeshRenderer, RootNode = node, LocalBoundsSphere = localBounds };
                        _componentToPatchKey[node.MeshRenderer] = key;
                        _patchDetails[key] = new PatchDetailInfo(null, mesh.Resolution, null, null);
                    }
                }
            }

            _ocean = new PlanetOceanController(this, _rebuildLock);
        }

        /// <summary>
        /// Kicks off background decoding of every tile in heightLibrary; returns immediately without
        /// waiting on it. Each patch swaps from smooth placeholder to real elevation/color as soon as
        /// its own tile finishes, independently of the others.
        /// </summary>
        public void LoadHeightDataAsync(GeoTiffLibrary heightLibrary)
        {
            if (_retired)
            {
                return;
            }
            lock (_rebuildLock)
            {
                if (_retired)
                {
                    return;
                }
                HeightLibrary = heightLibrary;
                _baselineHeightDataLoaded = false;
            }
            _ = _rebuilds.RunAsync("Planet height decode", () =>
            {
                try
                {
                    heightLibrary.LoadAllImageData(ApplyTileToOverlappingPatches, () => !_retired);
                }
                finally
                {
                    // In a finally so a failed or partial decode still releases whatever waits on this --
                    // an ocean that never appears is worse than one masked by incomplete data.
                    if (!_retired)
                    {
                        _baselineHeightDataLoaded = true;
                        Console.WriteLine("[Planet] Baseline height data decoded; ocean mask is now valid.");
                    }
                }
                return Task.CompletedTask;
            }, _ => { });
        }

        /// <summary>
        /// True once the baseline elevation decode has finished, so anything masked by height data (the ocean
        /// shell above all) is showing real coverage rather than a sea-level sphere. Also true when no height
        /// library was ever attached, since in that case no mask is coming and the placeholder is all there is.
        /// </summary>
        public bool IsOceanMaskReady => HeightLibrary == null || _baselineHeightDataLoaded;

        private void ApplyTileToOverlappingPatches(ManaGeoTools.Code.GeoTiff.GeoTiff tile)
        {
            PatchKey[] keys;
            lock (_rebuildLock)
            {
                if (_retired)
                {
                    return;
                }
                keys = _leaves.Keys.ToArray();
            }

            // One patch a lock: holding it for the whole planet (seconds) kept the picture off, so the globe sat plain green.
            foreach (var key in keys)
            {
                lock (_rebuildLock)
                {
                    if (_retired)
                    {
                        return;
                    }
                    if (!_leaves.TryGetValue(key, out var leaf) || leaf.Zoom.HasValue || !TileOverlapsPatch(tile, leaf.Mesh))
                    {
                        continue;
                    }

                    var sourceResolution = EstimateGeoTiffSourceResolutionForPatch(tile, leaf.Mesh);
                    var sourceMetersPerPixel = EstimateGeoTiffMetersPerPixel(tile, leaf.Mesh);
                    _baselineMetersPerPixel = Math.Max(_baselineMetersPerPixel ?? 0.0, sourceMetersPerPixel);
                    var meshResolution = Math.Max(
                        leaf.Mesh.Resolution,
                        Math.Min(sourceResolution, DefaultBaselineMaxPatchMeshResolution));

                    // SetImagery's worker seldom wins the lock between patches, so a picture asked for meanwhile goes on here.
                    if (_requestedImageryPath is { } imagery && !string.Equals(_imageryPath, imagery, StringComparison.Ordinal))
                    {
                        DressAllLocked(imagery);
                    }
                    ApplyHeightData(leaf.Mesh, HeightLibrary!, meshResolution);
                    leaf.SourceResolution = sourceResolution;
                    leaf.SourceMetersPerPixel = sourceMetersPerPixel;
                    leaf.Renderer.Name = PatchName(key, leaf.Zoom, leaf.Mesh.Resolution, leaf.SourceResolution, leaf.SourceMetersPerPixel);
                    _patchDetails[key] = new PatchDetailInfo(leaf.Zoom, leaf.Mesh.Resolution, leaf.SourceResolution, leaf.SourceMetersPerPixel);
                    StitchAroundLocked(key);
                    EnqueuePatchSnapshotLocked(key);
                }
            }

            lock (_rebuildLock)
            {
                if (!_retired)
                {
                    _ocean.ApplyHeightData(HeightLibrary!);
                }
            }
        }

        // Caller holds _rebuildLock. What SetImagery's worker does, all at once; it then finds nothing left to do.
        private void DressAllLocked(string imagery)
        {
            _imageryPath = imagery;
            foreach (var leaf in _leaves.Values)
            {
                if (!string.Equals(leaf.Mesh.ImageryPath, imagery, StringComparison.Ordinal) && !leaf.Mesh.TryDressInPlace(imagery))
                {
                    leaf.Mesh.SetImagery(imagery);
                }
            }
        }

        // Caller holds _rebuildLock. The leaf covering a point of a cube face, found by walking down from its root square.
        private (PatchKey Key, PatchLeaf Leaf)? FindLeafAtLocked(int face, float u, float v)
        {
            var step = 2f / PatchesPerFaceEdge;
            var col = Math.Clamp((int)MathF.Floor((u + 1f) / step), 0, PatchesPerFaceEdge - 1);
            var row = Math.Clamp((int)MathF.Floor((v + 1f) / step), 0, PatchesPerFaceEdge - 1);
            var key = PatchKey.Root(face, row, col);
            var minU = -1f + col * step;
            var maxU = minU + step;
            var minV = -1f + row * step;
            var maxV = minV + step;
            for (var depth = 0; depth < 40; depth++)
            {
                if (_leaves.TryGetValue(key, out var leaf))
                {
                    return (key, leaf);
                }
                var midU = (minU + maxU) * 0.5f;
                var midV = (minV + maxV) * 0.5f;
                var quadrant = 0;
                if (u >= midU)
                {
                    quadrant |= 1;
                    minU = midU;
                }
                else
                {
                    maxU = midU;
                }
                if (v >= midV)
                {
                    quadrant |= 2;
                    minV = midV;
                }
                else
                {
                    maxV = midV;
                }
                key = key.Child(quadrant);
            }
            return null;
        }

        // Caller holds _rebuildLock. Pins a patch's edges onto the coarser neighbours beside them; the finer side of an
        // edge always gives way, so two neighbours never pin to each other. Edges on a cube face's border keep their skirts.
        private bool StitchLocked(PatchKey key, PatchLeaf leaf)
        {
            var mesh = leaf.Mesh;
            var stitches = new PlanetEdgeStitch[]?[4];
            for (var edge = 0; edge < 4; edge++)
            {
                stitches[edge] = FindEdgeStitchesLocked(key, leaf, edge);
            }
            return mesh.SetStitches(stitches);
        }

        private PlanetEdgeStitch[]? FindEdgeStitchesLocked(PatchKey key, PatchLeaf leaf, int edge)
        {
            var mesh = leaf.Mesh;
            var spacing = (mesh.EdgeEnd(edge) - mesh.EdgeStart(edge)) / (mesh.Resolution - 1);
            List<PlanetEdgeStitch>? found = null;
            foreach (var (_, neighbour) in NeighboursAlongLocked(key, mesh, edge))
            {
                var other = neighbour.Mesh;
                var otherStart = other.EdgeStart(edge ^ 1);
                var otherEnd = other.EdgeEnd(edge ^ 1);
                if (GivesWayTo(spacing, leaf, (otherEnd - otherStart) / (other.Resolution - 1), neighbour)
                    && other.PreciseEdgePoints(edge ^ 1) is { } points)
                {
                    found ??= new List<PlanetEdgeStitch>();
                    found.Add(new PlanetEdgeStitch(otherStart, otherEnd, points));
                }
            }
            return found?.ToArray();
        }

        // Caller holds _rebuildLock. The leaves across one edge, in order along it (a neighbour's facing edge is edge ^ 1);
        // none on a cube face's border.
        private List<(PatchKey Key, PatchLeaf Leaf)> NeighboursAlongLocked(PatchKey key, PlanetPatchMesh mesh, int edge)
        {
            var neighbours = new List<(PatchKey Key, PatchLeaf Leaf)>();
            var start = mesh.EdgeStart(edge);
            var end = mesh.EdgeEnd(edge);
            var epsilon = (end - start) * 1e-3f;
            float across;
            switch (edge)
            {
                case 0:
                    across = mesh.MinV - epsilon;
                    break;
                case 1:
                    across = mesh.MaxV + epsilon;
                    break;
                case 2:
                    across = mesh.MinU - epsilon;
                    break;
                default:
                    across = mesh.MaxU + epsilon;
                    break;
            }
            if (across < -1f || across > 1f)
            {
                return neighbours;
            }

            var position = start;
            while (position < end - epsilon * 0.5f)
            {
                var probe = position + epsilon * 0.5f;
                (PatchKey Key, PatchLeaf Leaf)? hit;
                if (edge < 2)
                {
                    hit = FindLeafAtLocked(key.Face, probe, across);
                }
                else
                {
                    hit = FindLeafAtLocked(key.Face, across, probe);
                }
                if (hit is not { } neighbour)
                {
                    break;
                }
                var otherEnd = neighbour.Leaf.Mesh.EdgeEnd(edge ^ 1);
                if (otherEnd <= position)
                {
                    break;
                }
                neighbours.Add(neighbour);
                position = otherEnd;
            }
            return neighbours;
        }

        // The finer-spaced side gives way; at the same spacing, the one on finer height data (they may disagree).
        private static bool GivesWayTo(float spacing, PatchLeaf leaf, float otherSpacing, PatchLeaf other)
        {
            if (otherSpacing > spacing * 1.01f)
            {
                return true;
            }
            if (otherSpacing < spacing * 0.99f)
            {
                return false;
            }
            return (other.Zoom ?? -1) < (leaf.Zoom ?? -1);
        }

        // Caller holds _rebuildLock. After a patch's geometry changes: it and every neighbour along its edges re-pin.
        private void StitchAroundLocked(PatchKey key)
        {
            if (!_leaves.TryGetValue(key, out var leaf))
            {
                return;
            }
            StitchLocked(key, leaf);
            var seen = new HashSet<PatchKey> { key };
            for (var edge = 0; edge < 4; edge++)
            {
                foreach (var neighbour in NeighboursAlongLocked(key, leaf.Mesh, edge))
                {
                    if (seen.Add(neighbour.Key) && StitchLocked(neighbour.Key, neighbour.Leaf))
                    {
                        EnqueuePatchSnapshotLocked(neighbour.Key);
                    }
                }
            }
        }

        // Caller must already hold _rebuildLock. Captures everything a draped overlay needs so it
        // never has to reach back into locked state.
        private void EnqueuePatchSnapshotLocked(PatchKey key)
        {
            if (!_leaves.TryGetValue(key, out var leaf))
            {
                return;
            }

            var mesh = leaf.Mesh;
            var cpuData = mesh.CpuData;
            if (cpuData == null)
            {
                return;
            }

            _changedPatches.Enqueue(new PatchGeometrySnapshot(
                key, mesh.FaceNormal, mesh.MinU, mesh.MaxU, mesh.MinV, mesh.MaxV, mesh.Resolution, cpuData));
        }

        /// <summary>
        /// Hands over up to <paramref name="maxSnapshots"/> of the geometry snapshots produced since the
        /// last call. Lock-free on both sides. Whatever doesn't fit the budget stays queued in order for
        /// the next call, so a burst of finished patches spreads over frames instead of landing in one.
        ///
        /// Call this every frame even with no consumers attached: snapshots each pin a patch's CPU mesh,
        /// so a queue nobody drains grows without bound for as long as terrain streams.
        /// </summary>
        public List<PatchGeometrySnapshot> DrainChangedPatches(int maxSnapshots = DefaultMaxChangedPatchesPerDrain)
        {
            var budget = Math.Max(1, maxSnapshots);
            var drained = new List<PatchGeometrySnapshot>();
            while (drained.Count < budget && _changedPatches.TryDequeue(out var snapshot))
            {
                drained.Add(snapshot);
            }

            return drained;
        }

        /// <summary>Snapshots still waiting on a DrainChangedPatches call.</summary>
        public int PendingChangedPatchCount => _changedPatches.Count;

        /// <summary>
        /// Drops every queued snapshot without processing it. For a caller with no consumers to feed --
        /// keeping the queue from growing is the entire point, and the next real change re-snapshots.
        /// </summary>
        public void DiscardChangedPatches()
        {
            while (_changedPatches.TryDequeue(out _))
            {
            }
        }

        public bool TryGetPatchKey(MeshRendererComponent? renderer, out PatchKey key)
        {
            if (renderer == null)
            {
                key = default;
                return false;
            }

            return _componentToPatchKey.TryGetValue(renderer, out key);
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
            if (_retired)
            {
                return;
            }
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                if (_retired || HeightLibrary == null || !_leaves.TryGetValue(key, out var leaf) || _mergePending.ContainsKey(key))
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
            if (_retired)
            {
                return;
            }
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                if (_retired || !_leaves.TryGetValue(key, out var leaf) || _splitPending.ContainsKey(key) || _mergePending.ContainsKey(key))
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
                    SchedulePatchUpres(key, leaf, splitPlan.Children[0].Plan);
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
            if (_retired)
            {
                return false;
            }
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return false;
            }

            try
            {
                if (_retired || !_leaves.TryGetValue(key, out var leaf) || _splitPending.ContainsKey(key) || _mergePending.ContainsKey(key))
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
                SchedulePatchUpres(key, leaf, plannedPatches[0].Plan);
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

            var rootNode = parentLeaf.RootNode;

            // The parent's own renderer component goes stale the moment it's removed below -- if it was
            // the current selection, clear just that (not SelectedNode, since rootNode stays put and
            // selected/expanded in the Outliner, ready for the user to click a new child straight away).
            var selection = Context.World.Selection;
            if (ReferenceEquals(selection.SelectedComponent, parentLeaf.Renderer))
            {
                selection.SelectedComponent = null;
            }

            rootNode.Components.Remove(parentLeaf.Renderer);
            _leaves.Remove(parentKey);
            _componentToPatchKey.Remove(parentLeaf.Renderer);
            _patchDetails.TryRemove(parentKey, out _);
            _upresStatus.TryRemove(parentKey, out _);

            for (var index = 0; index < children.Count; index++)
            {
                var child = children[index];
                // A small stand-in, refined off this thread as the finer data arrives: built at the parent's resolution
                // (769 and up) every child of a split cost the main thread whole seconds and gigabytes.
                var childMesh = new PlanetPatchMesh(
                    Context, parentMesh.FaceNormal, child.MinU, child.MaxU, child.MinV, child.MaxV, Radius,
                    Math.Min(parentLeaf.Mesh.Resolution, SplitPlaceholderResolution), heightScale, HeightLibrary, _imageryPath, parentMesh.Detail);

                pending.ChildKeys[index] = child.Key;
                pending.ChildMeshes[index] = childMesh;

                var childRenderer = new MeshRendererComponent(rootNode, childMesh)
                {
                    Name = PatchName(child.Key, parentLeaf.Zoom, childMesh.Resolution, parentLeaf.SourceResolution, parentLeaf.SourceMetersPerPixel),
                    HandoverGroup = parentLeaf.Renderer.HandoverGroup
                };
                rootNode.Components.Add(childRenderer);

                _leaves[child.Key] = new PatchLeaf
                {
                    Mesh = childMesh,
                    Renderer = childRenderer,
                    RootNode = rootNode,
                    Zoom = parentLeaf.Zoom,
                    SourceResolution = parentLeaf.SourceResolution,
                    SourceMetersPerPixel = parentLeaf.SourceMetersPerPixel,
                    LocalBoundsSphere = ComputePatchLocalBoundsSphere(childMesh.FaceNormal, child.MinU, child.MaxU, child.MinV, child.MaxV, Radius),
                };
                _componentToPatchKey[childRenderer] = child.Key;
                _patchDetails[child.Key] = new PatchDetailInfo(parentLeaf.Zoom, childMesh.Resolution, parentLeaf.SourceResolution, parentLeaf.SourceMetersPerPixel);
                _upresStatus[child.Key] = PatchUpresStatus.InProgress;
            }
            foreach (var child in children)
            {
                StitchAroundLocked(child.Key);
            }

            _ = _rebuilds.RunAsync("Planet split child build", () => UpresPendingChildrenAsync(pending),
                _ =>
                {
                    for (var i = 0; i < pending.ChildKeys.Length; i++)
                    {
                        QueueSplitCompletion(pending, i);
                    }
                });
        }

        private void QueueSplitCompletion(PendingSplit pending, int childIndex) =>
            _rebuilds.Enqueue(PlanetRebuildKind.Split, () => FinalizeSplitChild(pending, childIndex));

        private async Task UpresPendingChildrenAsync(PendingSplit pending)
        {
            if (_retired)
            {
                return;
            }
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
            if (_retired)
            {
                return;
            }
            ManaLog.WriteVerboseElapsed($"[SplitTiming] fetch {tileKeys.Count} tile(s)", fetchStart);
            var fetchedByKey = fetched.ToDictionary(tile => new TerrariumTileKey(tile.Zoom, tile.TileX, tile.TileY));

            // GPU height-sampling path (see PlanetHeightSampler), attempted per child -- outside
            // the lock below since it involves awaiting a GPU dispatch+readback, which lock (a
            // synchronous primitive) can't contain. Each child is independent, so run them concurrently
            // rather than one at a time (PlanetHeightSampler still bounds how many actually
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
                precomputedHeightsTasks[childIndex] = PlanetHeightSampler.Shared.TrySampleAsync(
                    Context, childMesh.FaceNormal, childMesh.MinU, childMesh.MaxU, childMesh.MinV, childMesh.MaxV,
                    plan.MeshResolution, childFetchedTiles, plan.TileIndices.Count, () => !_retired);
            }
            var gpuSampleStart = ManaLog.GetTimestamp();
            var precomputedHeightsResults = await Task.WhenAll(precomputedHeightsTasks);
            ManaLog.WriteVerboseElapsed($"[SplitTiming] GPU sample {pending.ChildPlans.Length} child(ren)", gpuSampleStart);

            lock (_rebuildLock)
            {
                if (_retired)
                {
                    return;
                }
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
                        QueueSplitCompletion(pending, childIndex);
                        continue;
                    }

                    var precomputedHeights = precomputedHeightsResults[childIndex];
                    using (ManaLog.TimeVerbose($"[SplitTiming] child={childIndex} meshResolution={plan.MeshResolution} usedGpu={precomputedHeights != null}"))
                    {
                        ApplyHeightData(pending.ChildMeshes[childIndex], HeightLibrary!, plan.MeshResolution, precomputedHeights);
                    }
                    var childKey = pending.ChildKeys[childIndex];
                    if (_leaves.TryGetValue(childKey, out var childLeaf) && ReferenceEquals(childLeaf.Mesh, pending.ChildMeshes[childIndex]))
                    {
                        StitchAroundLocked(childKey);
                    }

                    pending.ChildZoom[childIndex] = plan.Zoom;
                    pending.ChildSucceeded[childIndex] = true;
                    QueueSplitCompletion(pending, childIndex);
                }

                // Metadata (zoom/resolution/status) is finalized on the main thread, not here -- _leaves
                // is read every frame by UpdateAutoLod/the Outliner, so mutating it off-thread risks a
                // "collection modified" race. Queue each child instead; ProcessPendingSplits drains it.
            }

            var succeededCount = pending.ChildSucceeded.Count(succeeded => succeeded);
            Console.WriteLine($"[Terrarium] Split {PatchName(pending.ParentKey, pending.ParentLeaf.Zoom, pending.ParentLeaf.Mesh.Resolution, pending.ParentLeaf.SourceResolution, pending.ParentLeaf.SourceMetersPerPixel)}: refined {succeededCount}/{pending.ChildPlans.Length} child patch(es) with {fetched.Count}/{tileKeys.Count} unique tile(s).");
        }

        /// <summary>
        /// Applies ready upres metadata and split children on the main thread with separate frame budgets.
        /// </summary>
        public void ProcessPendingSplits()
        {
            if (_retired)
            {
                return;
            }
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                _rebuilds.Drain(PlanetRebuildKind.Upres, 1);
                _rebuilds.Drain(PlanetRebuildKind.Split, MaxPendingSplitFinalizationsPerFrame);
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
                    childLeaf.Renderer.Name = PatchName(childKey, childZoom, childMesh.Resolution, childSourceResolution, childMetersPerPixel);
                }
                _patchDetails[childKey] = new PatchDetailInfo(childZoom, childMesh.Resolution, childSourceResolution, childMetersPerPixel);
                _upresStatus[childKey] = PatchUpresStatus.Done;
                EnqueuePatchSnapshotLocked(childKey);
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
            if (_retired)
            {
                return false;
            }
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
        // Zoom=null (whole-globe baseline) if that would go below the lowest Terrarium zoom ever
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
            var meshResolution = Math.Min(sourceResolution, MaxPatchVertices);
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

            // The children's sharp tile, when it covers the merged patch too: otherwise the patch comes up in the whole
            // picture, kilometres a pixel, until the tile is dressed back on a frame or more later.
            PlanetImageryDetail? detail = null;
            if (childLeaves[0].Mesh.Detail is { } childDetail
                && childDetail.MinU <= mergedMinU && childDetail.MaxU >= mergedMaxU
                && childDetail.MinV <= mergedMinV && childDetail.MaxV >= mergedMaxV)
            {
                detail = childDetail;
            }

            var heightScale = ComputeHeightScale();
            var mergedMesh = new PlanetPatchMesh(
                Context, faceNormal, mergedMinU, mergedMaxU, mergedMinV, mergedMaxV, Radius, resolution: 2, heightScale, heightLibrary: null, _imageryPath, detail);

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

            _ = _rebuilds.RunAsync("Planet merge rebuild", () =>
            {
                RebuildMergedPatchOffThread(pending);
                return Task.CompletedTask;
            }, exception => _rebuilds.Enqueue(PlanetRebuildKind.Merge, () =>
            {
                foreach (var childKey in pending.ChildKeys)
                {
                    _mergePending.TryRemove(childKey, out _);
                }
            }));
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
                if (_retired)
                {
                    return;
                }
                ApplyHeightData(pending.MergedMesh, HeightLibrary!, pending.MergedMeshResolution);
                _rebuilds.Enqueue(PlanetRebuildKind.Merge, () => FinalizeMerge(pending));
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
            if (_retired)
            {
                return;
            }
            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                _rebuilds.Drain(PlanetRebuildKind.Merge, MaxPendingMergeFinalizationsPerFrame);
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        // Caller must already hold _rebuildLock. Removes the 4 children, adds the 1 merged patch --
        // mirrors BeginSplitLocked's atomic remove/add swap, cardinality inverted (4 -> 1 instead of
        // 1 -> N), reusing the same shared RootNode all 4 children already live on.
        private void FinalizeMerge(PendingMerge pending)
        {
            var rootNode = pending.ChildLeaves[0].RootNode;
            var selection = Context.World.Selection;

            foreach (var childKey in pending.ChildKeys)
            {
                if (!_leaves.TryGetValue(childKey, out var childLeaf))
                {
                    continue;
                }

                if (ReferenceEquals(selection.SelectedComponent, childLeaf.Renderer))
                {
                    selection.SelectedComponent = null;
                }

                childLeaf.RootNode.Components.Remove(childLeaf.Renderer);
                _leaves.Remove(childKey);
                _componentToPatchKey.Remove(childLeaf.Renderer);
                _patchDetails.TryRemove(childKey, out _);
                _upresStatus.TryRemove(childKey, out _);
                _mergePending.TryRemove(childKey, out _);
            }

            var mergedMesh = pending.MergedMesh;
            // Built off the lock: the picture may have arrived since.
            if (!string.Equals(mergedMesh.ImageryPath, _imageryPath, StringComparison.Ordinal))
            {
                mergedMesh.SetImagery(_imageryPath);
            }

            var mergedRenderer = new MeshRendererComponent(rootNode, mergedMesh)
            {
                Name = PatchName(pending.MergedKey, pending.MergedZoom, mergedMesh.Resolution, pending.MergedSourceResolution, pending.MergedSourceMetersPerPixel),
                HandoverGroup = pending.ChildLeaves[0].Renderer.HandoverGroup,
            };
            rootNode.Components.Add(mergedRenderer);

            var localBounds = ComputePatchLocalBoundsSphere(
                mergedMesh.FaceNormal, mergedMesh.MinU, mergedMesh.MaxU, mergedMesh.MinV, mergedMesh.MaxV, Radius);
            var mergedLeaf = new PatchLeaf
            {
                Mesh = mergedMesh,
                Renderer = mergedRenderer,
                RootNode = rootNode,
                Zoom = pending.MergedZoom,
                SourceResolution = pending.MergedSourceResolution,
                SourceMetersPerPixel = pending.MergedSourceMetersPerPixel ?? _baselineMetersPerPixel,
                LocalBoundsSphere = localBounds,
            };
            _leaves[pending.MergedKey] = mergedLeaf;
            _componentToPatchKey[mergedRenderer] = pending.MergedKey;
            _patchDetails[pending.MergedKey] = new PatchDetailInfo(pending.MergedZoom, mergedMesh.Resolution, pending.MergedSourceResolution, pending.MergedSourceMetersPerPixel);
            _upresStatus[pending.MergedKey] = PatchUpresStatus.Done;
            StitchAroundLocked(pending.MergedKey);
            EnqueuePatchSnapshotLocked(pending.MergedKey);

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
            if (_retired)
            {
                return;
            }
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
                var refinements = new List<(PatchKey Key, float Distance, bool Upres)>();

                // A copy: a split or up-res served from the tile cache can finish inside this loop and change the leaves.
                foreach (var (key, leaf) in _leaves.ToArray())
                {
                    if (!_leaves.ContainsKey(key) || _splitPending.ContainsKey(key) || _mergePending.ContainsKey(key))
                    {
                        continue;
                    }

                    var status = GetPatchUpresStatus(key);
                    if (status == PatchUpresStatus.InProgress)
                    {
                        continue;
                    }

                    var groundDistance = NearestGroundDistance(leaf.LocalBoundsSphere, cameraDirection);

                    var patchTarget = PatchTargetMetersPerPixel(targetMetersPerPixel, altitude, groundDistance);
                    if (groundDistance < activationRadius)
                    {
                        // Proximity alone isn't enough to escalate once the patch's current source data
                        // already resolves finer than the screen can show at this distance.
                        if (!IsSourceResolutionAlreadySufficientForAltitude(leaf.SourceMetersPerPixel, patchTarget))
                        {
                            refinements.Add((key, groundDistance, status != PatchUpresStatus.Done));
                            continue;
                        }
                    }

                    // Folded back when far away, or when finer than this distance needs: by distance alone everything
                    // refined within the (~800 km) deactivation radius stayed so -- 525 patches, 120 million indices.
                    if (!key.IsRoot && status == PatchUpresStatus.Done
                        && (groundDistance > deactivationRadius || IsOverDetailed(leaf.SourceMetersPerPixel, patchTarget)))
                    {
                        mergeCandidateParents.Add(key.Parent());
                    }
                }

                // Nearest first, and only a few a frame: starting every eligible patch at once (each split building
                // dozens of children) took whole seconds in one frame. The rest are picked up again next frame.
                refinements.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
                var started = 0;
                foreach (var (key, _, upres) in refinements)
                {
                    if (started >= AutoLodStartsPerFrame)
                    {
                        break;
                    }
                    if (upres)
                    {
                        TryBeginPatchUpres(key);
                        started++;
                    }
                    else if (CanSplitPatch(key))
                    {
                        TrySplitPatch(key);
                        started++;
                    }
                }

                var merges = 0;
                foreach (var parentKey in mergeCandidateParents)
                {
                    if (merges >= AutoLodMergesPerFrame)
                    {
                        break;
                    }
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
                    var unionGroundDistance = NearestGroundDistance(unionLocalBounds, cameraDirection);
                    // Folded for detail only when what the merge makes (one zoom coarser, or the baseline) is itself still
                    // twice as fine as needed: the first refinement jumps from the 28 km baseline to zoom 7, and judging
                    // by the children alone folded that straight back, split and merge in turn.
                    var unionTarget = PatchTargetMetersPerPixel(targetMetersPerPixel, altitude, unionGroundDistance);
                    var mergedSource = ComputeMergedPatchTarget(faceNormal, mergedMinU, mergedMaxU, mergedMinV, mergedMaxV, childLeaves).SourceMetersPerPixel
                        ?? _baselineMetersPerPixel;
                    if (unionGroundDistance > deactivationRadius || (mergedSource.HasValue && mergedSource.Value * 2.0 <= unionTarget))
                    {
                        merges++;
                        TryMergeSiblingGroup(parentKey);
                    }
                }
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        // The tallest ground the horizon test allows for, so a peak just past the camera's horizon still shows over it.
        private const float HorizonPeakMeters = 9000.0f;

        /// <summary>
        /// Once a frame: hides the patches wholly past the horizon (allowing for the tallest mountain), so near the
        /// ground the far side of the planet is neither drawn nor shadowed. Skipped for a frame while the rebuild lock is busy.
        /// </summary>
        public void UpdateHorizonCulling(CameraComponent camera)
        {
            if (_retired || !Monitor.TryEnter(_rebuildLock))
            {
                return;
            }
            try
            {
                if (!Matrix4x4.Invert(WorldTransform, out var worldToLocal))
                {
                    return;
                }
                var cameraLocal = Vector3.Transform(camera.Owner.Position, worldToLocal);
                var distance = cameraLocal.Length();
                var cameraDirection = cameraLocal / MathF.Max(distance, 1e-6f);
                // Inside the sea-level sphere everything may be in view (a valley, a test camera at the centre).
                var limit = MathF.PI;
                if (distance > Radius)
                {
                    var peakRadius = Radius + HorizonPeakMeters * MathF.Max(ComputeHeightScale(), 1e-6f);
                    limit = MathF.Acos(Radius / distance) + MathF.Acos(Radius / peakRadius);
                }
                foreach (var leaf in _leaves.Values)
                {
                    var bounds = leaf.LocalBoundsSphere;
                    var angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(bounds.LocalCenter), cameraDirection), -1.0f, 1.0f));
                    var visible = angle - bounds.LocalRadius / Radius <= limit;
                    if (leaf.Renderer.IsVisible != visible)
                    {
                        leaf.Renderer.IsVisible = visible;
                    }
                }
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        /// <summary>Updates ocean visibility and wave detail once per frame on the main thread.</summary>
        public void UpdateNearOcean(CameraComponent camera)
        {
            if (!_retired)
            {
                _ocean.Update(camera);
            }
        }

        // Arc-length distance (in world units) between a patch's center and the point directly below
        // the camera, both expressed purely as directions from the planet's local origin -- this only
        // needs the ANGLE between them (great-circle geometry), never an actual lat/long conversion, so
        // it sidesteps pole/antimeridian degeneracies entirely.
        // How fine the whole-globe baseline is, from the tiles that set the starting patches: a patch folded back to it is
        // that fine. Left unknown it read as "not loaded yet, leave it alone" to auto-LOD and never refined again.
        private double? _baselineMetersPerPixel;

        // The terrain sample spacing a patch this far away needs: a sample every few pixels at its own distance (one a
        // pixel straight down meant 769-vertex patches all the way to the horizon), and coarser again seen at a slant --
        // toward the horizon a pixel spans `reach` times more ground in depth than across; half of that (its square
        // root), as silhouettes there still want their height detail.
        private static float PatchTargetMetersPerPixel(float targetMetersPerPixel, float altitude, float groundDistance)
        {
            var reach = MathF.Sqrt(altitude * altitude + groundDistance * groundDistance) / MathF.Max(0.001f, altitude);
            return targetMetersPerPixel * reach * MathF.Sqrt(reach) * AutoLodPixelsPerSample;
        }

        // Finer than needed by more than a merge undoes (one level, twice as coarse) and then twice again, so a patch
        // folded back is still well inside what its distance needs and does not split straight away.
        private static bool IsOverDetailed(double? sourceMetersPerPixel, float targetMetersPerPixel)
        {
            return sourceMetersPerPixel.HasValue && sourceMetersPerPixel.Value * 4.0 <= targetMetersPerPixel;
        }

        // To the patch's nearest edge, not its centre: a camera dropped low over a big patch is inside it, while its
        // centre can lie several activation radii away -- nothing near the camera ever refined.
        private float NearestGroundDistance(PlanetPatchGeometry.PatchBoundsSphere bounds, Vector3 cameraDirection)
        {
            return MathF.Max(0.0f, GreatCircleDistanceFromCameraDirection(bounds.LocalCenter, cameraDirection) - bounds.LocalRadius);
        }

        private float GreatCircleDistanceFromCameraDirection(Vector3 patchLocalCenter, Vector3 cameraDirection)
        {
            var patchDirection = Vector3.Normalize(patchLocalCenter);
            var cosAngle = Math.Clamp(Vector3.Dot(patchDirection, cameraDirection), -1f, 1f);
            return MathF.Acos(cosAngle) * Radius;
        }

        // Driven by an explicit external request (e.g. a place search "go here and load full detail"),
        // not by camera proximity like UpdateAutoLod -- repeatedly up-reses/splits whichever leaf
        // currently covers the target lat/long, one step per UpdateDetailChase call, until it reaches
        // TargetZoom or genuinely can't progress (undersampling/failure), at which point it clears itself.
        private (float Longitude, float Latitude, int TargetZoom)? _pendingDetailChase;

        public void RequestDetailAt(float longitudeDegrees, float latitudeDegrees, int targetZoom = UpresMaxZoom)
        {
            if (_retired)
            {
                return;
            }
            _pendingDetailChase = (longitudeDegrees, latitudeDegrees, Math.Clamp(targetZoom, InitialTerrariumUpresZoom, UpresMaxZoom));
        }

        /// <summary>
        /// Advances any pending RequestDetailAt chase by one step. Call once per frame from the main
        /// thread, alongside ProcessPendingSplits/ProcessPendingMerges -- cheap no-op when nothing's
        /// pending.
        /// </summary>
        public void UpdateDetailChase()
        {
            if (_retired)
            {
                return;
            }
            if (_pendingDetailChase is not { } chase)
            {
                return;
            }

            if (!Monitor.TryEnter(_rebuildLock))
            {
                return;
            }

            try
            {
                if (!TryFindNearestLeafPatchKey(chase.Longitude, chase.Latitude, out var key))
                {
                    _pendingDetailChase = null;
                    return;
                }

                var status = GetPatchUpresStatus(key);
                if (status == PatchUpresStatus.InProgress || _splitPending.ContainsKey(key))
                {
                    return;
                }

                if (status == PatchUpresStatus.Failed)
                {
                    Console.WriteLine($"[Terrarium] Detail chase stopped at {PatchName(key)}: up-res failed.");
                    _pendingDetailChase = null;
                    return;
                }

                var currentZoom = GetPatchDetailInfo(key)?.Zoom;
                if (currentZoom.HasValue && currentZoom.Value >= chase.TargetZoom)
                {
                    _pendingDetailChase = null;
                    return;
                }

                if (!currentZoom.HasValue)
                {
                    TryBeginPatchUpres(key);
                    return;
                }

                if (CanSplitPatch(key))
                {
                    TrySplitPatch(key);
                    return;
                }

                Console.WriteLine($"[Terrarium] Detail chase stopped at {PatchName(key, currentZoom)}: z{currentZoom} is the finest zoom this area's source data supports.");
                _pendingDetailChase = null;
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        // Finds whichever current leaf's center direction is closest to the target lat/long -- since
        // leaves tessellate the sphere without gaps, "nearest center" reliably resolves to the leaf
        // actually containing the point (or, right at a boundary, an immediate neighbor -- an acceptable
        // approximation here, the same precision tradeoff TileOverlapsPatch's own corner-sampling
        // already accepts elsewhere in this file). Caller must already hold _rebuildLock.
        private bool TryFindNearestLeafPatchKey(float longitudeDegrees, float latitudeDegrees, out PatchKey key)
        {
            var direction = new LatLong(longitudeDegrees, latitudeDegrees).ToUnitSphere();

            key = default;
            var found = false;
            var bestDot = float.NegativeInfinity;

            foreach (var (candidateKey, leaf) in _leaves)
            {
                var leafDirection = Vector3.Normalize(leaf.LocalBoundsSphere.LocalCenter);
                var dot = Vector3.Dot(direction, leafDirection);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    key = candidateKey;
                    found = true;
                }
            }

            return found;
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
                ActiveTileFetches: PlanetTileStream.Shared.ActiveFetches,
                MaxConcurrentTileFetches: PlanetTileStream.Shared.MaxConcurrentFetches);
        }

        /// <summary>
        /// Renderer components for every leaf currently doing background streaming work -- in-place
        /// up-res or splitting (both InProgress in _upresStatus), or merging (still-visible children in
        /// _mergePending) -- for a caller-side "highlight what's actively working" overlay. Component-
        /// grained (not node-grained), since several unrelated patches can share one root node. Safe to
        /// call without _rebuildLock, same as GetStreamingStatus.
        /// </summary>
        public IReadOnlyList<MeshRendererComponent> GetStreamingActivityComponents()
        {
            var renderers = new List<MeshRendererComponent>();

            foreach (var (key, status) in _upresStatus)
            {
                if (status == PatchUpresStatus.InProgress && _leaves.TryGetValue(key, out var leaf))
                {
                    renderers.Add(leaf.Renderer);
                }
            }

            foreach (var childKey in _mergePending.Keys)
            {
                if (_leaves.TryGetValue(childKey, out var leaf))
                {
                    renderers.Add(leaf.Renderer);
                }
            }

            return renderers;
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
        /// whole-globe baseline (Zoom null) rather than a Terrarium up-res. Paths aren't guaranteed
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

        private void SchedulePatchUpres(PatchKey key, PatchLeaf leaf, PatchUpresPlan plan) =>
            _ = _rebuilds.RunAsync("Planet patch up-res", () => UpresPatchAsync(key, leaf.Mesh, plan),
                _ => QueuePatchFailure(key, leaf.Mesh));

        private void QueuePatchFailure(PatchKey key, PlanetPatchMesh patch) =>
            _rebuilds.Enqueue(PlanetRebuildKind.Upres, () =>
            {
                if (_leaves.TryGetValue(key, out var current) && ReferenceEquals(current.Mesh, patch))
                {
                    _upresStatus[key] = PatchUpresStatus.Failed;
                }
            });

        private async Task UpresPatchAsync(PatchKey key, PlanetPatchMesh patch, PatchUpresPlan plan)
        {
            if (_retired)
            {
                return;
            }
            var fetched = await FetchTilesForPatchAsync(key, plan);
            if (_retired)
            {
                return;
            }
            if (fetched is not { Count: > 0 })
            {
                Console.WriteLine($"[Terrarium] Up-res {PatchName(key, plan.Zoom, plan.MeshResolution, plan.SourceResolution, plan.SourceMetersPerPixel)}: no tiles fetched successfully, leaving patch as-is.");
                QueuePatchFailure(key, patch);
                return;
            }

            var precomputedHeights = await PlanetHeightSampler.Shared.TrySampleAsync(
                Context, patch.FaceNormal, patch.MinU, patch.MaxU, patch.MinV, patch.MaxV,
                plan.MeshResolution, fetched, plan.TileIndices.Count, () => !_retired);

            lock (_rebuildLock)
            {
                if (_retired)
                {
                    return;
                }
                foreach (var tile in fetched)
                {
                    HeightLibrary!.AddTile(tile);
                }

                ApplyHeightData(patch, HeightLibrary!, plan.MeshResolution, precomputedHeights);
                if (_leaves.TryGetValue(key, out var builtLeaf) && ReferenceEquals(builtLeaf.Mesh, patch))
                {
                    StitchAroundLocked(key);
                }

                _rebuilds.Enqueue(PlanetRebuildKind.Upres, () =>
                {
                    if (_leaves.TryGetValue(key, out var leaf) && ReferenceEquals(leaf.Mesh, patch))
                    {
                        _upresStatus[key] = PatchUpresStatus.Done;
                        leaf.Zoom = plan.Zoom;
                        leaf.SourceResolution = plan.SourceResolution;
                        leaf.SourceMetersPerPixel = plan.SourceMetersPerPixel;
                        leaf.Renderer.Name = PatchName(key, plan.Zoom, patch.Resolution, plan.SourceResolution, plan.SourceMetersPerPixel);
                        _patchDetails[key] = new PatchDetailInfo(plan.Zoom, patch.Resolution, plan.SourceResolution, plan.SourceMetersPerPixel);
                        EnqueuePatchSnapshotLocked(key);
                    }
                });
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

        private static Task<List<TerrariumHeightTile>> FetchTilesAsync(IReadOnlyList<TerrariumTileKey> tileKeys) =>
            PlanetTileStream.Shared.FetchAsync(tileKeys);

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
            var visibleHeightMeters = 2f * realWorldAltitudeMeters * MathF.Tan(camera.EffectiveFieldOfViewDegrees.ToRadians() * 0.5f);
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

        private volatile string? _imageryPath;

        /// <summary>The equirectangular picture the surface wears (a PlanetImagery script's), or null for biome colours.</summary>
        public string? ImageryPath => _requestedImageryPath;

        /// <summary>
        /// Dresses every patch, and every one made from now on, in <paramref name="path"/>'s picture. Patches already
        /// built are re-dressed in place one at a time on a worker, each under the rebuild lock for only its own few
        /// milliseconds: done all at once on the main thread it froze the first frames for ten seconds.
        /// </summary>
        public void SetImagery(string? path)
        {
            // Only noted here: the rebuild lock can be held for seconds while heights load, and the main thread must not wait.
            if (_retired || string.Equals(_requestedImageryPath, path, StringComparison.Ordinal))
            {
                return;
            }

            _requestedImageryPath = path;
            var generation = Interlocked.Increment(ref _imageryGeneration);
            Task.Run(() =>
            {
                PatchLeaf[] leaves;
                lock (_rebuildLock)
                {
                    if (_retired || generation != Volatile.Read(ref _imageryGeneration))
                    {
                        return;
                    }

                    _imageryPath = path;
                    leaves = _leaves.Values.ToArray();
                }

                foreach (var leaf in leaves)
                {
                    lock (_rebuildLock)
                    {
                        // A newer picture (or none) was asked for meanwhile; it carries on from here.
                        if (_retired || generation != Volatile.Read(ref _imageryGeneration))
                        {
                            return;
                        }

                        if (string.Equals(leaf.Mesh.ImageryPath, path, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // Taking a picture off brings the biome colours back, which only a regeneration can.
                        if (path == null || !leaf.Mesh.TryDressInPlace(path))
                        {
                            leaf.Mesh.SetImagery(path);
                        }
                    }
                }
            });
        }

        private volatile string? _requestedImageryPath;
        private int _imageryGeneration;

        private bool IsLeafMesh(PlanetPatchMesh mesh)
        {
            foreach (var leaf in _leaves.Values)
            {
                if (ReferenceEquals(leaf.Mesh, mesh))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The patch drawn below a world point: its height data's zoom and spacing, its mesh's vertex spacing, the picture
        /// it wears and, when its mesh is built, the drawn ground's distance from the planet's centre (planet units).
        /// </summary>
        public readonly record struct PatchSurvey(int? Zoom, double? HeightMetresPerSample, float VertexMetres, PlanetImageryDetail? Detail,
            double? GroundRadius = null);

        /// <summary>What the ground straight below <paramref name="worldPosition"/> is drawn from; null while the rebuild lock is busy.</summary>
        public PatchSurvey? SurveyBelow(Vector3 worldPosition)
        {
            if (_retired || !Matrix4x4.Invert(WorldTransform, out var worldToLocal)
                || !CubeSphere.TryUnwarpSphereToFaceUv(Vector3.Transform(worldPosition, worldToLocal), out var face, out var u, out var v)
                || !Monitor.TryEnter(_rebuildLock))
            {
                return null;
            }
            try
            {
                var normal = CubeSphere.FaceNormals[face];
                foreach (var leaf in _leaves.Values)
                {
                    var mesh = leaf.Mesh;
                    if (mesh.FaceNormal != normal || u < mesh.MinU || u > mesh.MaxU || v < mesh.MinV || v > mesh.MaxV)
                    {
                        continue;
                    }
                    // Across the patch's bounding sphere is ~1.41 times its side.
                    var side = leaf.LocalBoundsSphere.LocalRadius * 2.0f / MathF.Sqrt(2.0f);
                    var vertexMetres = side / MathF.Max(1, mesh.Resolution - 1) * (PlanetLoader.EarthRadiusMeters / Radius);
                    return new PatchSurvey(leaf.Zoom, leaf.SourceMetersPerPixel ?? _baselineMetersPerPixel, vertexMetres, mesh.Detail,
                        GroundRadius(mesh, u, v));
                }
                return null;
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        // The drawn grid's radius at (u, v), bilinear between its four vertices (the grid is even in u and v).
        internal static double? GroundRadius(PlanetPatchMesh mesh, float u, float v)
        {
            var n = mesh.Resolution;
            if (n < 2 || mesh.CpuData?.Positions is not { } positions || positions.Length < n * n)
            {
                return null;
            }
            var fx = Math.Clamp((u - mesh.MinU) / (mesh.MaxU - mesh.MinU), 0.0f, 1.0f) * (n - 1);
            var fy = Math.Clamp((v - mesh.MinV) / (mesh.MaxV - mesh.MinV), 0.0f, 1.0f) * (n - 1);
            var x = Math.Min((int)fx, n - 2);
            var y = Math.Min((int)fy, n - 2);
            var tx = fx - x;
            var ty = fy - y;
            var top = double.Lerp(mesh.PlanetPosition(positions[y * n + x]).Length(), mesh.PlanetPosition(positions[y * n + x + 1]).Length(), tx);
            var bottom = double.Lerp(mesh.PlanetPosition(positions[(y + 1) * n + x]).Length(), mesh.PlanetPosition(positions[(y + 1) * n + x + 1]).Length(), tx);
            return double.Lerp(top, bottom, ty);
        }

        // Intersect the drawn triangle, including the chord across the sphere at coarse terrain LODs.
        internal static double? SurfaceRadius(PlanetPatchMesh mesh, float u, float v, ManaCore.Graphics.ManaPosition direction)
        {
            var n = mesh.Resolution;
            if (n < 2 || mesh.CpuData?.Positions is not { } points || points.Length < n * n)
            {
                return null;
            }
            var fx = Math.Clamp((u - mesh.MinU) / (mesh.MaxU - mesh.MinU), 0.0f, 1.0f) * (n - 1);
            var fy = Math.Clamp((v - mesh.MinV) / (mesh.MaxV - mesh.MinV), 0.0f, 1.0f) * (n - 1);
            var x = Math.Min((int)fx, n - 2);
            var y = Math.Min((int)fy, n - 2);
            var a = points[y * n + x];
            var diagonal = points[(y + 1) * n + x + 1] - a;
            var other = points[(y + 1) * n + x] - a;
            if (fx - x >= fy - y)
            {
                other = points[y * n + x + 1] - a;
            }
            var normal = new ManaCore.Graphics.ManaPosition(Vector3.Cross(other, diagonal));
            var denominator = ManaCore.Graphics.ManaPosition.Dot(normal, direction);
            if (Math.Abs(denominator) < 1e-12)
            {
                return null;
            }
            return ManaCore.Graphics.ManaPosition.Dot(normal, mesh.PlanetPosition(a)) / denominator;
        }

        /// <summary>The patches drawn now, or null while the rebuild lock is busy (try again next frame).</summary>
        public PlanetPatchMesh[]? TryGetLeafMeshes()
        {
            if (_retired || !Monitor.TryEnter(_rebuildLock))
            {
                return null;
            }
            try
            {
                var meshes = new PlanetPatchMesh[_leaves.Count];
                var index = 0;
                foreach (var leaf in _leaves.Values)
                {
                    meshes[index++] = leaf.Mesh;
                }
                return meshes;
            }
            finally
            {
                Monitor.Exit(_rebuildLock);
            }
        }

        /// <summary>
        /// Puts each patch's sharper picture on (or takes it off) on a worker, a patch at a time under the rebuild lock
        /// as <see cref="SetImagery"/> does; <paramref name="done"/> hears of each patch once it is settled.
        /// </summary>
        public void DressDetail(IReadOnlyList<(PlanetPatchMesh Mesh, PlanetImageryDetail? Detail)> changes, Action<PlanetPatchMesh> done)
        {
            Task.Run(() =>
            {
                foreach (var (mesh, detail) in changes)
                {
                    try
                    {
                        lock (_rebuildLock)
                        {
                            // A patch split or merged away meanwhile is gone; its successors are judged afresh.
                            if (!_retired && mesh.ImageryPath != null && IsLeafMesh(mesh))
                            {
                                mesh.TryDressDetail(detail);
                            }
                        }
                    }
                    finally
                    {
                        done(mesh);
                    }
                }
            });
        }

        private void RebuildAllPatches()
        {
            lock (_rebuildLock)
            {
                if (_retired)
                {
                    return;
                }
                var heightScale = ComputeHeightScale();
                foreach (var leaf in _leaves.Values)
                {
                    leaf.Mesh.Rebuild(heightScale);
                }
                // Every edge moved, so pin again once all are rebuilt.
                foreach (var (key, leaf) in _leaves)
                {
                    StitchLocked(key, leaf);
                }
            }
        }

        private float ComputeHeightScale()
        {
            return (Radius / PlanetLoader.EarthRadiusMeters) * _heightExaggeration;
        }

        // zoom is the Terrarium zoom level currently backing this patch's elevation data (see
        // PatchLeaf.Zoom) -- null for a patch still on the whole-globe baseline, never up-res'd.
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

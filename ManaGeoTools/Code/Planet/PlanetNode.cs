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
        // parent and adds its 4 children in one step.
        private sealed class PatchLeaf
        {
            public required PlanetPatchMesh Mesh { get; init; }
            public required ManaMeshSceneNode Node { get; init; }

            // The Terrarium zoom level currently backing this patch's elevation data, once it's been
            // up-res'd at least once (null for a patch still on the whole-globe ETOPO baseline) --
            // baked into the Node's display Name (see PatchName) so it's visible directly in the
            // Outliner instead of needing to cross-reference the log.
            public int? Zoom { get; set; }
        }

        private readonly Dictionary<PatchKey, PatchLeaf> _leaves = new();
        private readonly Dictionary<ManaSceneNode, PatchKey> _nodeToPatchKey = new();
        // Guarded by _rebuildLock like everything else here -- written from the background upres task,
        // read every frame from the main thread (see VoxelEditorGameMode's selection-outline logic) to
        // color a selected patch's outline by how its up-res is going.
        private readonly Dictionary<PatchKey, PatchUpresStatus> _upresStatus = new();

        // Tracks a split that's fetching its 4 children in the background -- see BeginSplitLocked's doc
        // comment for why this exists (avoiding a visible "downrez then refill" flash).
        private sealed class PendingSplit
        {
            public required PatchKey ParentKey { get; init; }
            public required PatchLeaf ParentLeaf { get; init; }
            public required PatchKey[] ChildKeys { get; init; }
            public required PlanetPatchMesh[] ChildMeshes { get; init; }
            public required bool[] ChildReady { get; init; }
            public required int?[] ChildZoom { get; init; }
        }

        // Parent keys with a split currently in flight -- guards against double-triggering a second
        // split for the same parent before the first one's children finish (see TrySplitPatch).
        private readonly HashSet<PatchKey> _splitPending = new();

        // Splits whose 4 children have all finished fetching, waiting for ProcessPendingSplits to
        // actually swap them into the scene graph on the main thread -- see UpresPendingChildAsync.
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

        // Raised to once a patch is up-res'd (see ApplyHeightDataWithResolution's doc comment) -- the
        // whole-globe default (passed as `resolution` to PlanetNode's constructor, currently 24) is
        // sized for every patch at once; a single on-demand up-res can afford far more density since
        // it's the user opting one patch in specifically, not a whole-planet cost.
        private const int UpresPatchResolution = 160;

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
                        var node = new ManaMeshSceneNode(context, mesh) { Name = PatchName(key) };
                        AddChild(node);
                        _leaves[key] = new PatchLeaf { Mesh = mesh, Node = node };
                        _nodeToPatchKey[node] = key;
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
                foreach (var leaf in _leaves.Values)
                {
                    if (TileOverlapsPatch(tile, leaf.Mesh))
                    {
                        leaf.Mesh.ApplyHeightData(HeightLibrary!);
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
        /// Reaching even finer detail than one up-res gives requires the separate, deliberate
        /// TrySplitPatch -- plain (re)selection alone never cascades into a split, precisely so
        /// casually re-examining a patch can't accidentally trigger one.
        /// </summary>
        public void TryBeginPatchUpres(PatchKey key)
        {
            lock (_rebuildLock)
            {
                if (HeightLibrary == null || !_leaves.TryGetValue(key, out var leaf) || _upresStatus.ContainsKey(key))
                {
                    return;
                }

                BeginPatchUpresLocked(key, leaf.Mesh);
            }
        }

        /// <summary>
        /// Deliberately splits an already up-res'd (Done) patch into 4 smaller patches, each of which
        /// immediately starts its own up-res fetch -- since each child covers 1/4 the area, it can reach
        /// a finer zoom within the same tile budget than the whole parent could, without any one
        /// patch's resolution growing without bound (see BeginSplitLocked). Unlike TryBeginPatchUpres,
        /// this is intentionally NEVER triggered by plain reselection -- only by an explicit user action
        /// (the Inspector's "Split for finer detail" button, shown once a patch reaches Done) --
        /// otherwise a couple of casual re-clicks near the same spot as it splits and its children
        /// finish (fast, since each child fetches far less than the original) can cascade several
        /// levels deep before you notice. No-ops if the patch isn't currently Done (already split away,
        /// still fetching, its up-res failed, or a split for it is already in flight -- see
        /// IsSplitPending).
        /// </summary>
        public void TrySplitPatch(PatchKey key)
        {
            lock (_rebuildLock)
            {
                if (!_leaves.TryGetValue(key, out var leaf) || _splitPending.Contains(key))
                {
                    return;
                }

                var status = _upresStatus.TryGetValue(key, out var existing) ? existing : PatchUpresStatus.NotRequested;
                if (status != PatchUpresStatus.Done)
                {
                    return;
                }

                _splitPending.Add(key);
                BeginSplitLocked(key, leaf);
            }
        }

        /// <summary>Whether key's split (see TrySplitPatch) is currently fetching its 4 children.</summary>
        public bool IsSplitPending(PatchKey key)
        {
            lock (_rebuildLock)
            {
                return _splitPending.Contains(key);
            }
        }

        // Caller must already hold _rebuildLock.
        private void BeginPatchUpresLocked(PatchKey key, PlanetPatchMesh mesh)
        {
            _upresStatus[key] = PatchUpresStatus.InProgress;
            Task.Run(() => UpresPatchAsync(key, mesh));
        }

        // Caller must already hold _rebuildLock. Builds all 4 child meshes now (quadrant = a quarter of
        // the parent's U/V range each) and kicks off each one's own up-res fetch, but touches neither
        // the scene graph nor _leaves yet -- the parent keeps rendering, unchanged, at its current
        // up-res'd resolution until every child has finished (successfully or not; see
        // UpresPendingChildAsync/CompletePendingChild), at which point FinalizeSplit swaps the parent
        // out and all 4 children in together as one step. Building each child fully (construction, then
        // its own ApplyHeightDataWithResolution once fetched) before it's ever added to the scene means
        // the very first frame a child is actually part of the scene graph, it's already at its final
        // resolution -- no visible "downrez then refill" flash where a lower-detail placeholder would
        // otherwise show up front and get replaced moments later.
        //
        // ComputePatchLonLatBounds/TileOverlapsPatch/PlanetPatchMesh's own skirt geometry all read only
        // a mesh's own FaceNormal/MinU/MaxU/MinV/MaxV, never grid coordinates or how the sub-rectangle
        // was derived, so a split child needs no special-casing anywhere else -- it's indistinguishable
        // from an original grid patch to every other part of this class.
        private void BeginSplitLocked(PatchKey parentKey, PatchLeaf parentLeaf)
        {
            var parentMesh = parentLeaf.Mesh;
            var midU = (parentMesh.MinU + parentMesh.MaxU) * 0.5f;
            var midV = (parentMesh.MinV + parentMesh.MaxV) * 0.5f;
            var heightScale = ComputeHeightScale();

            var pending = new PendingSplit
            {
                ParentKey = parentKey,
                ParentLeaf = parentLeaf,
                ChildKeys = new PatchKey[4],
                ChildMeshes = new PlanetPatchMesh[4],
                ChildReady = new bool[4],
                ChildZoom = new int?[4],
            };

            Console.WriteLine($"[Terrarium] Splitting {PatchName(parentKey)}: fetching all 4 children before swapping in (parent stays visible until then).");

            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var quadMinU = (quadrant & 1) == 0 ? parentMesh.MinU : midU;
                var quadMaxU = (quadrant & 1) == 0 ? midU : parentMesh.MaxU;
                var quadMinV = (quadrant & 2) == 0 ? parentMesh.MinV : midV;
                var quadMaxV = (quadrant & 2) == 0 ? midV : parentMesh.MaxV;

                var childKey = parentKey.Child(quadrant);
                var childMesh = new PlanetPatchMesh(
                    Context, parentMesh.FaceNormal, quadMinU, quadMaxU, quadMinV, quadMaxV, Radius, Resolution, heightScale, HeightLibrary);

                pending.ChildKeys[quadrant] = childKey;
                pending.ChildMeshes[quadrant] = childMesh;

                var quadrantCaptured = quadrant;
                Task.Run(() => UpresPendingChildAsync(pending, quadrantCaptured));
            }
        }

        private async Task UpresPendingChildAsync(PendingSplit pending, int quadrant)
        {
            var key = pending.ChildKeys[quadrant];
            var mesh = pending.ChildMeshes[quadrant];

            var fetched = await FetchTilesForPatchAsync(key, mesh);
            if (fetched is { Count: > 0 })
            {
                lock (_rebuildLock)
                {
                    foreach (var tile in fetched)
                    {
                        HeightLibrary!.AddTile(tile);
                    }
                    mesh.ApplyHeightDataWithResolution(HeightLibrary!, UpresPatchResolution);
                    pending.ChildZoom[quadrant] = fetched[0].Zoom;
                }
                Console.WriteLine($"[Terrarium] Up-res {PatchName(key)}: applied {fetched.Count} tile(s) (pending swap-in).");
            }
            else
            {
                Console.WriteLine($"[Terrarium] Up-res {PatchName(key)}: no tiles fetched, will swap in at base resolution.");
            }

            lock (_rebuildLock)
            {
                pending.ChildReady[quadrant] = true;
                if (Array.TrueForAll(pending.ChildReady, ready => ready))
                {
                    // NOT finalized here -- this runs on whichever background thread pool thread
                    // happens to complete the last of the 4 children's fetches, and FinalizeSplit
                    // mutates the scene graph (AddChild/RemoveChild on Children, a plain List<T>) --
                    // doing that from a background thread raced with ManaWorld.CreateRenderScene()'s
                    // every-frame main-thread traversal of that same list ("Collection was modified;
                    // enumeration operation may not execute"). Queue it instead; ProcessPendingSplits
                    // (called once per frame from VoxelEditorGameMode, main thread) drains and finalizes
                    // it there.
                    _readyToFinalize.Enqueue(pending);
                }
            }
        }

        /// <summary>
        /// Finalizes any splits whose 4 children have all finished fetching. MUST be called from the
        /// main thread once per frame (e.g. alongside other per-frame PlanetNode calls in
        /// VoxelEditorGameMode) -- see UpresPendingChildAsync's comment for why finalizing can't safely
        /// happen directly from the background fetch task that detects readiness.
        /// </summary>
        public void ProcessPendingSplits()
        {
            List<PendingSplit>? ready = null;
            lock (_rebuildLock)
            {
                if (_readyToFinalize.Count > 0)
                {
                    ready = new List<PendingSplit>(_readyToFinalize);
                    _readyToFinalize.Clear();
                }
            }

            if (ready == null)
            {
                return;
            }

            lock (_rebuildLock)
            {
                foreach (var pending in ready)
                {
                    FinalizeSplit(pending);
                }
            }
        }

        // Caller must already hold _rebuildLock.
        private void FinalizeSplit(PendingSplit pending)
        {
            RemoveChild(pending.ParentLeaf.Node);
            _leaves.Remove(pending.ParentKey);
            _nodeToPatchKey.Remove(pending.ParentLeaf.Node);
            _upresStatus.Remove(pending.ParentKey);
            _splitPending.Remove(pending.ParentKey);

            for (var quadrant = 0; quadrant < 4; quadrant++)
            {
                var childKey = pending.ChildKeys[quadrant];
                var childMesh = pending.ChildMeshes[quadrant];
                var childZoom = pending.ChildZoom[quadrant];
                var childNode = new ManaMeshSceneNode(Context, childMesh) { Name = PatchName(childKey, childZoom) };
                AddChild(childNode);

                _leaves[childKey] = new PatchLeaf { Mesh = childMesh, Node = childNode, Zoom = childZoom };
                _nodeToPatchKey[childNode] = childKey;
                _upresStatus[childKey] = PatchUpresStatus.Done;
            }

            Console.WriteLine($"[Terrarium] Split {PatchName(pending.ParentKey)} into 4 child patches (all pre-fetched, swapped in together). Total leaves now: {_leaves.Count}.");
        }

        public PatchUpresStatus GetPatchUpresStatus(PatchKey key)
        {
            lock (_rebuildLock)
            {
                return _upresStatus.TryGetValue(key, out var status) ? status : PatchUpresStatus.NotRequested;
            }
        }

        private async Task UpresPatchAsync(PatchKey key, PlanetPatchMesh patch)
        {
            var fetched = await FetchTilesForPatchAsync(key, patch);
            if (fetched is not { Count: > 0 })
            {
                if (fetched == null)
                {
                    Console.WriteLine($"[Terrarium] Patch {PatchName(key)} spans the antimeridian -- skipping up-res (v1 limitation).");
                }
                else
                {
                    Console.WriteLine($"[Terrarium] Up-res {PatchName(key)}: no tiles fetched successfully, leaving patch as-is.");
                }
                lock (_rebuildLock) { _upresStatus[key] = PatchUpresStatus.Failed; }
                return;
            }

            var zoom = fetched[0].Zoom;
            lock (_rebuildLock)
            {
                foreach (var tile in fetched)
                {
                    HeightLibrary!.AddTile(tile);
                }
                patch.ApplyHeightDataWithResolution(HeightLibrary!, UpresPatchResolution);
                _upresStatus[key] = PatchUpresStatus.Done;
                if (_leaves.TryGetValue(key, out var leaf))
                {
                    leaf.Zoom = zoom;
                    leaf.Node.Name = PatchName(key, zoom);
                }
            }

            Console.WriteLine($"[Terrarium] Up-res {PatchName(key, zoom)}: applied {fetched.Count} tile(s).");
        }

        // Shared by UpresPatchAsync (standalone up-res) and UpresPendingChildAsync (a split's child):
        // picks a zoom (SlippyMapMath.PickZoom) and fetches every tile it needs with bounded concurrency
        // -- a large patch can need hundreds of tiles (see UpresMaxTiles), and fetching those one at a
        // time would take an impractically long time for what's meant to be a fairly quick action.
        // Returns null for the (rare) antimeridian-spanning-bbox case PickZoom itself declines to
        // handle; otherwise the list of successfully fetched tiles, which the caller may find empty if
        // every individual fetch failed.
        private static async Task<List<TerrariumHeightTile>?> FetchTilesForPatchAsync(PatchKey key, PlanetPatchMesh patch)
        {
            var (lonMin, lonMax, latMin, latMax) = ComputePatchLonLatBounds(patch);
            var zoom = SlippyMapMath.PickZoom(lonMin, lonMax, latMin, latMax, UpresMaxTiles, UpresMaxZoom);
            if (zoom == null)
            {
                return null;
            }

            var tileIndices = SlippyMapMath.EnumerateTiles(lonMin, lonMax, latMin, latMax, zoom.Value).ToList();
            Console.WriteLine($"[Terrarium] Up-res {PatchName(key)}: fetching {tileIndices.Count} tile(s) @ zoom {zoom.Value}.");

            using var throttle = new SemaphoreSlim(UpresMaxConcurrentFetches);
            var fetchTasks = tileIndices.Select(async index =>
            {
                await throttle.WaitAsync();
                try
                {
                    return await TerrariumTileFetcher.FetchTileAsync(zoom.Value, index.X, index.Y);
                }
                finally
                {
                    throttle.Release();
                }
            });
            return (await Task.WhenAll(fetchTasks)).Where(tile => tile != null).Select(tile => tile!).ToList();
        }

        // Same corner+center sampling pattern TileOverlapsPatch uses below, plus edge midpoints for a
        // bit more margin -- this drives which Terrarium tiles get fetched, so erring slightly generous
        // (a few extra tiles, still bounded by UpresMaxTiles) is preferable to missing coverage at a
        // patch's edge.
        private static (float LonMin, float LonMax, float LatMin, float LatMax) ComputePatchLonLatBounds(PlanetPatchMesh patch)
        {
            var axisA = new Vector3(patch.FaceNormal.Y, patch.FaceNormal.Z, patch.FaceNormal.X);
            var axisB = Vector3.Cross(patch.FaceNormal, axisA);
            var midU = (patch.MinU + patch.MaxU) * 0.5f;
            var midV = (patch.MinV + patch.MaxV) * 0.5f;

            var sampleUVs = new[]
            {
                (patch.MinU, patch.MinV), (patch.MaxU, patch.MinV),
                (patch.MinU, patch.MaxV), (patch.MaxU, patch.MaxV),
                (midU, patch.MinV), (midU, patch.MaxV),
                (patch.MinU, midV), (patch.MaxU, midV),
                (midU, midV),
            };

            var lonMin = float.MaxValue;
            var lonMax = float.MinValue;
            var latMin = float.MaxValue;
            var latMax = float.MinValue;

            foreach (var (u, v) in sampleUVs)
            {
                var pointOnUnitCube = patch.FaceNormal + (u * axisA) + (v * axisB);
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
        // Appended to the name so the current detail level is visible directly in the Outliner, since
        // the split-path suffix alone (e.g. "_10233133") says how deep a patch was split but nothing
        // about how much real detail it actually has.
        private static string PatchName(PatchKey key, int? zoom = null)
        {
            var name = $"Patch_F{key.Face}_{key.Row}_{key.Col}";
            if (!key.SplitPath.IsEmpty)
            {
                name += $"_{string.Join("", key.SplitPath)}";
            }
            if (zoom.HasValue)
            {
                name += $"_z{zoom.Value}";
            }
            return name;
        }
    }
}

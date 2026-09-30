using System.Diagnostics;
using Mana;
using Mana.Code.Nodes;
using ManaCore.Diagnostics;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Kml;
using ManaGeoTools.Code.Planet;
using ManaGeoTools.Code.Region;
using ManaGraphics.Code.Diagnostics;
using ManaVoxel.Code.UI;

namespace Mana.Code.GameModes
{
    /// <summary>
    /// Everything geographic the editor hosts -- a loaded level's planet, the true-scale region the search
    /// panel can load, and the optional KML airspace overlay -- and the per-frame streaming that keeps them
    /// fed. Split out of <see cref="VoxelEditorGameMode"/>, which owns one of these, calls it each frame, and
    /// otherwise only reads <see cref="Planet"/> to pick and up-res its patches.
    /// </summary>
    public sealed class EditorPlanetScenery
    {
        private readonly ManaContext _context;
        private readonly VoxelEditorViewModel _viewModel;

        public EditorPlanetScenery(ManaContext context, VoxelEditorViewModel viewModel)
        {
            _context = context;
            _viewModel = viewModel;
        }

        /// <summary>The loaded level's planet, if it has one.</summary>
        public PlanetNode? Planet { get; private set; }

        /// <summary>The true-scale region last loaded from search, if any.</summary>
        public RegionNode? Region { get; private set; }

        private ManaSceneNode? _kmlDemo;
        private bool _kmlAirspaceAttached;
        private Task<ManaSceneNode?>? _kmlAirspaceLoadTask;

        /// <summary>Only checks the file is there: the overlay is built the first time it is switched on.</summary>
        public void ProbeKmlAirspace() => _viewModel.KmlAirspaceAvailable = File.Exists(KmlDemoFilePath);

        /// <summary>A level is being unloaded: let go of its planet and the overlays draped on it.</summary>
        public void Reset()
        {
            DetachKmlAirspace();
            Planet = null;
            lock (_drapedOverlays)
            {
                _drapedOverlays.Clear();
            }
        }

        /// <summary>A level brought a planet: adopt it, and put the airspace on it if that is switched on.</summary>
        public void Adopt(PlanetNode? planet)
        {
            Planet = planet;
            if (Planet != null && _viewModel.ShowKmlAirspace)
            {
                AttachKmlAirspace();
            }
        }

        /// <summary>The planet's streaming figures, for the World Perf panel.</summary>
        public void PushStreamingStatus()
        {
            var world = _context.World;
            if (Planet == null)
            {
                return;
            }

            world.RecordFrameStatText("Planet snapshot queue", $"{Planet.PendingChangedPatchCount} pending");

            // Surfaced next to the heap figure because the two are related: this cache never evicts by design
            // (merges rely on a child's fetched tiles still being there), so it is the standing explanation for
            // a heap that grows the longer you fly.
            var tileCount = Planet.HeightLibrary?.Tiles.Count ?? 0;
            world.RecordFrameStatText("Height tiles cached", $"{tileCount} (never evicted)");

            var status = Planet.GetStreamingStatus();
            world.RecordFrameStatText("Planet patches resident", status.TotalLeaves.ToString());
            world.RecordFrameStatText("Planet up-res in progress", status.PatchesUpresInProgress.ToString());
            world.RecordFrameStatText("Planet splits in progress", status.PatchesSplitting.ToString());
            world.RecordFrameStatText("Planet merges in progress", status.PatchesMerging.ToString());
            world.RecordFrameStatText("Planet tile fetches active", $"{status.ActiveTileFetches}/{status.MaxConcurrentTileFetches}");
        }

        // Local convenience path to real-world KML airspace data (AirspaceConverter output, sourced
        // externally, ~10.5MB) — not a repo asset, degrades to a no-op via the File.Exists guard on
        // any machine without it. The KML geometry layer renders LineString/LinearRing/Polygon data
        // with ManaLineMesh, so line-like airspace boundaries draw directly instead of relying on
        // polygon triangulation.
        // MANA_KML_AIRSPACE when set, else a ManaAirSpace checkout beside this repo -- not a fixed C:\Users path,
        // which only ever existed on one machine.
        private static readonly string KmlDemoFilePath = Environment.GetEnvironmentVariable("MANA_KML_AIRSPACE")
            ?? Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "ManaAirSpace", "AirSpaceModels", "UnitedStates.xml"));

        // Where a loaded RegionNode's center sits in world space. A region is built at true scale (1 unit
        // == 1 meter), so even the smallest offered size is a 1km square -- four times the 256-unit voxel
        // grid on a side. Parked well clear to the west of everything else in this scene (grid at
        // 0..256, planet centered at 200,60,200 with radius 40) so the largest offered region still can't
        // reach any of it.
        private static readonly Vector3 RegionWorldOrigin = new(-3000.0f, 0.0f, 0.0f);

        // Camera arrival framing, both as multiples of the region's half-extent so every offered size is
        // framed the same way: back off far enough to take the whole box in, from an eye height where
        // relief still reads as relief rather than as a flat map.
        private const float RegionArrivalDistanceFactor = 3.0f;
        private const float RegionArrivalHeightFactor = 0.5f;

        // The camera's default 1000-unit far clip was sized for this scene's original contents (a
        // 256-unit voxel grid, a radius-40 planet). A region is true-scale, so even the smallest offered
        // one is a 1km box viewed from ~1.5km back -- entirely beyond that plane. Widened to fit whenever
        // a region loads, and restored from this saved value if a later region needs less.
        //
        // The near plane moves with it. Pushing far out alone would leave a near:far ratio of over a
        // million at the largest region size, which is well past what a 24-bit depth buffer can resolve
        // and shows up as z-fighting across flat terrain. Scaling both keeps the ratio fixed at a
        // comfortable ~10,000:1 regardless of region size.
        private const float RegionNearClipDivisor = 2000.0f;
        private float? _farClipBeforeRegion;
        private float? _nearClipBeforeRegion;

        // Consumes the "load this place as a true-scale region" request the search panel queues (see
        // VoxelSearchPanelView / RegionLoadRequest). Main thread only: this swaps scene-graph children.
        public void ProcessPendingRegionRequest()
        {
            var request = _viewModel.PendingRegionRequest;
            if (request == null)
            {
                return;
            }

            _viewModel.PendingRegionRequest = null;

            try
            {
                lock (_context.World.SceneLock)
                {
                    // One region at a time -- these are large true-scale meshes and there's no placement
                    // scheme for a second one, so a new request replaces the old rather than stacking up.
                    if (Region != null)
                    {
                        _context.World.DefaultScene.Children.Remove(Region);
                    }

                    Region = new RegionNode(
                        _context,
                        request.PlaceName,
                        new LatLong(request.Longitude, request.Latitude),
                        request.HalfExtentMeters,
                        zoom: request.Zoom,
                        resolution: null,
                        hexRadiusMeters: request.HexRadiusMeters,
                        stepStrength: request.StepStrength);
                    Region.Position = RegionWorldOrigin;
                    Region.UpdateTransform();
                    _context.World.DefaultScene.Children.Add(Region);
                }

                var camera = _context.World.MainCamera;
                if (camera != null)
                {
                    _farClipBeforeRegion ??= camera.FarClipPlane;
                    _nearClipBeforeRegion ??= camera.NearClipPlane;
                    camera.FarClipPlane = MathF.Max(
                        _farClipBeforeRegion.Value,
                        request.HalfExtentMeters * (RegionArrivalDistanceFactor + 2.0f));
                    camera.NearClipPlane = MathF.Max(
                        _nearClipBeforeRegion.Value,
                        request.HalfExtentMeters / RegionNearClipDivisor);

                    // Required: NearClipPlane/FarClipPlane are plain fields, and the projection matrix is
                    // only rebuilt on demand (constructor, window resize, or an Inspector edit -- see
                    // CameraComponentImGuiView, which calls this after every clip-plane drag). Without it
                    // the widened range never reaches the matrix and the region stays clipped away
                    // entirely, until some unrelated edit happens to rebuild the projection.
                    camera.UpdateProjection();

                    // Flown to immediately, while the node is still an empty placeholder -- the terrain
                    // pops in underneath the camera a moment later via ProcessPendingLoad. Better than
                    // waiting on the fetch and leaving the user staring at the planet with no feedback.
                    // Aimed above the center rather than at it so a horizontally-oriented camera ends up
                    // looking down at the terrain instead of standing inside it.
                    var arrivalTarget = RegionWorldOrigin + new Vector3(0.0f, request.HalfExtentMeters * RegionArrivalHeightFactor, 0.0f);
                    camera.FlyToWorldPosition(arrivalTarget, request.HalfExtentMeters * RegionArrivalDistanceFactor);
                }

                ManaConsole.WriteDiagnostic($"Loading region '{request.PlaceName}': {Region.DescribeStatus()}");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to start region load: {ex}");
                ManaConsole.WriteDiagnostic($"Failed to start region load: {ex}");
            }
        }

        // Parses the KML and builds its scene node WITHOUT attaching it to anything -- runs on a
        // background thread (see SyncKmlAirspaceVisibility), so it must not touch the live scene graph.
        // Returns null if the load failed; the caller turns that into "unavailable".
        private ManaSceneNode? BuildKmlAirspaceNode()
        {
            try
            {
                // Same "shrink ratio" reasoning as the planet, and the same radius, so the two read
                // as the same-scale world: real Earth radius/altitude scaled down to fit this scene.
                const float radius = 40.0f;
                var altitudeScale = radius / PlanetLoader.EarthRadiusMeters;
                var kml = new Kml(KmlDemoFilePath, radius, altitudeScale);

                // Still one draw call per rendered KML mesh with no culling/LOD/batching, so revisit
                // if airspace overlays ever become a frame bottleneck.
                var node = KmlSceneBuilder.BuildPlacemarks(_context, kml);
                node.Name = "US Airspace (KML)";
                return node;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Failed to load demo KML: {ex}");
                ManaConsole.WriteDiagnostic($"Failed to load demo KML: {ex}");
                return null;
            }
        }

        // Main thread only. Drives the whole lifecycle of the View > Show Airspace (KML) toggle: kicks off
        // the lazy first load, adopts the finished node, and attaches/detaches it on every later toggle.
        // Attachment is kept on the main thread (rather than done by the loading task itself) because the
        // node parents under Planet, whose children are concurrently rewritten by ProcessPendingSplits/
        // ProcessPendingMerges -- same reasoning as the comment on those calls in Update().
        public void SyncKmlAirspaceVisibility()
        {
            if (!_viewModel.KmlAirspaceAvailable)
            {
                return;
            }

            if (_kmlAirspaceLoadTask != null && _kmlAirspaceLoadTask.IsCompleted)
            {
                // BuildKmlAirspaceNode swallows its own failures into a null return, so a faulted task
                // here means something escaped it entirely -- treat both the same way.
                _kmlDemo = _kmlAirspaceLoadTask.IsCompletedSuccessfully ? _kmlAirspaceLoadTask.Result : null;
                _kmlAirspaceLoadTask = null;
                _viewModel.KmlAirspaceLoading = false;

                if (_kmlDemo == null)
                {
                    // Nothing to show and retrying would just fail again -- retire the menu item rather
                    // than leaving a toggle that silently does nothing.
                    _viewModel.KmlAirspaceAvailable = false;
                    _viewModel.ShowKmlAirspace = false;
                    return;
                }
            }

            if (!_viewModel.ShowKmlAirspace)
            {
                DetachKmlAirspace();
                return;
            }

            if (_kmlDemo == null)
            {
                if (_kmlAirspaceLoadTask == null)
                {
                    _viewModel.KmlAirspaceLoading = true;
                    _kmlAirspaceLoadTask = Task.Run(() =>
                    {
                        if (string.IsNullOrEmpty(System.Threading.Thread.CurrentThread.Name))
                        {
                            System.Threading.Thread.CurrentThread.Name = "KML airspace load";
                        }

                        return BuildKmlAirspaceNode();
                    });
                }

                return;
            }

            AttachKmlAirspace();
        }

        private void AttachKmlAirspace()
        {
            if (_kmlDemo == null || _kmlAirspaceAttached)
            {
                return;
            }

            lock (_context.World.SceneLock)
            {
                // Parented under the planet (not the scene root) so it moves with it — its geometry
                // is already built on the same radius/origin convention as the planet (Kml.EarthRadius
                // == the planet's own radius above), so it needs to sit at the planet's local origin,
                // not an arbitrary world-space offset.
                if (Planet != null)
                {
                    Planet.AddChild(_kmlDemo);
                    _kmlDemo.Position = Vector3.Zero;
                }
                else
                {
                    _kmlDemo.Position = new Vector3(280.0f, 60.0f, 200.0f);
                    _context.World.DefaultScene.Children.Add(_kmlDemo);
                }

                _kmlDemo.UpdateTransform();
            }

            _kmlAirspaceAttached = true;
        }

        private void DetachKmlAirspace()
        {
            if (_kmlDemo == null || !_kmlAirspaceAttached)
            {
                return;
            }

            lock (_context.World.SceneLock)
            {
                // Tracked with an explicit bool rather than inferred from Parent: the scene-root path
                // below is a bare list Add, which (unlike ManaSceneNode.AddChild) never sets Parent.
                if (_kmlDemo.Parent != null)
                {
                    _kmlDemo.Parent.RemoveChild(_kmlDemo);
                }
                else
                {
                    _context.World.DefaultScene.Children.Remove(_kmlDemo);
                }
            }

            _kmlAirspaceAttached = false;
        }

        private readonly List<PlanetDrapedLines> _drapedOverlays = new();

        // Points re-draped per overlay per frame. The work is a batched radius sample plus a vector
        // multiply per point, so this is a small slice of a frame -- measured at well under a millisecond
        // for the whole budget, against 25-75ms for the whole-mesh ribbon rebuild that follows a drain.
        // Generous on purpose for that reason: the points are nearly free and clearing them sooner is what
        // lets the expensive rebuild coalesce into one (see PlanetDrapedLines.RebuildSettleFrames).
        private const int DrapePointsPerFrame = 8192;

        // Every per-frame streaming step that has to happen on the main thread, in dependency order:
        // decide what to refine, then adopt whatever finished, then feed the overlays that read the
        // results. Each step is individually budgeted per frame, so calling this more than once per frame
        // would quietly multiply every one of those budgets -- hence the UpdateFrame guard.
        //
        // Three callers, at most one of which pumps on any given frame: Update always runs, DrawImGui
        // only when ImGui owns the UI, and UpdateEditorPerFrameState only when ManaUI does (see
        // Render's comment on why that third one exists at all).
        private long _lastStreamingPumpFrame = -1;

        public void PumpStreaming(long frame)
        {
            if (_lastStreamingPumpFrame == frame)
            {
                return;
            }

            _lastStreamingPumpFrame = frame;

            // Individually scoped rather than timed as one block: every one of these is a plausible hitch
            // source with a different fix, and the whole point is to find out which.
            using (ManaFrameProfiler.Measure("Streaming: Total"))
            {
                // Must run on the main thread -- finalizing a completed patch split touches the scene
                // graph (AddChild/RemoveChild), which can't safely happen from the background fetch task
                // that detects a split is ready (see PlanetNode.UpresPendingChildrenAsync's comment).
                using (ManaFrameProfiler.Measure("Streaming: AutoLod"))
                {
                    Planet?.UpdateAutoLod(_context.World.MainCamera);
                }

                using (ManaFrameProfiler.Measure("Streaming: NearOcean"))
                {
                    Planet?.UpdateNearOcean(_context.World.MainCamera);
                }

                using (ManaFrameProfiler.Measure("Streaming: DetailChase"))
                {
                    Planet?.UpdateDetailChase();
                }

                using (ManaFrameProfiler.Measure("Streaming: Split Finalize"))
                {
                    Planet?.ProcessPendingSplits();
                }

                using (ManaFrameProfiler.Measure("Streaming: Merge Finalize"))
                {
                    Planet?.ProcessPendingMerges();
                }

                // Same main-thread requirement, for the same reason -- adopts a finished region mesh.
                using (ManaFrameProfiler.Measure("Streaming: Region Adopt"))
                {
                    Region?.ProcessPendingLoad();
                }

                using (ManaFrameProfiler.Measure("Streaming: Overlay Drape"))
                {
                    RedrapePlanetOverlays();
                }
            }
        }

        // Main thread, once per frame from Update. Never blocks: patches the planet is too busy to
        // report on go back on its queue, and draping itself is spread over as many frames as it needs.
        private void RedrapePlanetOverlays()
        {
            if (Planet == null)
            {
                return;
            }

            PlanetDrapedLines[] overlays;
            lock (_drapedOverlays)
            {
                overlays = _drapedOverlays.ToArray();
            }

            // No overlays is the common case (nothing has loaded a border/airspace dataset yet), and the
            // snapshot queue still has to be emptied -- each entry pins a patch's CPU mesh, so leaving it
            // alone grows it for as long as terrain streams.
            if (overlays.Length == 0)
            {
                Planet.DiscardChangedPatches();
                return;
            }

            // Bounded on purpose: QueueChanged is paid in full per snapshot (it scans the whole root
            // patch's point bucket), before the per-point drape budget below gets any say.
            var changed = Planet.DrainChangedPatches();
            if (changed.Count > 0)
            {
                foreach (var overlay in overlays)
                {
                    overlay.QueueChanged(changed);
                }
            }

            // Overlays are told whether the producer still has snapshots queued: a rebuild that fires
            // between two halves of one streaming burst costs the same as one that covers the whole burst.
            var moreChangesInbound = Planet.PendingChangedPatchCount > 0;

            foreach (var overlay in overlays)
            {
                if (overlay.Tick(DrapePointsPerFrame, moreChangesInbound))
                {
                    ManaConsole.WriteDiagnostic(
                        $"[Drape] {overlay.Mesh.Name}: {overlay.LastMovedCount} point(s) moved, " +
                        $"max {overlay.LastMaxDisplacement:0.####} units.");
                    overlay.ResetStats();
                }
            }
        }

    }
}

using Mana.Code.Shapes;
using ManaCore.Diagnostics;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Keeps a ManaLineMesh's points pinned to the planet's rendered surface.
    ///
    /// Each point's cube-face (u, v) and unit direction are fixed for their lifetime -- they come from
    /// lon/lat, which never changes -- so a patch update only re-samples the points inside that patch's
    /// box, and only their radius changes. Points are bucketed by root patch so that test scans one
    /// root's worth rather than all of them.
    ///
    /// Nothing here locks. Heights come from PatchGeometrySnapshot, captured by PlanetNode while it
    /// already held its rebuild lock, so the drape never touches live quadtree state.
    ///
    /// Work is time-sliced: patch changes queue points, and Tick drains a bounded number per frame. A
    /// border dataset is tens of thousands of points and the ribbon rebuild is whole-mesh, so doing it
    /// all in the frame a patch happens to finish is a visible hitch.
    /// </summary>
    public sealed class PlanetDrapedLines
    {
        private readonly struct DrapedPoint
        {
            public required int LineIndex { get; init; }
            public required int PointIndex { get; init; }
            public required int Face { get; init; }
            public required float U { get; init; }
            public required float V { get; init; }

            /// <summary>Precomputed unit-sphere direction. Constant, so the drape never re-runs the trig.</summary>
            public required Vector3 Direction { get; init; }
        }

        private readonly ManaLineMesh _mesh;
        private readonly Vector3[][] _linePoints;
        private readonly float _surfaceOffset;
        private readonly int _patchesPerFaceEdge;
        private readonly Dictionary<(int Face, int Row, int Col), List<DrapedPoint>> _pointsByRoot = new();

        // Points awaiting re-drape, each paired with the snapshot that supplies its height.
        // Main thread only.
        private readonly List<(DrapedPoint Point, PatchGeometrySnapshot Snapshot)> _pending = new();
        private bool _geometryDirty;

        // Frames since a point last moved, and since the ribbon was last rebuilt. Both drive the rebuild
        // coalescing below.
        private int _framesSinceMove;
        private int _framesSinceRebuild;

        // Consecutive frames with no point movement before a rebuild is allowed. The rebuild is whole-mesh
        // and allocates the entire ribbon, so it must not be paid once per streaming batch -- and with the
        // drain bounded (PlanetNode.DrainChangedPatches) a batch is small and frequent, which means "rebuild
        // whenever the queue happens to empty" is very nearly "rebuild every frame".
        private const int RebuildSettleFrames = 8;

        // Backstop: rebuild anyway once this many frames have passed with the geometry dirty, even if points
        // are still moving every frame. Without it, continuous streaming (a long flight) would defer the
        // rebuild indefinitely and the overlay would visibly float off the terrain until the camera stopped.
        private const int ForcedRebuildFrames = 120;

        public ManaLineMesh Mesh => _mesh;

        /// <summary>Points still queued for re-drape.</summary>
        public int PendingPointCount => _pending.Count;

        /// <summary>Largest distance any point moved since the last stats reset, in local units.</summary>
        public float LastMaxDisplacement { get; private set; }

        /// <summary>Points moved since the last stats reset.</summary>
        public int LastMovedCount { get; private set; }

        /// <param name="surfaceOffset">Local units held above the sampled surface, so lines read as sitting on it rather than z-fighting with it.</param>
        public PlanetDrapedLines(
            int patchesPerFaceEdge,
            ManaLineMesh mesh,
            Vector3[][] linePoints,
            IReadOnlyList<IReadOnlyList<(float Longitude, float Latitude)>> lineLatLongs,
            float surfaceOffset)
        {
            _mesh = mesh;
            _linePoints = linePoints;
            _surfaceOffset = surfaceOffset;
            _patchesPerFaceEdge = Math.Max(1, patchesPerFaceEdge);

            var step = 2f / _patchesPerFaceEdge;
            for (var lineIndex = 0; lineIndex < lineLatLongs.Count; lineIndex++)
            {
                var points = lineLatLongs[lineIndex];
                for (var pointIndex = 0; pointIndex < points.Count; pointIndex++)
                {
                    var (longitude, latitude) = points[pointIndex];
                    var direction = LatLong.FromLongitudeAndLatitude(longitude, latitude).ToUnitSphere();
                    if (!CubeSphere.TryUnwarpSphereToFaceUv(direction, out var face, out var u, out var v))
                    {
                        continue;
                    }

                    var col = Math.Clamp((int)((u + 1f) / step), 0, _patchesPerFaceEdge - 1);
                    var row = Math.Clamp((int)((v + 1f) / step), 0, _patchesPerFaceEdge - 1);
                    var rootKey = (face, row, col);
                    if (!_pointsByRoot.TryGetValue(rootKey, out var bucket))
                    {
                        bucket = new List<DrapedPoint>();
                        _pointsByRoot[rootKey] = bucket;
                    }

                    bucket.Add(new DrapedPoint
                    {
                        LineIndex = lineIndex,
                        PointIndex = pointIndex,
                        Face = face,
                        U = u,
                        V = v,
                        Direction = direction
                    });
                }
            }
        }

        /// <summary>
        /// Queues the points covered by each snapshot. Main thread, and cheap -- bounds tests against
        /// data the snapshot already carries. Nothing here can block or fail.
        /// </summary>
        public void QueueChanged(IReadOnlyList<PatchGeometrySnapshot> snapshots)
        {
            using var _ = ManaFrameProfiler.Measure("Drape: Queue Changed");
            foreach (var snapshot in snapshots)
            {
                var key = snapshot.Key;
                if (!_pointsByRoot.TryGetValue((key.Face, key.Row, key.Col), out var bucket))
                {
                    continue;
                }

                foreach (var point in bucket)
                {
                    if (!snapshot.Contains(point.Face, point.U, point.V))
                    {
                        continue;
                    }

                    _pending.Add((point, snapshot));
                }
            }
        }

        /// <summary>
        /// Re-drapes up to pointBudget queued points. Main thread. Returns true if geometry was rebuilt.
        /// <para>
        /// The per-point work is trivial; the whole-mesh ribbon rebuild that follows it is not, so the
        /// rebuild is coalesced rather than run whenever the queue happens to empty -- see
        /// <see cref="RebuildSettleFrames"/>. Pass <paramref name="moreChangesInbound"/> true while the
        /// producer still has snapshots queued, so a rebuild waits for the whole burst instead of firing
        /// between two halves of it.
        /// </para>
        /// </summary>
        public bool Tick(int pointBudget, bool moreChangesInbound = false)
        {
            _framesSinceRebuild++;

            if (_pending.Count == 0)
            {
                _framesSinceMove++;
                return RebuildIfSettled(moreChangesInbound);
            }

            var count = Math.Min(Math.Max(pointBudget, 1), _pending.Count);

            // Scoped to the loop alone, not the method: the rebuild below has its own scope, and nesting
            // one inside the other would double-count it.
            using (ManaFrameProfiler.Measure("Drape: Sample Points"))
            {
                // Taken from the end so removal is O(count), not O(_pending.Count).
                var start = _pending.Count - count;
                for (var i = 0; i < count; i++)
                {
                    var (point, snapshot) = _pending[start + i];
                    if (!snapshot.TrySampleRadius(point.U, point.V, out var radius))
                    {
                        continue;
                    }

                    var updated = point.Direction * (radius + _surfaceOffset);
                    var existing = _linePoints[point.LineIndex][point.PointIndex];
                    var displacement = Vector3.Distance(existing, updated);
                    if (displacement < 1e-5f)
                    {
                        continue;
                    }

                    _linePoints[point.LineIndex][point.PointIndex] = updated;
                    _geometryDirty = true;
                    _framesSinceMove = 0;
                    LastMovedCount++;
                    LastMaxDisplacement = MathF.Max(LastMaxDisplacement, displacement);
                }

                _pending.RemoveRange(start, count);
            }

            return RebuildIfSettled(moreChangesInbound);
        }

        /// <summary>Frames since a point last moved -- how close this overlay is to its settle threshold.</summary>
        public int FramesSinceMove => _framesSinceMove;

        /// <summary>True when a rebuild is owed but is being held back waiting for the churn to settle.</summary>
        public bool RebuildPending => _geometryDirty;

        // Two ways to earn a rebuild: the churn stopped (the common case -- one rebuild per streaming burst
        // instead of one per frame of it), or it has been dirty long enough that staleness is now the bigger
        // problem than the cost.
        private bool RebuildIfSettled(bool moreChangesInbound)
        {
            if (!_geometryDirty)
            {
                return false;
            }

            var settled = _pending.Count == 0
                && !moreChangesInbound
                && _framesSinceMove >= RebuildSettleFrames;

            if (!settled && _framesSinceRebuild < ForcedRebuildFrames)
            {
                return false;
            }

            // A drape only ever moves existing points, which is exactly the case the in-place refresh covers
            // -- and it is the difference between ~40MB and nothing. RebuildGeometry is the fallback for a
            // mesh whose layout the refresh cannot replay (a tube profile, or geometry replaced from
            // elsewhere since it was generated). Scoped separately so the profiler says which one ran.
            var refreshed = false;
            using (ManaFrameProfiler.Measure("Drape: Ribbon Refresh"))
            {
                refreshed = _mesh.TryRefreshRibbonGeometry();
            }

            if (!refreshed)
            {
                using (ManaFrameProfiler.Measure("Drape: Ribbon Full Rebuild"))
                {
                    _mesh.RebuildGeometry();
                }
            }

            _geometryDirty = false;
            _framesSinceRebuild = 0;
            return true;
        }

        /// <summary>Clears the moved/displacement counters, which accumulate across a drain.</summary>
        public void ResetStats()
        {
            LastMovedCount = 0;
            LastMaxDisplacement = 0f;
        }
    }
}

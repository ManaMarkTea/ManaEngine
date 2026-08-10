using Mana.Code.Shapes;

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
        /// Re-drapes up to pointBudget queued points. Main thread. The ribbon is only rebuilt once the
        /// queue drains, since that rebuild is whole-mesh and would otherwise dominate every frame of a
        /// long drain. Returns true if geometry was rebuilt this call.
        /// </summary>
        public bool Tick(int pointBudget)
        {
            if (_pending.Count == 0)
            {
                return FinishIfDirty();
            }

            var count = Math.Min(Math.Max(pointBudget, 1), _pending.Count);

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
                LastMovedCount++;
                LastMaxDisplacement = MathF.Max(LastMaxDisplacement, displacement);
            }

            _pending.RemoveRange(start, count);
            return FinishIfDirty();
        }

        private bool FinishIfDirty()
        {
            if (_pending.Count > 0 || !_geometryDirty)
            {
                return false;
            }

            _mesh.RebuildGeometry();
            _geometryDirty = false;
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

using Mana.Code.Shapes;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Keeps a ManaLineMesh's points pinned to the planet's rendered surface.
    ///
    /// Each point's cube-face (u, v) is fixed for its lifetime -- it comes from lon/lat, which never
    /// changes -- so a patch update only needs to re-sample the points whose (u, v) fall inside that
    /// patch's box. Points are bucketed by root patch so that test scans one root's worth rather than
    /// all of them (the border datasets run to tens of thousands of points after decimation).
    /// </summary>
    public sealed class PlanetDrapedLines
    {
        private readonly struct DrapedPoint
        {
            public required int LineIndex { get; init; }
            public required int PointIndex { get; init; }
            public required float Longitude { get; init; }
            public required float Latitude { get; init; }
            public required int Face { get; init; }
            public required float U { get; init; }
            public required float V { get; init; }
        }

        private readonly PlanetNode _planet;
        private readonly ManaLineMesh _mesh;
        private readonly Vector3[][] _linePoints;
        private readonly float _surfaceOffset;
        private readonly Dictionary<(int Face, int Row, int Col), List<DrapedPoint>> _pointsByRoot = new();

        public ManaLineMesh Mesh => _mesh;

        /// <param name="surfaceOffset">Local units held above the sampled surface, so lines read as sitting on it rather than z-fighting with it.</param>
        public PlanetDrapedLines(
            PlanetNode planet,
            ManaLineMesh mesh,
            Vector3[][] linePoints,
            IReadOnlyList<IReadOnlyList<(float Longitude, float Latitude)>> lineLatLongs,
            float surfaceOffset)
        {
            _planet = planet;
            _mesh = mesh;
            _linePoints = linePoints;
            _surfaceOffset = surfaceOffset;

            var step = 2f / planet.PatchesPerFaceEdge;
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

                    var col = Math.Clamp((int)((u + 1f) / step), 0, planet.PatchesPerFaceEdge - 1);
                    var row = Math.Clamp((int)((v + 1f) / step), 0, planet.PatchesPerFaceEdge - 1);
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
                        Longitude = longitude,
                        Latitude = latitude,
                        Face = face,
                        U = u,
                        V = v
                    });
                }
            }
        }

        /// <summary>Re-drapes every point. Main thread -- rebuilds the ribbon geometry.</summary>
        public void DrapeAll()
        {
            var moved = false;
            foreach (var bucket in _pointsByRoot.Values)
            {
                foreach (var point in bucket)
                {
                    moved |= DrapePoint(point);
                }
            }

            if (moved)
            {
                _mesh.RebuildGeometry();
            }
        }

        /// <summary>
        /// Re-drapes only the points inside the given patches. Main thread. Returns true if anything
        /// moved, in which case the ribbon geometry has been rebuilt.
        /// </summary>
        public bool DrapeChanged(IReadOnlyList<PatchKey> changedPatches)
        {
            var moved = false;
            foreach (var key in changedPatches)
            {
                if (!_pointsByRoot.TryGetValue((key.Face, key.Row, key.Col), out var bucket))
                {
                    continue;
                }

                if (!_planet.TryGetPatchFaceBounds(key, out _, out var minU, out var maxU, out var minV, out var maxV))
                {
                    continue;
                }

                foreach (var point in bucket)
                {
                    if (point.U < minU || point.U > maxU || point.V < minV || point.V > maxV)
                    {
                        continue;
                    }

                    moved |= DrapePoint(point);
                }
            }

            if (moved)
            {
                // Whole-mesh rebuild: the ribbon is one vertex buffer, so there's no partial update to
                // make. The scoping above is what keeps the cost down -- it avoids re-sampling every
                // point against the quadtree, which is the expensive half.
                _mesh.RebuildGeometry();
            }

            return moved;
        }

        private bool DrapePoint(DrapedPoint point)
        {
            if (!_planet.TrySampleSurfaceRadiusAtFaceUv(point.Face, point.U, point.V, out var radius))
            {
                return false;
            }

            var direction = LatLong.FromLongitudeAndLatitude(point.Longitude, point.Latitude).ToUnitSphere();
            var updated = direction * (radius + _surfaceOffset);
            var existing = _linePoints[point.LineIndex][point.PointIndex];
            if (Vector3.DistanceSquared(existing, updated) < 1e-8f)
            {
                return false;
            }

            _linePoints[point.LineIndex][point.PointIndex] = updated;
            return true;
        }
    }
}

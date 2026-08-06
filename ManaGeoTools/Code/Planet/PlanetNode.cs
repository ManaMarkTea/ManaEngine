using Mana.Code;
using Mana.Code.Nodes;
using ManaGeoTools.Code.GeoTiff;

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
    public sealed class PlanetNode : ManaSceneNode
    {
        public float Radius { get; }
        public int Resolution { get; }
        public int PatchesPerFaceEdge { get; }
        public GeoTiffLibrary? HeightLibrary { get; private set; }

        // Serializes patch rebuilds: a background tile finishing and a main-thread HeightExaggeration
        // edit could otherwise race on the same patch's DynamicGeometryContainer/CpuData.
        private readonly object _rebuildLock = new();
        private readonly Dictionary<(int Face, int Row, int Col), PlanetPatchMesh> _patches = new();

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
            float heightExaggeration)
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
                        var node = new ManaMeshSceneNode(context, mesh) { Name = PatchName(face, row, col) };
                        AddChild(node);
                        _patches[(face, row, col)] = mesh;
                    }
                }
            }

            // Always present, the same way a real planet just has oceans -- not a separate opt-in
            // layer like the KML overlay or the flat demo water pond. A single un-tiled shell (not
            // one per terrain patch) since it only needs to carry gentle wave detail, not real
            // elevation streaming -- see PlanetOceanMesh's doc comment for how it avoids needing any
            // per-vertex "is this ocean" classification at all.
            var oceanMesh = new PlanetOceanMesh(context, radius, resolution);
            var oceanNode = new ManaMeshSceneNode(context, oceanMesh) { Name = "Ocean" };
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
                foreach (var patch in _patches.Values)
                {
                    if (TileOverlapsPatch(tile, patch))
                    {
                        patch.ApplyHeightData(HeightLibrary!);
                    }
                }
            }
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
                foreach (var patch in _patches.Values)
                {
                    patch.Rebuild(heightScale);
                }
            }
        }

        private float ComputeHeightScale()
        {
            return (Radius / PlanetLoader.EarthRadiusMeters) * _heightExaggeration;
        }

        private static string PatchName(int face, int row, int col)
        {
            return $"Patch_F{face}_{row}_{col}";
        }
    }
}

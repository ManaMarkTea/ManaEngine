using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// One cube-face patch of the planet's surface: a resolution x resolution grid spanning
    /// [MinU, MaxU] x [MinV, MaxV] of one cube face's local [-1,1] x [-1,1] space, warped onto the
    /// sphere via CubeSphere.WarpCubeToSphere (same per-vertex math the earlier whole-sphere
    /// PlanetMesh used), displaced by real elevation wherever a GeoTiffLibrary tile covers a vertex.
    /// Cube-face subdivision has no polar coordinate singularity, unlike the lat/long-degree patch
    /// scheme this replaced — patches stay uniformly sized everywhere instead of degenerating into
    /// thin wedges near the poles. Adjacent patches (including across a cube-face boundary) meet
    /// without seams because boundary vertices on both sides are computed from the same cube-space
    /// input through the same pure formula, independently landing on identical world positions.
    /// </summary>
    public sealed class PlanetPatchMesh : ManaProceduralMesh
    {
        public Vector3 FaceNormal { get; }
        public float MinU { get; }
        public float MaxU { get; }
        public float MinV { get; }
        public float MaxV { get; }
        public float Radius { get; }
        public int Resolution { get; }
        public float HeightScale { get; private set; }
        public GeoTiffLibrary? HeightLibrary { get; private set; }

        public PlanetPatchMesh(
            ManaContext context,
            Vector3 faceNormal,
            float minU,
            float maxU,
            float minV,
            float maxV,
            float radius,
            int resolution,
            float heightScale,
            GeoTiffLibrary? heightLibrary)
            : base(context)
        {
            FaceNormal = faceNormal;
            MinU = minU;
            MaxU = maxU;
            MinV = minV;
            MaxV = maxV;
            Radius = radius;
            Resolution = Math.Max(2, resolution);
            HeightScale = heightScale;
            HeightLibrary = heightLibrary;
            DefaultVertexColor = Colors.White;

            GenerateData();
        }

        /// <summary>
        /// Swaps in real elevation/color data (e.g. once a background GeoTIFF decode finishes) and
        /// regenerates. Safe to call from any thread — see SetCpuData's doc comment on ManaMesh.
        /// </summary>
        public void ApplyHeightData(GeoTiffLibrary heightLibrary)
        {
            HeightLibrary = heightLibrary;
            GenerateData();
        }

        /// <summary>Updates the height displacement scale (e.g. from an Inspector edit) and regenerates.</summary>
        public void Rebuild(float heightScale)
        {
            HeightScale = heightScale;
            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            // The base ManaProceduralMesh material leaves Shader null, which only gets backfilled by
            // the OpenGL-only finalize step — under WebGPU that leaves the Inspector showing no PBR
            // channels. Assign it explicitly instead, mirroring ManaWaterPlane.GenerateMaterial().
            var material = new ManaMaterial(Context) { Name = "Planet Patch Material" };
            material.Shader = new ManaPbrShader(Context);
            material.Albedo.Value = DefaultVertexColor;
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var axisA = new Vector3(FaceNormal.Y, FaceNormal.Z, FaceNormal.X);
            var axisB = Vector3.Cross(FaceNormal, axisA);

            var verts = new ManaGeometryVertex[Resolution, Resolution];
            for (var y = 0; y < Resolution; y++)
            {
                var ty = (float)y / (Resolution - 1);
                var v = float.Lerp(MinV, MaxV, ty);

                for (var x = 0; x < Resolution; x++)
                {
                    var tx = (float)x / (Resolution - 1);
                    var u = float.Lerp(MinU, MaxU, tx);

                    var pointOnUnitCube = FaceNormal + (u * axisA) + (v * axisB);
                    var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(pointOnUnitCube);
                    var latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);

                    float? rawHeightMeters = null;
                    var tile = HeightLibrary?.FindCurrentTile(latLong);
                    if (tile != null)
                    {
                        rawHeightMeters = tile.GetHeightAtLatLong(latLong);
                    }

                    var displacement = (rawHeightMeters ?? 0f) * HeightScale;
                    var position = pointOnUnitSphere * (Radius + displacement);
                    // Placeholder normal -- overwritten below once every vertex's displaced position
                    // is known, from the actual built geometry rather than the undisplaced sphere.
                    var vert = DynamicGeometryContainer.AddPoint(position, pointOnUnitSphere);
                    vert.TexCoord0 = latLong.ToUv();
                    vert.Color = PlanetColors.GetBiomeColor(rawHeightMeters, latLong.Latitude);
                    verts[x, y] = vert;
                }
            }

            // Real elevation displacement can be substantial (HeightExaggeration commonly pushes it
            // well past what's visually negligible), so lighting the surface with the raw undisplaced
            // sphere-radial direction (the old behavior) noticeably mismatches the actual geometry --
            // it reads as faceted/triangulated because the shading doesn't track the true local slope.
            // Fix: replace each vertex's normal with the average of the face normals of every quad
            // that touches it, computed directly from the already-displaced positions above -- this
            // guarantees the lighting normal can never disagree with what's actually rendered (unlike
            // a separate finite-difference estimate against the height function, which would introduce
            // its own subtly different notion of the surface).
            for (var y = 0; y < Resolution; y++)
            {
                for (var x = 0; x < Resolution; x++)
                {
                    var normalSum = Vector3.Zero;
                    if (x > 0 && y > 0)
                    {
                        normalSum += QuadNormal(verts[x - 1, y - 1], verts[x, y - 1], verts[x - 1, y]);
                    }
                    if (x < Resolution - 1 && y > 0)
                    {
                        normalSum += QuadNormal(verts[x, y - 1], verts[x + 1, y - 1], verts[x, y]);
                    }
                    if (x > 0 && y < Resolution - 1)
                    {
                        normalSum += QuadNormal(verts[x - 1, y], verts[x, y], verts[x - 1, y + 1]);
                    }
                    if (x < Resolution - 1 && y < Resolution - 1)
                    {
                        normalSum += QuadNormal(verts[x, y], verts[x + 1, y], verts[x, y + 1]);
                    }
                    verts[x, y].Normal = Vector3.Normalize(normalSum);
                }
            }

            // (x,y),(x+1,y),(x+1,y+1),(x,y+1) is CCW-as-seen-from-outside for every one of the 6
            // axis-aligned face normals (axisA x axisB == FaceNormal algebraically for all of them),
            // so one consistent quad order works regardless of which face this patch is on.
            for (var y = 0; y < Resolution - 1; y++)
            {
                for (var x = 0; x < Resolution - 1; x++)
                {
                    var a = verts[x, y];
                    var b = verts[x + 1, y];
                    var c = verts[x + 1, y + 1];
                    var d = verts[x, y + 1];
                    DynamicGeometryContainer.CreateFacesFromQuad(a, b, c, d);
                }
            }

            MinSize = new Vector3(-Radius, -Radius, -Radius);
            MaxSize = new Vector3(Radius, Radius, Radius);

            // Explicit SetCpuData (now public) so ApplyHeightData()/Rebuild() calling GenerateData()
            // again after the mesh has already rendered once actually takes effect — see
            // GeoTiffTerrainMesh for the fuller explanation of why this matters.
            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }

        // Outward-facing flat normal of quad (a,b,c,d) in the same CCW-as-seen-from-outside vertex
        // order used everywhere else in this file -- cross(b-a, d-a) is the right-hand-rule normal for
        // that winding (verified against the axis-aligned case: a=(0,0,0), b=(1,0,0), d=(0,1,0) gives
        // cross((1,0,0),(0,1,0)) = (0,0,1), the correct outward +Z for a CCW square viewed from +Z).
        private static Vector3 QuadNormal(ManaGeometryVertex a, ManaGeometryVertex b, ManaGeometryVertex d)
        {
            return Vector3.Cross(b.Position - a.Position, d.Position - a.Position);
        }
    }
}

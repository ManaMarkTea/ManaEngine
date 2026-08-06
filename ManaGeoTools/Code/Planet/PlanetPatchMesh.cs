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
                    var vert = DynamicGeometryContainer.AddPoint(position, pointOnUnitSphere);
                    vert.TexCoord0 = latLong.ToUv();
                    vert.Color = PlanetColors.GetBiomeColor(rawHeightMeters, latLong.Latitude);
                    verts[x, y] = vert;
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
    }
}

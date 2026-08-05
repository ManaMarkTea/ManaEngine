using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// One lat/long-degree patch of the planet's surface — the tile-shaped alternative to the
    /// whole-sphere cube-sphere PlanetMesh. Modeled on the source's
    /// PlanetMeshBuilder.create_lat_long_patch: a resolution x resolution grid spanning
    /// [latDeg, latDeg+patchSizeDeg] x [lonDeg, lonDeg+patchSizeDeg], displaced by real elevation
    /// wherever a GeoTiffLibrary tile covers a vertex (same height/color sampling as PlanetMesh).
    /// Adjacent patches meet without seams because boundary vertices on both sides are computed from
    /// the same lat/long input through the same pure formula, independently landing on identical
    /// world positions.
    /// </summary>
    public sealed class PlanetPatchMesh : ManaProceduralMesh
    {
        public float LatitudeDegrees { get; }
        public float LongitudeDegrees { get; }
        public float PatchSizeDegrees { get; }
        public float Radius { get; }
        public int Resolution { get; }
        public float HeightScale { get; private set; }
        public GeoTiffLibrary? HeightLibrary { get; private set; }

        public PlanetPatchMesh(
            ManaContext context,
            float latitudeDegrees,
            float longitudeDegrees,
            float patchSizeDegrees,
            float radius,
            int resolution,
            float heightScale,
            GeoTiffLibrary? heightLibrary)
            : base(context)
        {
            LatitudeDegrees = latitudeDegrees;
            LongitudeDegrees = longitudeDegrees;
            PatchSizeDegrees = patchSizeDegrees;
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
            // Same reasoning as PlanetMesh.GenerateMaterial(): the base ManaProceduralMesh material
            // leaves Shader null, which only gets backfilled by the OpenGL-only finalize step — under
            // WebGPU that leaves the Inspector showing no PBR channels. Assign it explicitly instead.
            var material = new ManaMaterial(Context) { Name = "Planet Patch Material" };
            material.Shader = new ManaPbrShader(Context);
            material.Albedo.Value = DefaultVertexColor;
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var verts = new ManaGeometryVertex[Resolution, Resolution];
            for (var y = 0; y < Resolution; y++)
            {
                var ty = (float)y / (Resolution - 1);
                var latitude = float.Lerp(LatitudeDegrees, LatitudeDegrees + PatchSizeDegrees, ty);

                for (var x = 0; x < Resolution; x++)
                {
                    var tx = (float)x / (Resolution - 1);
                    var longitude = float.Lerp(LongitudeDegrees, LongitudeDegrees + PatchSizeDegrees, tx);

                    var latLong = LatLong.FromLongitudeAndLatitude(longitude, latitude);
                    var pointOnUnitSphere = latLong.ToUnitSphere();

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

            // Same (x,y),(x+1,y),(x+1,y+1),(x,y+1) CCW-outward convention used throughout this
            // library (verified for lat/long parameterization the same way it was for cube-sphere
            // faces in PlanetMesh: tangent_lon x tangent_lat == the outward radial direction).
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

using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// A 6-face cube-sphere shell at exactly sea-level radius, rendered with ManaOceanShader
    /// (ocean.wgsl), masked to real ocean coverage: a quad is only generated if at least one of its
    /// corners samples as below sea level (or has no height data at all -- treated as ocean, same
    /// fallback PlanetColors itself uses). Land quads are skipped entirely rather than relying on the
    /// terrain to occlude a full, undisplaced sphere -- the earlier full-sphere version could still
    /// show through over land wherever the shader's own wave displacement pushed the (otherwise-
    /// occluded) ocean surface above nearby terrain, which is exactly the "ocean popping up over land"
    /// problem this replaces. All wave motion still happens in the shader (radial displacement, 3D
    /// position-keyed noise so it's seamless across the 6 cube faces) -- this mesh itself only carries
    /// the flat (undisplaced), masked geometry.
    /// </summary>
    public sealed class PlanetOceanMesh : ManaProceduralMesh
    {
        // Sea level is 0m by definition (matches PlanetColors' own `h < 0f` ocean check) -- not a
        // tunable, since "ocean" and "sea level" are the same thing here.
        private const float SeaLevelMeters = 0f;

        // Assumed depth for open-ocean points with no height-data coverage at all (distinct from a
        // point whose tile says it's below sea level) -- roughly Earth's actual mean ocean depth, so
        // uncovered deep ocean reads as deep rather than defaulting to a bright shoreline color.
        private const float DefaultOceanDepthMeters = 4000f;

        // How quickly depthFactor (fed to the shader as the deep/shallow color mix amount) approaches
        // 1.0 as depth increases -- an exponential falloff rather than a hard linear clamp so the
        // shoreline-to-open-ocean transition looks like a gradient, not a band. At this scale, ~200m
        // (shelf depth) is already visibly past the shallow end and ~4-5x this value is essentially
        // fully "deep".
        private const float DepthFalloffMeters = 1200f;

        public float Radius { get; }
        public int Resolution { get; }
        public GeoTiffLibrary? HeightLibrary { get; private set; }

        public PlanetOceanMesh(ManaContext context, float radius, int resolution, GeoTiffLibrary? heightLibrary)
            : base(context)
        {
            Radius = radius;
            Resolution = Math.Max(2, resolution);
            HeightLibrary = heightLibrary;
            DefaultVertexColor = Colors.White;

            GenerateData();
        }

        /// <summary>
        /// Swaps in real elevation data (e.g. once a background GeoTIFF decode finishes) and
        /// regenerates, re-masking which quads exist. Safe to call from any thread -- see
        /// SetCpuData's doc comment on ManaMesh.
        /// </summary>
        public void ApplyHeightData(GeoTiffLibrary heightLibrary)
        {
            HeightLibrary = heightLibrary;
            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            // Same reasoning as PlanetPatchMesh.GenerateMaterial(): the base ManaProceduralMesh
            // material leaves Shader null, which only gets backfilled by the OpenGL-only finalize
            // step -- under WebGPU that leaves the Inspector showing no PBR channels. Assign it
            // explicitly instead.
            var material = new ManaMaterial(Context) { Name = "Planet Ocean Material" };
            material.Shader = new ManaOceanShader(Context);
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            foreach (var normal in CubeSphere.FaceNormals)
            {
                BuildFace(normal);
            }

            MinSize = new Vector3(-Radius, -Radius, -Radius);
            MaxSize = new Vector3(Radius, Radius, Radius);

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }

        private void BuildFace(Vector3 normal)
        {
            var axisA = new Vector3(normal.Y, normal.Z, normal.X);
            var axisB = Vector3.Cross(normal, axisA);

            var verts = new ManaGeometryVertex[Resolution, Resolution];
            var isOcean = new bool[Resolution, Resolution];
            for (var y = 0; y < Resolution; y++)
            {
                var ty = (float)y / (Resolution - 1);
                for (var x = 0; x < Resolution; x++)
                {
                    var tx = (float)x / (Resolution - 1);

                    var pointOnUnitCube = normal + ((tx - 0.5f) * 2f * axisA) + ((ty - 0.5f) * 2f * axisB);
                    var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(pointOnUnitCube);

                    var rawHeightMeters = SampleHeightMeters(pointOnUnitSphere);
                    isOcean[x, y] = !rawHeightMeters.HasValue || rawHeightMeters.Value < SeaLevelMeters;

                    var depthMeters = rawHeightMeters.HasValue
                        ? MathF.Max(0f, -rawHeightMeters.Value)
                        : DefaultOceanDepthMeters;
                    var depthFactor = 1f - MathF.Exp(-depthMeters / DepthFalloffMeters);

                    var position = pointOnUnitSphere * Radius;
                    var vert = DynamicGeometryContainer.AddPoint(position, pointOnUnitSphere);
                    vert.TexCoord0 = new Vector2(tx, ty);
                    // RGB stays white (the shader multiplies it in as a no-op tint); alpha carries the
                    // depth factor through to ocean.wgsl's fragment shader, which uses it to pick
                    // between deepColor/shallowColor instead of the old lighting-driven mix -- see
                    // ocean.wgsl's fs_main for why that swap also helps with visual noise, not just
                    // depth accuracy.
                    vert.Color = new Vector4(DefaultVertexColor.X, DefaultVertexColor.Y, DefaultVertexColor.Z, depthFactor);
                    verts[x, y] = vert;
                }
            }

            // Same CCW-as-seen-from-outside quad convention used throughout this library (see
            // PlanetPatchMesh) -- axisA x axisB == normal algebraically for all 6 face normals, so one
            // consistent quad order works regardless of which face this is. A quad is skipped (no
            // triangles created, its 4 vertices stay unused/unreferenced) only if EVERY corner samples
            // as dry land -- including a quad if even one corner is ocean means the mesh's edge always
            // meets the coastline cleanly rather than leaving a gap of missing quads right where land
            // and water actually meet.
            for (var y = 0; y < Resolution - 1; y++)
            {
                for (var x = 0; x < Resolution - 1; x++)
                {
                    if (isOcean[x, y] || isOcean[x + 1, y] || isOcean[x + 1, y + 1] || isOcean[x, y + 1])
                    {
                        DynamicGeometryContainer.CreateFacesFromQuad(verts[x, y], verts[x + 1, y], verts[x + 1, y + 1], verts[x, y + 1]);
                    }
                }
            }
        }

        // Null means "no data at all" (as opposed to real data that happens to be at/above sea level),
        // so callers can tell the two cases apart -- e.g. DefaultOceanDepthMeters only applies to the
        // former.
        private float? SampleHeightMeters(Vector3 pointOnUnitSphere)
        {
            if (HeightLibrary == null)
            {
                return null;
            }

            var latLong = LatLong.FromPointOnSphere(pointOnUnitSphere, 1f, 0f);
            var tile = HeightLibrary.FindCurrentTile(latLong);
            return tile?.GetHeightAtLatLong(latLong);
        }
    }
}

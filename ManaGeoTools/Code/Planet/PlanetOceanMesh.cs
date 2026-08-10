using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// A 6-face cube-sphere shell at exactly sea-level radius, rendered with ManaOceanShader
    /// (ocean.wgsl), masked to real ocean coverage: a quad is only generated if at least one of its
    /// corners samples as below sea level (or has no height data at all). Land quads are skipped
    /// entirely rather than relying on the terrain to occlude a full, undisplaced sphere.
    ///
    /// This is the FAR tier of a two-tier ocean and is deliberately wave-free. At the scales this
    /// actually renders at (a 40-unit planet standing in for 6371km means roughly 160km per scene
    /// unit, and this shell's vertex spacing is ~100km) no wave that a person would recognize as a
    /// wave is representable -- the earlier vertex-displacement version was really rendering ~60km
    /// tall noise sampled at 100km, which is what read as blotchy "camouflage" and drove the long
    /// chain of frequency/epsilon caps that used to live in ocean.wgsl. Waves are now the near tier's
    /// job (PlanetNearOceanMesh), which only exists close to the surface where its vertex spacing can
    /// actually resolve them; this shell just carries flat, masked geometry plus per-vertex depth.
    /// </summary>
    public sealed class PlanetOceanMesh : ManaProceduralMesh
    {
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
            var (axisA, axisB) = CubeSphere.GetFaceAxes(normal);

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

                    var rawHeightMeters = OceanSurface.SampleHeightMeters(HeightLibrary, pointOnUnitSphere);
                    isOcean[x, y] = OceanSurface.IsOcean(rawHeightMeters);

                    var position = pointOnUnitSphere * Radius;
                    var vert = DynamicGeometryContainer.AddPoint(position, pointOnUnitSphere);
                    vert.TexCoord0 = new Vector2(tx, ty);
                    // RGB stays white (the shader multiplies it in as a no-op tint); alpha carries the
                    // depth factor through to ocean.wgsl's fragment shader, which uses it to pick
                    // between deepColor/shallowColor. Real bathymetry is smooth continuous data, so
                    // driving color identity off it (rather than off lighting) keeps the shell's color
                    // free of per-vertex noise.
                    vert.Color = new Vector4(
                        DefaultVertexColor.X,
                        DefaultVertexColor.Y,
                        DefaultVertexColor.Z,
                        OceanSurface.ComputeDepthFactor(rawHeightMeters));
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
    }
}

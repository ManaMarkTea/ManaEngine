using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// A smooth, un-tiled 6-face cube-sphere shell at exactly sea-level radius, rendered with
    /// ManaOceanShader (ocean.wgsl). Sits underneath the terrain (PlanetPatchMesh, which dips below
    /// this radius in ocean regions and rises above it on land) -- land occludes it via the normal
    /// depth test, and it shows through wherever terrain sinks below sea level, giving an ocean
    /// surface with no per-vertex "is this ocean" classification needed at all. All wave motion
    /// happens in the shader (radial displacement, 3D position-keyed noise so it's seamless across
    /// the 6 cube faces) -- this mesh itself is a flat (undisplaced) sphere.
    /// </summary>
    public sealed class PlanetOceanMesh : ManaProceduralMesh
    {
        public float Radius { get; }
        public int Resolution { get; }

        public PlanetOceanMesh(ManaContext context, float radius, int resolution)
            : base(context)
        {
            Radius = radius;
            Resolution = Math.Max(2, resolution);
            DefaultVertexColor = Colors.White;

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
            for (var y = 0; y < Resolution; y++)
            {
                var ty = (float)y / (Resolution - 1);
                for (var x = 0; x < Resolution; x++)
                {
                    var tx = (float)x / (Resolution - 1);

                    var pointOnUnitCube = normal + ((tx - 0.5f) * 2f * axisA) + ((ty - 0.5f) * 2f * axisB);
                    var pointOnUnitSphere = CubeSphere.WarpCubeToSphere(pointOnUnitCube);

                    var position = pointOnUnitSphere * Radius;
                    var vert = DynamicGeometryContainer.AddPoint(position, pointOnUnitSphere);
                    vert.TexCoord0 = new Vector2(tx, ty);
                    vert.Color = DefaultVertexColor;
                    verts[x, y] = vert;
                }
            }

            // Same CCW-as-seen-from-outside quad convention used throughout this library (see
            // PlanetPatchMesh) -- axisA x axisB == normal algebraically for all 6 face normals, so one
            // consistent quad order works regardless of which face this is.
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
        }
    }
}

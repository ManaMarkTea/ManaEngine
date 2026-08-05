using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using ManaGeoTools.Code.GeoTiff;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// A 6-face cube-sphere mesh (see CubeSphere.WarpCubeToSphere), optionally displaced by real
    /// elevation from whichever tiles of a GeoTiffLibrary are loaded — the rest of the sphere stays
    /// smooth. Ported from PlanetMeshBuilder.gd's create_face, minus the degree-patch LOD/baking/
    /// streaming machinery the source layers on top (out of scope — see the Planet node plan).
    /// </summary>
    public sealed class PlanetMesh : ManaProceduralMesh
    {
        private static readonly Vector3[] FaceNormals =
        {
            Vector3.UnitY, -Vector3.UnitY,
            Vector3.UnitX, -Vector3.UnitX,
            Vector3.UnitZ, -Vector3.UnitZ,
        };

        public float Radius { get; }
        public int Resolution { get; }
        public float HeightScale { get; }
        public GeoTiffLibrary? HeightLibrary { get; }

        public PlanetMesh(ManaContext context, float radius, int resolution, float heightScale, GeoTiffLibrary? heightLibrary)
            : base(context)
        {
            Radius = radius;
            Resolution = Math.Max(2, resolution);
            HeightScale = heightScale;
            HeightLibrary = heightLibrary;
            DefaultVertexColor = Colors.White;

            HeightLibrary?.LoadAllImageData();

            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            // ManaProceduralMesh's base GenerateMaterial() leaves Shader null, relying on
            // ManaGameModeGraphicsBridge.FinalizeGpuUploads to backfill a ManaPbrShader later — but
            // that backfill is OpenGL-only (see FinalizeMaterial's `backend == OpenGL` gate), so a
            // material built during WebGPU startup would keep Shader null forever: the Inspector's
            // material panel gates the whole Albedo/Metallic/Roughness/etc. section on
            // `Shader is ManaPbrShader` and shows "Shader: None" / no editable uniforms otherwise.
            // Assign it explicitly here instead, mirroring ManaWaterPlane.GenerateMaterial().
            var material = new ManaMaterial(Context) { Name = "Planet Material" };
            material.Shader = new ManaPbrShader(Context);
            material.Albedo.Value = DefaultVertexColor;
            material.EnsureShaderMetadataDefaults();
            return material;
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            foreach (var normal in FaceNormals)
            {
                BuildFace(normal);
            }

            MinSize = new Vector3(-Radius, -Radius, -Radius);
            MaxSize = new Vector3(Radius, Radius, Radius);

            // Explicit SetCpuData (now public) so a second GenerateData()/RebuildGeometry() call
            // after the mesh has already rendered once actually takes effect — see GeoTiffTerrainMesh.
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
            // axis-aligned face normals here (axisA x axisB == normal algebraically for all of them),
            // so one consistent quad order works for all faces — no per-face winding special-case.
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

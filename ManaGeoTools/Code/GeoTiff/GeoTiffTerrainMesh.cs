using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;

namespace ManaGeoTools.Code.GeoTiff
{
    /// <summary>
    /// Port of GeoTiffTerrainMesh.gd: builds a flat XZ grid (not sphere-projected) sized
    /// TerrainWidth x TerrainLength, sampling the GeoTIFF heightmap by nearest pixel. Vertex
    /// position is (x, height, z) in raw integer grid units — not resized to world units, matching
    /// the source; scale the owning ManaSceneNode if a different world size is wanted.
    /// </summary>
    public sealed class GeoTiffTerrainMesh : ManaProceduralMesh
    {
        public GeoTiff HeightMap { get; }
        public int TerrainWidth { get; }
        public int TerrainLength { get; }
        public float MaxHeight { get; }

        public GeoTiffTerrainMesh(ManaContext context, GeoTiff heightMap, int terrainWidth, int terrainLength, float maxHeight)
            : base(context)
        {
            HeightMap = heightMap;
            TerrainWidth = Math.Max(1, terrainWidth);
            TerrainLength = Math.Max(1, terrainLength);
            MaxHeight = maxHeight;
            DefaultVertexColor = Colors.White;

            if (!HeightMap.IsDetailLoaded)
            {
                HeightMap.LoadImageData();
            }

            GenerateData();
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var heightMapWidth = HeightMap.Metadata.Width;
            var heightMapHeight = HeightMap.Metadata.Height;
            var scaleX = heightMapWidth / (float)TerrainWidth;
            var scaleZ = heightMapHeight / (float)TerrainLength;

            // Sample raw heights up front so per-vertex normals can be computed via central
            // difference — this engine has no SurfaceTool.generate_normals() post-pass equivalent,
            // ManaGeometryVertex.Normal must be supplied when the vertex is added.
            var heights = new float[TerrainWidth, TerrainLength];
            for (var x = 0; x < TerrainWidth; x++)
            {
                for (var z = 0; z < TerrainLength; z++)
                {
                    var pixelX = (int)(x * scaleX);
                    var pixelZ = (int)(z * scaleZ);
                    heights[x, z] = HeightMap.GetHeightAtPixel(pixelX, pixelZ) * MaxHeight;
                }
            }

            var verts = new ManaGeometryVertex[TerrainWidth, TerrainLength];
            for (var x = 0; x < TerrainWidth; x++)
            {
                for (var z = 0; z < TerrainLength; z++)
                {
                    var height = heights[x, z];
                    var hLeft = heights[Math.Max(x - 1, 0), z];
                    var hRight = heights[Math.Min(x + 1, TerrainWidth - 1), z];
                    var hDown = heights[x, Math.Max(z - 1, 0)];
                    var hUp = heights[x, Math.Min(z + 1, TerrainLength - 1)];
                    var normal = Vector3.Normalize(new Vector3(hLeft - hRight, 2.0f, hDown - hUp));

                    var position = new Vector3(x, height, z);
                    var vert = DynamicGeometryContainer.AddPoint(position, normal);
                    vert.TexCoord0 = new Vector2((float)x / (TerrainWidth - 1 == 0 ? 1 : TerrainWidth - 1), (float)z / (TerrainLength - 1 == 0 ? 1 : TerrainLength - 1));
                    vert.Color = DefaultVertexColor;
                    verts[x, z] = vert;
                }
            }

            // Standard CCW quad split (a,b,c,d) around each cell, matching the proven convention
            // used by ManaWaterPlane for other XZ grids in this engine.
            for (var x = 0; x < TerrainWidth - 1; x++)
            {
                for (var z = 0; z < TerrainLength - 1; z++)
                {
                    var a = verts[x, z];
                    var b = verts[x + 1, z];
                    var c = verts[x + 1, z + 1];
                    var d = verts[x, z + 1];
                    DynamicGeometryContainer.CreateFacesFromQuad(a, b, c, d);
                }
            }

            MinSize = new Vector3(0, 0, 0);
            MaxSize = new Vector3(TerrainWidth - 1, MaxHeight, TerrainLength - 1);

            // Explicit SetCpuData (now public) rather than relying on GetOrCreateCpuData()'s lazy
            // build: that lazy path only ever runs once (it returns the cached CpuData immediately
            // if non-null), so without this a second GenerateData()/RebuildGeometry() call after the
            // mesh has already rendered once would silently have no visible effect.
            SetCpuData(DynamicGeometryContainer.CreateCpuData());
        }
    }
}

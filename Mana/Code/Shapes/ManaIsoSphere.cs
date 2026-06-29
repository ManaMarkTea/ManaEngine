namespace Mana.Code.Shapes
{
    public class ManaIsoSphere : ManaProceduralMesh
    {
        public float Radius { get; set; }
        public int RecursionLevel { get; set; } = 3;

        public ManaIsoSphere(ManaContext context, Vector4 color, float radius) : base(context)
        {
            Radius = radius;
            DefaultVertexColor = color;
            GenerateData();
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            Vector3[] vertices = new Vector3[0];
            List<Vector3> vertList = new List<Vector3>();
            Dictionary<long, int> middlePointIndexCache = new Dictionary<long, int>();

            int recursionLevel = Math.Clamp(RecursionLevel, 0, 5);
            float radius = Radius;

            // create 12 vertices of a icosahedron
            float t = (1f + MathF.Sqrt(5f)) / 2f;

            vertList.Add(new Vector3(-1f, t, 0f).Normalized() * radius);
            vertList.Add(new Vector3(1f, t, 0f).Normalized() * radius);
            vertList.Add(new Vector3(-1f, -t, 0f).Normalized() * radius);
            vertList.Add(new Vector3(1f, -t, 0f).Normalized() * radius);

            vertList.Add(new Vector3(0f, -1f, t).Normalized() * radius);
            vertList.Add(new Vector3(0f, 1f, t).Normalized() * radius);
            vertList.Add(new Vector3(0f, -1f, -t).Normalized() * radius);
            vertList.Add(new Vector3(0f, 1f, -t).Normalized() * radius);

            vertList.Add(new Vector3(t, 0f, -1f).Normalized() * radius);
            vertList.Add(new Vector3(t, 0f, 1f).Normalized() * radius);
            vertList.Add(new Vector3(-t, 0f, -1f).Normalized() * radius);
            vertList.Add(new Vector3(-t, 0f, 1f).Normalized() * radius);


            // create 20 triangles of the icosahedron
            List<TriangleIndices> faces = new List<TriangleIndices>();

            // 5 faces around point 0
            faces.Add(new TriangleIndices(0, 11, 5));
            faces.Add(new TriangleIndices(0, 5, 1));
            faces.Add(new TriangleIndices(0, 1, 7));
            faces.Add(new TriangleIndices(0, 7, 10));
            faces.Add(new TriangleIndices(0, 10, 11));

            // 5 adjacent faces 
            faces.Add(new TriangleIndices(1, 5, 9));
            faces.Add(new TriangleIndices(5, 11, 4));
            faces.Add(new TriangleIndices(11, 10, 2));
            faces.Add(new TriangleIndices(10, 7, 6));
            faces.Add(new TriangleIndices(7, 1, 8));

            // 5 faces around point 3
            faces.Add(new TriangleIndices(3, 9, 4));
            faces.Add(new TriangleIndices(3, 4, 2));
            faces.Add(new TriangleIndices(3, 2, 6));
            faces.Add(new TriangleIndices(3, 6, 8));
            faces.Add(new TriangleIndices(3, 8, 9));

            // 5 adjacent faces 
            faces.Add(new TriangleIndices(4, 9, 5));
            faces.Add(new TriangleIndices(2, 4, 11));
            faces.Add(new TriangleIndices(6, 2, 10));
            faces.Add(new TriangleIndices(8, 6, 7));
            faces.Add(new TriangleIndices(9, 8, 1));


            // refine triangles
            for (int i = 0; i < recursionLevel; i++)
            {
                List<TriangleIndices> faces2 = new List<TriangleIndices>();
                foreach (var tri in faces)
                {
                    // replace triangle by 4 triangles
                    int a = getMiddlePoint(tri.v1, tri.v2, ref vertList, ref middlePointIndexCache, radius);
                    int b = getMiddlePoint(tri.v2, tri.v3, ref vertList, ref middlePointIndexCache, radius);
                    int c = getMiddlePoint(tri.v3, tri.v1, ref vertList, ref middlePointIndexCache, radius);

                    faces2.Add(new TriangleIndices(tri.v1, a, c));
                    faces2.Add(new TriangleIndices(tri.v2, b, a));
                    faces2.Add(new TriangleIndices(tri.v3, c, b));
                    faces2.Add(new TriangleIndices(a, b, c));
                }
                faces = faces2;
            }

            vertices = vertList.ToArray();

            List<uint> triList = new List<uint>();
            for (int i = 0; i < faces.Count; i++)
            {
                triList.Add((uint)faces[i].v1);
                triList.Add((uint)faces[i].v2);
                triList.Add((uint)faces[i].v3);
            }

            Vector2[] texCoords = new Vector2[vertList.Count];
            Vector4[] colors = new Vector4[vertList.Count];
            Vector3[] normales = new Vector3[vertList.Count];

            Parallel.For(0, normales.Length, (i) =>
            {
                var vertCoord = vertList[i];
                normales[i] = vertList[i].Normalized();
                colors[i] = DefaultVertexColor;

                float u = (MathF.Atan2(vertCoord.Z, vertCoord.X) / (2f * MathF.PI));
                float v = (MathF.Asin(vertCoord.Y) / MathF.PI) - 0.4f;

                texCoords[i] = new Vector2(u, v) * 4.0f;
            });

            var meshData = new ManaCore.Graphics.ManaMeshCpuData
            {
                Indexes = triList.ToArray(),
                Positions = vertices,
                Normals = normales,
                Colors = colors,
                TextureCoordinates0 = texCoords,
                TextureCoordinates1 = texCoords
            };

            SetCpuData(meshData);
            if (Context.OpenGl != null && !Context.DeferGpuUploads)
            {
                var gpuMesh = Context.RequireOpenGlDevice().CreateMesh(Context, meshData);
                if (gpuMesh is Mana.Code.Graphics.ManaOpenGlMesh openGlMesh)
                {
                    VertexBufferObject = openGlMesh.VertexBufferObject;
                }
            }
        }

        // return index of point in the middle of p1 and p2
        private static int getMiddlePoint(int p1, int p2, ref List<Vector3> vertices, ref Dictionary<long, int> cache, float radius)
        {
            // first check if we have it already
            bool firstIsSmaller = p1 < p2;
            long smallerIndex = firstIsSmaller ? p1 : p2;
            long greaterIndex = firstIsSmaller ? p2 : p1;
            long key = (smallerIndex << 32) + greaterIndex;

            int ret;
            if (cache.TryGetValue(key, out ret))
            {
                return ret;
            }

            // not in cache, calculate it
            Vector3 point1 = vertices[p1];
            Vector3 point2 = vertices[p2];
            Vector3 middle = new Vector3
            (
                (point1.X + point2.X) / 2f,
                (point1.Y + point2.Y) / 2f,
                (point1.Z + point2.Z) / 2f
            );

            // add vertex makes sure point is on unit sphere
            int i = vertices.Count;
            vertices.Add(middle.Normalized() * radius);

            // store it, return index
            cache.Add(key, i);

            return i;
        }


    }

}

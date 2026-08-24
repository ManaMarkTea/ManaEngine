namespace Mana.Code.Shapes
{
    public class ManaSphere : ManaProceduralMesh
    {
        public float Radius { get; set; }
        public int LatitudeSegments = 60;
        public int LongitudeSegments = 60;

        public Vector2 TextureScale = new Vector2(4, 2);
        public Vector2 TextureOffset = new Vector2(0.0f, -0.5f);

        public ManaSphere(ManaContext context, Vector4 color, float radius) : base(context)
        {
            Radius = radius;
            DefaultVertexColor = color;
            GenerateData();
        }


        protected override void GenerateData()
        {
            base.GenerateData();

            int numLatitudeLines = LatitudeSegments;
            int numLongitudeLines = LongitudeSegments;

            ManaDynamicGeometryContainer container = DynamicGeometryContainer;

            uint X_SEGMENTS = (uint)numLongitudeLines;
            uint Y_SEGMENTS = (uint)numLatitudeLines;
            const float PI = 3.14159265359f;

            for (uint x = 0; x <= X_SEGMENTS; ++x)
            {
                for (uint y = 0; y <= Y_SEGMENTS; ++y)
                {
                    float xSegment = (float)x / (float)X_SEGMENTS;
                    float ySegment = (float)y / (float)Y_SEGMENTS;
                    float xPos = MathF.Cos(xSegment * 2.0f * PI) * MathF.Sin(ySegment * PI);
                    float yPos = MathF.Cos(ySegment * PI);
                    float zPos = MathF.Sin(xSegment * 2.0f * PI) * MathF.Sin(ySegment * PI);

                    var spherePoint = new Vector3(xPos, yPos, zPos) * Radius;
                    var newPoint = container.AddPoint(spherePoint, spherePoint.Normalized());
                    newPoint.TexCoord0 = new Vector2(xSegment * TextureScale.X + TextureOffset.X, 1.0f - ySegment * TextureScale.Y + TextureOffset.Y);
                }
            }

            bool oddRow = false;

            List<uint> triStrip = new List<uint>();
            List<uint> triList = new List<uint>();

            for (uint y = 0; y < Y_SEGMENTS; ++y)
            {
                if (!oddRow) // even rows: y == 0, y == 2; and so on
                {
                    for (uint x = 0; x <= X_SEGMENTS; ++x)
                    {
                        triStrip.Add(y * (X_SEGMENTS + 1) + x);
                        triStrip.Add((y + 1) * (X_SEGMENTS + 1) + x);
                    }
                }
                else
                {
                    for (int x = (int)X_SEGMENTS; x >= 0; --x)
                    {
                        uint index1 = (uint)((y + 1) * (X_SEGMENTS + 1) + x);
                        uint index2 = (uint)(y * (X_SEGMENTS + 1) + x);

                        triStrip.Add(index1);
                        triStrip.Add(index2);
                    }
                }
                oddRow = !oddRow;
            }

            oddRow = false;
            for (int v = 0; v < triStrip.Count - 2; v++)
            {
                if (oddRow)
                {
                    triList.Add(triStrip[v]);
                    triList.Add(triStrip[v + 1]);
                    triList.Add(triStrip[v + 2]);
                }
                else
                {
                    triList.Add(triStrip[v]);
                    triList.Add(triStrip[v + 2]);
                    triList.Add(triStrip[v + 1]);
                }
                oddRow = !oddRow;
            }

            container.AddFaces(triList);

            SetCpuData(container.CreateCpuData());
            if (Context.OpenGl != null)
            {
#if !MANA_BROWSER
                VertexBufferObject = container.CreateVertexBufferObject(Context);
#endif
            }
        }

    }

}

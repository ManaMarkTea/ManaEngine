using Mana.Code.Materials;
using Silk.NET.Assimp;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.SDL;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Shapes
{
    public class ManaSphere : ManaProceduralMesh
    {
        public float Radius { get; set; }
        public int LatitudeSegments = 64;
        public int LongitudeSegments = 64;

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

            int numVertices = (numLatitudeLines * (numLongitudeLines + 1)) + 2;

            ManaDynamicGeometryContainer container = new ManaDynamicGeometryContainer();

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
                    var newPoint = container.AddPoint(spherePoint);
                    newPoint.TexCoord0 = new Vector2(xSegment, ySegment);
                    newPoint.Normal = spherePoint.Normalized();
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
                    triList.Add(triStrip[v+1]);
                    triList.Add(triStrip[v+2]);
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

            //// +1.0f because there's a gap between the poles and the first parallel.
            //float latitudeSpacing = 1.0f / (numLatitudeLines);
            //float longitudeSpacing = 1.0f / numLongitudeLines;

            //for (int latitude = 0; latitude < numLatitudeLines; latitude++)
            //{
            //    int nextLat = latitude + 1;

            //    for (int longitude = 0; longitude < numLongitudeLines; longitude++)
            //    {
            //        int nextLongitude = longitude + 1;

            //        Vector2[] spherePoints = new Vector2[] 
            //        {
            //            new Vector2(longitude * longitudeSpacing, latitude * latitudeSpacing),
            //            new Vector2(nextLongitude * longitudeSpacing, latitude * latitudeSpacing),
            //            new Vector2(nextLongitude * longitudeSpacing, nextLat * latitudeSpacing),
            //            new Vector2(longitude * longitudeSpacing, nextLat * latitudeSpacing),
            //        };

            //        for (int i = 0; i < 4; i++)
            //        {
            //            Vector2 spherePoint = spherePoints[i];

            //            float theta = (float)(spherePoint.X * 2.0f * Math.PI);
            //            float phi = (float)((spherePoint.Y - 0.5f) * Math.PI);
            //            float c = (float)Math.Cos(phi);

            //            var positionOnSphere = new Vector3(
            //                (float)(c * Math.Cos(theta)),
            //                (float)Math.Sin(phi),
            //                (float)(c * Math.Sin(theta))) * Radius;

            //            var point = container.AddPoint(positionOnSphere);
            //            point.TexCoord0 = spherePoint*2;
            //            point.Normal = positionOnSphere.Normalized();
            //        }

            //        container.CreateQuadFromDisconnectedPoints();
            //    }
            //}

            //while (container.DisconnectedPoints.Count > 3)
            //{
            //    container.CreateFaceFromDisconnectedPoints();
            //}

            List<ManaMaterial> newMaterials = new List<ManaMaterial>
            {
                GenerateMaterial()
            };
        

            var positions = container.Positions.ToArray();
            var normals = container.Normals.ToArray();
            var colors = container.Colors.ToArray();
            var textureCoordinates0 = container.TexCoords0.ToArray();
            var textureCoordinates1 = container.TexCoords1.ToArray();
            var indexes = container.Indexes.ToArray();

            MeshBufferObject = new MetaVertexBufferObject(Context, indexes, positions, normals, colors, textureCoordinates0, textureCoordinates1);
            MeshBufferObject.ConstructVBO();

            Materials = newMaterials.ToArray();
        }

    }

}

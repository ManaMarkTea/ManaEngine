using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Shapes
{
    public class ManaProceduralMesh : ManaMesh
    {
        protected struct TriangleIndices
        {
            public int v1;
            public int v2;
            public int v3;

            public TriangleIndices(int v1, int v2, int v3)
            {
                this.v1 = v1;
                this.v2 = v2;
                this.v3 = v3;
            }
        }

        public Vector4 DefaultVertexColor { get; set; }

        public ManaProceduralMesh(ManaWorld world) : base(world)
        {

        }

        protected virtual void GenerateData() { }



        void ExtrudeQuad(float height)
        {
            // Create a new quad mesh
            var vertices = new Vector3[] 
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, -0.5f)
            };

            var triangles = new int[] 
            {
                0, 1, 2,
                0, 2, 3
            };

            // Extrude the quad by adding new vertices            
            Vector3[] newVertices = new Vector3[vertices.Length * 2];
            for (int i = 0; i < vertices.Length; i++)
            {
                newVertices[i] = vertices[i];
                newVertices[i + vertices.Length] = vertices[i] + new Vector3(0f, height, 0f);
            }

            // Create new triangles to connect the new vertices            
            int[] newTriangles = new int[triangles.Length * 2 + 12];
            int vertCount = vertices.Length;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                int a1 = a + vertCount;
                int b1 = b + vertCount;
                int c1 = c + vertCount;

                newTriangles[i] = a;
                newTriangles[i + 1] = b;
                newTriangles[i + 2] = c;
                newTriangles[i + 3] = a1;
                newTriangles[i + 4] = b1;
                newTriangles[i + 5] = c1;
                newTriangles[i + 6] = a;
                newTriangles[i + 7] = c;
                newTriangles[i + 8] = b1;
                newTriangles[i + 9] = a;
                newTriangles[i + 10] = b1;
                newTriangles[i + 11] = c1;
            }

            // Update the mesh with the new vertices and triangles

            // Normalize
            
        }

    }
}

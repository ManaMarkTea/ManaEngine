using Mana.Code.Materials;
using Silk.NET.Assimp;
using Silk.NET.Core.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Shapes
{
    public static class ManaMeshLoaderGltf
    {
        public unsafe static ManaMesh LoadFromGltf(ManaContext context, Mesh* mesh)
        {
            ManaMesh output = new ManaMesh(context);

            uint[] indexes = new uint[0];
            Vector3[] positions = new Vector3[0];
            Vector3[] normals = new Vector3[0];
            Vector4[] colors = new Vector4[0];
            Vector2[] textureCoordinates0 = new Vector2[0];
            Vector2[] textureCoordinates1 = new Vector2[0];

            
            Vector3 minSize = Vector3.One * float.MaxValue;
            Vector3 maxSize = Vector3.One * float.MinValue;

            List<ManaMaterial> newMaterials = new List<ManaMaterial>();
            var foundMaterial = context.World.Materials[(int)mesh->MMaterialIndex];
            if (foundMaterial != null)
            {
                newMaterials.Add(foundMaterial);
            }

            List<uint> indices = new List<uint>();
            for (uint i = 0; i < mesh->MNumFaces; i++)
            {
                Face face = mesh->MFaces[i];
                // retrieve all indices of the face and store them in the indices vector
                for (uint j = 0; j < face.MNumIndices; j++)
                {
                    indices.Add(face.MIndices[j]);
                }
            }

            indexes = indices.ToArray();

            positions = new Vector3[mesh->MNumVertices];

            if (mesh->MNormals != null)
            {
                normals = new Vector3[mesh->MNumVertices];
            }

            if (mesh->MColors.Element0 != null)
            {
                colors = new Vector4[mesh->MNumVertices];
            }

            if (mesh->MTextureCoords.Element0 != null)
            {
                textureCoordinates0 = new Vector2[mesh->MNumVertices];
            }

            if (mesh->MTextureCoords.Element1 != null)
            {
                textureCoordinates1 = new Vector2[mesh->MNumVertices];
            }

            Dictionary<int, int> threadCounter = new Dictionary<int, int>();

            Parallel.For(0, mesh->MNumVertices, (i) =>
            {
                Vector3 vector = new Vector3(mesh->MVertices[i].X, mesh->MVertices[i].Y, mesh->MVertices[i].Z);
                positions[i] = vector;

                lock (output)
                {
                    minSize = Vector3.Min(minSize, positions[i]);
                    maxSize = Vector3.Max(maxSize, positions[i]);

                    if (!threadCounter.ContainsKey(Thread.CurrentThread.ManagedThreadId))
                    {
                        threadCounter[Thread.CurrentThread.ManagedThreadId] = 0;
                    }

                    threadCounter[Thread.CurrentThread.ManagedThreadId]++;
                }

                if (mesh->MNormals != null)
                {
                    Vector3 normal = new Vector3(mesh->MNormals[i].X, mesh->MNormals[i].Y, mesh->MNormals[i].Z);
                    normals[i] = normal;
                }

                if (mesh->MColors.Element0 != null)
                {
                    Vector4 color = new Vector4(mesh->MColors.Element0[i].X, mesh->MColors.Element0[i].Y, mesh->MColors.Element0[i].Z, mesh->MColors.Element0[i].W);
                    colors[i] = color;
                }

                if (mesh->MTextureCoords.Element0 != null)
                {
                    Vector2 normal = new Vector2(mesh->MTextureCoords.Element0[i].X, mesh->MTextureCoords.Element0[i].Y);
                    textureCoordinates0[i] = normal;
                }

                if (mesh->MTextureCoords.Element1 != null)
                {
                    Vector2 normal = new Vector2(mesh->MTextureCoords.Element1[i].X, mesh->MTextureCoords.Element1[i].Y);
                    textureCoordinates1[i] = normal;
                }
            });

            output.SetSize(minSize, maxSize);
                        
            var meshBufferObject = new ManaVertexBufferObject(context, indexes, positions, normals, colors, textureCoordinates0, textureCoordinates1);
            meshBufferObject.ConstructVBO();
            
            output.VertexBufferObject = meshBufferObject;
            output.Materials = newMaterials.ToArray();
            return output;
        }
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using MetaRenderer.Code.ImageLoaders;
using Silk.NET.Assimp;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Mana.Code;

namespace Mana
{

    public class ManaMesh : ManaObject, IDisposable
    {
        public string Name { get; set; }
        
        public ManaMaterial[] Materials;        
        protected Vector3 MinSize;
        protected Vector3 MaxSize;

        protected MetaVertexBufferObject MeshBufferObject;
        
        public ManaMesh(ManaContext context) : base(context)
        {
            this.Context = context;
        }

        private static string[] Attributes =
        {
            "Position",
            "Normal",
            "Color",
            "TEXCOORD_0",
            "TEXCOORD_1"
        };
        
        public unsafe void LoadFromGltf(Mesh* mesh)
        {
            uint[] indexes = new uint[0];
            Vector3[] positions = new Vector3[0];
            Vector3[] normals = new Vector3[0];
            Vector4[] colors = new Vector4[0];
            Vector2[] textureCoordinates0 = new Vector2[0];
            Vector2[] textureCoordinates1 = new Vector2[0];

            MinSize.X = MinSize.Y = MinSize.Z = float.MaxValue;
            MaxSize.X = MaxSize.Y = MaxSize.Z = float.MinValue;

            List<ManaMaterial> newMaterials = new List<ManaMaterial>();
            var foundMaterial = Context.World.Materials[(int)mesh->MMaterialIndex];
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

                lock (this)
                {
                    MinSize = Vector3.Min(MinSize, positions[i]);
                    MaxSize = Vector3.Max(MaxSize, positions[i]);

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
            

            Materials = newMaterials.ToArray();

            MeshBufferObject = new MetaVertexBufferObject(Context, indexes, positions, normals, colors, textureCoordinates0, textureCoordinates1);
            MeshBufferObject.ConstructVBO();
        }        

        public void Draw(ManaSceneNode owner)
        {
            if (MeshBufferObject != null)
            {
                Materials[0].Bind(owner.WorldTransform);

                MeshBufferObject.Bind();
                MeshBufferObject.DrawElements();
                MeshBufferObject.UnBind();

                Materials[0].UnBind();
            }
        }

        float GetUnitScale()
        {
            Vector3 diff = MaxSize - MinSize;
            return MathF.Max(diff.X, MathF.Max(diff.Y, diff.Z));
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
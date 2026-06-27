// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using Mana.Code.Nodes;
using Mana.Code.Shapes.Modifiers;

namespace Mana.Code.Shapes
{

    public class ManaMesh : ManaObject
    {
        public ManaMaterial[] Materials;
        protected Vector3 MinSize;
        protected Vector3 MaxSize;

        public ManaMeshCpuData? CpuData { get; private set; }

        public ManaVertexBufferObject? VertexBufferObject { get; internal set; }

        public ManaDynamicGeometryContainer DynamicGeometryContainer { get; internal set; }

        public List<ManaMeshModifier> Modifiers;

        public ManaMesh(ManaContext context) : base(context)
        {
            Context = context;
            Modifiers = new List<ManaMeshModifier>();
            Materials = new ManaMaterial[0];
            DynamicGeometryContainer = new ManaDynamicGeometryContainer();
        }

        public void Draw(ManaSceneNode owner, MeshRendererComponent? meshRenderer)
        {
            if (VertexBufferObject != null)
            {
                ManaMaterial material = Materials[0];
                if (meshRenderer != null)
                {
                    material = meshRenderer.Material;
                }

                material.Bind(owner.WorldTransform, owner);

                VertexBufferObject.Bind();
                VertexBufferObject.DrawElements();
                VertexBufferObject.UnBind();

                material.UnBind();
            }
        }

        public void RenderDirectly()
        {
            if (VertexBufferObject != null)
            {
                VertexBufferObject.Bind();
                VertexBufferObject.DrawElements();
                VertexBufferObject.UnBind();
            }
        }

        public void SetSize(float minSize, float maxSize)
        {
            MinSize = Vector3.One * minSize;
            MaxSize = Vector3.One * maxSize;
        }

        public void SetSize(Vector3 minSize, Vector3 maxSize)
        {
            MinSize = minSize;
            MaxSize = maxSize;
        }

        public ManaMeshCpuData GetOrCreateCpuData()
        {
            if (CpuData != null)
            {
                return CpuData;
            }

            if (DynamicGeometryContainer.Verts.Count > 0)
            {
                CpuData = new ManaMeshCpuData
                {
                    Indexes = DynamicGeometryContainer.Indexes.ToArray(),
                    Positions = DynamicGeometryContainer.Positions.ToArray(),
                    Normals = DynamicGeometryContainer.Normals.ToArray(),
                    Colors = DynamicGeometryContainer.Colors.ToArray(),
                    TextureCoordinates0 = DynamicGeometryContainer.TexCoords0.ToArray(),
                    TextureCoordinates1 = DynamicGeometryContainer.TexCoords1.ToArray(),
                    AlbedoTexturePath = null,
                    AlbedoFactor = null
                };

                return CpuData;
            }

            if (VertexBufferObject != null)
            {
                CpuData = new ManaMeshCpuData
                {
                    Indexes = VertexBufferObject.indexes.ToArray(),
                    Positions = VertexBufferObject.positions.ToArray(),
                    Normals = VertexBufferObject.normals.ToArray(),
                    Colors = VertexBufferObject.colors.ToArray(),
                    TextureCoordinates0 = VertexBufferObject.textureCoordinates0.ToArray(),
                    TextureCoordinates1 = VertexBufferObject.textureCoordinates1.ToArray(),
                    AlbedoTexturePath = null,
                    AlbedoFactor = null
                };

                return CpuData;
            }

            throw new InvalidOperationException($"Mesh '{Name}' does not have CPU geometry data available.");
        }

        internal void SetCpuData(ManaMeshCpuData cpuData)
        {
            CpuData = cpuData;
        }

    }
}
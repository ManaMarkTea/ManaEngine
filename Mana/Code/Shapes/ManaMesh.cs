// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using Mana.Code.Nodes;
using Mana.Code.Graphics;
using Mana.Code.Shapes.Modifiers;
using ManaCore.Graphics;

namespace Mana.Code.Shapes
{

    public class ManaMesh : ManaObject
    {
        public ManaMaterial[] Materials;
        protected Vector3 MinSize;
        protected Vector3 MaxSize;

        public ManaMeshCpuData? CpuData { get; private set; }
        public string RenderKey { get; } = Guid.NewGuid().ToString("N");
        public ulong CpuDataVersion { get; private set; }

        private ManaVertexBufferObject? vertexBufferObject;

        public IManaGpuMesh? GpuMesh { get; private set; }

        public IManaImmediateGpuMesh? ImmediateGpuMesh => GpuMesh as IManaImmediateGpuMesh;

        public ManaVertexBufferObject? VertexBufferObject
        {
            get => vertexBufferObject;
            internal set
            {
                vertexBufferObject = value;
                GpuMesh = value == null ? null : new ManaOpenGlMesh(value);
            }
        }

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
            // Snapshotted: ImmediateGpuMesh recomputes from GpuMesh on every read, and GpuMesh is nulled
            // whenever VertexBufferObject is reassigned (procedural rebuilds, planet patch splits). Testing
            // the property and then dereferencing it again is a null between the two.
            var gpuMesh = ImmediateGpuMesh;
            if (gpuMesh != null)
            {
                ManaMaterial material = Materials[0];
                if (meshRenderer != null)
                {
                    material = meshRenderer.Material;
                }

                material.Bind(owner.WorldTransform, owner);

                gpuMesh.Draw();

                material.UnBind();
            }
        }

        public void RenderDirectly()
        {
            var gpuMesh = ImmediateGpuMesh;
            if (gpuMesh != null)
            {
                gpuMesh.Draw();
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
                var meshData = new ManaMeshCpuData
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
                SetCpuData(meshData);

                return meshData;
            }

            if (VertexBufferObject != null)
            {
                var meshData = new ManaMeshCpuData
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
                SetCpuData(meshData);

                return meshData;
            }

            throw new InvalidOperationException($"Mesh '{Name}' does not have CPU geometry data available.");
        }

        // Public so procedural mesh types defined outside this assembly (e.g. ManaGeoTools) can
        // rebuild themselves after their first render — GetOrCreateCpuData() below only ever builds
        // CpuData lazily once (it returns the cached value immediately if non-null), so a mesh type
        // that can't call this directly has no way to make a second GenerateData() pass take effect.
        public void SetCpuData(ManaMeshCpuData cpuData)
        {
            CpuData = cpuData;
            CpuDataVersion++;
        }

    }
}

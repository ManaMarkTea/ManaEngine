// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using Mana.Code.Materials;
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


        public ManaDynamicGeometryContainer DynamicGeometryContainer { get; internal set; }

        public List<ManaMeshModifier> Modifiers;

        public ManaMesh(ManaContext context) : base(context)
        {
            Context = context;
            Modifiers = new List<ManaMeshModifier>();
            Materials = new ManaMaterial[0];
            DynamicGeometryContainer = new ManaDynamicGeometryContainer();
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

        /// <summary>
        /// Snapshots <see cref="DynamicGeometryContainer"/> into <see cref="CpuData"/> and then releases the
        /// build-time object graph. The closing move of a GenerateData override.
        /// <para>
        /// Releasing is the point. The container holds a heap object per vertex and two per quad, all of it
        /// kept alive for the mesh's whole life even though nothing reads it once CpuData exists -- the
        /// backends, the picker and the physics baker all go through CpuData's flat arrays. For a streaming
        /// planet that is the dominant term in the managed heap, and it is what makes pauses long: collection
        /// pause time scales with heap size, so a multi-gigabyte heap of vertex objects turns an ordinary gen1
        /// collection into a 30-60ms frame hitch. Measured going from ~2.7GB to a small fraction of it.
        /// </para>
        /// <para>
        /// Not for a mesh that keeps editing its own build data (see ManaVoxelShape, which clears and refills
        /// the same container per edit).
        /// </para>
        /// </summary>
        public ManaMeshCpuData PublishGeneratedGeometry()
        {
            var cpuData = DynamicGeometryContainer.CreateCpuData();
            SetCpuData(cpuData);
            ReleaseGeometryBuildData();
            return cpuData;
        }

        /// <summary>
        /// Drops the build-time vertex/face graph, leaving an empty container rather than null so nothing has
        /// to null-check it. Safe once <see cref="CpuData"/> is set; a later RebuildGeometry rebuilds the
        /// container from scratch anyway.
        /// </summary>
        public void ReleaseGeometryBuildData()
        {
            // A fresh instance rather than Clear(): the whole old graph becomes garbage in one piece, and the
            // Lists do not keep their (large) backing arrays alive at their high-water mark.
            DynamicGeometryContainer = new ManaDynamicGeometryContainer();
        }

    }
}

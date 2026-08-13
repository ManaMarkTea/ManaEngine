using System.Diagnostics.CodeAnalysis;

using System.Diagnostics;

namespace Mana
{
    public struct ManaVoxelData : IEqualityComparer<ManaVoxelData>
    {
        public Vector3 Position;
        public Vector4 Color;

        bool Equals(ManaVoxelData x, ManaVoxelData y)
        {
            return x.Position.AlmostEquals(y.Position);
        }

        public int GetHashCode([DisallowNull] ManaVoxelData obj)
        {
            return obj.Position.GetHashCode();
        }

        bool IEqualityComparer<ManaVoxelData>.Equals(ManaVoxelData x, ManaVoxelData y)
        {
            return x.Equals(y);
        }
    }

    public class ManaVoxelShape : ManaProceduralMesh
    {
        public const float DefaultVoxelRoughness = 0.72f;

        protected List<ManaVoxelData> Voxels = new List<ManaVoxelData>();

        protected List<ManaVoxelData> AddVoxels = new List<ManaVoxelData>();
        private readonly HashSet<(int X, int Y, int Z)> _voxelKeys = new HashSet<(int X, int Y, int Z)>();
        private readonly HashSet<(int X, int Y, int Z)> _pendingVoxelKeys = new HashSet<(int X, int Y, int Z)>();
        private static readonly (int X, int Y, int Z)[] FaceNeighborKeyOffsets =
        {
            (0, 0, -2),
            (2, 0, 0),
            (0, 0, 2),
            (-2, 0, 0),
            (0, 2, 0),
            (0, -2, 0)
        };

        public int VoxelCount => Voxels.Count;
        public int PendingVoxelCount => AddVoxels.Count;
        public int DuplicateVoxelRejectCount { get; private set; }
        public int LastInputVoxelCount { get; private set; }
        public int LastDuplicateVoxelCount { get; private set; }
        public int LastVisibleFaceCount { get; private set; }
        public int LastCulledFaceCount { get; private set; }
        public int LastGeneratedVertexCount { get; private set; }
        public int LastNaiveVertexCount { get; private set; }
        public int LastSavedVertexCount { get; private set; }
        public IReadOnlyList<ManaVoxelData> CommittedVoxels => Voxels;
        public IReadOnlyList<ManaVoxelData> PendingVoxels => AddVoxels;

        private static (int X, int Y, int Z) ToVoxelKey(Vector3 position)
        {
            return (
                (int)MathF.Round(position.X * 2.0f),
                (int)MathF.Round(position.Y * 2.0f),
                (int)MathF.Round(position.Z * 2.0f)
            );
        }


        public ManaVoxelShape(ManaContext context) : base(context)
        {
            DefaultVertexColor = Colors.White;
            GenerateData();
        }

        public override ManaMaterial GenerateMaterial()
        {
            var material = base.GenerateMaterial();
            ConfigureVoxelPbrMaterial(Context, material);
            return material;
        }

        public static void ConfigureVoxelPbrMaterial(ManaContext context, ManaMaterial material)
        {
            material.Shader = new ManaPbrShader(context);
            material.Metallic.Value = 0.0f;
            material.Roughness.Value = DefaultVoxelRoughness;
            material.Occlusion.Value = 1.0f;
        }

        public event Action<ManaVoxelData>? OnVoxelAdded;

        public void AddVoxel(Vector3 position, Vector4 color)
        {
            var newVoxelData = new ManaVoxelData { Position = position, Color = color };
            var key = ToVoxelKey(position);

            if (_voxelKeys.Contains(key) || _pendingVoxelKeys.Contains(key))
            {
                DuplicateVoxelRejectCount++;
                return;
            }

            AddVoxels.Add(newVoxelData);
            _pendingVoxelKeys.Add(key);
            OnVoxelAdded?.Invoke(newVoxelData);
        }

        public void CommitData()
        {
            GenerateData();
        }

        public void ReplaceCommittedVoxels(IEnumerable<ManaVoxelData> voxels, bool logStats = true)
        {
            Voxels.Clear();
            AddVoxels.Clear();
            _voxelKeys.Clear();
            _pendingVoxelKeys.Clear();

            var inputVoxelCount = 0;
            var duplicateVoxelCount = 0;

            foreach (var voxel in voxels)
            {
                inputVoxelCount++;
                var key = ToVoxelKey(voxel.Position);
                if (!_voxelKeys.Add(key))
                {
                    DuplicateVoxelRejectCount++;
                    duplicateVoxelCount++;
                    continue;
                }

                Voxels.Add(voxel);
            }

            RebuildGeometryFromCommittedVoxels(
                "replace",
                inputVoxelCount,
                duplicateVoxelCount,
                logStats);
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            var inputVoxelCount = Voxels.Count + AddVoxels.Count;
            var duplicateVoxelCount = 0;

            foreach (var voxel in AddVoxels)
            {
                var key = ToVoxelKey(voxel.Position);
                if (!_voxelKeys.Add(key))
                {
                    DuplicateVoxelRejectCount++;
                    duplicateVoxelCount++;
                    continue;
                }

                Voxels.Add(voxel);
            }

            AddVoxels.Clear();
            _pendingVoxelKeys.Clear();
            RebuildGeometryFromCommittedVoxels(
                "commit",
                inputVoxelCount,
                duplicateVoxelCount,
                inputVoxelCount > 0 || duplicateVoxelCount > 0);

            // TODO: Reuse the existing buffers better
        }

        private void RebuildGeometryFromCommittedVoxels(
            string reason,
            int inputVoxelCount,
            int duplicateVoxelCount,
            bool logStats)
        {
            DynamicGeometryContainer.Clear();

            var visibleFaceCount = 0;
            foreach (var voxel in Voxels)
            {
                visibleFaceCount += AddExposedVoxelFaces(voxel);
            }

            LastInputVoxelCount = inputVoxelCount;
            LastDuplicateVoxelCount = duplicateVoxelCount;
            LastVisibleFaceCount = visibleFaceCount;
            LastCulledFaceCount = Math.Max(0, Voxels.Count * 6 - visibleFaceCount);
            LastGeneratedVertexCount = DynamicGeometryContainer.Verts.Count;
            LastNaiveVertexCount = inputVoxelCount * 24;
            LastSavedVertexCount = Math.Max(0, LastNaiveVertexCount - LastGeneratedVertexCount);

            SetCpuData(DynamicGeometryContainer.CreateCpuData());
            if (Context.OpenGl != null)
            {
                if (VertexBufferObject != null)
                {
                    var meshData = DynamicGeometryContainer.CreateCpuData();
                    VertexBufferObject.indexes = meshData.Indexes;
                    VertexBufferObject.positions = meshData.Positions;
                    VertexBufferObject.normals = meshData.Normals;
                    VertexBufferObject.colors = meshData.Colors;
                    VertexBufferObject.textureCoordinates0 = meshData.TextureCoordinates0;
                    VertexBufferObject.textureCoordinates1 = meshData.TextureCoordinates1;
                    VertexBufferObject.ConstructVBO();
                }
                else
                {
                    VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);
                }
            }

            if (logStats && (inputVoxelCount > 0 || duplicateVoxelCount > 0))
            {
                Trace.WriteLine(
                    $"ManaVoxelShape {reason}: inputVoxels={inputVoxelCount}, uniqueVoxels={Voxels.Count}, " +
                    $"duplicateVoxels={duplicateVoxelCount}, visibleFaces={LastVisibleFaceCount}, " +
                    $"culledFaces={LastCulledFaceCount}, verts={LastGeneratedVertexCount}/{LastNaiveVertexCount}, " +
                    $"dedupedVerts={LastSavedVertexCount}");
            }
        }

        private int AddExposedVoxelFaces(ManaVoxelData voxel)
        {
            var key = ToVoxelKey(voxel.Position);
            var addedFaceCount = 0;

            for (var faceIndex = 0; faceIndex < 6; faceIndex++)
            {
                var neighborOffset = FaceNeighborKeyOffsets[faceIndex];
                var neighborKey = (
                    key.X + neighborOffset.X,
                    key.Y + neighborOffset.Y,
                    key.Z + neighborOffset.Z);

                if (_voxelKeys.Contains(neighborKey))
                {
                    continue;
                }

                DynamicGeometryContainer.AddQuad(
                    voxel.Position,
                    Vector3.One,
                    ManaDynamicGeometryContainer.CubeFaceRotations[faceIndex],
                    ManaDynamicGeometryContainer.CubeFaceScales[faceIndex],
                    voxel.Color);
                addedFaceCount++;
            }

            return addedFaceCount;
        }

    }
}

using System.Diagnostics.CodeAnalysis;

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
        protected List<ManaVoxelData> Voxels = new List<ManaVoxelData>();

        protected List<ManaVoxelData> AddVoxels = new List<ManaVoxelData>();
        private readonly HashSet<(int X, int Y, int Z)> _voxelKeys = new HashSet<(int X, int Y, int Z)>();
        private readonly HashSet<(int X, int Y, int Z)> _pendingVoxelKeys = new HashSet<(int X, int Y, int Z)>();

        public int VoxelCount => Voxels.Count;
        public int PendingVoxelCount => AddVoxels.Count;
        public int DuplicateVoxelRejectCount { get; private set; }
        public IReadOnlyList<ManaVoxelData> CommittedVoxels => Voxels;

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

        public void ReplaceCommittedVoxels(IEnumerable<ManaVoxelData> voxels)
        {
            Voxels.Clear();
            AddVoxels.Clear();
            _voxelKeys.Clear();
            _pendingVoxelKeys.Clear();
            DynamicGeometryContainer.Clear();

            foreach (var voxel in voxels)
            {
                Voxels.Add(voxel);
                _voxelKeys.Add(ToVoxelKey(voxel.Position));
                DynamicGeometryContainer.AddCube(voxel.Position, Vector3.One, voxel.Color);
            }

            VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);
        }

        protected override void GenerateData()
        {
            base.GenerateData();

			Vector3 c = Vector3.One;

			foreach (var voxel in AddVoxels)
            {
                Voxels.Add(voxel);
                _voxelKeys.Add(ToVoxelKey(voxel.Position));
                DynamicGeometryContainer.AddCube(voxel.Position, c, voxel.Color);
			}            

            AddVoxels.Clear();
            _pendingVoxelKeys.Clear();
            VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);

            // TODO: Reuse the existing buffers better
        }

    }
}

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


        public ManaVoxelShape(ManaContext context) : base(context)
        {
            DefaultVertexColor = Colors.White;
            GenerateData();
        }

        public event Action<ManaVoxelData> OnVoxelAdded;
        public event Action<ManaVoxelData> OnVoxelRemoved;

        public void AddVoxel(Vector3 position, Vector4 color)
        {
            var newVoxelData = new ManaVoxelData { Position = position, Color = color };
            
            if (Voxels.Contains(newVoxelData))
            {
                return;
            }

            AddVoxels.Add(newVoxelData);
            OnVoxelAdded?.Invoke(newVoxelData);
        }

        public void CommitData()
        {
            GenerateData();
        }

        protected override void GenerateData()
        {
            base.GenerateData();

			Vector3 c = Vector3.One;

			foreach (var voxel in AddVoxels)
            {
                Voxels.Add(voxel);
                DynamicGeometryContainer.AddCube(voxel.Position, c, voxel.Color);
			}            

            AddVoxels.Clear();         
            VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);

            // TODO: Reuse the existing buffers better
        }

    }
}

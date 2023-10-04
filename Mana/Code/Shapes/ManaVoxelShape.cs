namespace Mana
{
    public struct ManaVoxelData
    {
        public Vector3 Position;
        public Vector4 Color;
    }

    public class ManaVoxelShape : ManaProceduralMesh
    {
        protected List<ManaVoxelData> Voxels = new List<ManaVoxelData>();

        public ManaVoxelShape(ManaContext context) : base(context)
        {
            DefaultVertexColor = Colors.White;
            GenerateData();
        }

        public void AddVoxel(Vector3 position, Vector4 color)
        {
            Voxels.Add(new ManaVoxelData {  Position = position, Color = color });
        }

        public void CommitData()
        {
            GenerateData();
        }

        protected override void GenerateData()
        {
            base.GenerateData();


			Vector3 c = Vector3.One;

			foreach (var voxel in Voxels)
            {
				DynamicGeometryContainer.AddCube(voxel.Position, c, voxel.Color);
			}            
         
            VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);
        }

    }
}

namespace Mana
{
    public class ManaPyramid : ManaProceduralMesh
    {
        public Vector3 CubeSize { get; set; }

        public ManaPyramid(ManaContext context, Vector4 color, Vector3 size) : base(context)
        {
            CubeSize = size;
            DefaultVertexColor = color;
            GenerateData();
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            MinSize.X = MinSize.Y = MinSize.Z = float.MaxValue;
            MaxSize.X = MaxSize.Y = MaxSize.Z = float.MinValue;

            MinSize = -CubeSize;
            MaxSize = CubeSize;

            var rotations = new Matrix4x4[]
            {
                 Matrix4x4.CreateRotationY(0 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(2 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(3 * 90.0f.ToRadians(), Vector3.Zero),

                 Matrix4x4.CreateRotationX(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationX(-1 * 90.0f.ToRadians(), Vector3.Zero),
            };

            var scaleRotations = new Matrix4x4[]
            {
                 Matrix4x4.CreateRotationY(0 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(0 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(1 * 90.0f.ToRadians(), Vector3.Zero),

                 Matrix4x4.CreateRotationX(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationX(1 * 90.0f.ToRadians(), Vector3.Zero),
            };

            DynamicGeometryContainer.AddTriangle(CubeSize.Y, CubeSize.X, rotations[0], scaleRotations[0], this.DefaultVertexColor);
            DynamicGeometryContainer.AddTriangle(CubeSize.Y, CubeSize.X, rotations[1], scaleRotations[1], this.DefaultVertexColor);
            DynamicGeometryContainer.AddTriangle(CubeSize.Y, CubeSize.X, rotations[2], scaleRotations[2], this.DefaultVertexColor);
            DynamicGeometryContainer.AddTriangle(CubeSize.Y, CubeSize.X, rotations[3], scaleRotations[3], this.DefaultVertexColor);

            DynamicGeometryContainer.AddQuad(Vector3.Zero, CubeSize, rotations[5], scaleRotations[5], this.DefaultVertexColor);
            VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);
        }

    }
}

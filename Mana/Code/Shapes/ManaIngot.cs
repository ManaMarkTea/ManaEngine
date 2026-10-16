namespace Mana.Code.Shapes
{
    public class ManaIngot : ManaProceduralMesh
    {
        public Vector3 CubeSize { get; set; }

        public ManaIngot(ManaContext context, Vector4 color, Vector3 size) : base(context)
        {
            CubeSize = size;
            DefaultVertexColor = color;
            GenerateData();
        }

        protected override void GenerateData()
        {
            base.GenerateData();

            Vector3 c = CubeSize;
            MinSize.X = MinSize.Y = MinSize.Z = float.MaxValue;
            MaxSize.X = MaxSize.Y = MaxSize.Z = float.MinValue;
            MinSize = -c;
            MaxSize = c;

            var rotations = new Matrix4x4[]
            {
                 Matrix4x4.CreateRotationY(0 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(2 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationY(3 * 90.0f.ToRadians(), Vector3.Zero),

                 Matrix4x4.CreateRotationX(-1 * 90.0f.ToRadians(), Vector3.Zero),
                 Matrix4x4.CreateRotationX(1 * 90.0f.ToRadians(), Vector3.Zero),
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


            ManaGeometryQuad builtQuad = new ManaGeometryQuad();
            for (int i = 0; i < 6; i++)
            {
                var rotation = rotations[i];
                var scaleRotation = scaleRotations[i];
                builtQuad = DynamicGeometryContainer.AddQuad(Vector3.Zero, c, rotation, scaleRotation, this.DefaultVertexColor)[0];
            }

            builtQuad.A.Position = builtQuad.A.Position - (builtQuad.A.Position - builtQuad.Center).Normalized() * 0.25f;
            builtQuad.B.Position = builtQuad.B.Position - (builtQuad.B.Position - builtQuad.Center).Normalized() * 0.25f;
            builtQuad.C.Position = builtQuad.C.Position - (builtQuad.C.Position - builtQuad.Center).Normalized() * 0.25f;
            builtQuad.D.Position = builtQuad.D.Position - (builtQuad.D.Position - builtQuad.Center).Normalized() * 0.25f;


        }

    }
}

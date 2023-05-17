using Mana.Code;
using Mana.Code.Materials;
using Mana.Code.Shapes;
using Silk.NET.Assimp;
using Silk.NET.Core.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class ManaCube : ManaProceduralMesh
    {
        public Vector3 CubeSize { get; set; }

        public ManaCube(ManaContext context, Vector4 color, Vector3 size) : base(context)
        {
            CubeSize = size;
            DefaultVertexColor = color;
            GenerateData();
        }

        protected override void GenerateData()
        {
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


            for ( int i = 0; i < 6; i++)
            {
                var rotation = rotations[i];
                var scaleRotation = scaleRotations[i];
                DynamicGeometryContainer.AddQuad(c, rotation, scaleRotation, this.DefaultVertexColor);
            }

            VertexBufferObject = DynamicGeometryContainer.CreateVertexBufferObject(Context);            
        }

    }
}

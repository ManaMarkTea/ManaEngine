using Mana.Code;
using Silk.NET.OpenGL;

namespace Mana
{
    public class ManaTextureVector4 : ManaTextureDynamic
    {
        public Vector4 Value = Vector4.Zero;
        private Vector4? LoadedValue = null;


        public ManaTextureVector4(ManaContext context, Vector4 color) : base(context, 4)
        {
            Value = color;
            PixelData = new byte[1];
            Width = 1; Height = 1;
            ElementCount = 4;

            InternalDataFormat = InternalFormat.Rgba16f;
            InternalPixelFormat = PixelFormat.Rgba;
            InternalPixelType = GLEnum.Float;

            Load();
        }

        protected override void LoadBuffer()
        {
            base.LoadBuffer();
            buffer[0] = Value.X;
            buffer[1] = Value.Y;
            buffer[2] = Value.Z;
            buffer[3] = Value.W;
            LoadedValue = Value;
        }

        public override bool IsDirty()
        {
            return LoadedValue != Value;
        }
    }
}

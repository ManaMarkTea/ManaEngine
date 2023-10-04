using Silk.NET.OpenGL;

namespace Mana
{
    public class ManaTextureVector3 : ManaTextureDynamic
    {
        public Vector3 Value = Vector3.Zero;
        private Vector3? LoadedValue = null;


        public ManaTextureVector3(ManaContext context, Vector3 color) : base(context, 3)
        {
            Value = color;
            PixelData = new byte[1];
            Width = 1; Height = 1;
            ElementCount = 3;

            InternalDataFormat = InternalFormat.Rgb16f;
            InternalPixelFormat = PixelFormat.Rgb;
            InternalPixelType = GLEnum.Float;

            Load();
        }

        protected override void LoadBuffer()
        {
            base.LoadBuffer();
            buffer[0] = Value.X;
            buffer[1] = Value.Y;
            buffer[2] = Value.Z;
            LoadedValue = Value;
        }

        public override bool IsDirty()
        {
            return LoadedValue != Value;
        }
    }
}

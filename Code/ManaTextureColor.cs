using Mana.Code;
using Silk.NET.OpenGL;

namespace Mana
{
    public class ManaTextureColor : ManaTexture
    {
        public Vector4 Color = Colors.Magenta;
        private Vector4? LoadedColor = null;

        private float[] buffer = new float[4];

        public ManaTextureColor(ManaContext context, Vector4 color) : base(context)
        {            
            Color = color;
            PixelData = new byte[1];
            Width = 1; Height = 1;
            ElementCount = 4;

            InternalDataFormat = InternalFormat.Rgb16f;
            InternalPixelFormat = PixelFormat.Rgba;
            InternalPixelType = GLEnum.Float;

            Load();
        }

        protected override void LoadTextureData()
        {
            var gl = Context.gl;            
            SizeInBytes = (int)Width * (int)Height * 4;
            AllocateEmptyTextImage2D(gl, TextureTargetType, Width, Height);
            PushToTexture();
        }

        protected void PushToTexture()
        {
            var gl = Context.gl;
            buffer[0] = Color.X;
            buffer[1] = Color.Y;
            buffer[2] = Color.Z;
            buffer[3] = Color.W;
            SubTextImage2D(gl, TextureTargetType, 0, 0, 1, 1, new ReadOnlySpan<float>(buffer));
            LoadedColor = Color;
        }

        public override void Bind(int channel)
        {
            base.Bind(channel);

            if (LoadedColor != Color)
            {
                PushToTexture();
            }
        }

    }
}

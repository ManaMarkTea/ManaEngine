using Mana.Code;
using Silk.NET.OpenGL;

namespace Mana
{
    public class ManaTextureSingle : ManaTextureDynamic
    {
        public float Value = 0;
        private float? LoadedValue = null;

        public ManaTextureSingle(ManaContext context, double color) : this(context, (float)color)
        {
        }

        public ManaTextureSingle(ManaContext context, float color) : base(context, 1)
        {
            Value = color;
            PixelData = new byte[1];
            Width = 1; Height = 1;
            ElementCount = 1;

            InternalDataFormat = InternalFormat.Red;
            InternalPixelFormat = PixelFormat.Red;
            InternalPixelType = GLEnum.Float;

            Load();
        }

        protected override void LoadBuffer()
        {
            base.LoadBuffer();
            buffer[0] = Value;
            LoadedValue = Value;
        }

        public override bool IsDirty()
        {
            return LoadedValue != Value;
        }
    }
}

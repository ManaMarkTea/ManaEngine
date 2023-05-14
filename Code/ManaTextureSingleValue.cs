using Mana.Code;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp.Formats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class ManaTextureSingleValue : ManaTexture
    {
        public double Value = 0.5;
        private double? LoadedValue = null;

        private float[] buffer = new float[1];

        public ManaTextureSingleValue(ManaContext context, double value) : base(context)
        {
            Value = value;
            PixelData = new byte[1];
            Width = 1; Height = 1;
            ElementCount = 4;

            //InternalDataFormat = InternalFormat.Rgb16f;
            //InternalPixelFormat = PixelFormat.Rgba;
            //InternalPixelType = GLEnum.Float;

            //InternalDataFormat = InternalFormat.Rgb16f;
            InternalDataFormat = InternalFormat.Red;
            InternalPixelFormat = PixelFormat.Rgb;
            InternalPixelType = GLEnum.Float;
            Load();
        }

        protected override void LoadTextureData()
        {
            var gl = Context.gl;            
            SizeInBytes = (int)Width * (int)Height * 1;
            AllocateEmptyTextImage2D(gl, TextureTargetType, Width, Height);
            PushToTexture();
        }

        protected void PushToTexture()
        {
            var gl = Context.gl;
            buffer[0] = (float)Value;           
            SubTextImage2D(gl, TextureTargetType, 0, 0, 1, 1, new ReadOnlySpan<float>(buffer));
            LoadedValue = Value;
        }

        public override void Bind(int channel)
        {
            base.Bind(channel);
            if (LoadedValue != Value)
            {
                PushToTexture();
            }
        }

    }
}

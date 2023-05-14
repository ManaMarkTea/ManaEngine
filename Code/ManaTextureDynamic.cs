using Mana.Code;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp.Formats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Mana
{
    public class ManaTextureDynamic : ManaTexture
    {
        protected float[] buffer;

        public ManaTexture? TextureValue { get; set; }

        public ManaTextureDynamic(ManaContext context, int floatCount) : base(context)
        {
            buffer = new float[floatCount];
        }

        protected override void LoadTextureData()
        {
            var gl = Context.gl;
            SizeInBytes = (int)Width * (int)Height * buffer.Length;
            AllocateEmptyTextImage2D(gl, TextureTargetType, Width, Height);
            PushToTexture();
        }

        protected virtual void LoadBuffer() 
        {
        }        

        protected void PushToTexture()
        {
            var gl = Context.gl;
            LoadBuffer();
            SubTextImage2D(gl, TextureTargetType, 0, 0, 1, 1, new ReadOnlySpan<float>(buffer));
        }

        public virtual bool IsDirty() { return false; }

        public override void Bind(int channel)
        {
            if (TextureValue != null)
            {
                TextureValue.Bind(channel);
                return;
            }

            base.Bind(channel);
            if (IsDirty())
            {
                PushToTexture();
            }
        }
    }
}

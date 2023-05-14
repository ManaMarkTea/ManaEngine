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
    public class ManaTexture : ManaObject, ICloneable
    {
        public string OriginalFilename { get; set; }
        public uint Width { get; set; }
        public uint Height { get; set; }
        public int ElementCount { get; set; }
        public int SizeInBytes { get; set; }
        public byte[] PixelData { get; set; }
        public Matrix4x4 Transform { get; set; }
        public uint Handle { get; set; }
        public bool IsLoaded { get; set; }
        public bool IsFound { get; set; }
        public bool IsWrapU { get; set; }
        public bool IsWrapV { get; set; }

        public bool IsCube { get; set; }
        public TextureMinFilter MinFilter { get; set; }
        public TextureMagFilter MagFilter { get; set; }

        public bool HasMipMaps { get; set; }
        public bool isBlendRequired { get; set; }

        public GLEnum FboAttachmentType { get; set; }

        public InternalFormat InternalDataFormat { get; set; }
        public PixelFormat InternalPixelFormat { get; set; }
        public GLEnum InternalPixelType { get; set; }

        public TextureTarget TextureTargetType
        {
            get
            {
                TextureTarget targetType = TextureTarget.Texture2D;
                if (IsCube)
                {
                    targetType = TextureTarget.TextureCubeMap;
                }
                return targetType;
            }
        }

        public ManaTexture(ManaContext context) : base(context)
        {            
            PixelData = new byte[0];
            Width = 0; Height = 0;
            ElementCount = 4;
            Handle = 0;
            Name = "Unknown";
            OriginalFilename = "";
            IsFound = false;
            IsLoaded = false;
            IsWrapU = true;
            IsWrapV = true;
            MinFilter = TextureMinFilter.Nearest;
            MagFilter = TextureMagFilter.Nearest;

            InternalDataFormat = InternalFormat.Rgba8;
            InternalPixelFormat = PixelFormat.Rgba;
            InternalPixelType = GLEnum.UnsignedByte;
        }

        public void UpdateParameters()
        {
            var gl = Context.gl;

            var targetType = TextureTargetType;

            // Load texture
            gl.BindTexture(targetType, Handle);
            gl.TexParameter(targetType, TextureParameterName.TextureWrapS, IsWrapU ? (int)TextureWrapMode.Repeat : (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(targetType, TextureParameterName.TextureWrapT, IsWrapV ? (int)TextureWrapMode.Repeat : (int)TextureWrapMode.ClampToEdge);
            gl.TexParameter(targetType, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

            gl.TexParameter(targetType, TextureParameterName.TextureMinFilter, (uint)MinFilter);
            gl.TexParameter(targetType, TextureParameterName.TextureMagFilter, (uint)MagFilter);
        }

        public void UpdateTextureSize()
        {
            var gl = Context.gl;

            SizeInBytes = (int)Width * (int)Height * 4;
            gl.BindTexture(TextureTargetType, Handle);

            if (!IsCube)
            {
                AllocateEmptyTextImage2D(gl, TextureTargetType, Width, Height);
            }
            else
            {
                for (int i = 0; i < 6; i++)
                {
                    AllocateEmptyTextImage2D(gl, TextureTarget.TextureCubeMapPositiveX + i, Width, Height);
                }
            }
        }


        public virtual void Load()
        {
            var gl = Context.gl;

            if (IsLoaded == false)
            {
                if (Handle == 0)
                {
                    Handle = gl.GenTexture();
                }

                gl.BindTexture(TextureTargetType, Handle);

                UpdateParameters();

                LoadTextureData();

                if (HasMipMaps)
                {
                    gl.GenerateMipmap(TextureTargetType);
                }

                IsLoaded = true;
            }
        }

        protected virtual void LoadTextureData()
        {
            var gl = Context.gl;

            if (string.IsNullOrEmpty(OriginalFilename))
            {
                // Create an empty texture with allocated space (Common for Render Targets)
                UpdateTextureSize();
            }
            else
            {
                DecoderOptions options = new DecoderOptions();
                using (var img = Image.Load<Rgba32>(options, OriginalFilename))
                {
                    img.Mutate(x => x.Flip(FlipMode.Vertical));
                    Width = (uint)img.Width;
                    Height = (uint)img.Height;
                    SizeInBytes = img.Width * img.Height * (img.PixelType.BitsPerPixel / 4);
                    AllocateEmptyTextImage2D(gl, TextureTargetType, (uint)img.Width, (uint)img.Height);
                    img.ProcessPixelRows(accessor => ProcessPixelRows(accessor, gl));
                }
            }
        }

        protected unsafe void AllocateEmptyTextImage2D(GL gl, TextureTarget textureType, uint width, uint height)
        {
            gl.TexImage2D(textureType, 0, InternalDataFormat, width, height, 0, InternalPixelFormat, InternalPixelType, null);
        }

        protected unsafe void SubTextImage2D(GL gl, TextureTarget textureType, int x, int y, uint width, uint height, ReadOnlySpan<byte> data)
        {
            gl.TexSubImage2D(textureType, 0, x, y, width, height, InternalPixelFormat, InternalPixelType, data);
        }

        protected unsafe void SubTextImage2D(GL gl, TextureTarget textureType, int x, int y, uint width, uint height, ReadOnlySpan<float> data)
        {
            gl.TexSubImage2D(textureType, 0, x, y, width, height, InternalPixelFormat, InternalPixelType, data);
        }
         
        private unsafe void ProcessPixelRows(PixelAccessor<Rgba32> accessor, GL gl)
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                fixed (void* data = accessor.GetRowSpan(y))
                {
                    gl.TexSubImage2D(TextureTargetType, 0, 0, y, (uint)accessor.Width, 1, InternalPixelFormat, InternalPixelType, data);
                }
            }
        }

        public virtual void Bind(int channel)
        {
            var gl = Context.gl;

            switch (channel)
            {
                case 0: gl.ActiveTexture(TextureUnit.Texture0); break;
                case 1: gl.ActiveTexture(TextureUnit.Texture1); break;
                case 2: gl.ActiveTexture(TextureUnit.Texture2); break;
                case 3: gl.ActiveTexture(TextureUnit.Texture3); break;
                case 4: gl.ActiveTexture(TextureUnit.Texture4); break;

                case 5: gl.ActiveTexture(TextureUnit.Texture5); break;
                case 6: gl.ActiveTexture(TextureUnit.Texture6); break;
                case 7: gl.ActiveTexture(TextureUnit.Texture7); break;
                case 8: gl.ActiveTexture(TextureUnit.Texture8); break;
                case 9: gl.ActiveTexture(TextureUnit.Texture9); break;
            }
            
            if (Handle > 0)
            {                                
                gl.BindTexture(TextureTargetType, Handle);
            }
        }

        public void UnBind(int channel)
        {
            var gl = Context.gl;

            switch (channel)
            {
                case 0: gl.ActiveTexture(TextureUnit.Texture0); break;
                case 1: gl.ActiveTexture(TextureUnit.Texture1); break;
                case 2: gl.ActiveTexture(TextureUnit.Texture2); break;
                case 3: gl.ActiveTexture(TextureUnit.Texture3); break;
                case 4: gl.ActiveTexture(TextureUnit.Texture4); break;

                case 5: gl.ActiveTexture(TextureUnit.Texture5); break;
                case 6: gl.ActiveTexture(TextureUnit.Texture6); break;
                case 7: gl.ActiveTexture(TextureUnit.Texture7); break;
                case 8: gl.ActiveTexture(TextureUnit.Texture8); break;
                case 9: gl.ActiveTexture(TextureUnit.Texture9); break;
            }

            gl.BindTexture(TextureTargetType, 0);
            gl.ActiveTexture(TextureUnit.Texture0);
        }

        public object Clone()
        {
            var copy = MemberwiseClone() as ManaTexture;
            if (copy != null)
            {
                copy.Handle = 0;
                copy.IsLoaded = false;
            }
            return copy;
        }
    }
}

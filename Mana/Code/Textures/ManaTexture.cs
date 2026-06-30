using Mana.Code.Graphics;
using ManaCore.Graphics;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mana
{
    public class ManaTexture : ManaObject, ICloneable
    {
        public string OriginalFilename { get; set; }

        public Stream? PixelStream { get; set; }
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
        public IManaGpuTexture? GpuTexture { get; private set; }
        public IManaBindableGpuTexture? BindableGpuTexture => GpuTexture as IManaBindableGpuTexture;

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
            var gl = Context.RequireOpenGl();

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
            EnsureOpenGlTexture().UpdateTextureSize();
        }


        public void LoadCpuPixelData()
        {
            if (PixelStream != null)
            {
                DecoderOptions options = new DecoderOptions();
                using (var img = Image.Load<Rgba32>(options, PixelStream))
                {
                    img.Mutate(x => x.Flip(FlipMode.Vertical));
                    Width = (uint)img.Width;
                    Height = (uint)img.Height;
                    SizeInBytes = img.Width * img.Height * 4;
                    PixelData = new byte[SizeInBytes];
                    img.CopyPixelDataTo(PixelData);
                }
            }
            else if (!string.IsNullOrEmpty(OriginalFilename))
            {
                var resolvedFilename = ResolveTexturePath(OriginalFilename);
                DecoderOptions options = new DecoderOptions();
                using (var img = Image.Load<Rgba32>(options, resolvedFilename))
                {
                    img.Mutate(x => x.Flip(FlipMode.Vertical));
                    Width = (uint)img.Width;
                    Height = (uint)img.Height;
                    SizeInBytes = img.Width * img.Height * 4;
                    PixelData = new byte[SizeInBytes];
                    img.CopyPixelDataTo(PixelData);
                }
            }
        }

        public virtual void Load()
        {
            if (Context.DeferGpuUploads)
            {
                LoadCpuPixelData();
            }
            else
            {
                EnsureOpenGlTexture().Load();
            }
        }

        internal void LoadOpenGlTextureData()
        {
            LoadTextureData();
        }

        protected virtual unsafe void LoadTextureData()
        {
            var gl = Context.RequireOpenGl();

            if (PixelData != null && PixelData.Length > 0)
            {
                AllocateEmptyTextImage2D(gl, TextureTargetType, Width, Height);
                fixed (void* data = PixelData)
                {
                    gl.TexSubImage2D(TextureTargetType, 0, 0, 0, Width, Height, InternalPixelFormat, InternalPixelType, data);
                }
            }
            else if (PixelStream != null)
            {
                DecoderOptions options = new DecoderOptions();
                using (var img = Image.Load<Rgba32>(options, PixelStream))
                {
                    img.Mutate(x => x.Flip(FlipMode.Vertical));
                    Width = (uint)img.Width;
                    Height = (uint)img.Height;
                    SizeInBytes = img.Width * img.Height * (img.PixelType.BitsPerPixel / 4);
                    AllocateEmptyTextImage2D(gl, TextureTargetType, (uint)img.Width, (uint)img.Height);
                    UploadImageRows(img, gl);
                }
            }
            else if (string.IsNullOrEmpty(OriginalFilename))
            {
                // Create an empty texture with allocated space (Common for Render Targets)
                UpdateTextureSize();
            }
            else
            {
                var resolvedFilename = ResolveTexturePath(OriginalFilename);

                DecoderOptions options = new DecoderOptions();
                using (var img = Image.Load<Rgba32>(options, resolvedFilename))
                {
                    img.Mutate(x => x.Flip(FlipMode.Vertical));
                    Width = (uint)img.Width;
                    Height = (uint)img.Height;
                    SizeInBytes = img.Width * img.Height * (img.PixelType.BitsPerPixel / 4);
                    AllocateEmptyTextImage2D(gl, TextureTargetType, (uint)img.Width, (uint)img.Height);
                    UploadImageRows(img, gl);
                }
            }
        }

        private static string ResolveTexturePath(string texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath) || Path.IsPathRooted(texturePath))
            {
                return texturePath;
            }

            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, texturePath),
                Path.Combine(Environment.CurrentDirectory, "Mana", texturePath),
                Path.Combine(Environment.CurrentDirectory, "..", "Mana", texturePath),
                Path.Combine(AppContext.BaseDirectory, texturePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", texturePath)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return texturePath;
        }

        internal unsafe void AllocateEmptyTextImage2D(GL gl, TextureTarget textureType, uint width, uint height)
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

        private unsafe void UploadImageRows(Image<Rgba32> image, GL gl)
        {
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    fixed (void* data = accessor.GetRowSpan(y))
                    {
                        gl.TexSubImage2D(TextureTargetType, 0, 0, y, (uint)accessor.Width, 1, InternalPixelFormat, InternalPixelType, data);
                    }
                }
            });
        }

        public virtual void Bind(int channel)
        {
            EnsureOpenGlTexture().Bind(channel);
        }

        public void UnBind(int channel)
        {
            BindableGpuTexture?.UnBind(channel);
        }

        private IManaBindableGpuTexture EnsureOpenGlTexture()
        {
            if (GpuTexture is IManaBindableGpuTexture openGlTexture)
            {
                return openGlTexture;
            }

            GpuTexture = new ManaOpenGlTexture(this);
            return (IManaBindableGpuTexture)GpuTexture;
        }

        public object Clone()
        {
            var copy = MemberwiseClone() as ManaTexture;
            if (copy != null)
            {
                copy.Handle = 0;
                copy.IsLoaded = false;
                copy.GpuTexture = null;
            }
            return copy ?? this;
        }
    }
}

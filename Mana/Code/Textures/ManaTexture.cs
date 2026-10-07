using Mana.Code;
using Mana.Code.Graphics;
using ManaCore.Graphics;
using ManaCore.Imaging;
using Silk.NET.OpenGL;

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
            var gl = Context.OpenGl!;

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
            if (DecodeSource() is { } image)
            {
                Width = (uint)image.Width;
                Height = (uint)image.Height;
                SizeInBytes = image.Width * image.Height * 4;
                PixelData = image.Pixels;
            }
        }

        /// <summary>The texture's picture, flipped for GPU texture coordinates, or null when it has no source.</summary>
        private ManaImage? DecodeSource()
        {
            if (PixelStream != null)
            {
                using var memory = new MemoryStream();
                PixelStream.CopyTo(memory);
                return ManaImageCodec.Load(memory.ToArray()).FlipVertical();
            }
            if (!string.IsNullOrEmpty(OriginalFilename))
            {
                return ManaImageCodec.Load(ResolveTexturePath(OriginalFilename)).FlipVertical();
            }
            return null;
        }

        public virtual void Load()
        {
#if MANA_BROWSER
            // CPU pixels are the whole job on the browser target: there is no OpenGL, and WebGPU
            // uploads textures through its own bind groups from this data rather than through a
            // bound texture unit. Without this branch, anything that loads a texture while
            // DeferGpuUploads is false takes the OpenGL path below and throws
            // PlatformNotSupportedException -- which is exactly what happened in
            // ManaGameModeGraphicsBridge.FinalizeGpuUploads: it clears DeferGpuUploads first, then
            // walks every material calling Load(). The throw was swallowed by
            // ManaLoadingCoordinator.Update()'s catch into Fail(), so loading simply never
            // completed and, because BaseGameMode.update() only ticks the loading coordinator
            // while IsLoading, the game never simulated a single frame: no physics, no input, and
            // no finalized GPU uploads, with nothing logged anywhere.
            LoadCpuPixelData();
#else
            if (Context.DeferGpuUploads)
            {
                LoadCpuPixelData();
            }
            else
            {
                EnsureOpenGlTexture().Load();
            }
#endif
        }

        internal void LoadOpenGlTextureData()
        {
            LoadTextureData();
        }

        protected virtual unsafe void LoadTextureData()
        {
            var gl = Context.OpenGl!;

            if (PixelData != null && PixelData.Length > 0)
            {
                AllocateEmptyTextImage2D(gl, TextureTargetType, Width, Height);
                fixed (void* data = PixelData)
                {
                    gl.TexSubImage2D(TextureTargetType, 0, 0, 0, Width, Height, InternalPixelFormat, InternalPixelType, data);
                }
            }
            else if (DecodeSource() is { } image)
            {
                Width = (uint)image.Width;
                Height = (uint)image.Height;
                SizeInBytes = image.Width * image.Height * 8;
                AllocateEmptyTextImage2D(gl, TextureTargetType, Width, Height);
                fixed (void* data = image.Pixels)
                {
                    gl.TexSubImage2D(TextureTargetType, 0, 0, 0, Width, Height, InternalPixelFormat, InternalPixelType, data);
                }
            }
            else
            {
                // Create an empty texture with allocated space (Common for Render Targets)
                UpdateTextureSize();
            }
        }

        /// <summary>
        /// Turns a texture path stored on a material into one that exists on disk, trying the working
        /// directory and the build output. Public because the render path needs the same answer: a material
        /// stores a relative path so a scene survives being moved, and the backend needs an absolute one.
        /// </summary>
        public static string ResolveTexturePath(string texturePath)
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

#if MANA_BROWSER
            // No OpenGL on the browser target (WebGPU-only) -- this
            // legacy per-texture-unit Bind(int) path is OpenGL-only; WebGPU binds
            // textures through its own bind groups instead, never through here.
            throw new PlatformNotSupportedException(
                "ManaTexture.Bind (OpenGL texture units) is not supported on the browser target.");
#else
            GpuTexture = new ManaOpenGlTexture(this);
            return (IManaBindableGpuTexture)GpuTexture;
#endif
        }

        /// <summary>
        /// Called on the copy after a clone has had its GPU state cleared. A memberwise copy brings every
        /// field with it, including any a subclass keeps about resources the copy does not own.
        /// </summary>
        protected virtual void OnCloned()
        {
        }

        public object Clone()
        {
            var copy = MemberwiseClone() as ManaTexture;
            if (copy != null)
            {
                copy.Handle = 0;
                copy.IsLoaded = false;
                copy.GpuTexture = null;
                copy.OnCloned();
            }
            return copy ?? this;
        }
    }
}

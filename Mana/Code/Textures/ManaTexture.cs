using Mana.Code;
using Mana.Code.Graphics;
using ManaCore.Graphics;
using ManaCore.Imaging;

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

        public bool IsCube { get; set; }
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
                // Clones share the source stream; decoding must leave it usable by the next material.
                var position = PixelStream.CanSeek ? PixelStream.Position : 0;
                try
                {
                    if (PixelStream.CanSeek) PixelStream.Position = 0;
                    PixelStream.CopyTo(memory);
                }
                finally
                {
                    if (PixelStream.CanSeek) PixelStream.Position = position;
                }
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
            LoadCpuPixelData();
            IsLoaded = true;
        }

        /// <summary>
        /// Turns a texture path stored on a material into one that exists on disk, trying the working
        /// directory and the build output. Public because the render path needs the same answer: a material
        /// stores a relative path so a scene survives being moved, and the backend needs an absolute one.
        /// </summary>
        public static string ResolveTexturePath(string texturePath)
        {
            if (texturePath.StartsWith("zip:", StringComparison.OrdinalIgnoreCase))
            {
                return ManaSpritePictures.Resolve(texturePath) ?? texturePath;
            }
            if (ManaCore.Assets.ManaAssetCatalog.Current?.LocalFileOf(texturePath) is { } catalogPath)
            {
                return catalogPath;
            }
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

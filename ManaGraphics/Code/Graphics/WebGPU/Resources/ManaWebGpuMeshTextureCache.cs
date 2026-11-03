using System.Security.Cryptography;
using ManaCore.Graphics;
using ManaCore.Imaging;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>
    /// Every mesh's albedo and material maps, ref-counted and shared by file (untextured meshes share a neutral 1x1
    /// per map kind).
    /// </summary>
    internal unsafe sealed class ManaWebGpuMeshTextureCache : ManaWebGpuDeviceObject
    {
        private readonly Dictionary<string, ManaWebGpuMeshTexture> _cache = new(StringComparer.OrdinalIgnoreCase);

        public ManaWebGpuMeshTextureCache(ManaWebGpuDevice gpu)
            : base(gpu)
        {
        }

        public int Count => _cache.Count;

        // Pictures redrawn as the game runs, with the revision last uploaded.
        private readonly Dictionary<string, (ManaDynamicTexture Source, ManaWebGpuMeshTexture Texture, int Uploaded)> _dynamic = new();

        /// <summary>Uploads every live picture that changed since the last frame; once a frame, before the scene draws.</summary>
        public void UploadDynamic()
        {
            foreach (var (key, entry) in _dynamic.ToArray())
            {
                var revision = entry.Source.Revision;
                if (revision == entry.Uploaded || entry.Texture.Texture == null)
                {
                    continue;
                }
                WriteDynamic(entry.Source, entry.Texture.Texture);
                _dynamic[key] = (entry.Source, entry.Texture, revision);
            }
        }

        // One level, no mips: a live picture is pixel art seen up close, and remaking a chain every frame is waste.
        private ManaWebGpuMeshTexture CreateDynamicTexture(ManaDynamicTexture source, string key)
        {
            var width = (uint)Math.Max(1, source.Width);
            var height = (uint)Math.Max(1, source.Height);
            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8UnormSrgb,
                MipLevelCount = 1,
                SampleCount = 1
            };
            var texture = Api.DeviceCreateTexture(Device, &textureDescriptor);
            if (texture == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU dynamic texture.");
            }
            var viewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba8UnormSrgb,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            var view = Api.TextureCreateView(texture, &viewDescriptor);
            if (view == null)
            {
                Api.TextureRelease(texture);
                throw new InvalidOperationException("Failed to create WebGPU dynamic texture view.");
            }
            WriteDynamic(source, texture);
            var resource = new ManaWebGpuMeshTexture(key, texture, view) { RefCount = 1 };
            _dynamic[key] = (source, resource, source.Revision);
            return resource;
        }

        private void WriteDynamic(ManaDynamicTexture source, Texture* texture)
        {
            lock (source.Sync)
            {
                fixed (byte* pixels = source.Pixels)
                {
                    var destination = new ImageCopyTexture { Texture = texture, MipLevel = 0, Origin = new Origin3D(), Aspect = TextureAspect.All };
                    var layout = new TextureDataLayout { Offset = 0, BytesPerRow = (uint)source.Width * 4, RowsPerImage = (uint)source.Height };
                    var size = new Extent3D { Width = (uint)source.Width, Height = (uint)source.Height, DepthOrArrayLayers = 1 };
                    Api.QueueWriteTexture(Queue, &destination, pixels, (nuint)source.Pixels.Length, &layout, &size);
                }
            }
        }

        public ManaWebGpuMeshTexture AcquireMeshTexture(ManaMeshCpuData cpuData)
        {
            var textureKey = GetMeshTextureKey(cpuData);
            if (_cache.TryGetValue(textureKey, out var cachedTexture))
            {
                cachedTexture.RefCount++;
                return cachedTexture;
            }

            if (cpuData.AlbedoDynamicTexture is { } dynamic)
            {
                var live = CreateDynamicTexture(dynamic, textureKey);
                _cache[textureKey] = live;
                return live;
            }

            CreateMeshTexture(cpuData, out var texture, out var textureView);
            var textureResource = new ManaWebGpuMeshTexture(textureKey, texture, textureView)
            {
                RefCount = 1
            };
            _cache[textureKey] = textureResource;
            return textureResource;
        }

        /// <summary>The six non-albedo maps in binding order, which must match the layout and the WGSL.</summary>
        public ManaWebGpuMeshTexture[] AcquireMaterialMaps(ManaMeshCpuData cpuData)
        {
            (string? Path, ManaMaterialMapKind Kind)[] maps =
            [
                (cpuData.NormalTexturePath, ManaMaterialMapKind.Normal),
                (cpuData.EmissionTexturePath, ManaMaterialMapKind.Colour),
                (cpuData.MetallicTexturePath, ManaMaterialMapKind.Linear),
                (cpuData.RoughnessTexturePath, ManaMaterialMapKind.Linear),
                (cpuData.OcclusionTexturePath, ManaMaterialMapKind.Linear),
                (cpuData.OpacityTexturePath, ManaMaterialMapKind.Linear)
            ];
            return ManaWebGpuAcquisition.Acquire(maps.Length,
                index => AcquireMaterialMap(maps[index].Path, maps[index].Kind), ReleaseMeshTexture);
        }

        // Shutdown: whatever a reference was never returned for, so nothing outlives the device.
        public override void Dispose()
        {
            foreach (var texture in _cache.Values.ToArray())
            {
                texture.RefCount = 1;
                ReleaseMeshTexture(texture);
            }
        }

        public void ReleaseMeshTexture(ManaWebGpuMeshTexture textureResource)
        {
            if (textureResource.RefCount <= 0)
            {
                return;
            }

            textureResource.RefCount--;
            if (textureResource.RefCount > 0)
            {
                return;
            }

            _cache.Remove(textureResource.Key);
            _dynamic.Remove(textureResource.Key);
            if (textureResource.TextureView != null)
            {
                Api.TextureViewRelease(textureResource.TextureView);
                textureResource.TextureView = null;
            }

            if (textureResource.Texture != null)
            {
                Api.TextureRelease(textureResource.Texture);
                textureResource.Texture = null;
            }
        }

        /// <summary>One of a material's maps, shared by file; absent maps share one neutral 1x1 per kind.</summary>
        private ManaWebGpuMeshTexture AcquireMaterialMap(string? path, ManaMaterialMapKind kind)
        {
            var textureKey = string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                ? $"neutral:{kind}"
                : $"{kind}:{Path.GetFullPath(path)}";

            if (_cache.TryGetValue(textureKey, out var cachedTexture))
            {
                cachedTexture.RefCount++;
                return cachedTexture;
            }

            CreateMaterialMapTexture(path, kind, out var texture, out var textureView);
            var textureResource = new ManaWebGpuMeshTexture(textureKey, texture, textureView)
            {
                RefCount = 1
            };

            _cache[textureKey] = textureResource;
            return textureResource;
        }

        private void CreateMeshTexture(ManaMeshCpuData cpuData, out Texture* meshTexture, out TextureView* meshTextureView)
        {
            byte[] pixels;
            uint width;
            uint height;

            var hasFile = !string.IsNullOrWhiteSpace(cpuData.AlbedoTexturePath) && File.Exists(cpuData.AlbedoTexturePath);
            if (hasFile && TryCreateDdsTexture(() => File.ReadAllBytes(cpuData.AlbedoTexturePath!), srgb: true, "mesh", out meshTexture, out meshTextureView))
            {
                return;
            }
            if (!hasFile && cpuData.AlbedoTextureBytes is { Length: > 0 } ddsBytes
                && TryCreateDdsTexture(() => ddsBytes, srgb: true, "mesh", out meshTexture, out meshTextureView))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(cpuData.AlbedoTexturePath) && File.Exists(cpuData.AlbedoTexturePath)
                && TryDecode(() => ManaImageCodec.Load(cpuData.AlbedoTexturePath)) is { } image)
            {
                width = (uint)image.Width;
                height = (uint)image.Height;
                pixels = image.Pixels;
                ManaConsole.WriteDiagnostic($"WebGPU mesh texture loaded: {cpuData.AlbedoTexturePath}");
            }
            else if (cpuData.AlbedoTextureBytes is { Length: > 0 } embeddedBytes && TryDecode(() => ManaImageCodec.Load(embeddedBytes)) is { } embedded)
            {
                width = (uint)embedded.Width;
                height = (uint)embedded.Height;
                pixels = embedded.Pixels;
                ManaConsole.WriteDiagnostic($"WebGPU mesh texture loaded from embedded bytes ({embeddedBytes.Length} bytes).");
            }
            else
            {
                width = 1;
                height = 1;
                pixels = new[] { byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue };
                ManaConsole.WriteDiagnostic("WebGPU mesh texture fallback: solid albedo color.");
            }

            CreateRgba8SrgbTexture(pixels, width, height, "mesh", out meshTexture, out meshTextureView);
        }

        private void CreateRgba8SrgbTexture(byte[] pixels, uint width, uint height, string diagnosticName, out Texture* texture, out TextureView* textureView)
        {
            CreateRgba8Texture(pixels, width, height, diagnosticName, TextureFormat.Rgba8UnormSrgb, out texture, out textureView);
        }

        /// <summary>
        /// One of a material's maps in its own colour space: only albedo and emission are sRGB, the rest are data. An absent
        /// map is a 1x1 of the channel's identity value, so the shader needs no branch.
        /// </summary>
        private void CreateMaterialMapTexture(
            string? path,
            ManaMaterialMapKind kind,
            out Texture* texture,
            out TextureView* textureView)
        {
            var format = kind == ManaMaterialMapKind.Colour
                ? TextureFormat.Rgba8UnormSrgb
                : TextureFormat.Rgba8Unorm;

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)
                && TryCreateDdsTexture(() => File.ReadAllBytes(path), kind == ManaMaterialMapKind.Colour, kind.ToString(), out texture, out textureView))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && TryDecode(() => ManaImageCodec.Load(path)) is { } image)
            {
                CreateRgba8Texture(image.Pixels, (uint)image.Width, (uint)image.Height, kind.ToString(), format, out texture, out textureView);
                return;
            }

            CreateRgba8Texture(NeutralPixel(kind), 1, 1, $"{kind} fallback", format, out texture, out textureView);
        }

        /// <summary>The picture flipped for GPU coordinates, or null when unreadable (or, in the browser, not decoded yet).</summary>
        private static ManaImage? TryDecode(Func<ManaImage> load)
        {
            try
            {
                return load().FlipVertical();
            }
            catch (InvalidDataException exception)
            {
                ManaConsole.WriteDiagnostic($"WebGPU texture unreadable: {exception.Message}");
                return null;
            }
        }

        /// <summary>
        /// A DDS uploaded as stored -- its blocks and every mip -- when the device samples BC. False for anything else
        /// (not a DDS, no BC support, a format with no sRGB form where one is needed, rows that will not flip), which
        /// the caller decodes to RGBA as it does any picture.
        /// </summary>
        private bool TryCreateDdsTexture(Func<byte[]> read, bool srgb, string diagnosticName, out Texture* texture, out TextureView* textureView)
        {
            texture = null;
            textureView = null;
            if (!Gpu.SupportsBlockCompression)
            {
                return false;
            }

            ManaDds dds;
            try
            {
                var bytes = read();
                if (!ManaDds.IsDds(bytes))
                {
                    return false;
                }
                dds = ManaDds.Read(bytes);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                ManaConsole.WriteDiagnostic($"WebGPU DDS unreadable, decoding instead: {exception.Message}");
                return false;
            }

            TextureFormat format;
            switch (dds.Format)
            {
                case ManaDdsFormat.Bc1:
                    format = TextureFormat.BC1RgbaUnorm;
                    if (srgb)
                    {
                        format = TextureFormat.BC1RgbaUnormSrgb;
                    }
                    break;
                case ManaDdsFormat.Bc2:
                    format = TextureFormat.BC2RgbaUnorm;
                    if (srgb)
                    {
                        format = TextureFormat.BC2RgbaUnormSrgb;
                    }
                    break;
                case ManaDdsFormat.Bc3:
                    format = TextureFormat.BC3RgbaUnorm;
                    if (srgb)
                    {
                        format = TextureFormat.BC3RgbaUnormSrgb;
                    }
                    break;
                case ManaDdsFormat.Bc4 when !srgb:
                    format = TextureFormat.BC4RUnorm;
                    break;
                case ManaDdsFormat.Bc5 when !srgb:
                    format = TextureFormat.BC5RGUnorm;
                    break;
                case ManaDdsFormat.Rgba8:
                    format = TextureFormat.Rgba8Unorm;
                    if (srgb)
                    {
                        format = TextureFormat.Rgba8UnormSrgb;
                    }
                    break;
                default:
                    return false;
            }

            // A BC texture's size must be whole blocks; its smaller mips are padded out to them by the format itself.
            if (dds.IsBlockCompressed && (dds.Width % 4 != 0 || dds.Height % 4 != 0))
            {
                return false;
            }
            if (!dds.TryFlipVertical())
            {
                return false;
            }

            var levelCount = (uint)dds.Levels.Length;
            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = (uint)dds.Width, Height = (uint)dds.Height, DepthOrArrayLayers = 1 },
                Format = format,
                MipLevelCount = levelCount,
                SampleCount = 1
            };
            texture = Api.DeviceCreateTexture(Device, &textureDescriptor);
            if (texture == null)
            {
                throw new InvalidOperationException($"Failed to create WebGPU {diagnosticName} DDS texture.");
            }

            var texelsPerBlock = 1u;
            if (dds.IsBlockCompressed)
            {
                texelsPerBlock = 4u;
            }
            for (var level = 0; level < dds.Levels.Length; level++)
            {
                var data = dds.Levels[level];
                var columns = (uint)dds.LevelColumns(level);
                var rows = (uint)dds.LevelRows(level);
                fixed (byte* levelPointer = data)
                {
                    var destination = new ImageCopyTexture
                    {
                        Texture = texture,
                        MipLevel = (uint)level,
                        Origin = new Origin3D(),
                        Aspect = TextureAspect.All
                    };
                    var dataLayout = new TextureDataLayout
                    {
                        Offset = 0,
                        BytesPerRow = columns * (uint)dds.BlockBytes,
                        RowsPerImage = rows
                    };
                    var writeSize = new Extent3D { Width = columns * texelsPerBlock, Height = rows * texelsPerBlock, DepthOrArrayLayers = 1 };
                    Api.QueueWriteTexture(Queue, &destination, levelPointer, (nuint)data.Length, &dataLayout, &writeSize);
                }
            }

            var textureViewDescriptor = new TextureViewDescriptor
            {
                Format = format,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = levelCount,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            textureView = Api.TextureCreateView(texture, &textureViewDescriptor);
            if (textureView == null)
            {
                Api.TextureRelease(texture);
                texture = null;
                throw new InvalidOperationException($"Failed to create WebGPU {diagnosticName} DDS texture view.");
            }
            return true;
        }

        /// <summary>The value that leaves a channel exactly as its scalar left it.</summary>
        private static byte[] NeutralPixel(ManaMaterialMapKind kind)
        {
            // A normal map's neutral is straight up in tangent space, which encodes as (0.5, 0.5, 1).
            // Everything else multiplies, so white is the identity.
            return kind == ManaMaterialMapKind.Normal
                ? new byte[] { 128, 128, 255, 255 }
                : new[] { byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue };
        }

        private void CreateRgba8Texture(byte[] pixels, uint width, uint height, string diagnosticName, TextureFormat format, out Texture* texture, out TextureView* textureView)
        {
            // Every level down to 1 x 1: without them a texture tiled into the distance (ground, brick) shimmers.
            var mips = ManaCore.Imaging.ManaMipChain.Build(pixels, (int)width, (int)height, format == TextureFormat.Rgba8UnormSrgb);
            var levelCount = (uint)(mips.Count + 1);
            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = format,
                MipLevelCount = levelCount,
                SampleCount = 1
            };
            texture = Api.DeviceCreateTexture(Device, &textureDescriptor);
            if (texture == null)
            {
                throw new InvalidOperationException($"Failed to create WebGPU {diagnosticName} texture.");
            }

            fixed (byte* pixelPointer = pixels)
            {
                var destination = new ImageCopyTexture
                {
                    Texture = texture,
                    MipLevel = 0,
                    Origin = new Origin3D(),
                    Aspect = TextureAspect.All
                };
                var dataLayout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = width * 4,
                    RowsPerImage = height
                };
                var writeSize = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 };
                Api.QueueWriteTexture(Queue, &destination, pixelPointer, (nuint)pixels.Length, &dataLayout, &writeSize);
            }
            for (var level = 0; level < mips.Count; level++)
            {
                var (mipPixels, mipWidth, mipHeight) = mips[level];
                fixed (byte* mipPointer = mipPixels)
                {
                    var destination = new ImageCopyTexture { Texture = texture, MipLevel = (uint)(level + 1), Origin = new Origin3D(), Aspect = TextureAspect.All };
                    var dataLayout = new TextureDataLayout { Offset = 0, BytesPerRow = (uint)mipWidth * 4, RowsPerImage = (uint)mipHeight };
                    var writeSize = new Extent3D { Width = (uint)mipWidth, Height = (uint)mipHeight, DepthOrArrayLayers = 1 };
                    Api.QueueWriteTexture(Queue, &destination, mipPointer, (nuint)mipPixels.Length, &dataLayout, &writeSize);
                }
            }

            var textureViewDescriptor = new TextureViewDescriptor
            {
                Format = format,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = levelCount,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            textureView = Api.TextureCreateView(texture, &textureViewDescriptor);
            if (textureView == null)
            {
                Api.TextureRelease(texture);
                texture = null;
                throw new InvalidOperationException($"Failed to create WebGPU {diagnosticName} texture view.");
            }
        }

        private static string GetMeshTextureKey(ManaMeshCpuData cpuData)
        {
            if (cpuData.AlbedoDynamicTexture is { } dynamic)
            {
                return $"dynamic:{dynamic.Id}";
            }

            if (!string.IsNullOrWhiteSpace(cpuData.AlbedoTexturePath) && File.Exists(cpuData.AlbedoTexturePath))
            {
                return $"file:{Path.GetFullPath(cpuData.AlbedoTexturePath)}";
            }

            if (cpuData.AlbedoTextureBytes is { Length: > 0 } embeddedBytes)
            {
                var hash = Convert.ToHexString(SHA256.HashData(embeddedBytes));
                return $"embedded:{hash}";
            }

            return "fallback:white";
        }
    }

    internal unsafe sealed class ManaWebGpuMeshTexture
    {
        public ManaWebGpuMeshTexture(string key, Texture* texture, TextureView* textureView)
        {
            Key = key;
            Texture = texture;
            TextureView = textureView;
        }

        public string Key { get; }
        public Texture* Texture { get; set; }
        public TextureView* TextureView { get; set; }
        public int RefCount { get; set; }
    }
}

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

        public ManaWebGpuMeshTexture AcquireMeshTexture(ManaMeshCpuData cpuData)
        {
            var textureKey = GetMeshTextureKey(cpuData);
            if (_cache.TryGetValue(textureKey, out var cachedTexture))
            {
                cachedTexture.RefCount++;
                return cachedTexture;
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
            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = format,
                MipLevelCount = 1,
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

            var textureViewDescriptor = new TextureViewDescriptor
            {
                Format = format,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
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

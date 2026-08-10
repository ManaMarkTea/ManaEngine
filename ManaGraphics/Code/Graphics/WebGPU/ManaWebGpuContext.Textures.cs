using System.Runtime.InteropServices;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        private void CreateMeshTexture(ManaMeshCpuData cpuData, out Texture* meshTexture, out TextureView* meshTextureView)
        {
            byte[] pixels;
            uint width;
            uint height;

            if (!string.IsNullOrWhiteSpace(cpuData.AlbedoTexturePath) && File.Exists(cpuData.AlbedoTexturePath))
            {
                using var image = ImageSharpImage.Load<Rgba32>(cpuData.AlbedoTexturePath);
                image.Mutate(x => x.Flip(FlipMode.Vertical));
                width = (uint)image.Width;
                height = (uint)image.Height;
                pixels = new byte[image.Width * image.Height * 4];
                image.CopyPixelDataTo(pixels);
                ManaConsole.WriteDiagnostic($"WebGPU mesh texture loaded: {cpuData.AlbedoTexturePath}");
            }
            else if (cpuData.AlbedoTextureBytes is { Length: > 0 } embeddedBytes)
            {
                using var image = ImageSharpImage.Load<Rgba32>(embeddedBytes);
                image.Mutate(x => x.Flip(FlipMode.Vertical));
                width = (uint)image.Width;
                height = (uint)image.Height;
                pixels = new byte[image.Width * image.Height * 4];
                image.CopyPixelDataTo(pixels);
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

        private void CreateImageTexture(string texturePath, bool flipVertical, string diagnosticName, out Texture* texture, out TextureView* textureView)
        {
            using var image = ImageSharpImage.Load<Rgba32>(texturePath);
            if (flipVertical)
            {
                image.Mutate(x => x.Flip(FlipMode.Vertical));
            }

            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            CreateRgba8SrgbTexture(pixels, (uint)image.Width, (uint)image.Height, diagnosticName, out texture, out textureView);
            ManaConsole.WriteDiagnostic($"WebGPU {diagnosticName} texture loaded: {texturePath}");
        }

        private void CreateRgba8SrgbTexture(byte[] pixels, uint width, uint height, string diagnosticName, out Texture* texture, out TextureView* textureView)
        {
            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8UnormSrgb,
                MipLevelCount = 1,
                SampleCount = 1
            };
            texture = _api.DeviceCreateTexture(_device, &textureDescriptor);
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
                _api.QueueWriteTexture(_queue, &destination, pixelPointer, (nuint)pixels.Length, &dataLayout, &writeSize);
            }

            var textureViewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba8UnormSrgb,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            textureView = _api.TextureCreateView(texture, &textureViewDescriptor);
            if (textureView == null)
            {
                _api.TextureRelease(texture);
                texture = null;
                throw new InvalidOperationException($"Failed to create WebGPU {diagnosticName} texture view.");
            }
        }

        private ManaWebGpuMeshTexture AcquireMeshTexture(ManaMeshCpuData cpuData)
        {
            var textureKey = GetMeshTextureKey(cpuData);
            if (_meshTextureCache.TryGetValue(textureKey, out var cachedTexture))
            {
                cachedTexture.RefCount++;
                return cachedTexture;
            }

            CreateMeshTexture(cpuData, out var texture, out var textureView);
            var textureResource = new ManaWebGpuMeshTexture(textureKey, texture, textureView)
            {
                RefCount = 1
            };
            _meshTextureCache[textureKey] = textureResource;
            return textureResource;
        }

        private void ReleaseMeshTexture(ManaWebGpuMeshTexture textureResource)
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

            _meshTextureCache.Remove(textureResource.Key);
            if (textureResource.TextureView != null)
            {
                _api.TextureViewRelease(textureResource.TextureView);
                textureResource.TextureView = null;
            }

            if (textureResource.Texture != null)
            {
                _api.TextureRelease(textureResource.Texture);
                textureResource.Texture = null;
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
}

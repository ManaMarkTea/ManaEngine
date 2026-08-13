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
            CreateRgba8Texture(pixels, width, height, diagnosticName, TextureFormat.Rgba8UnormSrgb, out texture, out textureView);
        }

        /// <summary>
        /// One of a material's maps, uploaded in the colour space its contents actually live in.
        /// <para>
        /// Only albedo and emission are colour. Normal, metallic, roughness, occlusion and opacity carry
        /// measurements -- a direction, a coefficient, a coverage -- and uploading those as sRGB has the
        /// sampler apply a transfer curve to numbers that were never encoded with one, which bends every
        /// value and is invisible until the lighting looks subtly wrong.
        /// </para>
        /// <para>
        /// Absent maps get a 1x1 neutral rather than a flag in the shader: the identity value for each
        /// channel leaves the material's scalar untouched, so "no map" and "a map of all ones" are the
        /// same thing and the shader needs no branch.
        /// </para>
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

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                using var image = ImageSharpImage.Load<Rgba32>(path);
                image.Mutate(x => x.Flip(FlipMode.Vertical));

                var loaded = new byte[image.Width * image.Height * 4];
                image.CopyPixelDataTo(loaded);

                CreateRgba8Texture(loaded, (uint)image.Width, (uint)image.Height, kind.ToString(), format, out texture, out textureView);
                return;
            }

            CreateRgba8Texture(NeutralPixel(kind), 1, 1, $"{kind} fallback", format, out texture, out textureView);
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
                Format = format,
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

        /// <summary>
        /// One of a material's maps, shared by every mesh using the same file. Absent maps share a single
        /// neutral 1x1 per kind, so a scene of untextured meshes costs three tiny textures in total.
        /// </summary>
        private ManaWebGpuMeshTexture AcquireMaterialMap(string? path, ManaMaterialMapKind kind)
        {
            var textureKey = string.IsNullOrWhiteSpace(path) || !File.Exists(path)
                ? $"neutral:{kind}"
                : $"{kind}:{Path.GetFullPath(path)}";

            if (_meshTextureCache.TryGetValue(textureKey, out var cachedTexture))
            {
                cachedTexture.RefCount++;
                return cachedTexture;
            }

            CreateMaterialMapTexture(path, kind, out var texture, out var textureView);
            var textureResource = new ManaWebGpuMeshTexture(textureKey, texture, textureView)
            {
                RefCount = 1
            };

            _meshTextureCache[textureKey] = textureResource;
            return textureResource;
        }

        /// <summary>
        /// The six non-albedo maps in binding order. Kept as one call so the order lives in exactly one
        /// place -- it has to agree with the bind group layout and with the WGSL, and three copies of an
        /// ordering is three chances to get it wrong.
        /// </summary>
        private ManaWebGpuMeshTexture[] AcquireMaterialMaps(ManaMeshCpuData cpuData)
        {
            return new[]
            {
                AcquireMaterialMap(cpuData.NormalTexturePath, ManaMaterialMapKind.Normal),
                AcquireMaterialMap(cpuData.EmissionTexturePath, ManaMaterialMapKind.Colour),
                AcquireMaterialMap(cpuData.MetallicTexturePath, ManaMaterialMapKind.Linear),
                AcquireMaterialMap(cpuData.RoughnessTexturePath, ManaMaterialMapKind.Linear),
                AcquireMaterialMap(cpuData.OcclusionTexturePath, ManaMaterialMapKind.Linear),
                AcquireMaterialMap(cpuData.OpacityTexturePath, ManaMaterialMapKind.Linear)
            };
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

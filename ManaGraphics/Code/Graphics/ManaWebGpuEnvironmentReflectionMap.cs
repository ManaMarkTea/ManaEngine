using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using Silk.NET.WebGPU;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace ManaGraphics.Code.Graphics
{
    internal unsafe sealed class ManaWebGpuEnvironmentReflectionMap : IManaEnvironmentReflectionMap
    {
        private readonly WebGPU _api;

        private ManaWebGpuEnvironmentReflectionMap(
            WebGPU api,
            ManaEnvironmentReflectionSource source,
            ManaWebGpuTextureResource environmentMap,
            ManaWebGpuTextureResource irradianceMap,
            ManaWebGpuTextureResource prefilteredReflectionMap,
            ManaWebGpuTextureResource brdfLut)
        {
            _api = api;
            Source = source;
            EnvironmentMapResource = environmentMap;
            IrradianceMapResource = irradianceMap;
            PrefilteredReflectionMapResource = prefilteredReflectionMap;
            BrdfLutResource = brdfLut;
        }

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.WebGpu;
        public ManaEnvironmentReflectionSource Source { get; }
        public IManaGpuTexture? EnvironmentMap => EnvironmentMapResource;
        public IManaGpuTexture? IrradianceMap => IrradianceMapResource;
        public IManaGpuTexture? PrefilteredReflectionMap => PrefilteredReflectionMapResource;
        public IManaGpuTexture? BrdfLut => BrdfLutResource;
        public bool IsReady => EnvironmentMapResource.IsLoaded &&
            IrradianceMapResource.IsLoaded &&
            PrefilteredReflectionMapResource.IsLoaded &&
            BrdfLutResource.IsLoaded;

        public ManaWebGpuTextureResource EnvironmentMapResource { get; }
        public ManaWebGpuTextureResource IrradianceMapResource { get; }
        public ManaWebGpuTextureResource PrefilteredReflectionMapResource { get; }
        public ManaWebGpuTextureResource BrdfLutResource { get; }

        public static ManaWebGpuEnvironmentReflectionMap Create(
            WebGPU api,
            Device* device,
            Queue* queue,
            ManaEnvironmentReflectionSource source,
            string sourcePath)
        {
            var environmentSize = ClampTextureSize(source.EnvironmentCubeSize, 16, 2048);
            var irradianceSize = ClampTextureSize(source.IrradianceCubeSize, 8, 256);
            var prefilterSize = ClampTextureSize(source.PrefilterCubeSize, 16, 2048);
            var brdfSize = ClampTextureSize(source.BrdfLutSize, 16, 2048);
            var prefilterMipCount = CalculateMipCount(prefilterSize);

            var environmentMap = CreateCubeResource(api, device, "EnvironmentMap", environmentSize, 1);
            var irradianceMap = CreateCubeResource(api, device, "IrradianceMap", irradianceSize, 1);
            var prefilteredReflectionMap = CreateCubeResource(
                api,
                device,
                "PrefilteredReflectionMap",
                prefilterSize,
                prefilterMipCount,
                TextureFormat.Rgba8Unorm,
                TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.RenderAttachment | TextureUsage.StorageBinding,
                true);

            using var image = ImageSharpImage.Load<Rgba32>(sourcePath);
            var sourcePixels = new Rgba32[image.Width * image.Height];
            image.CopyPixelDataTo(sourcePixels);
            var equirect = new EquirectImage(sourcePixels, image.Width, image.Height);

            WriteCubeTexture(api, queue, environmentMap.Texture, equirect, environmentSize, 0, 0.0f, false);
            WriteCubeTexture(api, queue, irradianceMap.Texture, equirect, irradianceSize, 0, 0.85f, true);

            var brdfLut = Create2DResource(api, device, "BrdfLut", brdfSize, brdfSize, TextureFormat.RG16float);
            WriteBrdfLut(api, queue, brdfLut.Texture, brdfSize, brdfSize);

            return new ManaWebGpuEnvironmentReflectionMap(api, source.Clone(), environmentMap, irradianceMap, prefilteredReflectionMap, brdfLut);
        }

        public void Dispose()
        {
            EnvironmentMapResource.Dispose();
            IrradianceMapResource.Dispose();
            PrefilteredReflectionMapResource.Dispose();
            BrdfLutResource.Dispose();
        }

        private static ManaWebGpuTextureResource CreateCubeResource(
            WebGPU api,
            Device* device,
            string name,
            int size,
            int mipCount,
            TextureFormat format = TextureFormat.Rgba8UnormSrgb,
            TextureUsage usage = TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.RenderAttachment,
            bool createStorageViews = false)
        {
            var descriptor = new TextureDescriptor
            {
                Usage = usage,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D
                {
                    Width = (uint)size,
                    Height = (uint)size,
                    DepthOrArrayLayers = 6
                },
                Format = format,
                MipLevelCount = (uint)mipCount,
                SampleCount = 1
            };

            var texture = api.DeviceCreateTexture(device, &descriptor);
            if (texture == null)
            {
                throw new InvalidOperationException($"Failed to create WebGPU {name} cube texture.");
            }

            var cubeViewDescriptor = new TextureViewDescriptor
            {
                Format = format,
                Dimension = TextureViewDimension.DimensionCube,
                MipLevelCount = (uint)mipCount,
                ArrayLayerCount = 6,
                Aspect = TextureAspect.All
            };

            var cubeView = api.TextureCreateView(texture, &cubeViewDescriptor);
            if (cubeView == null)
            {
                api.TextureRelease(texture);
                throw new InvalidOperationException($"Failed to create WebGPU {name} cube texture view.");
            }

            var faceViews = new nint[mipCount * 6];
            for (var mip = 0; mip < mipCount; mip++)
            {
                for (var face = 0; face < 6; face++)
                {
                    var faceViewDescriptor = new TextureViewDescriptor
                    {
                        Format = format,
                        Dimension = TextureViewDimension.Dimension2D,
                        BaseMipLevel = (uint)mip,
                        MipLevelCount = 1,
                        BaseArrayLayer = (uint)face,
                        ArrayLayerCount = 1,
                        Aspect = TextureAspect.All
                    };

                    var faceView = api.TextureCreateView(texture, &faceViewDescriptor);
                    if (faceView == null)
                    {
                        foreach (var createdFaceView in faceViews)
                        {
                            if (createdFaceView != 0)
                            {
                                api.TextureViewRelease((TextureView*)createdFaceView);
                            }
                        }

                        api.TextureViewRelease(cubeView);
                        api.TextureRelease(texture);
                        throw new InvalidOperationException($"Failed to create WebGPU {name} face render target view.");
                    }

                    faceViews[mip * 6 + face] = (nint)faceView;
                }
            }

            var storageViews = Array.Empty<nint>();
            if (createStorageViews)
            {
                storageViews = new nint[mipCount];
                for (var mip = 0; mip < mipCount; mip++)
                {
                    var storageViewDescriptor = new TextureViewDescriptor
                    {
                        Format = format,
                        Dimension = TextureViewDimension.Dimension2DArray,
                        BaseMipLevel = (uint)mip,
                        MipLevelCount = 1,
                        BaseArrayLayer = 0,
                        ArrayLayerCount = 6,
                        Aspect = TextureAspect.All
                    };

                    var storageView = api.TextureCreateView(texture, &storageViewDescriptor);
                    if (storageView == null)
                    {
                        foreach (var createdStorageView in storageViews)
                        {
                            if (createdStorageView != 0)
                            {
                                api.TextureViewRelease((TextureView*)createdStorageView);
                            }
                        }

                        foreach (var createdFaceView in faceViews)
                        {
                            if (createdFaceView != 0)
                            {
                                api.TextureViewRelease((TextureView*)createdFaceView);
                            }
                        }

                        api.TextureViewRelease(cubeView);
                        api.TextureRelease(texture);
                        throw new InvalidOperationException($"Failed to create WebGPU {name} storage view.");
                    }

                    storageViews[mip] = (nint)storageView;
                }
            }

            return new ManaWebGpuTextureResource(api, name, texture, cubeView, faceViews, storageViews, (uint)size, (uint)size, true);
        }

        private static ManaWebGpuTextureResource Create2DResource(
            WebGPU api,
            Device* device,
            string name,
            int width,
            int height,
            TextureFormat format = TextureFormat.Rgba8Unorm)
        {
            var descriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.RenderAttachment,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    DepthOrArrayLayers = 1
                },
                Format = format,
                MipLevelCount = 1,
                SampleCount = 1
            };

            var texture = api.DeviceCreateTexture(device, &descriptor);
            if (texture == null)
            {
                throw new InvalidOperationException($"Failed to create WebGPU {name} texture.");
            }

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = format,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            var view = api.TextureCreateView(texture, &viewDescriptor);
            if (view == null)
            {
                api.TextureRelease(texture);
                throw new InvalidOperationException($"Failed to create WebGPU {name} texture view.");
            }

            return new ManaWebGpuTextureResource(api, name, texture, view, Array.Empty<nint>(), Array.Empty<nint>(), (uint)width, (uint)height, false);
        }

        private static void WriteCubeTexture(WebGPU api, Queue* queue, Texture* texture, EquirectImage source, int size, int mip, float roughness, bool irradiance)
        {
            var sourceRowBytes = size * 4;
            var paddedRowBytes = Align(sourceRowBytes, 256);
            var bytesPerRow = (uint)paddedRowBytes;
            var paddedFaceSize = bytesPerRow * (uint)size;
            var combinedPixels = new byte[paddedFaceSize * 6];

            for (var face = 0; face < 6; face++)
            {
                var pixels = GenerateCubeFace(source, face, size, roughness, irradiance);
                var combinedOffset = (int)(face * paddedFaceSize);
                if (sourceRowBytes == paddedRowBytes)
                {
                    System.Buffer.BlockCopy(pixels, 0, combinedPixels, combinedOffset, pixels.Length);
                    continue;
                }

                for (var y = 0; y < size; y++)
                {
                    System.Buffer.BlockCopy(
                        pixels,
                        y * sourceRowBytes,
                        combinedPixels,
                        combinedOffset + y * paddedRowBytes,
                        sourceRowBytes);
                }
            }

            fixed (byte* pixelPointer = combinedPixels)
            {
                var destination = new ImageCopyTexture
                {
                    Texture = texture,
                    MipLevel = (uint)mip,
                    Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
                    Aspect = TextureAspect.All
                };
                var dataLayout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = bytesPerRow,
                    RowsPerImage = (uint)size
                };
                var writeSize = new Extent3D { Width = (uint)size, Height = (uint)size, DepthOrArrayLayers = 6 };
                api.QueueWriteTexture(queue, &destination, pixelPointer, (nuint)combinedPixels.Length, &dataLayout, &writeSize);
            }
        }

        private static float RadicalInverse_VdC(uint bits)
        {
            bits = (bits << 16) | (bits >> 16);
            bits = ((bits & 0x55555555) << 1) | ((bits & 0xAAAAAAAA) >> 1);
            bits = ((bits & 0x33333333) << 2) | ((bits & 0xCCCCCCCC) >> 2);
            bits = ((bits & 0x0F0F0F0F) << 4) | ((bits & 0xF0F0F0F0) >> 4);
            bits = ((bits & 0x00FF00FF) << 8) | ((bits & 0xFF00FF00) >> 8);
            return bits * 2.3283064365386963e-10f; // / 0x100000000
        }

        private static Vector2 Hammersley(uint i, uint N)
        {
            return new Vector2(i / (float)N, RadicalInverse_VdC(i));
        }

        private static Vector3 ImportanceSampleGGX(Vector2 Xi, Vector3 N, float roughness)
        {
            float a = roughness * roughness;
            float phi = 2.0f * MathF.PI * Xi.X;
            float cosTheta = MathF.Sqrt((1.0f - Xi.Y) / (1.0f + (a * a - 1.0f) * Xi.Y));
            float sinTheta = MathF.Sqrt(1.0f - cosTheta * cosTheta);

            Vector3 H;
            H.X = MathF.Cos(phi) * sinTheta;
            H.Y = MathF.Sin(phi) * sinTheta;
            H.Z = cosTheta;

            Vector3 up = MathF.Abs(N.Z) < 0.999f ? new Vector3(0.0f, 0.0f, 1.0f) : new Vector3(1.0f, 0.0f, 0.0f);
            Vector3 tangent = Vector3.Normalize(Vector3.Cross(up, N));
            Vector3 bitangent = Vector3.Cross(N, tangent);

            return Vector3.Normalize(tangent * H.X + bitangent * H.Y + N * H.Z);
        }

        private static float GeometrySchlickGGX(float NdotV, float roughness)
        {
            float a = roughness;
            float k = (a * a) / 2.0f;
            float nom = NdotV;
            float denom = NdotV * (1.0f - k) + k;
            return nom / Math.Max(denom, 0.0001f);
        }

        private static float GeometrySmith(Vector3 N, Vector3 V, Vector3 L, float roughness)
        {
            float NdotV = Math.Max(Vector3.Dot(N, V), 0.0f);
            float NdotL = Math.Max(Vector3.Dot(N, L), 0.0f);
            float ggx2 = GeometrySchlickGGX(NdotV, roughness);
            float ggx1 = GeometrySchlickGGX(NdotL, roughness);
            return ggx1 * ggx2;
        }

        private static Vector2 IntegrateBRDF(float NdotV, float roughness)
        {
            NdotV = Math.Clamp(NdotV, 0.0001f, 0.9999f);
            Vector3 V;
            V.X = MathF.Sqrt(Math.Max(1.0f - NdotV * NdotV, 0.0f));
            V.Y = 0.0f;
            V.Z = NdotV;

            float A = 0.0f;
            float B = 0.0f;

            Vector3 N = new Vector3(0.0f, 0.0f, 1.0f);
            const uint SAMPLE_COUNT = 1024u;
            
            for (uint i = 0u; i < SAMPLE_COUNT; ++i)
            {
                Vector2 Xi = Hammersley(i, SAMPLE_COUNT);
                Vector3 H = ImportanceSampleGGX(Xi, N, roughness);
                Vector3 L = Vector3.Normalize(2.0f * Vector3.Dot(V, H) * H - V);

                float NdotL = Math.Max(L.Z, 0.0f);
                float NdotH = Math.Max(H.Z, 0.0f);
                float VdotH = Math.Max(Vector3.Dot(V, H), 0.0f);

                if (NdotL > 0.0f)
                {
                    float G = GeometrySmith(N, V, L, roughness);
                    float G_Vis = (G * VdotH) / Math.Max(NdotH * NdotV, 0.0001f);
                    float Fc = MathF.Pow(1.0f - VdotH, 5.0f);

                    A += (1.0f - Fc) * G_Vis;
                    B += Fc * G_Vis;
                }
            }
            return new Vector2(A / SAMPLE_COUNT, B / SAMPLE_COUNT);
        }

        private static void WriteBrdfLut(WebGPU api, Queue* queue, Texture* texture, int width, int height)
        {
            var values = new Half[width * height * 2];
            Parallel.For(0, height, y =>
            {
                var roughness = 1.0f - y / (float)Math.Max(1, height - 1);
                for (var x = 0; x < width; x++)
                {
                    var ndotv = x / (float)Math.Max(1, width - 1);
                    var brdf = IntegrateBRDF(ndotv, roughness);
                    var offset = (y * width + x) * 2;
                    values[offset + 0] = (Half)brdf.X;
                    values[offset + 1] = (Half)brdf.Y;
                }
            });

            var pixels = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
            var uploadPixels = PadRows(pixels, width, height, out var bytesPerRow, 4);
            fixed (byte* pixelPointer = uploadPixels)
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
                    BytesPerRow = bytesPerRow,
                    RowsPerImage = (uint)height
                };
                var writeSize = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 };
                api.QueueWriteTexture(queue, &destination, pixelPointer, (nuint)uploadPixels.Length, &dataLayout, &writeSize);
            }
        }

        private static byte[] GenerateCubeFace(EquirectImage source, int face, int size, float roughness, bool irradiance)
        {
            var pixels = new byte[size * size * 4];
            Parallel.For(0, size, y =>
            {
                for (var x = 0; x < size; x++)
                {
                    var u = 2.0f * ((x + 0.5f) / size) - 1.0f;
                    var v = 2.0f * ((y + 0.5f) / size) - 1.0f;
                    var direction = GetCubeDirection(face, u, v);
                    var color = SampleReflection(source, direction, roughness, irradiance);
                    var offset = (y * size + x) * 4;
                    pixels[offset + 0] = ToByte(color.X);
                    pixels[offset + 1] = ToByte(color.Y);
                    pixels[offset + 2] = ToByte(color.Z);
                    pixels[offset + 3] = ToByte(color.W);
                }
            });

            return pixels;
        }

        private static Vector4 SampleReflection(EquirectImage source, Vector3 direction, float roughness, bool irradiance)
        {
            var d = Vector3.Normalize(direction);
            if (!irradiance)
            {
                return SamplePrefilteredReflection(source, d, roughness);
            }

            var radius = irradiance ? 0.85f : roughness * roughness * 0.5f;
            if (radius <= 0.0001f)
            {
                return source.Sample(d);
            }

            CreateBasis(d, out var tangent, out var bitangent);
            var color = source.Sample(d) * 2.0f;
            color += source.Sample(Vector3.Normalize(d + tangent * radius));
            color += source.Sample(Vector3.Normalize(d - tangent * radius));
            color += source.Sample(Vector3.Normalize(d + bitangent * radius));
            color += source.Sample(Vector3.Normalize(d - bitangent * radius));

            if (irradiance)
            {
                color += source.Sample(Vector3.Normalize(d + (tangent + bitangent) * radius * 0.7f));
                color += source.Sample(Vector3.Normalize(d + (tangent - bitangent) * radius * 0.7f));
                color += source.Sample(Vector3.Normalize(d + (-tangent + bitangent) * radius * 0.7f));
                color += source.Sample(Vector3.Normalize(d - (tangent + bitangent) * radius * 0.7f));
                return color / 10.0f;
            }

            return color / 6.0f;
        }

        private static Vector4 SamplePrefilteredReflection(EquirectImage source, Vector3 direction, float roughness)
        {
            if (roughness <= 0.0001f)
            {
                return source.Sample(direction);
            }

            var n = Vector3.Normalize(direction);
            var v = n;
            var color = Vector4.Zero;
            var totalWeight = 0.0f;
            var sampleCount = GetPrefilterSampleCount(roughness);

            for (uint i = 0; i < sampleCount; ++i)
            {
                var xi = Hammersley(i, sampleCount);
                var h = ImportanceSampleGGX(xi, n, roughness);
                var l = Vector3.Normalize(2.0f * Vector3.Dot(v, h) * h - v);
                var nDotL = MathF.Max(Vector3.Dot(n, l), 0.0f);
                if (nDotL <= 0.0f)
                {
                    continue;
                }

                color += source.Sample(l) * nDotL;
                totalWeight += nDotL;
            }

            if (totalWeight <= 0.0001f)
            {
                return source.Sample(direction);
            }

            return color / totalWeight;
        }

        private static uint GetPrefilterSampleCount(float roughness)
        {
            if (roughness < 0.08f)
            {
                return 32u;
            }

            if (roughness < 0.35f)
            {
                return 64u;
            }

            return 128u;
        }

        private static Vector3 GetCubeDirection(int face, float u, float v)
        {
            return Vector3.Normalize(face switch
            {
                0 => new Vector3(1.0f, -v, -u),
                1 => new Vector3(-1.0f, -v, u),
                2 => new Vector3(u, 1.0f, v),
                3 => new Vector3(u, -1.0f, -v),
                4 => new Vector3(u, -v, 1.0f),
                _ => new Vector3(-u, -v, -1.0f)
            });
        }

        private static void CreateBasis(Vector3 normal, out Vector3 tangent, out Vector3 bitangent)
        {
            var up = MathF.Abs(normal.Y) > 0.96f ? Vector3.UnitX : Vector3.UnitY;
            tangent = Vector3.Normalize(Vector3.Cross(up, normal));
            bitangent = Vector3.Normalize(Vector3.Cross(normal, tangent));
        }

        private static int CalculateMipCount(int size)
        {
            var count = 1;
            while (size > 1)
            {
                size >>= 1;
                count++;
            }

            return count;
        }

        private static int ClampTextureSize(int value, int min, int max)
        {
            return Math.Clamp(value, min, max);
        }

        private static byte ToByte(float value)
        {
            return (byte)Math.Clamp(MathF.Round(value * 255.0f), 0.0f, 255.0f);
        }

        private static byte[] PadRows(byte[] pixels, int width, int height, out uint bytesPerRow, int bytesPerPixel = 4)
        {
            var sourceRowBytes = width * bytesPerPixel;
            var paddedRowBytes = Align(sourceRowBytes, 256);
            bytesPerRow = (uint)paddedRowBytes;
            if (sourceRowBytes == paddedRowBytes)
            {
                return pixels;
            }

            var padded = new byte[paddedRowBytes * height];
            for (var y = 0; y < height; y++)
            {
                System.Buffer.BlockCopy(pixels, y * sourceRowBytes, padded, y * paddedRowBytes, sourceRowBytes);
            }

            return padded;
        }

        private static int Align(int value, int alignment)
        {
            return ((value + alignment - 1) / alignment) * alignment;
        }

        private readonly struct EquirectImage
        {
            private readonly Rgba32[] _pixels;

            public EquirectImage(Rgba32[] pixels, int width, int height)
            {
                _pixels = pixels;
                Width = width;
                Height = height;
            }

            public int Width { get; }
            public int Height { get; }

            public Vector4 Sample(Vector3 direction)
            {
                var d = Vector3.Normalize(direction);
                var u = MathF.Atan2(d.Z, d.X) / (MathF.PI * 2.0f) + 0.5f;
                var v = 1.0f - (MathF.Asin(Math.Clamp(d.Y, -1.0f, 1.0f)) / MathF.PI + 0.5f);

                u -= MathF.Floor(u);
                v = Math.Clamp(v, 0.0f, 1.0f);

                var x = u * (Width - 1);
                var y = v * (Height - 1);
                var x0 = (int)MathF.Floor(x);
                var y0 = (int)MathF.Floor(y);
                var x1 = (x0 + 1) % Width;
                var y1 = Math.Min(y0 + 1, Height - 1);
                var tx = x - x0;
                var ty = y - y0;

                var c00 = ToVector(_pixels[y0 * Width + x0]);
                var c10 = ToVector(_pixels[y0 * Width + x1]);
                var c01 = ToVector(_pixels[y1 * Width + x0]);
                var c11 = ToVector(_pixels[y1 * Width + x1]);
                return Vector4.Lerp(Vector4.Lerp(c00, c10, tx), Vector4.Lerp(c01, c11, tx), ty);
            }

            private static Vector4 ToVector(Rgba32 pixel)
            {
                return new Vector4(pixel.R / 255.0f, pixel.G / 255.0f, pixel.B / 255.0f, pixel.A / 255.0f);
            }
        }
    }

    internal unsafe sealed class ManaWebGpuTextureResource : IManaGpuTexture
    {
        private readonly WebGPU _api;
        private nint[] _renderTargetViews;
        private nint[] _storageViews;

        public ManaWebGpuTextureResource(
            WebGPU api,
            string name,
            Texture* texture,
            TextureView* textureView,
            nint[] renderTargetViews,
            nint[] storageViews,
            uint width,
            uint height,
            bool isCube)
        {
            _api = api;
            Name = name;
            Texture = texture;
            TextureView = textureView;
            _renderTargetViews = renderTargetViews;
            _storageViews = storageViews;
            Width = width;
            Height = height;
            IsCube = isCube;
        }

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.WebGpu;
        public string Name { get; }
        public uint Width { get; }
        public uint Height { get; }
        public bool IsLoaded => Texture != null && TextureView != null;
        public bool IsCube { get; }
        public Texture* Texture { get; private set; }
        public TextureView* TextureView { get; private set; }
        public IReadOnlyList<nint> RenderTargetViews => _renderTargetViews;
        public IReadOnlyList<nint> StorageViews => _storageViews;

        public void Dispose()
        {
            foreach (var renderTargetView in _renderTargetViews)
            {
                if (renderTargetView != 0)
                {
                    _api.TextureViewRelease((TextureView*)renderTargetView);
                }
            }

            _renderTargetViews = Array.Empty<nint>();

            foreach (var storageView in _storageViews)
            {
                if (storageView != 0)
                {
                    _api.TextureViewRelease((TextureView*)storageView);
                }
            }

            _storageViews = Array.Empty<nint>();

            if (TextureView != null)
            {
                _api.TextureViewRelease(TextureView);
                TextureView = null;
            }

            if (Texture != null)
            {
                _api.TextureRelease(Texture);
                Texture = null;
            }
        }
    }
}

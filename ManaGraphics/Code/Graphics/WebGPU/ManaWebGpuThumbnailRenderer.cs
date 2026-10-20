using System.Numerics;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    // Square off-screen renders for the asset browser's thumbnails (MeshThumbnailCache). Its single worker waits on
    // each request, so one set of targets is reused.
    internal sealed unsafe class ManaWebGpuThumbnailRenderer : IDisposable
    {
        private const uint ThumbnailBytesPerPixel = 4;
        private const float ThumbnailInset = 0.1f;

        // One fixed isometric angle for every thumbnail, so a grid of assets reads as a catalogue.
        private static readonly Vector3 ThumbnailForwardWorld = Vector3.Normalize(new Vector3(-1.0f, -1.35f, -1.0f));
        private static readonly Vector3 ThumbnailUpWorld = Vector3.UnitY;
        private static readonly Vector3 ThumbnailLightDirectionWorld = Vector3.Normalize(new Vector3(-0.45f, 0.85f, 0.35f));

        private readonly ManaWebGpuDevice _gpu;
        private readonly WebGPU _api;
        private readonly ManaWebGpuReadbackRegistry _readbacks;
        private readonly ManaWebGpuMeshTextureCache _meshTextures;
        private ShaderModule* _shaderModule;
        private RenderPipeline* _pipeline;
        private Texture* _colorTexture;
        private TextureView* _colorView;
        private Texture* _depthTexture;
        private TextureView* _depthView;
        private uint _targetSize;

        private sealed class DrawResources
        {
            public ManaWebGpuMesh? Mesh;
            public ManaWebGpuMeshTexture? Texture;
            public nint UniformBuffer;
            public nint BindGroup;
        }

        public ManaWebGpuThumbnailRenderer(ManaWebGpuDevice gpu, ManaWebGpuReadbackRegistry readbacks, ManaWebGpuMeshTextureCache meshTextures)
        {
            _gpu = gpu;
            _api = gpu.Api;
            _readbacks = readbacks;
            _meshTextures = meshTextures;
        }

        public int PipelineCount => _pipeline != null ? 1 : 0;
        public int ShaderModuleCount => _shaderModule != null ? 1 : 0;

        /// <summary>
        /// Render thread only. Draws <paramref name="instances"/> to a square straight-alpha RGBA buffer and completes with
        /// it once read back, or null when there is nothing to draw.
        /// </summary>
        public void EncodeAndSubmit(
            IReadOnlyList<ManaRenderMeshInstance> instances,
            uint thumbnailSize,
            ManaWebGpuSceneBindings bindings,
            TaskCompletionSource<byte[]?> completion)
        {
            List<DrawResources>? drawResources = new(instances.Count);
            CommandEncoder* commandEncoder = null;
            RenderPassEncoder* renderPass = null;
            CommandBuffer* commandBuffer = null;
            WebGpuBuffer* outputBuffer = null;
            try
            {
                if (!TryComputeWorldBounds(instances, out var min, out var max))
                {
                    completion.TrySetResult(null);
                    return;
                }

                var camera = BuildCamera(min, max);
                EnsureTargets(thumbnailSize);
                var pipeline = GetOrCreatePipeline(bindings);

                // Readback retirement owns submitted draw resources; finally owns partial construction.
                var commandEncoderDescriptor = new CommandEncoderDescriptor();
                commandEncoder = _api.DeviceCreateCommandEncoder(_gpu.Device, &commandEncoderDescriptor);
                if (commandEncoder == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU thumbnail command encoder.");
                }

                var colorAttachment = new RenderPassColorAttachment
                {
                    View = _colorView,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    // Transparent: straight alpha, nothing drawn where the model is not.
                    ClearValue = new Color(0.0, 0.0, 0.0, 0.0)
                };

                var depthAttachment = new RenderPassDepthStencilAttachment
                {
                    View = _depthView,
                    DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store,
                    DepthClearValue = 1.0f,
                    DepthReadOnly = false,
                    StencilLoadOp = LoadOp.Undefined,
                    StencilStoreOp = StoreOp.Undefined,
                    StencilClearValue = 0,
                    StencilReadOnly = true
                };

                var renderPassDescriptor = new RenderPassDescriptor
                {
                    ColorAttachmentCount = 1,
                    ColorAttachments = &colorAttachment,
                    DepthStencilAttachment = &depthAttachment
                };

                renderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &renderPassDescriptor);
                if (renderPass == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU thumbnail render pass.");
                }
                _api.RenderPassEncoderSetPipeline(renderPass, pipeline);

                var bindGroupEntries = stackalloc BindGroupEntry[3];
                foreach (var instance in instances)
                {
                    if (instance.MeshData.Positions.Length == 0 || instance.MeshData.Indexes.Length == 0)
                    {
                        continue;
                    }

                    var resources = new DrawResources();
                    drawResources.Add(resources);
                    var mesh = ManaWebGpuMesh.CreateFromMeshData(_api, _gpu.Device, _gpu.Queue, instance.MeshData, instance.Name);
                    resources.Mesh = mesh;
                    var texture = _meshTextures.AcquireMeshTexture(instance.MeshData);
                    resources.Texture = texture;

                    var uniformBufferDescriptor = new BufferDescriptor
                    {
                        Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                        Size = (ulong)sizeof(ManaWebGpuSceneCaptureUniform)
                    };
                    var uniformBuffer = _api.DeviceCreateBuffer(_gpu.Device, &uniformBufferDescriptor);
                    resources.UniformBuffer = (nint)uniformBuffer;
                    if (uniformBuffer == null)
                    {
                        throw new InvalidOperationException("Failed to create WebGPU thumbnail uniform buffer.");
                    }
                    var uniform = BuildUniform(instance, camera);
                    _api.QueueWriteBuffer(_gpu.Queue, uniformBuffer, 0, &uniform, (nuint)sizeof(ManaWebGpuSceneCaptureUniform));

                    bindGroupEntries[0] = new BindGroupEntry
                    {
                        Binding = 0,
                        Buffer = uniformBuffer,
                        Offset = 0,
                        Size = (ulong)sizeof(ManaWebGpuSceneCaptureUniform)
                    };
                    bindGroupEntries[1] = new BindGroupEntry { Binding = 1, Sampler = bindings.LinearSampler };
                    bindGroupEntries[2] = new BindGroupEntry { Binding = 2, TextureView = texture.TextureView };

                    var bindGroupDescriptor = new BindGroupDescriptor
                    {
                        Layout = bindings.SceneCaptureGroupLayout,
                        EntryCount = 3,
                        Entries = bindGroupEntries
                    };
                    var bindGroup = _api.DeviceCreateBindGroup(_gpu.Device, &bindGroupDescriptor);
                    resources.BindGroup = (nint)bindGroup;
                    if (bindGroup == null)
                    {
                        throw new InvalidOperationException($"Failed to create WebGPU thumbnail bind group for mesh '{instance.Name}'.");
                    }

                    _api.RenderPassEncoderSetBindGroup(renderPass, 0, bindGroup, 0, null);
                    mesh.Draw(renderPass);
                }

                _api.RenderPassEncoderEnd(renderPass);
                _api.RenderPassEncoderRelease(renderPass);
                renderPass = null;

                // Texture-copy rows must be 256-byte aligned; padded rows are compacted after the readback.
                var rowBytes = checked(thumbnailSize * ThumbnailBytesPerPixel);
                var paddedRowBytes = ManaWebGpuDevice.AlignCopyRow(rowBytes);
                var readbackByteCount = checked((ulong)paddedRowBytes * thumbnailSize);

                var outputBufferDescriptor = new BufferDescriptor
                {
                    Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
                    Size = readbackByteCount
                };
                outputBuffer = _api.DeviceCreateBuffer(_gpu.Device, &outputBufferDescriptor);
                if (outputBuffer == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU thumbnail readback buffer.");
                }

                var copySource = new ImageCopyTexture
                {
                    Texture = _colorTexture,
                    MipLevel = 0,
                    Origin = new Origin3D(),
                    Aspect = TextureAspect.All
                };
                var copyDestination = new ImageCopyBuffer
                {
                    Buffer = outputBuffer,
                    Layout = new TextureDataLayout
                    {
                        Offset = 0,
                        BytesPerRow = paddedRowBytes,
                        RowsPerImage = thumbnailSize
                    }
                };
                var copySize = new Extent3D
                {
                    Width = thumbnailSize,
                    Height = thumbnailSize,
                    DepthOrArrayLayers = 1
                };
                _api.CommandEncoderCopyTextureToBuffer(commandEncoder, &copySource, &copyDestination, &copySize);

                var commandBufferDescriptor = new CommandBufferDescriptor();
                commandBuffer = _api.CommandEncoderFinish(commandEncoder, &commandBufferDescriptor);
                if (commandBuffer == null)
                {
                    throw new InvalidOperationException("Failed to finish WebGPU thumbnail commands.");
                }
                _api.QueueSubmit(_gpu.Queue, 1, &commandBuffer);
                var retiredDrawResources = drawResources;
                var readbackTask = ManaWebGpuBufferReadback.MapAsync(_api, outputBuffer, readbackByteCount,
                    _readbacks, () => DisposeDrawResources(retiredDrawResources));
                outputBuffer = null;
                drawResources = null;

                // Only CPU pixel conversion remains after retirement; shutdown needs no queued GPU cleanup.
                readbackTask.ContinueWith(
                    readback =>
                    {
                        try
                        {
                            if (readback.IsCanceled)
                            {
                                completion.TrySetCanceled();
                            }
                            else if (readback.Exception != null)
                            {
                                completion.TrySetException(readback.Exception.InnerExceptions);
                            }
                            else
                            {
                                completion.TrySetResult(NormalizePixels(readback.Result, thumbnailSize, paddedRowBytes));
                            }
                        }
                        catch (Exception ex)
                        {
                            completion.TrySetException(ex);
                        }
                    },
                    TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                if (renderPass != null)
                {
                    _api.RenderPassEncoderRelease(renderPass);
                }
                if (commandBuffer != null)
                {
                    _api.CommandBufferRelease(commandBuffer);
                }
                if (commandEncoder != null)
                {
                    _api.CommandEncoderRelease(commandEncoder);
                }
                if (outputBuffer != null)
                {
                    _api.BufferRelease(outputBuffer);
                }
                if (drawResources != null)
                {
                    DisposeDrawResources(drawResources);
                }
            }
        }

        private void DisposeDrawResources(List<DrawResources> drawResources)
        {
            foreach (var resource in drawResources)
            {
                if (resource.BindGroup != 0)
                {
                    _api.BindGroupRelease((BindGroup*)resource.BindGroup);
                }
                if (resource.UniformBuffer != 0)
                {
                    _api.BufferRelease((WebGpuBuffer*)resource.UniformBuffer);
                }
                if (resource.Texture != null)
                {
                    _meshTextures.ReleaseMeshTexture(resource.Texture);
                }
                resource.Mesh?.Dispose();
            }
        }

        /// <summary>World-space AABB across every instance's own local bounds transformed by its WorldTransform.</summary>
        private static bool TryComputeWorldBounds(
            IReadOnlyList<ManaRenderMeshInstance> instances,
            out Vector3 min,
            out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);
            var found = false;
            Span<Vector3> corners = stackalloc Vector3[8];

            foreach (var instance in instances)
            {
                var positions = instance.MeshData.Positions;
                if (positions.Length == 0)
                {
                    continue;
                }

                var localMin = positions[0];
                var localMax = positions[0];
                foreach (var position in positions)
                {
                    localMin = Vector3.Min(localMin, position);
                    localMax = Vector3.Max(localMax, position);
                }

                corners[0] = new Vector3(localMin.X, localMin.Y, localMin.Z);
                corners[1] = new Vector3(localMax.X, localMin.Y, localMin.Z);
                corners[2] = new Vector3(localMin.X, localMax.Y, localMin.Z);
                corners[3] = new Vector3(localMax.X, localMax.Y, localMin.Z);
                corners[4] = new Vector3(localMin.X, localMin.Y, localMax.Z);
                corners[5] = new Vector3(localMax.X, localMin.Y, localMax.Z);
                corners[6] = new Vector3(localMin.X, localMax.Y, localMax.Z);
                corners[7] = new Vector3(localMax.X, localMax.Y, localMax.Z);

                foreach (var corner in corners)
                {
                    var worldCorner = Vector3.Transform(corner, instance.WorldTransform);
                    min = Vector3.Min(min, worldCorner);
                    max = Vector3.Max(max, worldCorner);
                    found = true;
                }
            }

            var radius = found ? Vector3.Distance(min, max) * 0.5f : 0.0f;
            return radius > 0.0001f;
        }

        private static ManaCameraData BuildCamera(Vector3 min, Vector3 max)
        {
            var centre = (min + max) * 0.5f;
            var radius = Vector3.Distance(min, max) * 0.5f;
            var depthPadding = MathF.Max(radius * 0.15f, 0.01f);
            var eyeDistance = MathF.Max(radius * 3.0f, 0.1f) + depthPadding;
            var eye = centre - (ThumbnailForwardWorld * eyeDistance);
            var view = Matrix4x4.CreateLookAt(eye, centre, ThumbnailUpWorld);

            Span<Vector3> corners = stackalloc Vector3[8]
            {
                new Vector3(min.X, min.Y, min.Z),
                new Vector3(max.X, min.Y, min.Z),
                new Vector3(min.X, max.Y, min.Z),
                new Vector3(max.X, max.Y, min.Z),
                new Vector3(min.X, min.Y, max.Z),
                new Vector3(max.X, min.Y, max.Z),
                new Vector3(min.X, max.Y, max.Z),
                new Vector3(max.X, max.Y, max.Z)
            };

            var viewMin = new Vector3(float.MaxValue);
            var viewMax = new Vector3(float.MinValue);
            var minDistance = float.MaxValue;
            var maxDistance = 0.0f;

            foreach (var corner in corners)
            {
                var viewCorner = Vector3.Transform(corner, view);
                viewMin = Vector3.Min(viewMin, viewCorner);
                viewMax = Vector3.Max(viewMax, viewCorner);

                // System.Numerics' right-handed look-at places visible points at negative view-space Z;
                // projections take positive near/far distances.
                var distance = -viewCorner.Z;
                minDistance = MathF.Min(minDistance, distance);
                maxDistance = MathF.Max(maxDistance, distance);
            }

            var halfWidth = MathF.Max((viewMax.X - viewMin.X) * 0.5f, 0.001f);
            var halfHeight = MathF.Max((viewMax.Y - viewMin.Y) * 0.5f, 0.001f);
            var half = MathF.Max(halfWidth, halfHeight) / (1.0f - (ThumbnailInset * 2.0f));
            var nearPlane = MathF.Max(0.001f, minDistance - depthPadding);
            var farPlane = MathF.Max(nearPlane + 0.01f, maxDistance + depthPadding);
            var projection = Matrix4x4.CreateOrthographic(half * 2.0f, half * 2.0f, nearPlane, farPlane);

            return new ManaCameraData(view, projection, eye, nearPlane, farPlane);
        }

        private static byte[]? NormalizePixels(byte[] readback, uint thumbnailSize, uint bytesPerRow)
        {
            var rowBytes = checked(thumbnailSize * ThumbnailBytesPerPixel);
            if (bytesPerRow < rowBytes)
            {
                return null;
            }

            var expectedReadbackBytes = checked((int)(bytesPerRow * thumbnailSize));
            if (readback.Length != expectedReadbackBytes)
            {
                return null;
            }

            var pixels = new byte[checked((int)(rowBytes * thumbnailSize))];
            var opaquePixels = 0;
            for (uint y = 0; y < thumbnailSize; y++)
            {
                var sourceRow = checked((int)(y * bytesPerRow));
                var targetRow = checked((int)(y * rowBytes));

                for (uint x = 0; x < rowBytes; x += ThumbnailBytesPerPixel)
                {
                    var source = sourceRow + (int)x;
                    var target = targetRow + (int)x;
                    if (readback[source + 3] < 128)
                    {
                        continue;
                    }

                    pixels[target] = readback[source];
                    pixels[target + 1] = readback[source + 1];
                    pixels[target + 2] = readback[source + 2];
                    pixels[target + 3] = byte.MaxValue;
                    opaquePixels++;
                }
            }

            return opaquePixels == 0 ? null : pixels;
        }

        private static ManaWebGpuSceneCaptureUniform BuildUniform(ManaRenderMeshInstance instance, ManaCameraData camera)
        {
            var model = instance.WorldTransform;
            if (!Matrix4x4.Invert(model, out var inverseModel))
            {
                inverseModel = Matrix4x4.Identity;
            }

            return new ManaWebGpuSceneCaptureUniform
            {
                ModelProjection = model * camera.View * camera.Projection,
                Model = model,
                NormalMatrix = Matrix4x4.Transpose(inverseModel),
                CameraPosition = new Vector4(camera.Position, 1.0f),
                MaterialAlbedo = instance.Albedo,
                // Repurposed: xyz carries the fixed key-light direction -- see mesh_thumbnail.wgsl's header.
                IrradianceFactors = new Vector4(ThumbnailLightDirectionWorld, 0.0f)
                // Every other field intentionally left zeroed -- unused by mesh_thumbnail.wgsl.
            };
        }

        private RenderPipeline* GetOrCreatePipeline(ManaWebGpuSceneBindings bindings)
        {
            if (_pipeline != null)
            {
                return _pipeline;
            }

            if (_shaderModule == null)
            {
                _shaderModule = _gpu.CreateShaderModule("mesh_thumbnail.wgsl");
            }
            _pipeline = ManaWebGpuMeshPipeline.Create(_gpu, _shaderModule, TextureFormat.Rgba8UnormSrgb, bindings.SceneCaptureLayout, "MeshThumbnail");
            ManaConsole.WriteDiagnostic("WebGPU mesh thumbnail pipeline created successfully.");
            return _pipeline;
        }

        private void EnsureTargets(uint thumbnailSize)
        {
            if (_colorTexture != null)
            {
                if (_targetSize == thumbnailSize)
                {
                    return;
                }

                ReleaseTargets();
            }

            _targetSize = thumbnailSize;

            var colorDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = thumbnailSize, Height = thumbnailSize, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8UnormSrgb,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _colorTexture = _api.DeviceCreateTexture(_gpu.Device, &colorDescriptor);
            if (_colorTexture == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU thumbnail color texture.");
            }

            var colorViewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba8UnormSrgb,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            _colorView = _api.TextureCreateView(_colorTexture, &colorViewDescriptor);
            if (_colorView == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU thumbnail color texture view.");
            }

            // Its own depth target, not the reflection capture's: that one follows the cube size, and sharing
            // would only have the two fight over one target.
            var depthDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = thumbnailSize, Height = thumbnailSize, DepthOrArrayLayers = 1 },
                Format = _gpu.DepthFormat,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _depthTexture = _api.DeviceCreateTexture(_gpu.Device, &depthDescriptor);
            if (_depthTexture == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU thumbnail depth texture.");
            }

            var depthViewDescriptor = new TextureViewDescriptor
            {
                Format = _gpu.DepthFormat,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.DepthOnly
            };
            _depthView = _api.TextureCreateView(_depthTexture, &depthViewDescriptor);
            if (_depthView == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU thumbnail depth texture view.");
            }
        }

        private void ReleaseTargets()
        {
            if (_colorView != null)
            {
                _api.TextureViewRelease(_colorView);
                _colorView = null;
            }

            if (_colorTexture != null)
            {
                _api.TextureRelease(_colorTexture);
                _colorTexture = null;
            }

            if (_depthView != null)
            {
                _api.TextureViewRelease(_depthView);
                _depthView = null;
            }

            if (_depthTexture != null)
            {
                _api.TextureRelease(_depthTexture);
                _depthTexture = null;
            }

            _targetSize = 0;
        }

        public void Dispose()
        {
            ReleaseTargets();

            if (_pipeline != null)
            {
                _api.RenderPipelineRelease(_pipeline);
                _pipeline = null;
            }

            if (_shaderModule != null)
            {
                _api.ShaderModuleRelease(_shaderModule);
                _shaderModule = null;
            }
        }
    }
}

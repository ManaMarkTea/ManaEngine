using System.Numerics;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    // Off-screen square renders for the mesh-asset picker's thumbnails (MeshThumbnailCache), through the
    // real renderer rather than the hand-rolled CPU rasterizer that used to be the only option -- see
    // mesh_thumbnail.wgsl's header for why a real GPU render was worth the extra plumbing.
    //
    // One render at a time is assumed: MeshThumbnailCache's single background worker calls
    // RenderThumbnailAsync synchronously (blocks on the returned Task) per asset, never overlapping two
    // requests, which is why the color/depth render targets below are created once and reused rather than
    // pooled per request.
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        private const uint ThumbnailBytesPerPixel = 4;
        private const uint WebGpuCopyBytesPerRowAlignment = 256;
        private const float ThumbnailInset = 0.1f;

        // Fixed top-down isometric view, identical across every thumbnail. The fit changes per asset; the
        // angle does not, so a grid of assets reads as a consistent catalogue instead of a set of random
        // screenshots. Forward is camera-to-target, so the eye is placed in the opposite direction.
        private static readonly Vector3 ThumbnailForwardWorld = Vector3.Normalize(new Vector3(-1.0f, -1.35f, -1.0f));
        private static readonly Vector3 ThumbnailUpWorld = Vector3.UnitY;
        private static readonly Vector3 ThumbnailLightDirectionWorld = Vector3.Normalize(new Vector3(-0.45f, 0.85f, 0.35f));

        private RenderPipeline* _thumbnailPipeline;
        private Texture* _thumbnailColorTexture;
        private TextureView* _thumbnailColorTextureView;
        private Texture* _thumbnailDepthTexture;
        private TextureView* _thumbnailDepthTextureView;
        private uint _thumbnailResourceSize;

        private sealed class ThumbnailDrawResources
        {
            public ManaWebGpuMesh? Mesh;
            public ManaWebGpuMeshTexture? Texture;
            public nint UniformBuffer;
            public nint BindGroup;
        }

        /// <summary>
        /// Renders <paramref name="instances"/> (already loaded, world transforms already composed) to a
        /// square straight-alpha RGBA buffer and reads it back, or completes with null if there was nothing
        /// to draw. Callable from any thread; the actual GPU work is marshalled onto the render thread via
        /// <see cref="EnqueueRenderThreadWork"/> (see its own doc comment for why -- wgpu-native deadlocks
        /// if a background thread submits concurrently with SurfacePresent).
        /// </summary>
        public Task<byte[]?> RenderThumbnailAsync(IReadOnlyList<ManaRenderMeshInstance> instances, int thumbnailSize)
        {
            if (_disposed || thumbnailSize <= 0)
            {
                return Task.FromResult<byte[]?>(null);
            }

            var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            EnqueueRenderThreadWork(() => EncodeAndSubmitThumbnail(instances, (uint)thumbnailSize, completion));
            return completion.Task;
        }

        private void EncodeAndSubmitThumbnail(
            IReadOnlyList<ManaRenderMeshInstance> instances,
            uint thumbnailSize,
            TaskCompletionSource<byte[]?> completion)
        {
            if (_disposed)
            {
                completion.TrySetResult(null);
                return;
            }

            List<ThumbnailDrawResources>? drawResources = new(instances.Count);
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

                var camera = BuildThumbnailCamera(min, max);
                EnsureThumbnailResources(thumbnailSize);
                var pipeline = GetOrCreateThumbnailPipeline();

                // Readback retirement owns submitted draw resources; finally owns partial construction.
                var commandEncoderDescriptor = new CommandEncoderDescriptor();
                commandEncoder = _api.DeviceCreateCommandEncoder(_device, &commandEncoderDescriptor);

                if (commandEncoder == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU thumbnail command encoder.");
                }

                var colorAttachment = new RenderPassColorAttachment
                {
                    View = _thumbnailColorTextureView,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    // Transparent, matching the CPU rasterizer's zero-filled buffer -- straight alpha,
                    // nothing drawn where the model is not.
                    ClearValue = new Color(0.0, 0.0, 0.0, 0.0)
                };

                var depthAttachment = new RenderPassDepthStencilAttachment
                {
                    View = _thumbnailDepthTextureView,
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

                foreach (var instance in instances)
                {
                    if (instance.MeshData.Positions.Length == 0 || instance.MeshData.Indexes.Length == 0)
                    {
                        continue;
                    }

                    var resources = new ThumbnailDrawResources();
                    drawResources.Add(resources);
                    var mesh = ManaWebGpuMesh.CreateFromMeshData(_api, _device, _queue, instance.MeshData, instance.Name);
                    resources.Mesh = mesh;
                    var texture = _meshTextures.AcquireMeshTexture(instance.MeshData);
                    resources.Texture = texture;

                    var uniformBufferDescriptor = new BufferDescriptor
                    {
                        Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                        Size = (ulong)sizeof(ManaWebGpuSceneCaptureUniform)
                    };
                    var uniformBuffer = _api.DeviceCreateBuffer(_device, &uniformBufferDescriptor);
                    resources.UniformBuffer = (nint)uniformBuffer;
                    if (uniformBuffer == null)
                    {
                        throw new InvalidOperationException("Failed to create WebGPU thumbnail uniform buffer.");
                    }
                    var uniform = BuildThumbnailUniform(instance, camera);
                    _api.QueueWriteBuffer(_queue, uniformBuffer, 0, &uniform, (nuint)sizeof(ManaWebGpuSceneCaptureUniform));

                    var bindGroupEntries = new BindGroupEntry[3];
                    bindGroupEntries[0] = new BindGroupEntry
                    {
                        Binding = 0,
                        Buffer = uniformBuffer,
                        Offset = 0,
                        Size = (ulong)sizeof(ManaWebGpuSceneCaptureUniform)
                    };
                    bindGroupEntries[1] = new BindGroupEntry { Binding = 1, Sampler = _demoSampler };
                    bindGroupEntries[2] = new BindGroupEntry { Binding = 2, TextureView = texture.TextureView };

                    BindGroup* bindGroup;
                    fixed (BindGroupEntry* bindGroupEntriesPointer = bindGroupEntries)
                    {
                        var bindGroupDescriptor = new BindGroupDescriptor
                        {
                            Layout = _sceneCaptureBindGroupLayout,
                            EntryCount = 3,
                            Entries = bindGroupEntriesPointer
                        };
                        bindGroup = _api.DeviceCreateBindGroup(_device, &bindGroupDescriptor);
                    }

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

                var rowBytes = checked(thumbnailSize * ThumbnailBytesPerPixel);
                var paddedRowBytes = AlignTo(rowBytes, WebGpuCopyBytesPerRowAlignment);
                var readbackByteCount = checked((ulong)paddedRowBytes * thumbnailSize);

                var outputBufferDescriptor = new BufferDescriptor
                {
                    Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
                    Size = readbackByteCount
                };
                outputBuffer = _api.DeviceCreateBuffer(_device, &outputBufferDescriptor);

                if (outputBuffer == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU thumbnail readback buffer.");
                }

                // WebGPU requires texture-copy rows to be 256-byte aligned. 64px RGBA used to satisfy that
                // exactly; larger square thumbnails need padded readback rows compacted after MapAsync.
                var copySource = new ImageCopyTexture
                {
                    Texture = _thumbnailColorTexture,
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
                _api.QueueSubmit(_queue, 1, &commandBuffer);
                var retiredDrawResources = drawResources;
                var readbackTask = ManaWebGpuBufferReadback.MapAsync(_api, outputBuffer, readbackByteCount,
                    _readbackRegistry, () => DisposeThumbnailDrawResources(retiredDrawResources));
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
                                completion.TrySetResult(NormalizeThumbnailPixels(readback.Result, thumbnailSize, paddedRowBytes));
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
                    DisposeThumbnailDrawResources(drawResources);
                }
            }
        }

        private void DisposeThumbnailDrawResources(List<ThumbnailDrawResources> drawResources)
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

                var corners = new Vector3[8]
                {
                    new Vector3(localMin.X, localMin.Y, localMin.Z),
                    new Vector3(localMax.X, localMin.Y, localMin.Z),
                    new Vector3(localMin.X, localMax.Y, localMin.Z),
                    new Vector3(localMax.X, localMax.Y, localMin.Z),
                    new Vector3(localMin.X, localMin.Y, localMax.Z),
                    new Vector3(localMax.X, localMin.Y, localMax.Z),
                    new Vector3(localMin.X, localMax.Y, localMax.Z),
                    new Vector3(localMax.X, localMax.Y, localMax.Z)
                };

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

        private static ManaCameraData BuildThumbnailCamera(Vector3 min, Vector3 max)
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

        private static uint AlignTo(uint value, uint alignment)
        {
            return ((value + alignment - 1) / alignment) * alignment;
        }

        private static byte[]? NormalizeThumbnailPixels(byte[] readback, uint thumbnailSize, uint bytesPerRow)
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

        private static ManaWebGpuSceneCaptureUniform BuildThumbnailUniform(ManaRenderMeshInstance instance, ManaCameraData camera)
        {
            var model = instance.WorldTransform;
            var modelProjection = model * camera.View * camera.Projection;
            if (!Matrix4x4.Invert(model, out var inverseModel))
            {
                inverseModel = Matrix4x4.Identity;
            }

            var normalMatrix = Matrix4x4.Transpose(inverseModel);

            return new ManaWebGpuSceneCaptureUniform
            {
                ModelProjection = modelProjection,
                Model = model,
                NormalMatrix = normalMatrix,
                CameraPosition = new Vector4(camera.Position, 1.0f),
                MaterialAlbedo = instance.Albedo,
                // Repurposed: xyz carries the fixed key-light direction -- see mesh_thumbnail.wgsl's header.
                IrradianceFactors = new Vector4(ThumbnailLightDirectionWorld, 0.0f)
                // Every other field intentionally left zeroed -- unused by mesh_thumbnail.wgsl.
            };
        }

        private RenderPipeline* GetOrCreateThumbnailPipeline()
        {
            if (_thumbnailPipeline != null)
            {
                return _thumbnailPipeline;
            }

            var shaderModule = CreateShaderModule(Path.Combine("Assets", "Shaders", "WebGpu", "mesh_thumbnail.wgsl"));
            _thumbnailPipeline = CreateDemoRenderPipeline(shaderModule, TextureFormat.Rgba8UnormSrgb, _sceneCapturePipelineLayout, "MeshThumbnail");
            _shaderModules["MeshThumbnail"] = (nint)shaderModule;
            ManaConsole.WriteDiagnostic("WebGPU mesh thumbnail pipeline created successfully.");
            return _thumbnailPipeline;
        }

        private void EnsureThumbnailResources(uint thumbnailSize)
        {
            if (_thumbnailColorTexture != null)
            {
                if (_thumbnailResourceSize == thumbnailSize)
                {
                    return;
                }

                ReleaseThumbnailResources();
            }

            _thumbnailResourceSize = thumbnailSize;

            var colorDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = thumbnailSize, Height = thumbnailSize, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8UnormSrgb,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _thumbnailColorTexture = _api.DeviceCreateTexture(_device, &colorDescriptor);
            if (_thumbnailColorTexture == null)
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
            _thumbnailColorTextureView = _api.TextureCreateView(_thumbnailColorTexture, &colorViewDescriptor);
            if (_thumbnailColorTextureView == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU thumbnail color texture view.");
            }

            // Dedicated depth target, not shared with _sceneCaptureDepthTexture -- that one exists to
            // amortize create/release churn for the cubemap capture's much larger, variable sizes. Mesh
            // thumbnails are a small fixed-size cache, so sharing would only risk fighting over one field.
            var depthDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = thumbnailSize, Height = thumbnailSize, DepthOrArrayLayers = 1 },
                Format = _depthFormat,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _thumbnailDepthTexture = _api.DeviceCreateTexture(_device, &depthDescriptor);
            if (_thumbnailDepthTexture == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU thumbnail depth texture.");
            }

            var depthViewDescriptor = new TextureViewDescriptor
            {
                Format = _depthFormat,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.DepthOnly
            };
            _thumbnailDepthTextureView = _api.TextureCreateView(_thumbnailDepthTexture, &depthViewDescriptor);
            if (_thumbnailDepthTextureView == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU thumbnail depth texture view.");
            }
        }

        private void ReleaseThumbnailResources()
        {
            if (_thumbnailPipeline != null)
            {
                _api.RenderPipelineRelease(_thumbnailPipeline);
                _thumbnailPipeline = null;
            }

            if (_thumbnailColorTextureView != null)
            {
                _api.TextureViewRelease(_thumbnailColorTextureView);
                _thumbnailColorTextureView = null;
            }

            if (_thumbnailColorTexture != null)
            {
                _api.TextureRelease(_thumbnailColorTexture);
                _thumbnailColorTexture = null;
            }

            if (_thumbnailDepthTextureView != null)
            {
                _api.TextureViewRelease(_thumbnailDepthTextureView);
                _thumbnailDepthTextureView = null;
            }

            if (_thumbnailDepthTexture != null)
            {
                _api.TextureRelease(_thumbnailDepthTexture);
                _thumbnailDepthTexture = null;
            }

            _thumbnailResourceSize = 0;
        }
    }
}

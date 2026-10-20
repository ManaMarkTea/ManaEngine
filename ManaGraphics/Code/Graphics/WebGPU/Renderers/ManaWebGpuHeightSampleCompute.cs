using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Renderers
{
    // Samples planet heights on the GPU (height_sample_compute.wgsl) for PlanetPatchMesh: callable from any thread, encoded
    // on the render thread. The pipeline is built on first use; buffers are made per call.
    // Not unsafe at class level: it awaits, so pointer members are marked unsafe one by one.
    internal sealed class ManaWebGpuHeightSampleCompute : ManaWebGpuDeviceObject, IManaComputeShader
    {
        // Bounds pre-projected to Web Mercator here, once per tile, instead of per point and tile in the shader's hot loop,
        // which hung the GPU.
        [StructLayout(LayoutKind.Sequential)]
        private struct GpuTileDescriptor
        {
            public float MercXMin;
            public float MercXMax;
            public float MercYMin;
            public float MercYMax;
            public uint PixelOffset;
            public uint Width;
            public uint Height;
            public uint Padding0;
        }

        // Must match the shader's MAX_LATITUDE_DEGREES; ManaGraphics cannot reference ManaGeoTools.
        private const float MaxLatitudeDegrees = 85.0511287798f;

        // The shader's mercator_xy, done once per tile here.
        private static Vector2 MercatorXy(float lonDegrees, float latDegrees)
        {
            var latClamped = Math.Clamp(latDegrees, -MaxLatitudeDegrees, MaxLatitudeDegrees);
            var latRad = latClamped * MathF.PI / 180f;
            var x = (lonDegrees + 180f) / 360f;
            var y = (1f - MathF.Asinh(MathF.Tan(latRad)) / MathF.PI) / 2f;
            return new Vector2(x, y);
        }

        private readonly ManaWebGpuReadbackRegistry _readbackRegistry;
        private readonly ManaWebGpuRenderThreadQueue _renderThread;
        private unsafe ShaderModule* _shaderModule;
        private unsafe BindGroupLayout* _bindGroupLayout;
        private unsafe PipelineLayout* _pipelineLayout;
        private unsafe ComputePipeline* _pipeline;

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.WebGpu;
        public string Name => "Height Sample";

        public ManaWebGpuHeightSampleCompute(ManaWebGpuDevice gpu, ManaWebGpuReadbackRegistry readbackRegistry, ManaWebGpuRenderThreadQueue renderThread)
            : base(gpu)
        {
            _readbackRegistry = readbackRegistry;
            _renderThread = renderThread;
        }

        /// <summary>Callable from any thread; the encode and submit run on the render thread, the readback completes off it.</summary>
        public Task<float[]> SampleHeightsAsync(
            ReadOnlyMemory<Vector2> lonLatPointsDegrees,
            IReadOnlyList<HeightSampleTileDescriptor> tilesNewestFirst)
        {
            if (_renderThread.IsClosed)
            {
                return Task.FromCanceled<float[]>(new CancellationToken(true));
            }

            // Asynchronous continuations keep callers off the render thread mid-drain.
            var completion = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _renderThread.Enqueue(() =>
            {
                if (_renderThread.IsClosed)
                {
                    completion.TrySetCanceled();
                    return;
                }

                try
                {
                    EnsurePipeline();
                    Complete(SampleOnRenderThreadAsync(lonLatPointsDegrees, tilesNewestFirst), completion);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
            return completion.Task;
        }

        private static async void Complete(Task<float[]> sampling, TaskCompletionSource<float[]> completion)
        {
            try
            {
                completion.TrySetResult(await sampling.ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        // A failed build leaves nothing behind, so the next request tries again.
        private unsafe void EnsurePipeline()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pipeline != null)
            {
                return;
            }

            try
            {
                _shaderModule = CreateShaderModule(ManaWebGpuDevice.ResolveAssetPath(ManaWebGpuDevice.ShaderPath("height_sample_compute.wgsl")));
                _bindGroupLayout = CreateBindGroupLayout();
                _pipelineLayout = CreatePipelineLayout();
                _pipeline = CreatePipeline();
            }
            catch
            {
                ReleasePipeline();
                throw;
            }
        }

        private async Task<float[]> SampleOnRenderThreadAsync(
            ReadOnlyMemory<Vector2> lonLatPointsDegrees,
            IReadOnlyList<HeightSampleTileDescriptor> tilesNewestFirst)
        {
            if (lonLatPointsDegrees.Length == 0)
            {
                return Array.Empty<float>();
            }

            // Split into dispatch-submit vs. readback-await so it's clear which side any future slowness
            // is coming from.
            var label = $"[HeightSampleTiming][WebGPU] points={lonLatPointsDegrees.Length} tiles={tilesNewestFirst.Count}";
            Task<byte[]> bytesTask;
            using (ManaLog.TimeVerbose($"{label} phase=dispatchSubmit"))
            {
                bytesTask = DispatchAndRequestReadback(lonLatPointsDegrees.Span, tilesNewestFirst);
            }
            // ManaLog.TimeVerbose's scope is a ref struct and can't be held alive across an await, so
            // this span uses the GetTimestamp()/WriteVerboseElapsed() pair instead.
            var readbackStart = ManaLog.GetTimestamp();
            var bytes = await bytesTask;
            ManaLog.WriteVerboseElapsed($"{label} phase=readbackAwait", readbackStart);
            var heights = new float[lonLatPointsDegrees.Length];
            MemoryMarshal.Cast<byte, float>(bytes).CopyTo(heights);
            return heights;
        }

        // The pointer work, apart from the async methods so their machinery stays out of unsafe code.
        private unsafe Task<byte[]> DispatchAndRequestReadback(
            ReadOnlySpan<Vector2> points,
            IReadOnlyList<HeightSampleTileDescriptor> tilesNewestFirst)
        {
            var tileDescriptors = new GpuTileDescriptor[Math.Max(1, tilesNewestFirst.Count)];
            var heightDataFloatCount = 0;
            for (var i = 0; i < tilesNewestFirst.Count; i++)
            {
                var tile = tilesNewestFirst[i];
                // lat_max (north) maps to the SMALLER Mercator Y (top of a north-up image), lat_min
                // (south) to the larger one -- matches the shader's own documented convention.
                var mercMin = MercatorXy(tile.LonMin, tile.LatMax);
                var mercMax = MercatorXy(tile.LonMax, tile.LatMin);
                tileDescriptors[i] = new GpuTileDescriptor
                {
                    MercXMin = mercMin.X,
                    MercXMax = mercMax.X,
                    MercYMin = mercMin.Y,
                    MercYMax = mercMax.Y,
                    PixelOffset = (uint)heightDataFloatCount,
                    Width = (uint)tile.Width,
                    Height = (uint)tile.Height,
                    Padding0 = 0,
                };
                heightDataFloatCount += tile.Width * tile.Height;
            }

            var heightData = new float[Math.Max(1, heightDataFloatCount)];
            var writeOffset = 0;
            foreach (var tile in tilesNewestFirst)
            {
                tile.HeightData.Span.CopyTo(heightData.AsSpan(writeOffset));
                writeOffset += tile.HeightData.Length;
            }

            WebGpuBuffer* pointsBuffer = null, tilesBuffer = null, heightDataBuffer = null;
            WebGpuBuffer* outputBuffer = null, tileCountBuffer = null, stagingBuffer = null;
            BindGroup* bindGroup = null;
            CommandEncoder* commandEncoder = null;
            ComputePassEncoder* computePass = null;
            CommandBuffer* commandBuffer = null;
            try
            {
                pointsBuffer = CreateStorageBuffer<Vector2>(points, BufferUsage.Storage | BufferUsage.CopyDst);
                tilesBuffer = CreateStorageBuffer<GpuTileDescriptor>(tileDescriptors, BufferUsage.Storage | BufferUsage.CopyDst);
                heightDataBuffer = CreateStorageBuffer<float>(heightData, BufferUsage.Storage | BufferUsage.CopyDst);
                var outputByteSize = (ulong)(points.Length * sizeof(float));
                outputBuffer = CreateBuffer(outputByteSize, BufferUsage.Storage | BufferUsage.CopySrc);
                tileCountBuffer = CreateUniformBuffer((uint)tilesNewestFirst.Count);

                bindGroup = CreateBindGroup(pointsBuffer, tilesBuffer, heightDataBuffer, outputBuffer, tileCountBuffer);

                var commandEncoderDescriptor = new CommandEncoderDescriptor();
                commandEncoder = Api.DeviceCreateCommandEncoder(Device, &commandEncoderDescriptor);
                if (commandEncoder == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU command encoder for height sampling.");
                }

                var computePassDescriptor = new ComputePassDescriptor();
                computePass = Api.CommandEncoderBeginComputePass(commandEncoder, &computePassDescriptor);
                if (computePass == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU height-sample compute pass.");
                }
                Api.ComputePassEncoderSetPipeline(computePass, _pipeline);
                Api.ComputePassEncoderSetBindGroup(computePass, 0, bindGroup, 0, null);
                var workgroups = (uint)((points.Length + 63) / 64);
                Api.ComputePassEncoderDispatchWorkgroups(computePass, workgroups, 1, 1);
                Api.ComputePassEncoderEnd(computePass);
                Api.ComputePassEncoderRelease(computePass);
                computePass = null;

                stagingBuffer = ManaWebGpuBufferReadback.EncodeCopyToStaging(Api, Device, commandEncoder, outputBuffer, outputByteSize);

                var commandBufferDescriptor = new CommandBufferDescriptor();
                commandBuffer = Api.CommandEncoderFinish(commandEncoder, &commandBufferDescriptor);
                if (commandBuffer == null)
                {
                    throw new InvalidOperationException("Failed to finish WebGPU height-sample commands.");
                }
                Api.QueueSubmit(Queue, 1, &commandBuffer);

                var readback = ManaWebGpuBufferReadback.MapAsync(Api, stagingBuffer, outputByteSize, _readbackRegistry);
                stagingBuffer = null;
                return readback;
            }
            finally
            {
                if (computePass != null) { Api.ComputePassEncoderRelease(computePass); }
                if (commandBuffer != null) { Api.CommandBufferRelease(commandBuffer); }
                if (commandEncoder != null) { Api.CommandEncoderRelease(commandEncoder); }
                if (bindGroup != null) { Api.BindGroupRelease(bindGroup); }
                if (pointsBuffer != null) { Api.BufferRelease(pointsBuffer); }
                if (tilesBuffer != null) { Api.BufferRelease(tilesBuffer); }
                if (heightDataBuffer != null) { Api.BufferRelease(heightDataBuffer); }
                if (outputBuffer != null) { Api.BufferRelease(outputBuffer); }
                if (tileCountBuffer != null) { Api.BufferRelease(tileCountBuffer); }
                if (stagingBuffer != null) { Api.BufferRelease(stagingBuffer); }
            }
        }

        private bool _disposed;

        public override unsafe void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            ReleasePipeline();
        }

        private unsafe void ReleasePipeline()
        {
            if (_pipeline != null)
            {
                Api.ComputePipelineRelease(_pipeline);
                _pipeline = null;
            }

            if (_pipelineLayout != null)
            {
                Api.PipelineLayoutRelease(_pipelineLayout);
                _pipelineLayout = null;
            }

            if (_bindGroupLayout != null)
            {
                Api.BindGroupLayoutRelease(_bindGroupLayout);
                _bindGroupLayout = null;
            }

            if (_shaderModule != null)
            {
                Api.ShaderModuleRelease(_shaderModule);
                _shaderModule = null;
            }
        }

        private unsafe WebGpuBuffer* CreateBuffer(ulong byteSize, BufferUsage usage)
        {
            var descriptor = new BufferDescriptor
            {
                Usage = usage,
                Size = Math.Max(4ul, byteSize)
            };
            var buffer = Api.DeviceCreateBuffer(Device, &descriptor);
            if (buffer == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU height-sample buffer.");
            }

            return buffer;
        }

        private unsafe WebGpuBuffer* CreateStorageBuffer<T>(ReadOnlySpan<T> data, BufferUsage usage) where T : unmanaged
        {
            var byteSize = (ulong)(data.Length * sizeof(T));
            var buffer = CreateBuffer(byteSize, usage);
            if (data.Length > 0)
            {
                fixed (T* dataPointer = data)
                {
                    Api.QueueWriteBuffer(Queue, buffer, 0, dataPointer, (nuint)byteSize);
                }
            }

            return buffer;
        }

        private unsafe WebGpuBuffer* CreateUniformBuffer(uint value)
        {
            var buffer = CreateBuffer(sizeof(uint), BufferUsage.Uniform | BufferUsage.CopyDst);
            Api.QueueWriteBuffer(Queue, buffer, 0, &value, sizeof(uint));
            return buffer;
        }

        private unsafe BindGroup* CreateBindGroup(
            WebGpuBuffer* pointsBuffer,
            WebGpuBuffer* tilesBuffer,
            WebGpuBuffer* heightDataBuffer,
            WebGpuBuffer* outputBuffer,
            WebGpuBuffer* tileCountBuffer)
        {
            var entries = stackalloc BindGroupEntry[5];
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = pointsBuffer, Offset = 0, Size = ulong.MaxValue };
            entries[1] = new BindGroupEntry { Binding = 1, Buffer = tilesBuffer, Offset = 0, Size = ulong.MaxValue };
            entries[2] = new BindGroupEntry { Binding = 2, Buffer = heightDataBuffer, Offset = 0, Size = ulong.MaxValue };
            entries[3] = new BindGroupEntry { Binding = 3, Buffer = outputBuffer, Offset = 0, Size = ulong.MaxValue };
            entries[4] = new BindGroupEntry { Binding = 4, Buffer = tileCountBuffer, Offset = 0, Size = ulong.MaxValue };

            var descriptor = new BindGroupDescriptor
            {
                Layout = _bindGroupLayout,
                EntryCount = 5,
                Entries = entries
            };
            var bindGroup = Api.DeviceCreateBindGroup(Device, &descriptor);
            if (bindGroup == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU height-sample bind group.");
            }

            return bindGroup;
        }

        private unsafe ShaderModule* CreateShaderModule(string shaderPath)
        {
            var source = ManaWgslPreprocessor.LoadResolved(shaderPath);
            nint sourcePointer = Marshal.StringToCoTaskMemUTF8(source);
            try
            {
                var wgslDescriptor = new ShaderModuleWGSLDescriptor
                {
                    Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                    Code = (byte*)sourcePointer
                };
                var shaderDescriptor = new ShaderModuleDescriptor { NextInChain = &wgslDescriptor.Chain };
                var shaderModule = Api.DeviceCreateShaderModule(Device, &shaderDescriptor);
                if (shaderModule == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU height-sample shader module from {shaderPath}.");
                }

                return shaderModule;
            }
            finally
            {
                Marshal.FreeCoTaskMem(sourcePointer);
            }
        }

        private unsafe BindGroupLayout* CreateBindGroupLayout()
        {
            var entries = stackalloc BindGroupLayoutEntry[5];
            entries[0] = new BindGroupLayoutEntry { Binding = 0, Visibility = ShaderStage.Compute, Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage } };
            entries[1] = new BindGroupLayoutEntry { Binding = 1, Visibility = ShaderStage.Compute, Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage } };
            entries[2] = new BindGroupLayoutEntry { Binding = 2, Visibility = ShaderStage.Compute, Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage } };
            entries[3] = new BindGroupLayoutEntry { Binding = 3, Visibility = ShaderStage.Compute, Buffer = new BufferBindingLayout { Type = BufferBindingType.Storage } };
            entries[4] = new BindGroupLayoutEntry { Binding = 4, Visibility = ShaderStage.Compute, Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform } };

            var descriptor = new BindGroupLayoutDescriptor { EntryCount = 5, Entries = entries };
            var layout = Api.DeviceCreateBindGroupLayout(Device, &descriptor);
            if (layout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU height-sample bind group layout.");
            }

            return layout;
        }

        private unsafe PipelineLayout* CreatePipelineLayout()
        {
            var bindGroupLayout = _bindGroupLayout;
            var descriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &bindGroupLayout };
            var layout = Api.DeviceCreatePipelineLayout(Device, &descriptor);
            if (layout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU height-sample pipeline layout.");
            }

            return layout;
        }

        private unsafe ComputePipeline* CreatePipeline()
        {
            nint entryPoint = Marshal.StringToCoTaskMemUTF8("cs_main");
            try
            {
                var descriptor = new ComputePipelineDescriptor
                {
                    Layout = _pipelineLayout,
                    Compute = new ProgrammableStageDescriptor { Module = _shaderModule, EntryPoint = (byte*)entryPoint }
                };
                var pipeline = Api.DeviceCreateComputePipeline(Device, &descriptor);
                if (pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU height-sample compute pipeline.");
                }

                return pipeline;
            }
            finally
            {
                Marshal.FreeCoTaskMem(entryPoint);
            }
        }
    }
}

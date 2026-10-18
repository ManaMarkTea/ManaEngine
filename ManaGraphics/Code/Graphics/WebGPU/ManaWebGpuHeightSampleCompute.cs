using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    // GPU-accelerated replacement for PlanetPatchMesh.GenerateData()'s per-vertex CPU height sampling
    // (see height_sample_compute.wgsl for the actual sampling algorithm). Structured like
    // ManaWebGpuEnvironmentPrefilterCompute (pipeline/bind-group-layout built once in the constructor),
    // but has no persistent per-dispatch resources -- every buffer here is created fresh per call and
    // released once encoded/submitted.
    //
    // Deliberately NOT declared `unsafe` at the class level (unlike ManaWebGpuEnvironmentPrefilterCompute)
    // -- SampleHeightsAsync needs to `await`, and `await` is illegal anywhere inside a type whose unsafe
    // modifier is declared at the class level, even in methods that don't themselves touch a pointer. Each
    // pointer-touching member below is individually marked unsafe instead.
    internal sealed class ManaWebGpuHeightSampleCompute : ManaComputeShader
    {
        // Bounds are pre-projected into normalized Web Mercator space (not raw lon/lat degrees) -- see
        // this struct's field-level comment inline in height_sample_compute.wgsl for why: computing the
        // lon/lat->Mercator projection (asinh+tan) once per tile here, instead of once per (point, tile)
        // pair inside the shader's hot loop, is what actually fixes the GPU hang this was built to avoid.
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

        // Const MaxLatitudeDegrees is intentionally duplicated (not shared with e.g. SlippyMapMath)
        // -- ManaGraphics doesn't reference ManaGeoTools (wrong dependency direction), and this needs to
        // match height_sample_compute.wgsl's own MAX_LATITUDE_DEGREES constant exactly regardless.
        private const float MaxLatitudeDegrees = 85.0511287798f;

        // Mirrors height_sample_compute.wgsl's own mercator_xy function exactly -- called once per tile
        // here (CPU), matching the shader's once-per-point call, instead of the shader ALSO doing it
        // once per tile inside its hot loop (the original, much slower design).
        private static Vector2 MercatorXy(float lonDegrees, float latDegrees)
        {
            var latClamped = Math.Clamp(latDegrees, -MaxLatitudeDegrees, MaxLatitudeDegrees);
            var latRad = latClamped * MathF.PI / 180f;
            var x = (lonDegrees + 180f) / 360f;
            var y = (1f - MathF.Asinh(MathF.Tan(latRad)) / MathF.PI) / 2f;
            return new Vector2(x, y);
        }

        private readonly WebGPU _api;
        private readonly unsafe Device* _device;
        private readonly unsafe Queue* _queue;
        private readonly ManaWebGpuReadbackRegistry _readbackRegistry;
        private readonly unsafe ShaderModule* _shaderModule;
        private readonly unsafe BindGroupLayout* _bindGroupLayout;
        private readonly unsafe PipelineLayout* _pipelineLayout;
        private readonly unsafe ComputePipeline* _pipeline;

        public unsafe ManaWebGpuHeightSampleCompute(
            WebGPU api,
            Device* device,
            Queue* queue,
            ManaWebGpuReadbackRegistry readbackRegistry,
            string shaderPath)
            : base(ManaGraphicsBackendKind.WebGpu, "Height Sample")
        {
            _api = api;
            _device = device;
            _queue = queue;
            _readbackRegistry = readbackRegistry;
            try
            {
                _shaderModule = CreateShaderModule(shaderPath);
                _bindGroupLayout = CreateBindGroupLayout();
                _pipelineLayout = CreatePipelineLayout();
                _pipeline = CreatePipeline();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public async Task<float[]> SampleHeightsAsync(
            ReadOnlyMemory<Vector2> lonLatPointsDegrees,
            IReadOnlyList<HeightSampleTileDescriptor> tilesNewestFirst)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
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

        // All the actual WebGPU/pointer work -- builds every input buffer, dispatches the compute pass,
        // encodes the readback copy, submits, and returns the (still in-flight) readback Task. Kept
        // separate from SampleHeightsAsync above so the async/await machinery never has to live inside an
        // unsafe context.
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
                commandEncoder = _api.DeviceCreateCommandEncoder(_device, &commandEncoderDescriptor);
                if (commandEncoder == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU command encoder for height sampling.");
                }

                var computePassDescriptor = new ComputePassDescriptor();
                computePass = _api.CommandEncoderBeginComputePass(commandEncoder, &computePassDescriptor);
                if (computePass == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU height-sample compute pass.");
                }
                _api.ComputePassEncoderSetPipeline(computePass, _pipeline);
                _api.ComputePassEncoderSetBindGroup(computePass, 0, bindGroup, 0, null);
                var workgroups = (uint)((points.Length + 63) / 64);
                _api.ComputePassEncoderDispatchWorkgroups(computePass, workgroups, 1, 1);
                _api.ComputePassEncoderEnd(computePass);
                _api.ComputePassEncoderRelease(computePass);
                computePass = null;

                stagingBuffer = ManaWebGpuBufferReadback.EncodeCopyToStaging(_api, _device, commandEncoder, outputBuffer, outputByteSize);

                var commandBufferDescriptor = new CommandBufferDescriptor();
                commandBuffer = _api.CommandEncoderFinish(commandEncoder, &commandBufferDescriptor);
                if (commandBuffer == null)
                {
                    throw new InvalidOperationException("Failed to finish WebGPU height-sample commands.");
                }
                _api.QueueSubmit(_queue, 1, &commandBuffer);

                var readback = ManaWebGpuBufferReadback.MapAsync(_api, stagingBuffer, outputByteSize, _readbackRegistry);
                stagingBuffer = null;
                return readback;
            }
            finally
            {
                if (computePass != null) { _api.ComputePassEncoderRelease(computePass); }
                if (commandBuffer != null) { _api.CommandBufferRelease(commandBuffer); }
                if (commandEncoder != null) { _api.CommandEncoderRelease(commandEncoder); }
                if (bindGroup != null) { _api.BindGroupRelease(bindGroup); }
                if (pointsBuffer != null) { _api.BufferRelease(pointsBuffer); }
                if (tilesBuffer != null) { _api.BufferRelease(tilesBuffer); }
                if (heightDataBuffer != null) { _api.BufferRelease(heightDataBuffer); }
                if (outputBuffer != null) { _api.BufferRelease(outputBuffer); }
                if (tileCountBuffer != null) { _api.BufferRelease(tileCountBuffer); }
                if (stagingBuffer != null) { _api.BufferRelease(stagingBuffer); }
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
            if (_pipeline != null)
            {
                _api.ComputePipelineRelease(_pipeline);
            }

            if (_pipelineLayout != null)
            {
                _api.PipelineLayoutRelease(_pipelineLayout);
            }

            if (_bindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_bindGroupLayout);
            }

            if (_shaderModule != null)
            {
                _api.ShaderModuleRelease(_shaderModule);
            }
        }

        private unsafe WebGpuBuffer* CreateBuffer(ulong byteSize, BufferUsage usage)
        {
            var descriptor = new BufferDescriptor
            {
                Usage = usage,
                Size = Math.Max(4ul, byteSize)
            };
            var buffer = _api.DeviceCreateBuffer(_device, &descriptor);
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
                    _api.QueueWriteBuffer(_queue, buffer, 0, dataPointer, (nuint)byteSize);
                }
            }

            return buffer;
        }

        private unsafe WebGpuBuffer* CreateUniformBuffer(uint value)
        {
            var buffer = CreateBuffer(sizeof(uint), BufferUsage.Uniform | BufferUsage.CopyDst);
            _api.QueueWriteBuffer(_queue, buffer, 0, &value, sizeof(uint));
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
            var bindGroup = _api.DeviceCreateBindGroup(_device, &descriptor);
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
                var shaderModule = _api.DeviceCreateShaderModule(_device, &shaderDescriptor);
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
            var layout = _api.DeviceCreateBindGroupLayout(_device, &descriptor);
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
            var layout = _api.DeviceCreatePipelineLayout(_device, &descriptor);
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
                var pipeline = _api.DeviceCreateComputePipeline(_device, &descriptor);
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

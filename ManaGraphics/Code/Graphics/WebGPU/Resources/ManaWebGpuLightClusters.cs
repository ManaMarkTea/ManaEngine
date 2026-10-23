using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>
    /// The placed lights and their clusters (<see cref="ManaLightClusters"/>) on the GPU: three storage buffers the scene
    /// shaders read (frame group bindings 3-5). The lights are uploaded each frame; the cells and their light lists are
    /// worked out by a compute pass (<c>light_clusters.wgsl</c>) at the start of the frame, so the CPU never loops over
    /// lights and cells. Sized for the most a frame can hold, so the bind groups never change.
    /// </summary>
    internal sealed unsafe class ManaWebGpuLightClusters : ManaWebGpuDeviceObject
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Params
        {
            public Matrix4x4 View;
            public Matrix4x4 InverseProjection;
            public Vector4 Depth;
        }

        private readonly ManaLightClusters _clusters = new();
        private WebGpuBuffer* _params;
        private WebGpuBuffer* _used;
        private ShaderModule* _shader;
        private BindGroupLayout* _layout;
        private PipelineLayout* _pipelineLayout;
        private ComputePipeline* _pipeline;
        private BindGroup* _bindGroup;
        private bool _pending;

        public ManaWebGpuLightClusters(ManaWebGpuDevice gpu)
            : base(gpu)
        {
            try
            {
                LightBuffer = Create(LightBufferSize, BufferUsage.Storage | BufferUsage.CopyDst);
                CellBuffer = Create(CellBufferSize, BufferUsage.Storage | BufferUsage.CopyDst);
                IndexBuffer = Create(IndexBufferSize, BufferUsage.Storage);
                _params = Create((ulong)sizeof(Params), BufferUsage.Uniform | BufferUsage.CopyDst);
                _used = Create(sizeof(uint), BufferUsage.Storage | BufferUsage.CopyDst);
                CreatePipeline();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public WebGpuBuffer* LightBuffer { get; private set; }
        public WebGpuBuffer* CellBuffer { get; private set; }
        public WebGpuBuffer* IndexBuffer { get; private set; }

        public ulong LightBufferSize => (ulong)(ManaRenderScene.MaxLocalLights * 2 * sizeof(Vector4));
        public ulong CellBufferSize => (ulong)(ManaLightClusters.CellCount * 2 * sizeof(uint));
        public ulong IndexBufferSize => (ulong)(ManaLightClusters.MaxIndices * sizeof(uint));

        /// <summary>The frame's slicing, for the frame uniform.</summary>
        public ManaLightClusters Clusters => _clusters;

        /// <summary>Uploads the scene's lights and what the binning needs; <see cref="Encode"/> then bins them.</summary>
        public void Update(ManaRenderScene scene, Vector2 viewport)
        {
            _clusters.Prepare(scene.LocalLights, scene.Camera, viewport);
            _pending = _clusters.LightCount > 0;
            if (!_pending)
            {
                return;
            }

            var parameters = new Params
            {
                View = _clusters.View,
                InverseProjection = _clusters.InverseProjection,
                Depth = new Vector4(_clusters.Near, _clusters.SliceScale, _clusters.LightCount, 0.0f)
            };
            uint zero = 0;
            fixed (Vector4* lights = _clusters.Lights)
            {
                Api.QueueWriteBuffer(Queue, LightBuffer, 0, lights, (nuint)(_clusters.LightCount * 2 * sizeof(Vector4)));
            }
            Api.QueueWriteBuffer(Queue, _params, 0, &parameters, (nuint)sizeof(Params));
            Api.QueueWriteBuffer(Queue, _used, 0, &zero, sizeof(uint));
        }

        /// <summary>Bins the lights into the cells, ahead of anything that draws with them. Nothing when there are none.</summary>
        public void Encode(CommandEncoder* encoder)
        {
            if (!_pending)
            {
                return;
            }
            _pending = false;

            var descriptor = new ComputePassDescriptor();
            var pass = Api.CommandEncoderBeginComputePass(encoder, &descriptor);
            try
            {
                Api.ComputePassEncoderSetPipeline(pass, _pipeline);
                Api.ComputePassEncoderSetBindGroup(pass, 0, _bindGroup, 0, null);
                Api.ComputePassEncoderDispatchWorkgroups(pass, ManaLightClusters.TilesX, ManaLightClusters.TilesY, ManaLightClusters.Slices);
                Api.ComputePassEncoderEnd(pass);
            }
            finally
            {
                Api.ComputePassEncoderRelease(pass);
            }
        }

        private void CreatePipeline()
        {
            var source = ManaWgslPreprocessor.LoadResolved(ManaWebGpuDevice.ResolveAssetPath(ManaWebGpuDevice.ShaderPath("light_clusters.wgsl")));
            var code = Marshal.StringToCoTaskMemUTF8(source);
            var entryPoint = Marshal.StringToCoTaskMemUTF8("cs_main");
            try
            {
                var wgsl = new ShaderModuleWGSLDescriptor
                {
                    Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                    Code = (byte*)code
                };
                var shaderDescriptor = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
                _shader = Api.DeviceCreateShaderModule(Device, &shaderDescriptor);
                if (_shader == null)
                {
                    throw new InvalidOperationException("Failed to create the light cluster shader.");
                }

                var entries = stackalloc BindGroupLayoutEntry[5];
                entries[0] = Entry(0, BufferBindingType.Uniform);
                entries[1] = Entry(1, BufferBindingType.ReadOnlyStorage);
                entries[2] = Entry(2, BufferBindingType.Storage);
                entries[3] = Entry(3, BufferBindingType.Storage);
                entries[4] = Entry(4, BufferBindingType.Storage);
                var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 5, Entries = entries };
                _layout = Api.DeviceCreateBindGroupLayout(Device, &layoutDescriptor);
                if (_layout == null)
                {
                    throw new InvalidOperationException("Failed to create the light cluster bind group layout.");
                }

                var layout = _layout;
                var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &layout };
                _pipelineLayout = Api.DeviceCreatePipelineLayout(Device, &pipelineLayoutDescriptor);
                var pipelineDescriptor = new ComputePipelineDescriptor
                {
                    Layout = _pipelineLayout,
                    Compute = new ProgrammableStageDescriptor { Module = _shader, EntryPoint = (byte*)entryPoint }
                };
                _pipeline = Api.DeviceCreateComputePipeline(Device, &pipelineDescriptor);
                if (_pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create the light cluster pipeline.");
                }

                var groupEntries = stackalloc BindGroupEntry[5];
                groupEntries[0] = new BindGroupEntry { Binding = 0, Buffer = _params, Size = (ulong)sizeof(Params) };
                groupEntries[1] = new BindGroupEntry { Binding = 1, Buffer = LightBuffer, Size = LightBufferSize };
                groupEntries[2] = new BindGroupEntry { Binding = 2, Buffer = CellBuffer, Size = CellBufferSize };
                groupEntries[3] = new BindGroupEntry { Binding = 3, Buffer = IndexBuffer, Size = IndexBufferSize };
                groupEntries[4] = new BindGroupEntry { Binding = 4, Buffer = _used, Size = sizeof(uint) };
                var groupDescriptor = new BindGroupDescriptor { Layout = _layout, EntryCount = 5, Entries = groupEntries };
                _bindGroup = Api.DeviceCreateBindGroup(Device, &groupDescriptor);
                if (_bindGroup == null)
                {
                    throw new InvalidOperationException("Failed to create the light cluster bind group.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(code);
                Marshal.FreeCoTaskMem(entryPoint);
            }
        }

        private static BindGroupLayoutEntry Entry(uint binding, BufferBindingType type) =>
            new() { Binding = binding, Visibility = ShaderStage.Compute, Buffer = new BufferBindingLayout { Type = type } };

        private WebGpuBuffer* Create(ulong size, BufferUsage usage)
        {
            var descriptor = new BufferDescriptor { Usage = usage, Size = size };
            var buffer = Api.DeviceCreateBuffer(Device, &descriptor);
            if (buffer == null)
            {
                throw new InvalidOperationException("Failed to create a WebGPU light cluster buffer.");
            }
            return buffer;
        }

        public override void Dispose()
        {
            if (_bindGroup != null) { Api.BindGroupRelease(_bindGroup); _bindGroup = null; }
            if (_pipeline != null) { Api.ComputePipelineRelease(_pipeline); _pipeline = null; }
            if (_pipelineLayout != null) { Api.PipelineLayoutRelease(_pipelineLayout); _pipelineLayout = null; }
            if (_layout != null) { Api.BindGroupLayoutRelease(_layout); _layout = null; }
            if (_shader != null) { Api.ShaderModuleRelease(_shader); _shader = null; }
            if (_params != null) { Api.BufferRelease(_params); _params = null; }
            if (_used != null) { Api.BufferRelease(_used); _used = null; }
            if (LightBuffer != null) { Api.BufferRelease(LightBuffer); LightBuffer = null; }
            if (CellBuffer != null) { Api.BufferRelease(CellBuffer); CellBuffer = null; }
            if (IndexBuffer != null) { Api.BufferRelease(IndexBuffer); IndexBuffer = null; }
        }
    }
}

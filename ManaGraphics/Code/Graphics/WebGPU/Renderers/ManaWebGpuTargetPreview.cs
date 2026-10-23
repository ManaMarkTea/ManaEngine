using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Renderers
{
    /// <summary>
    /// Draws the frame's intermediate targets -- HDR scene colour, depth, the three sun shadow cascades and the placed
    /// lights per screen tile -- small into one texture (<see cref="ManaRenderTargetPreview"/>, <c>target_preview.wgsl</c>)
    /// for the editor's Render Targets panel. Built on first use; encoded only on frames whose UI shows it.
    /// </summary>
    internal sealed unsafe class ManaWebGpuTargetPreview : ManaWebGpuDeviceObject
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Params
        {
            public Matrix4x4 InverseProjection;
            public Vector4 Info;
        }

        private Texture* _texture;
        private ShaderModule* _shader;
        private BindGroupLayout* _layout;
        private PipelineLayout* _pipelineLayout;
        private RenderPipeline* _pipeline;
        private WebGpuBuffer* _params;

        public ManaWebGpuTargetPreview(ManaWebGpuDevice gpu)
            : base(gpu)
        {
            try
            {
                Create();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>The preview picture, for the UI to draw from.</summary>
        public TextureView* View { get; private set; }

        /// <summary>Fills the preview from this frame's targets; the views are whatever the frame drew into.</summary>
        public void Encode(
            CommandEncoder* encoder, ManaRenderScene scene, TextureView* sceneColor, TextureView* depth, TextureView* shadows,
            WebGpuBuffer* lightCells, ulong lightCellsSize, int lightCount)
        {
            Matrix4x4.Invert(scene.Camera.Projection, out var inverse);
            var parameters = new Params
            {
                InverseProjection = inverse,
                Info = new Vector4(Math.Clamp(scene.IrradianceSettings.Exposure, 0.0f, 8.0f), lightCount, 0.0f, 0.0f)
            };
            Api.QueueWriteBuffer(Queue, _params, 0, &parameters, (nuint)sizeof(Params));

            // Made each time: the scene's targets are remade with the window, and this runs only while someone looks.
            var entries = stackalloc BindGroupEntry[5];
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = _params, Size = (ulong)sizeof(Params) };
            entries[1] = new BindGroupEntry { Binding = 1, TextureView = sceneColor };
            entries[2] = new BindGroupEntry { Binding = 2, TextureView = depth };
            entries[3] = new BindGroupEntry { Binding = 3, TextureView = shadows };
            entries[4] = new BindGroupEntry { Binding = 4, Buffer = lightCells, Size = lightCellsSize };
            var groupDescriptor = new BindGroupDescriptor { Layout = _layout, EntryCount = 5, Entries = entries };
            var group = Api.DeviceCreateBindGroup(Device, &groupDescriptor);
            if (group == null)
            {
                return;
            }

            try
            {
                var attachment = new RenderPassColorAttachment
                {
                    View = View,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearValue = new Color(0, 0, 0, 1)
                };
                var passDescriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
                var pass = Api.CommandEncoderBeginRenderPass(encoder, &passDescriptor);
                try
                {
                    Api.RenderPassEncoderSetPipeline(pass, _pipeline);
                    Api.RenderPassEncoderSetBindGroup(pass, 0, group, 0, null);
                    Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
                    Api.RenderPassEncoderEnd(pass);
                }
                finally
                {
                    Api.RenderPassEncoderRelease(pass);
                }
            }
            finally
            {
                Api.BindGroupRelease(group);
            }
        }

        private void Create()
        {
            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = ManaRenderTargetPreview.Width, Height = ManaRenderTargetPreview.Height, DepthOrArrayLayers = 1 },
                // The UI's own atlas format, so the UI samples it exactly as it does its pictures.
                Format = TextureFormat.Rgba8UnormSrgb,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _texture = Api.DeviceCreateTexture(Device, &textureDescriptor);
            if (_texture == null)
            {
                throw new InvalidOperationException("Failed to create the render-target preview texture.");
            }
            View = Api.TextureCreateView(_texture, null);

            var bufferDescriptor = new BufferDescriptor { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = (ulong)sizeof(Params) };
            _params = Api.DeviceCreateBuffer(Device, &bufferDescriptor);

            var source = ManaWgslPreprocessor.LoadResolved(ManaWebGpuDevice.ResolveAssetPath(ManaWebGpuDevice.ShaderPath("target_preview.wgsl")));
            var code = Marshal.StringToCoTaskMemUTF8(source);
            var vertexEntry = Marshal.StringToCoTaskMemUTF8("vs_main");
            var fragmentEntry = Marshal.StringToCoTaskMemUTF8("fs_main");
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
                    throw new InvalidOperationException("Failed to create the render-target preview shader.");
                }

                var entries = stackalloc BindGroupLayoutEntry[5];
                entries[0] = new BindGroupLayoutEntry
                {
                    Binding = 0, Visibility = ShaderStage.Fragment, Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform }
                };
                entries[1] = new BindGroupLayoutEntry
                {
                    Binding = 1, Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.UnfilterableFloat, ViewDimension = TextureViewDimension.Dimension2D }
                };
                entries[2] = new BindGroupLayoutEntry
                {
                    Binding = 2, Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.Dimension2D }
                };
                entries[3] = new BindGroupLayoutEntry
                {
                    Binding = 3, Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout { SampleType = TextureSampleType.Depth, ViewDimension = TextureViewDimension.Dimension2DArray }
                };
                entries[4] = new BindGroupLayoutEntry
                {
                    Binding = 4, Visibility = ShaderStage.Fragment, Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage }
                };
                var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 5, Entries = entries };
                _layout = Api.DeviceCreateBindGroupLayout(Device, &layoutDescriptor);

                var layout = _layout;
                var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &layout };
                _pipelineLayout = Api.DeviceCreatePipelineLayout(Device, &pipelineLayoutDescriptor);

                var target = new ColorTargetState { Format = TextureFormat.Rgba8UnormSrgb, WriteMask = ColorWriteMask.All };
                var fragment = new FragmentState { Module = _shader, EntryPoint = (byte*)fragmentEntry, TargetCount = 1, Targets = &target };
                var pipelineDescriptor = new RenderPipelineDescriptor
                {
                    Layout = _pipelineLayout,
                    Vertex = new VertexState { Module = _shader, EntryPoint = (byte*)vertexEntry, BufferCount = 0 },
                    Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, CullMode = CullMode.None },
                    Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                    Fragment = &fragment
                };
                _pipeline = Api.DeviceCreateRenderPipeline(Device, &pipelineDescriptor);
                if (_pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create the render-target preview pipeline.");
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(code);
                Marshal.FreeCoTaskMem(vertexEntry);
                Marshal.FreeCoTaskMem(fragmentEntry);
            }
        }

        public override void Dispose()
        {
            if (_pipeline != null) { Api.RenderPipelineRelease(_pipeline); _pipeline = null; }
            if (_pipelineLayout != null) { Api.PipelineLayoutRelease(_pipelineLayout); _pipelineLayout = null; }
            if (_layout != null) { Api.BindGroupLayoutRelease(_layout); _layout = null; }
            if (_shader != null) { Api.ShaderModuleRelease(_shader); _shader = null; }
            if (_params != null) { Api.BufferRelease(_params); _params = null; }
            if (View != null) { Api.TextureViewRelease(View); View = null; }
            if (_texture != null) { Api.TextureRelease(_texture); _texture = null; }
        }
    }
}

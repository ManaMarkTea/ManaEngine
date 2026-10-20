using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using GpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Renderers;

/// <summary>Six procedural vertices per instance; one uniform and draw per emitter.</summary>
internal unsafe sealed class ManaWebGpuParticles : ManaWebGpuDeviceObject
{
    private BindGroupLayout* _layout;
    private PipelineLayout* _pipelineLayout;
    private RenderPipeline* _pipeline;
    private sealed class Slot
    {
        public nint Buffer, Group, History;
        public ReadOnlyMemory<Matrix4x4> SpawnTransforms;
        public uint SpawnRevision;
    }
    private readonly List<Slot> _slots = [];

    [StructLayout(LayoutKind.Sequential)]
    private struct Uniform
    {
        public Matrix4x4 ViewProjection, Model;
        public Vector4 Right, Up, Motion, GravityDrag, Sizes, StartColor, EndColor;
        public uint Count, Seed, HardEdge, SpawnHead;
        public Vector4 History, BouncePlane1, BouncePlane2, BounceSettings, Wind;
    }

    public ManaWebGpuParticles(ManaWebGpuDevice gpu, TextureFormat colorFormat, TextureFormat depthFormat, string shaderPath)
        : base(gpu)
    {
        try { CreatePipeline(colorFormat, depthFormat, shaderPath); }
        catch { Dispose(); throw; }
    }

    public long Draw(RenderPassEncoder* pass, IReadOnlyList<ManaGpuParticleEmitter> emitters,
        ManaCameraData camera, Matrix4x4 projection, ManaWind.State wind, double density = 1.0)
    {
        long drawn = 0;
        Matrix4x4.Invert(camera.View, out var inverseView);
        Api.RenderPassEncoderSetPipeline(pass, _pipeline);
        for (var i = 0; i < emitters.Count; i++)
        {
            var emitter = emitters[i];
            var count = (int)Math.Floor(Math.Max(0, emitter.Count) * Math.Clamp(density, 0.0, 1.0));
            if (count <= 0) continue;
            EnsureSlot(i);
            var uniform = new Uniform
            {
                ViewProjection = camera.View * projection, Model = emitter.Transform,
                Right = new(inverseView.M11, inverseView.M12, inverseView.M13, 0),
                Up = new(inverseView.M21, inverseView.M22, inverseView.M23, 0),
                Motion = new(emitter.Seconds, emitter.Lifetime, emitter.Speed, MathF.Cos(emitter.SpreadDegrees*MathF.PI/180)),
                GravityDrag = new(emitter.Gravity, emitter.Drag),
                Wind = new(wind.Direction.X * wind.Speed, 0, wind.Direction.Y * wind.Speed, emitter.WindInfluence),
                Sizes = new(emitter.StartSize, emitter.EndSize, emitter.Radius, emitter.Brightness),
                StartColor = emitter.StartColor, EndColor = emitter.EndColor,
                // Phase uses the rendered count, keeping the reduced stream spread across its lifetime.
                Count = (uint)count, Seed = unchecked((uint)emitter.Seed), HardEdge = emitter.HardEdge ? 1u : 0u,
                BouncePlane1 = emitter.BouncePlane1, BouncePlane2 = emitter.BouncePlane2, BounceSettings = emitter.BounceSettings,
                SpawnHead = emitter.SpawnHead, History = new(emitter.SpawnTickWithinLifetime, emitter.SpawnInterval, 0, 0)
            };
            var slot = _slots[i];
            if (!slot.SpawnTransforms.Equals(emitter.SpawnTransforms) || slot.SpawnRevision != emitter.SpawnRevision)
            {
                fixed (Matrix4x4* history = emitter.SpawnTransforms.Span)
                    Api.QueueWriteBuffer(Queue, (GpuBuffer*)slot.History, 0, history,
                        (nuint)(emitter.SpawnTransforms.Length * sizeof(Matrix4x4)));
                slot.SpawnTransforms = emitter.SpawnTransforms;
                slot.SpawnRevision = emitter.SpawnRevision;
            }
            Api.QueueWriteBuffer(Queue, (GpuBuffer*)slot.Buffer, 0, &uniform, (nuint)sizeof(Uniform));
            Api.RenderPassEncoderSetBindGroup(pass, 0, (BindGroup*)slot.Group, 0, null);
            Api.RenderPassEncoderDraw(pass, 6, (uint)count, 0, 0);
            drawn += count;
        }
        return drawn;
    }

    private void EnsureSlot(int index)
    {
        var entries = stackalloc BindGroupEntry[2];
        while (_slots.Count <= index)
        {
            var descriptor = new BufferDescriptor { Size = (ulong)sizeof(Uniform), Usage = BufferUsage.Uniform | BufferUsage.CopyDst };
            var buffer = Api.DeviceCreateBuffer(Device, &descriptor);
            if (buffer == null) throw new InvalidOperationException("Cannot create GPU particle uniform.");
            var historyDescriptor = new BufferDescriptor { Size = 256 * (ulong)sizeof(Matrix4x4), Usage = BufferUsage.Storage | BufferUsage.CopyDst };
            var history = Api.DeviceCreateBuffer(Device, &historyDescriptor);
            if (history == null) { Api.BufferRelease(buffer); throw new InvalidOperationException("Cannot create GPU particle spawn history."); }
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = buffer, Size = (ulong)sizeof(Uniform) };
            entries[1] = new BindGroupEntry { Binding = 1, Buffer = history, Size = historyDescriptor.Size };
            var groupDescriptor = new BindGroupDescriptor { Layout = _layout, EntryCount = 2, Entries = entries };
            var group = Api.DeviceCreateBindGroup(Device, &groupDescriptor);
            if (group == null) { Api.BufferRelease(history); Api.BufferRelease(buffer); throw new InvalidOperationException("Cannot create GPU particle bind group."); }
            _slots.Add(new Slot { Buffer = (nint)buffer, Group = (nint)group, History = (nint)history });
        }
    }

    private void CreatePipeline(TextureFormat colorFormat, TextureFormat depthFormat, string shaderPath)
    {
        var entry = new BindGroupLayoutEntry
        {
            Binding = 0, Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = (ulong)sizeof(Uniform) }
        };
        var entries = stackalloc BindGroupLayoutEntry[2];
        entries[0] = entry;
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1, Visibility = ShaderStage.Vertex,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, MinBindingSize = 256 * (ulong)sizeof(Matrix4x4) }
        };
        var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 2, Entries = entries };
        _layout = Api.DeviceCreateBindGroupLayout(Device, &layoutDescriptor);
        if (_layout == null) throw new InvalidOperationException("Cannot create GPU particle layout.");
        var layout = _layout;
        var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &layout };
        _pipelineLayout = Api.DeviceCreatePipelineLayout(Device, &pipelineLayoutDescriptor);
        if (_pipelineLayout == null) throw new InvalidOperationException("Cannot create GPU particle pipeline layout.");
        var source = Marshal.StringToCoTaskMemUTF8(File.ReadAllText(shaderPath));
        var vertexEntry = Marshal.StringToCoTaskMemUTF8("vs_main");
        var fragmentEntry = Marshal.StringToCoTaskMemUTF8("fs_main");
        ShaderModule* shader = null;
        try
        {
            var wgsl = new ShaderModuleWGSLDescriptor { Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor }, Code = (byte*)source };
            var shaderDescriptor = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
            shader = Api.DeviceCreateShaderModule(Device, &shaderDescriptor);
            if (shader == null) throw new InvalidOperationException("Cannot create GPU particle shader.");
            var blend = new BlendState
            {
                Color = new BlendComponent { SrcFactor = BlendFactor.SrcAlpha, DstFactor = BlendFactor.One, Operation = BlendOperation.Add },
                Alpha = new BlendComponent { SrcFactor = BlendFactor.Zero, DstFactor = BlendFactor.One, Operation = BlendOperation.Add }
            };
            var target = new ColorTargetState { Format = colorFormat, Blend = &blend, WriteMask = ColorWriteMask.All };
            var fragment = new FragmentState { Module = shader, EntryPoint = (byte*)fragmentEntry, TargetCount = 1, Targets = &target };
            var stencil = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep };
            var depth = new DepthStencilState { Format = depthFormat, DepthWriteEnabled = false, DepthCompare = CompareFunction.LessEqual, StencilFront = stencil, StencilBack = stencil };
            var descriptor = new RenderPipelineDescriptor
            {
                Layout = _pipelineLayout,
                Vertex = new VertexState { Module = shader, EntryPoint = (byte*)vertexEntry },
                Fragment = &fragment, DepthStencil = &depth,
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue }
            };
            _pipeline = Api.DeviceCreateRenderPipeline(Device, &descriptor);
            if (_pipeline == null) throw new InvalidOperationException("Cannot create GPU particle pipeline.");
        }
        finally
        {
            if (shader != null) Api.ShaderModuleRelease(shader);
            Marshal.FreeCoTaskMem(source); Marshal.FreeCoTaskMem(vertexEntry); Marshal.FreeCoTaskMem(fragmentEntry);
        }
    }

    public override void Dispose()
    {
        foreach (var slot in _slots) { Api.BindGroupRelease((BindGroup*)slot.Group); Api.BufferRelease((GpuBuffer*)slot.Buffer); Api.BufferRelease((GpuBuffer*)slot.History); }
        _slots.Clear();
        if (_pipeline != null) { Api.RenderPipelineRelease(_pipeline); _pipeline = null; }
        if (_pipelineLayout != null) { Api.PipelineLayoutRelease(_pipelineLayout); _pipelineLayout = null; }
        if (_layout != null) { Api.BindGroupLayoutRelease(_layout); _layout = null; }
    }
}

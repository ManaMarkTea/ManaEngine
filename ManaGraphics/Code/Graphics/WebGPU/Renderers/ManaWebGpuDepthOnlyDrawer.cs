using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using ManaGraphics.Code.Graphics.Pipelines;
using ManaGraphics.Code.Graphics.Resources;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Renderers
{
    /// <summary>
    /// Draws scene meshes depth-only into one or more views (each its own view-projection and depth target): the shadow
    /// cascades, the ambient-occlusion prepass. Meshes alike (same geometry, no cutout) go out as one instanced draw a
    /// view (shadow_depth_instanced.wgsl); cutouts, which need their textures to discard, one by one (shadow_depth.wgsl).
    /// </summary>
    internal sealed unsafe class ManaWebGpuDepthOnlyDrawer : ManaWebGpuDeviceObject
    {
        // One view matrix buffer per view. A single shared one, rewritten between views, left every view drawn with
        // the last one's matrix: queue writes all land before the command buffer runs.
        private readonly WebGpuBuffer*[] _uniformBuffers;
        private readonly BindGroup*[] _bindGroups;
        private readonly TextureFormat _format;
        private readonly bool _bias;
        private BindGroupLayout* _bindGroupLayout;
        private PipelineLayout* _pipelineLayout;
        private ShaderModule* _shaderModule;
        private RenderPipeline* _pipeline;
        private ShaderModule* _instancedModule;
        private BindGroupLayout* _instancedLayout;
        private PipelineLayout* _instancedPipelineLayout;
        private RenderPipeline* _instancedPipeline;
        private BindGroup* _instancedGroup;
        private nint _instancedGroupBuffer;
        private readonly ManaWebGpuInstanceBuffer _models;
        private readonly List<(ulong Geometry, ManaWebGpuSceneMesh Mesh, (Vector3 Min, Vector3 Max) Bounds)> _meshes = new();
        private readonly List<(int View, ManaWebGpuSceneMesh First, int Count, uint Offset)> _runs = new();
        private readonly List<(int View, ManaWebGpuSceneMesh Mesh)> _alone = new();
        private readonly Matrix4x4[] _batch = new Matrix4x4[ManaWebGpuSceneBindings.InstancesPerDraw];

        /// <param name="bias">Slope-scaled depth bias, for shadow maps (acne); off for a camera's own depth.</param>
        public ManaWebGpuDepthOnlyDrawer(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, int viewCount, TextureFormat format, bool bias, string name)
            : base(gpu)
        {
            _uniformBuffers = new WebGpuBuffer*[viewCount];
            _bindGroups = new BindGroup*[viewCount];
            _format = format;
            _bias = bias;
            _models = new ManaWebGpuInstanceBuffer(gpu, sizeof(Matrix4x4) * ManaWebGpuSceneBindings.InstancesPerDraw, name);
            try
            {
                Create(bindings);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public int PipelineCount => 2;

        private void Create(ManaWebGpuSceneBindings bindings)
        {
            var uniformBufferDescriptor = new BufferDescriptor { Usage = BufferUsage.Uniform | BufferUsage.CopyDst, Size = (ulong)sizeof(Matrix4x4) };
            for (var view = 0; view < _uniformBuffers.Length; view++)
            {
                _uniformBuffers[view] = Api.DeviceCreateBuffer(Device, &uniformBufferDescriptor);
                if (_uniformBuffers[view] == null)
                {
                    throw new InvalidOperationException("Failed to create a WebGPU depth view uniform buffer.");
                }
            }

            var layoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, HasDynamicOffset = false, MinBindingSize = (ulong)sizeof(Matrix4x4) }
            };
            var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 1, Entries = &layoutEntry };
            _bindGroupLayout = Api.DeviceCreateBindGroupLayout(Device, &layoutDescriptor);
            if (_bindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create a WebGPU depth view bind group layout.");
            }

            for (var view = 0; view < _uniformBuffers.Length; view++)
            {
                var entry = new BindGroupEntry { Binding = 0, Buffer = _uniformBuffers[view], Offset = 0, Size = (ulong)sizeof(Matrix4x4) };
                var descriptor = new BindGroupDescriptor { Layout = _bindGroupLayout, EntryCount = 1, Entries = &entry };
                _bindGroups[view] = Api.DeviceCreateBindGroup(Device, &descriptor);
                if (_bindGroups[view] == null)
                {
                    throw new InvalidOperationException("Failed to create a WebGPU depth view bind group.");
                }
            }

            // Group 1 is the ordinary object layout, so every scene mesh's own object bind group works here unchanged --
            // shadow_depth.wgsl reads only object.model (and the albedo a cutout tests) out of it.
            var groups = stackalloc BindGroupLayout*[2];
            groups[0] = _bindGroupLayout;
            groups[1] = bindings.ObjectLayout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 2, BindGroupLayouts = groups };
            _pipelineLayout = Api.DeviceCreatePipelineLayout(Device, &pipelineLayoutDescriptor);
            if (_pipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create a WebGPU depth pipeline layout.");
            }
            _shaderModule = Gpu.CreateShaderModule("shadow_depth.wgsl");
            _pipeline = CreatePipeline(_shaderModule, _pipelineLayout, withFragment: true);

            var modelsEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.ReadOnlyStorage,
                    HasDynamicOffset = true,
                    MinBindingSize = (ulong)(sizeof(Matrix4x4) * ManaWebGpuSceneBindings.InstancesPerDraw)
                }
            };
            var modelsLayoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 1, Entries = &modelsEntry };
            _instancedLayout = Api.DeviceCreateBindGroupLayout(Device, &modelsLayoutDescriptor);
            groups[1] = _instancedLayout;
            _instancedPipelineLayout = Api.DeviceCreatePipelineLayout(Device, &pipelineLayoutDescriptor);
            if (_instancedLayout == null || _instancedPipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU instanced depth layouts.");
            }
            _instancedModule = Gpu.CreateShaderModule("shadow_depth_instanced.wgsl");
            _instancedPipeline = CreatePipeline(_instancedModule, _instancedPipelineLayout, withFragment: false);
        }

        private RenderPipeline* CreatePipeline(ShaderModule* module, PipelineLayout* layout, bool withFragment)
        {
            nint vertexEntryPoint = Marshal.StringToCoTaskMemUTF8("vs_main");
            nint fragmentEntryPoint = Marshal.StringToCoTaskMemUTF8("fs_main");
            try
            {
                // Position, then the colour and texture coordinate a cutout's alpha test needs (the mesh vertex is
                // position 3, normal 3, colour 4, uv 2 floats).
                var vertexAttributes = stackalloc VertexAttribute[3];
                vertexAttributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
                vertexAttributes[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = (ulong)(sizeof(float) * (3 + 3)), ShaderLocation = 1 };
                vertexAttributes[2] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = (ulong)(sizeof(float) * (3 + 3 + 4)), ShaderLocation = 2 };
                var vertexBufferLayout = new VertexBufferLayout
                {
                    ArrayStride = ManaWebGpuMesh.VertexStride,
                    StepMode = VertexStepMode.Vertex,
                    AttributeCount = 3,
                    Attributes = vertexAttributes
                };

                // No colour targets: the fragment stage is there only to discard a cutout's see-through texels.
                var fragmentState = new FragmentState { Module = module, EntryPoint = (byte*)fragmentEntryPoint, TargetCount = 0, Targets = null };
                var vertexState = new VertexState { Module = module, EntryPoint = (byte*)vertexEntryPoint, BufferCount = 1, Buffers = &vertexBufferLayout };
                var keep = new StencilFaceState
                {
                    Compare = CompareFunction.Always,
                    FailOp = StencilOperation.Keep,
                    DepthFailOp = StencilOperation.Keep,
                    PassOp = StencilOperation.Keep
                };
                var depthStencilState = new DepthStencilState
                {
                    Format = _format,
                    DepthWriteEnabled = true,
                    DepthCompare = CompareFunction.Less,
                    StencilFront = keep,
                    StencilBack = keep,
                    StencilReadMask = uint.MaxValue,
                    StencilWriteMask = uint.MaxValue
                };
                if (_bias)
                {
                    // A little slope-scaled bias fights shadow acne without much peter-panning at a voxel scene's scale.
                    depthStencilState.DepthBias = 2;
                    depthStencilState.DepthBiasSlopeScale = 2.0f;
                }

                var pipelineDescriptor = new RenderPipelineDescriptor
                {
                    Layout = layout,
                    Vertex = vertexState,
                    Primitive = new PrimitiveState
                    {
                        Topology = PrimitiveTopology.TriangleList,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = CullMode.None
                    },
                    Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                    DepthStencil = &depthStencilState,
                    Fragment = null
                };
                if (withFragment)
                {
                    pipelineDescriptor.Fragment = &fragmentState;
                }

                var pipeline = Api.DeviceCreateRenderPipeline(Device, &pipelineDescriptor);
                if (pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create a WebGPU depth pipeline.");
                }
                return pipeline;
            }
            finally
            {
                Marshal.FreeCoTaskMem(vertexEntryPoint);
                Marshal.FreeCoTaskMem(fragmentEntryPoint);
            }
        }

        /// <summary>
        /// One depth-only pass per view, each into its own target and cleared first. A mesh is drawn in a view only
        /// when <paramref name="include"/> takes it and its bounds can land in that view, and <paramref name="skip"/>
        /// (given the view and the bounds) does not leave it out there.
        /// </summary>
        public void Draw(ManaWebGpuFrame frame, ReadOnlySpan<Matrix4x4> viewProjections, Func<int, nint> target, Func<ManaRenderMeshInstance, bool> include,
            Func<int, (Vector3 Min, Vector3 Max), bool>? skip = null)
        {
            _meshes.Clear();
            foreach (var sceneMesh in frame.Meshes)
            {
                if (!include(sceneMesh.Instance))
                {
                    continue;
                }
                // 0 for a mesh drawn alone: a cutout needs its textures, which the instanced shader does not read.
                var geometry = 0UL;
                if ((int)sceneMesh.Instance.OpacityMode < 2)
                {
                    geometry = sceneMesh.Instance.MeshData.GeometryHash;
                    if (geometry == 0)
                    {
                        geometry = 1;
                    }
                }
                _meshes.Add((geometry, sceneMesh, sceneMesh.Instance.MeshData.WorldBounds(sceneMesh.Instance.WorldTransform)));
            }
            _meshes.Sort(static (a, b) => a.Geometry.CompareTo(b.Geometry));

            // Gathered for every view and uploaded once before any draw: the model array may be remade larger.
            _models.Begin();
            _runs.Clear();
            _alone.Clear();
            for (var view = 0; view < viewProjections.Length; view++)
            {
                var viewProjection = viewProjections[view];
                var index = 0;
                while (index < _meshes.Count)
                {
                    var geometry = _meshes[index].Geometry;
                    if (geometry == 0)
                    {
                        // Outside this view it draws nothing, but costs a draw call all the same.
                        if (InsideBox(_meshes[index].Bounds, viewProjection) && skip?.Invoke(view, _meshes[index].Bounds) != true)
                        {
                            _alone.Add((view, _meshes[index].Mesh));
                        }
                        index++;
                        continue;
                    }

                    ManaWebGpuSceneMesh? first = null;
                    var count = 0;
                    while (index < _meshes.Count && _meshes[index].Geometry == geometry)
                    {
                        var (_, mesh, bounds) = _meshes[index];
                        index++;
                        if (!InsideBox(bounds, viewProjection) || skip?.Invoke(view, bounds) == true)
                        {
                            continue;
                        }
                        first ??= mesh;
                        _batch[count++] = mesh.Instance.WorldTransform;
                        if (count == _batch.Length)
                        {
                            AddRun(view, first, count);
                            first = null;
                            count = 0;
                        }
                    }
                    if (first != null)
                    {
                        AddRun(view, first, count);
                    }
                }
            }
            _models.Upload();
            if (_instancedGroup == null || _instancedGroupBuffer != (nint)_models.Buffer)
            {
                if (_instancedGroup != null)
                {
                    Api.BindGroupRelease(_instancedGroup);
                }
                var entry = new BindGroupEntry
                {
                    Binding = 0,
                    Buffer = _models.Buffer,
                    Offset = 0,
                    Size = (ulong)(sizeof(Matrix4x4) * ManaWebGpuSceneBindings.InstancesPerDraw)
                };
                var descriptor = new BindGroupDescriptor { Layout = _instancedLayout, EntryCount = 1, Entries = &entry };
                _instancedGroup = Api.DeviceCreateBindGroup(Device, &descriptor);
                _instancedGroupBuffer = (nint)_models.Buffer;
            }

            for (var view = 0; view < viewProjections.Length; view++)
            {
                var viewProjection = viewProjections[view];
                Api.QueueWriteBuffer(Queue, _uniformBuffers[view], 0, &viewProjection, (nuint)sizeof(Matrix4x4));
                var depthAttachment = new RenderPassDepthStencilAttachment
                {
                    View = (TextureView*)target(view),
                    DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store,
                    DepthClearValue = 1.0f,
                    DepthReadOnly = false,
                    StencilLoadOp = LoadOp.Undefined,
                    StencilStoreOp = StoreOp.Undefined,
                    StencilClearValue = 0,
                    StencilReadOnly = true
                };
                var passDescriptor = new RenderPassDescriptor { ColorAttachmentCount = 0, ColorAttachments = null, DepthStencilAttachment = &depthAttachment };
                var pass = Api.CommandEncoderBeginRenderPass(frame.Encoder, &passDescriptor);
                try
                {
                    Api.RenderPassEncoderSetPipeline(pass, _pipeline);
                    Api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroups[view], 0, null);
                    foreach (var (meshView, sceneMesh) in _alone)
                    {
                        if (meshView != view)
                        {
                            continue;
                        }
                        // This runs before the scene pass; the object uniform is made once a frame for both.
                        sceneMesh.PrepareObjectUniform(frame.Number);
                        if (sceneMesh.ObjectBindGroup != null)
                        {
                            Api.RenderPassEncoderSetBindGroup(pass, 1, sceneMesh.ObjectBindGroup, 0, null);
                        }
                        sceneMesh.Mesh.Draw(pass);
                    }

                    var instancedBound = false;
                    foreach (var (runView, first, count, offset) in _runs)
                    {
                        if (runView != view)
                        {
                            continue;
                        }
                        if (!instancedBound)
                        {
                            Api.RenderPassEncoderSetPipeline(pass, _instancedPipeline);
                            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroups[view], 0, null);
                            instancedBound = true;
                        }
                        var dynamicOffset = offset;
                        Api.RenderPassEncoderSetBindGroup(pass, 1, _instancedGroup, 1, &dynamicOffset);
                        first.Mesh.DrawInstanced(pass, (uint)count);
                    }
                    Api.RenderPassEncoderEnd(pass);
                }
                finally
                {
                    Api.RenderPassEncoderRelease(pass);
                }
            }
        }

        // One mesh alone needs no array; several share one instanced draw.
        private void AddRun(int view, ManaWebGpuSceneMesh first, int count)
        {
            if (count == 1)
            {
                _alone.Add((view, first));
                return;
            }
            _runs.Add((view, first, count, _models.Append<Matrix4x4>(_batch.AsSpan(0, count))));
        }

        // Whether any of a box can land in a view: false only when all eight corners are past one face of its frustum.
        private static bool InsideBox((Vector3 Min, Vector3 Max) bounds, Matrix4x4 viewProjection)
        {
            Span<int> outside = stackalloc int[6];
            for (var corner = 0; corner < 8; corner++)
            {
                var point = bounds.Min;
                if ((corner & 1) != 0)
                {
                    point.X = bounds.Max.X;
                }
                if ((corner & 2) != 0)
                {
                    point.Y = bounds.Max.Y;
                }
                if ((corner & 4) != 0)
                {
                    point.Z = bounds.Max.Z;
                }
                var clip = Vector4.Transform(new Vector4(point, 1.0f), viewProjection);
                if (clip.X < -clip.W) { outside[0]++; }
                if (clip.X > clip.W) { outside[1]++; }
                if (clip.Y < -clip.W) { outside[2]++; }
                if (clip.Y > clip.W) { outside[3]++; }
                if (clip.Z < 0.0f) { outside[4]++; }
                if (clip.Z > clip.W) { outside[5]++; }
            }
            foreach (var count in outside)
            {
                if (count == 8)
                {
                    return false;
                }
            }
            return true;
        }

        public override void Dispose()
        {
            _models.Dispose();
            if (_instancedGroup != null) { Api.BindGroupRelease(_instancedGroup); _instancedGroup = null; }
            if (_instancedPipeline != null) { Api.RenderPipelineRelease(_instancedPipeline); _instancedPipeline = null; }
            if (_instancedPipelineLayout != null) { Api.PipelineLayoutRelease(_instancedPipelineLayout); _instancedPipelineLayout = null; }
            if (_instancedLayout != null) { Api.BindGroupLayoutRelease(_instancedLayout); _instancedLayout = null; }
            if (_instancedModule != null) { Api.ShaderModuleRelease(_instancedModule); _instancedModule = null; }
            if (_pipeline != null) { Api.RenderPipelineRelease(_pipeline); _pipeline = null; }
            if (_shaderModule != null) { Api.ShaderModuleRelease(_shaderModule); _shaderModule = null; }
            if (_pipelineLayout != null) { Api.PipelineLayoutRelease(_pipelineLayout); _pipelineLayout = null; }
            for (var view = 0; view < _uniformBuffers.Length; view++)
            {
                if (_bindGroups[view] != null) { Api.BindGroupRelease(_bindGroups[view]); _bindGroups[view] = null; }
                if (_uniformBuffers[view] != null) { Api.BufferRelease(_uniformBuffers[view]); _uniformBuffers[view] = null; }
            }
            if (_bindGroupLayout != null) { Api.BindGroupLayoutRelease(_bindGroupLayout); _bindGroupLayout = null; }
        }
    }
}

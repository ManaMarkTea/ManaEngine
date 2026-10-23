using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>Draws the opaque meshes depth-only into each shadow cascade, before the scene pass samples them.</summary>
    internal sealed unsafe class ManaWebGpuShadowPass : ManaWebGpuRenderPass
    {
        private const int CascadeCount = ManaWebGpuShadowMap.CascadeCount;

        private readonly ManaWebGpuShadowMap _map;

        public override string ProfileName => "GPU: Shadow Cascades";

        // One light matrix buffer per cascade. A single shared one, rewritten between cascades, left every
        // cascade drawn with the last one's matrix: queue writes all land before the command buffer runs.
        private readonly WebGpuBuffer*[] _uniformBuffers = new WebGpuBuffer*[CascadeCount];
        private readonly BindGroup*[] _bindGroups = new BindGroup*[CascadeCount];
        private BindGroupLayout* _bindGroupLayout;
        private PipelineLayout* _pipelineLayout;
        private ShaderModule* _shaderModule;
        private RenderPipeline* _pipeline;

        // Opaque casters alike are drawn instanced: their model matrices from one array (shadow_depth_instanced.wgsl).
        private ShaderModule* _instancedModule;
        private BindGroupLayout* _instancedLayout;
        private PipelineLayout* _instancedPipelineLayout;
        private RenderPipeline* _instancedPipeline;
        private BindGroup* _instancedGroup;
        private nint _instancedGroupBuffer;
        private readonly ManaWebGpuInstanceBuffer _models;
        private readonly List<(ulong Geometry, ManaWebGpuSceneMesh Mesh, (Vector3 Min, Vector3 Max) Bounds)> _casters = new();
        private readonly List<(int Cascade, ManaWebGpuSceneMesh First, int Count, uint Offset)> _runs = new();
        private readonly List<(int Cascade, ManaWebGpuSceneMesh Mesh)> _alone = new();
        private readonly Matrix4x4[] _batch = new Matrix4x4[ManaWebGpuSceneBindings.InstancesPerDraw];

        public ManaWebGpuShadowPass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, ManaWebGpuShadowMap map)
            : base(gpu)
        {
            _map = map;
            _models = new ManaWebGpuInstanceBuffer(gpu, sizeof(Matrix4x4) * ManaWebGpuSceneBindings.InstancesPerDraw, "shadow");
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

        private void Create(ManaWebGpuSceneBindings bindings)
        {
            var uniformBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = (ulong)sizeof(Matrix4x4)
            };
            for (var cascade = 0; cascade < CascadeCount; cascade++)
            {
                _uniformBuffers[cascade] = Api.DeviceCreateBuffer(Device, &uniformBufferDescriptor);
                if (_uniformBuffers[cascade] == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU shadow uniform buffer.");
                }
            }

            var layoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    HasDynamicOffset = false,
                    MinBindingSize = (ulong)sizeof(Matrix4x4)
                }
            };
            var layoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 1,
                Entries = &layoutEntry
            };
            _bindGroupLayout = Api.DeviceCreateBindGroupLayout(Device, &layoutDescriptor);
            if (_bindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU shadow bind group layout.");
            }

            for (var cascade = 0; cascade < CascadeCount; cascade++)
            {
                var entry = new BindGroupEntry
                {
                    Binding = 0,
                    Buffer = _uniformBuffers[cascade],
                    Offset = 0,
                    Size = (ulong)sizeof(Matrix4x4)
                };
                var descriptor = new BindGroupDescriptor
                {
                    Layout = _bindGroupLayout,
                    EntryCount = 1,
                    Entries = &entry
                };
                _bindGroups[cascade] = Api.DeviceCreateBindGroup(Device, &descriptor);
                if (_bindGroups[cascade] == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU shadow bind group.");
                }
            }

            // Group 1 is the ordinary object layout, so every scene mesh's own object bind group works here
            // unchanged -- shadow_depth.wgsl reads only object.model (and the albedo a cutout tests) out of it.
            var groups = stackalloc BindGroupLayout*[2];
            groups[0] = _bindGroupLayout;
            groups[1] = bindings.ObjectLayout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 2,
                BindGroupLayouts = groups
            };
            _pipelineLayout = Api.DeviceCreatePipelineLayout(Device, &pipelineLayoutDescriptor);
            if (_pipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU shadow pipeline layout.");
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
                throw new InvalidOperationException("Failed to create WebGPU instanced shadow layouts.");
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
                var fragmentState = new FragmentState
                {
                    Module = module,
                    EntryPoint = (byte*)fragmentEntryPoint,
                    TargetCount = 0,
                    Targets = null
                };

                var vertexState = new VertexState
                {
                    Module = module,
                    EntryPoint = (byte*)vertexEntryPoint,
                    BufferCount = 1,
                    Buffers = &vertexBufferLayout
                };

                var keep = new StencilFaceState
                {
                    Compare = CompareFunction.Always,
                    FailOp = StencilOperation.Keep,
                    DepthFailOp = StencilOperation.Keep,
                    PassOp = StencilOperation.Keep
                };
                var depthStencilState = new DepthStencilState
                {
                    Format = TextureFormat.Depth32float,
                    DepthWriteEnabled = true,
                    DepthCompare = CompareFunction.Less,
                    StencilFront = keep,
                    StencilBack = keep,
                    StencilReadMask = uint.MaxValue,
                    StencilWriteMask = uint.MaxValue,
                    // A little slope-scaled bias to fight shadow acne without introducing much peter-panning
                    // at the scale a voxel editor scene runs at.
                    DepthBias = 2,
                    DepthBiasSlopeScale = 2.0f,
                    DepthBiasClamp = 0.0f
                };

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
                    Multisample = new MultisampleState
                    {
                        Count = 1,
                        Mask = uint.MaxValue
                    },
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
                    throw new InvalidOperationException("Failed to create WebGPU shadow depth pipeline.");
                }

                return pipeline;
            }
            finally
            {
                Marshal.FreeCoTaskMem(vertexEntryPoint);
                Marshal.FreeCoTaskMem(fragmentEntryPoint);
            }
        }

        // One depth-only pass per cascade, into that cascade's own layer of the array. Only opaque meshes cast. Casters
        // alike (the same geometry, no cutout) go out as one instanced draw per cascade; the rest one by one.
        public override void Prepare(ManaWebGpuFrame frame)
        {
            if (!ManaWebGpuShadowMap.IsCasting(frame.Scene))
            {
                return;
            }

            _casters.Clear();
            foreach (var sceneMesh in frame.Meshes)
            {
                // The editor room and helpers cast nothing: the room's floor would only shadow itself.
                if (!ManaWebGpuShadowMap.IsCaster(sceneMesh.Instance))
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
                _casters.Add((geometry, sceneMesh, sceneMesh.Instance.MeshData.WorldBounds(sceneMesh.Instance.WorldTransform)));
            }
            _casters.Sort(static (a, b) => a.Geometry.CompareTo(b.Geometry));

            // Gathered for every cascade and uploaded once before any draw: the model array may be remade larger.
            _models.Begin();
            _runs.Clear();
            _alone.Clear();
            for (var cascade = 0; cascade < CascadeCount; cascade++)
            {
                var lightViewProjection = _map.CascadeViewProjections[cascade];
                var index = 0;
                while (index < _casters.Count)
                {
                    var geometry = _casters[index].Geometry;
                    if (geometry == 0)
                    {
                        // Outside this cascade's box it draws nothing, but costs a draw call all the same.
                        if (InsideBox(_casters[index].Bounds, lightViewProjection))
                        {
                            _alone.Add((cascade, _casters[index].Mesh));
                        }
                        index++;
                        continue;
                    }

                    ManaWebGpuSceneMesh? first = null;
                    var count = 0;
                    while (index < _casters.Count && _casters[index].Geometry == geometry)
                    {
                        var (_, mesh, bounds) = _casters[index];
                        index++;
                        if (!InsideBox(bounds, lightViewProjection))
                        {
                            continue;
                        }
                        first ??= mesh;
                        _batch[count++] = mesh.Instance.WorldTransform;
                        if (count == _batch.Length)
                        {
                            AddRun(cascade, first, count);
                            first = null;
                            count = 0;
                        }
                    }
                    if (first != null)
                    {
                        AddRun(cascade, first, count);
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

            for (var cascade = 0; cascade < CascadeCount; cascade++)
            {
                var lightViewProjection = _map.CascadeViewProjections[cascade];
                Api.QueueWriteBuffer(Queue, _uniformBuffers[cascade], 0, &lightViewProjection, (nuint)sizeof(Matrix4x4));

                var depthAttachment = new RenderPassDepthStencilAttachment
                {
                    View = _map.CascadeView(cascade),
                    DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store,
                    DepthClearValue = 1.0f,
                    DepthReadOnly = false,
                    StencilLoadOp = LoadOp.Undefined,
                    StencilStoreOp = StoreOp.Undefined,
                    StencilClearValue = 0,
                    StencilReadOnly = true
                };

                var passDescriptor = new RenderPassDescriptor
                {
                    ColorAttachmentCount = 0,
                    ColorAttachments = null,
                    DepthStencilAttachment = &depthAttachment
                };

                var pass = Api.CommandEncoderBeginRenderPass(frame.Encoder, &passDescriptor);
                try
                {
                    Api.RenderPassEncoderSetPipeline(pass, _pipeline);
                    Api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroups[cascade], 0, null);
                    foreach (var (meshCascade, sceneMesh) in _alone)
                    {
                        if (meshCascade != cascade)
                        {
                            continue;
                        }

                        // This pass runs before the scene pass; the object uniform is made once a frame for both.
                        sceneMesh.PrepareObjectUniform(frame.Number);
                        if (sceneMesh.ObjectBindGroup != null)
                        {
                            Api.RenderPassEncoderSetBindGroup(pass, 1, sceneMesh.ObjectBindGroup, 0, null);
                        }
                        sceneMesh.Mesh.Draw(pass);
                    }

                    var instancedBound = false;
                    foreach (var (runCascade, first, count, offset) in _runs)
                    {
                        if (runCascade != cascade)
                        {
                            continue;
                        }
                        if (!instancedBound)
                        {
                            Api.RenderPassEncoderSetPipeline(pass, _instancedPipeline);
                            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroups[cascade], 0, null);
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
        private void AddRun(int cascade, ManaWebGpuSceneMesh first, int count)
        {
            if (count == 1)
            {
                _alone.Add((cascade, first));
                return;
            }
            _runs.Add((cascade, first, count, _models.Append<Matrix4x4>(_batch.AsSpan(0, count))));
        }

        // Whether any of a box can land in a cascade's light box: false only when all eight corners are past one face.
        private static bool InsideBox((Vector3 Min, Vector3 Max) bounds, Matrix4x4 viewProjection)
        {
            Span<int> outside = stackalloc int[6];
            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                    (corner & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                    (corner & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
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
            if (_pipeline != null)
            {
                Api.RenderPipelineRelease(_pipeline);
                _pipeline = null;
            }

            if (_shaderModule != null)
            {
                Api.ShaderModuleRelease(_shaderModule);
                _shaderModule = null;
            }

            if (_pipelineLayout != null)
            {
                Api.PipelineLayoutRelease(_pipelineLayout);
                _pipelineLayout = null;
            }

            for (var cascade = 0; cascade < CascadeCount; cascade++)
            {
                if (_bindGroups[cascade] != null)
                {
                    Api.BindGroupRelease(_bindGroups[cascade]);
                    _bindGroups[cascade] = null;
                }

                if (_uniformBuffers[cascade] != null)
                {
                    Api.BufferRelease(_uniformBuffers[cascade]);
                    _uniformBuffers[cascade] = null;
                }
            }

            if (_bindGroupLayout != null)
            {
                Api.BindGroupLayoutRelease(_bindGroupLayout);
                _bindGroupLayout = null;
            }
        }
    }
}

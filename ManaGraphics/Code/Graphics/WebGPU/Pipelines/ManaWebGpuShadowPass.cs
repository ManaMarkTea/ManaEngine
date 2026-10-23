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

        public ManaWebGpuShadowPass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, ManaWebGpuShadowMap map)
            : base(gpu)
        {
            _map = map;
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
            _pipeline = CreatePipeline();
        }

        private RenderPipeline* CreatePipeline()
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
                    Module = _shaderModule,
                    EntryPoint = (byte*)fragmentEntryPoint,
                    TargetCount = 0,
                    Targets = null
                };

                var vertexState = new VertexState
                {
                    Module = _shaderModule,
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
                    Layout = _pipelineLayout,
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
                    Fragment = &fragmentState
                };

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

        // One depth-only pass per cascade, into that cascade's own layer of the array. Only opaque meshes cast.
        public override void Prepare(ManaWebGpuFrame frame)
        {
            if (!ManaWebGpuShadowMap.IsCasting(frame.Scene))
            {
                return;
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

                    foreach (var sceneMesh in frame.Meshes)
                    {
                        // The editor room and helpers cast nothing: the room's floor would only shadow itself.
                        if (!ManaWebGpuShadowMap.IsCaster(sceneMesh.Instance))
                        {
                            continue;
                        }

                        // Outside this cascade's box it draws nothing, but costs a draw call all the same.
                        if (!InsideBox(sceneMesh.Instance.MeshData.WorldBounds(sceneMesh.Instance.WorldTransform), lightViewProjection))
                        {
                            continue;
                        }

                        // This pass runs before the scene pass uploads the frame's object uniforms; a dirty-check
                        // against the same value, so the scene pass then finds nothing left to write.
                        sceneMesh.PrepareObjectUniform(frame.Number);

                        if (sceneMesh.ObjectBindGroup != null)
                        {
                            Api.RenderPassEncoderSetBindGroup(pass, 1, sceneMesh.ObjectBindGroup, 0, null);
                        }

                        sceneMesh.Mesh.Draw(pass);
                    }

                    Api.RenderPassEncoderEnd(pass);
                }
                finally
                {
                    Api.RenderPassEncoderRelease(pass);
                }
            }
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

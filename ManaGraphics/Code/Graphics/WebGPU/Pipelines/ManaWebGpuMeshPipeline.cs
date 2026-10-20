using System.Numerics;
using System.Runtime.InteropServices;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>Builds a render pipeline over the scene mesh vertex layout; every mesh-drawing pass uses it.</summary>
    internal static unsafe class ManaWebGpuMeshPipeline
    {
        /// <summary>How far the grid is pushed behind whatever shares its depth.</summary>
        private const int GridDepthBias = 64;

        private const float GridDepthBiasSlope = 2.0f;

        public static RenderPipeline* Create(
            ManaWebGpuDevice gpu,
            ShaderModule* shaderModule,
            TextureFormat colorFormat,
            PipelineLayout* pipelineLayout,
            string? shaderName = null,
            bool isTransparent = false,
            bool disableDepthWrite = false,
            CompareFunction depthCompare = CompareFunction.Less,
            PrimitiveTopology primitiveTopology = PrimitiveTopology.TriangleList,
            bool cullBackFaces = false,
            bool additiveBlend = false)
        {
            nint vertexEntryPoint = Marshal.StringToCoTaskMemUTF8("vs_main");
            nint fragmentEntryPoint = Marshal.StringToCoTaskMemUTF8("fs_main");

            try
            {
                ManaConsole.WriteDiagnostic($"[WebGPU Pipeline] Creating '{shaderName}' with isTransparent={isTransparent}, disableDepthWrite={disableDepthWrite}");
                var vertexAttributes = stackalloc VertexAttribute[4];
                vertexAttributes[0] = new VertexAttribute
                {
                    Format = VertexFormat.Float32x3,
                    Offset = 0,
                    ShaderLocation = 0
                };
                vertexAttributes[1] = new VertexAttribute
                {
                    Format = VertexFormat.Float32x3,
                    Offset = (ulong)sizeof(Vector3),
                    ShaderLocation = 1
                };
                vertexAttributes[2] = new VertexAttribute
                {
                    Format = VertexFormat.Float32x4,
                    Offset = (ulong)(sizeof(float) * (3 + 3)),
                    ShaderLocation = 2
                };
                vertexAttributes[3] = new VertexAttribute
                {
                    Format = VertexFormat.Float32x2,
                    Offset = (ulong)(sizeof(float) * (3 + 3 + 4)),
                    ShaderLocation = 3
                };

                var vertexBufferLayout = new VertexBufferLayout
                {
                    ArrayStride = ManaWebGpuMesh.VertexStride,
                    StepMode = VertexStepMode.Vertex,
                    AttributeCount = 4,
                    Attributes = vertexAttributes
                };

                var vertexState = new VertexState
                {
                    Module = shaderModule,
                    EntryPoint = (byte*)vertexEntryPoint,
                    BufferCount = 1,
                    Buffers = &vertexBufferLayout
                };

                // Blending and depth settings are now parameter driven

                var useAdditiveBlend = shaderName == "ManaLightningBallShader" || additiveBlend;
                var isGrid = shaderName == "ManaGridShader";
                var blendState = new BlendState
                {
                    Color = new BlendComponent
                    {
                        Operation = BlendOperation.Add,
                        SrcFactor = additiveBlend
                            ? BlendFactor.SrcAlpha
                            : useAdditiveBlend
                            ? BlendFactor.One
                            : isTransparent
                                ? BlendFactor.SrcAlpha
                                : BlendFactor.One,
                        DstFactor = useAdditiveBlend
                            ? BlendFactor.One
                            : isTransparent
                                ? BlendFactor.OneMinusSrcAlpha
                                : BlendFactor.Zero
                    },
                    Alpha = new BlendComponent
                    {
                        Operation = BlendOperation.Add,
                        SrcFactor = isTransparent ? BlendFactor.One : BlendFactor.One,
                        DstFactor = useAdditiveBlend
                            ? BlendFactor.One
                            : isTransparent
                                ? BlendFactor.OneMinusSrcAlpha
                                : BlendFactor.Zero
                    }
                };

                var colorTarget = new ColorTargetState
                {
                    Format = colorFormat,
                    Blend = &blendState,
                    WriteMask = ColorWriteMask.All
                };

                var fragmentState = new FragmentState
                {
                    Module = shaderModule,
                    EntryPoint = (byte*)fragmentEntryPoint,
                    TargetCount = 1,
                    Targets = &colorTarget
                };

                var depthStencilState = new DepthStencilState
                {
                    Format = gpu.DepthFormat,
                    DepthWriteEnabled = !disableDepthWrite,
                    DepthCompare = depthCompare,
                    StencilFront = new StencilFaceState
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.Keep
                    },
                    StencilBack = new StencilFaceState
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.Keep
                    },
                    StencilReadMask = uint.MaxValue,
                    StencilWriteMask = uint.MaxValue,

                    // The grid is pushed back a hair so tiles laid at its height win every depth tie.
                    DepthBias = isGrid ? GridDepthBias : 0,
                    DepthBiasSlopeScale = isGrid ? GridDepthBiasSlope : 0,
                    DepthBiasClamp = 0
                };

                var pipelineDescriptor = new RenderPipelineDescriptor
                {
                    Layout = pipelineLayout,
                    Vertex = vertexState,
                    Primitive = new PrimitiveState
                    {
                        Topology = primitiveTopology,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        // Both faces by default: procedural meshes have no fixed winding. Blended materials cull back faces,
                        // since one draw cannot sort a glass mesh's own front and back.
                        CullMode = cullBackFaces ? CullMode.Back : CullMode.None
                    },
                    Multisample = new MultisampleState
                    {
                        Count = 1,
                        Mask = uint.MaxValue
                    },
                    DepthStencil = &depthStencilState,
                    Fragment = &fragmentState
                };

                ManaConsole.WriteDiagnostic("Creating WebGPU render pipeline object.");
                var pipeline = gpu.Api.DeviceCreateRenderPipeline(gpu.Device, &pipelineDescriptor);
                if (pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU mesh render pipeline.");
                }

                ManaConsole.WriteDiagnostic("WebGPU render pipeline object created.");

                return pipeline;
            }
            finally
            {
                Marshal.FreeCoTaskMem(vertexEntryPoint);
                Marshal.FreeCoTaskMem(fragmentEntryPoint);
            }
        }
    }
}

using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// The environment shown as a background, behind every mesh -- WebGPU's counterpart to what
    /// ManaIrradianceRenderer.RenderEnvironmentBackground already does on OpenGL.
    /// <para>
    /// Standalone rather than another slice of <see cref="ManaWebGpuContext"/>, the same shape as
    /// <see cref="ManaWebGpuUiBackdrop"/>: everything it touches (api/device/queue/depth format, and a
    /// clamp sampler and environment view passed in per draw) is a value the context can hand over once,
    /// not a private field it needs to reach back into.
    /// </para>
    /// </summary>
    public unsafe sealed class ManaWebGpuSkybox : IDisposable
    {
        private readonly WebGPU _api;
        private readonly Device* _device;
        private readonly Queue* _queue;
        private readonly TextureFormat _depthFormat;
        private readonly string _shaderPath;

        private BindGroupLayout* _bindGroupLayout;
        private PipelineLayout* _pipelineLayout;
        private RenderPipeline* _pipeline;
        private ShaderModule* _shaderModule;
        private WebGpuBuffer* _uniformBuffer;
        private BindGroup* _bindGroup;
        private TextureView* _boundTextureView;

        /// <summary>
        /// True once the skybox has failed to build. Latched rather than retried every frame, the same as
        /// the ManaUI renderer's own first-use failure: a background that cannot draw should disappear
        /// once and quietly, not throw again on every subsequent frame.
        /// </summary>
        private bool _failed;

        [StructLayout(LayoutKind.Sequential)]
        private struct SkyboxUniform
        {
            public Matrix4x4 InverseDirectionViewProjection;
        }

        public ManaWebGpuSkybox(WebGPU api, Device* device, Queue* queue, TextureFormat depthFormat, string shaderPath)
        {
            _api = api;
            _device = device;
            _queue = queue;
            _depthFormat = depthFormat;
            _shaderPath = shaderPath;
        }

        /// <summary>
        /// Drawn first in the scene pass, with depth write off and the compare forced to Always, so it can
        /// never block or be blocked by anything drawn after it: it is the one thing in the pass that is
        /// not geometry.
        /// </summary>
        public void Draw(
            RenderPassEncoder* renderPass,
            TextureFormat colorFormat,
            Sampler* clampSampler,
            TextureView* environmentView,
            ManaCameraData camera,
            Matrix4x4 aspectCorrectedProjection)
        {
            if (_failed || clampSampler == null || environmentView == null)
            {
                return;
            }

            try
            {
                EnsurePipeline(colorFormat);
                EnsureBindGroup(clampSampler, environmentView);
                UpdateUniform(camera, aspectCorrectedProjection);
            }
            catch (Exception ex)
            {
                _failed = true;
                ManaConsole.WriteDiagnostic($"WebGPU skybox failed to initialise, disabling it: {ex.Message}");
                return;
            }

            if (_pipeline == null || _bindGroup == null)
            {
                return;
            }

            _api.RenderPassEncoderSetPipeline(renderPass, _pipeline);
            _api.RenderPassEncoderSetBindGroup(renderPass, 0, _bindGroup, 0, null);
            _api.RenderPassEncoderDraw(renderPass, 3, 1, 0, 0);
        }

        private void UpdateUniform(ManaCameraData camera, Matrix4x4 aspectCorrectedProjection)
        {
            // Same trick ManaIrradianceRenderer.RenderEnvironmentBackground uses on OpenGL: the view's
            // translation is zeroed before combining with the projection, so unprojecting a screen position
            // through the inverse yields a direction straight away rather than a point that would still
            // need the camera's position subtracted back out of it.
            var directionOnlyView = camera.View;
            directionOnlyView.Translation = Vector3.Zero;

            var directionViewProjection = directionOnlyView * aspectCorrectedProjection;
            if (!Matrix4x4.Invert(directionViewProjection, out var inverse))
            {
                // Degenerate only when the projection itself is degenerate (a zero far plane, say), which
                // means every other draw this frame is already in trouble. Leaving the last good matrix in
                // the uniform buffer is a safer failure than writing a NaN-filled one.
                return;
            }

            var uniform = new SkyboxUniform { InverseDirectionViewProjection = inverse };
            _api.QueueWriteBuffer(_queue, _uniformBuffer, 0, &uniform, (nuint)sizeof(SkyboxUniform));
        }

        private void EnsurePipeline(TextureFormat colorFormat)
        {
            if (_pipeline != null)
            {
                return;
            }

            var entries = stackalloc BindGroupLayoutEntry[3];
            entries[0] = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    MinBindingSize = (ulong)sizeof(SkyboxUniform)
                }
            };
            entries[1] = new BindGroupLayoutEntry
            {
                Binding = 1,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering }
            };
            entries[2] = new BindGroupLayoutEntry
            {
                Binding = 2,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.DimensionCube,
                    Multisampled = false
                }
            };

            var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 3, Entries = entries };
            _bindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &layoutDescriptor);
            if (_bindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox bind group layout.");
            }

            var setLayout = _bindGroupLayout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &setLayout
            };

            _pipelineLayout = _api.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);
            if (_pipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox pipeline layout.");
            }

            _shaderModule = CreateShaderModule(_shaderPath);

            var uniformDescriptor = new BufferDescriptor
            {
                Size = (ulong)sizeof(SkyboxUniform),
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst
            };

            _uniformBuffer = _api.DeviceCreateBuffer(_device, &uniformDescriptor);
            if (_uniformBuffer == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox uniform buffer.");
            }

            var vertexEntryPoint = (byte*)Marshal.StringToCoTaskMemUTF8("vs_main");
            var fragmentEntryPoint = (byte*)Marshal.StringToCoTaskMemUTF8("fs_main");

            try
            {
                var colorTarget = new ColorTargetState
                {
                    Format = colorFormat,
                    Blend = null,
                    WriteMask = ColorWriteMask.All
                };

                var fragmentState = new FragmentState
                {
                    Module = _shaderModule,
                    EntryPoint = fragmentEntryPoint,
                    TargetCount = 1,
                    Targets = &colorTarget
                };

                // Never writes depth and always passes the compare -- a background cannot occlude anything
                // and cannot be occluded by whatever the depth buffer held before this draw, which is the
                // whole point of drawing it first rather than sorting it against real geometry.
                var depthStencilState = new DepthStencilState
                {
                    Format = _depthFormat,
                    DepthWriteEnabled = false,
                    DepthCompare = CompareFunction.Always,
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
                    StencilWriteMask = uint.MaxValue
                };

                var pipelineDescriptor = new RenderPipelineDescriptor
                {
                    Layout = _pipelineLayout,
                    Vertex = new VertexState
                    {
                        Module = _shaderModule,
                        EntryPoint = vertexEntryPoint,
                        BufferCount = 0
                    },
                    Primitive = new PrimitiveState
                    {
                        Topology = PrimitiveTopology.TriangleList,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = CullMode.None
                    },
                    Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                    DepthStencil = &depthStencilState,
                    Fragment = &fragmentState
                };

                _pipeline = _api.DeviceCreateRenderPipeline(_device, &pipelineDescriptor);
                if (_pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create the WebGPU skybox render pipeline.");
                }

                ManaConsole.WriteDiagnostic("WebGPU skybox pipeline created successfully.");
            }
            finally
            {
                Marshal.FreeCoTaskMem((nint)vertexEntryPoint);
                Marshal.FreeCoTaskMem((nint)fragmentEntryPoint);
            }
        }

        /// <summary>
        /// Rebuilt whenever the bound texture view changes -- which happens exactly once in the ordinary
        /// case, when the real environment map swaps in for the startup placeholder, since scene capture is
        /// off on this backend and nothing else replaces it afterwards.
        /// </summary>
        private void EnsureBindGroup(Sampler* clampSampler, TextureView* environmentView)
        {
            if (_bindGroup != null && _boundTextureView == environmentView)
            {
                return;
            }

            if (_bindGroup != null)
            {
                _api.BindGroupRelease(_bindGroup);
                _bindGroup = null;
            }

            var entries = stackalloc BindGroupEntry[3];
            entries[0] = new BindGroupEntry
            {
                Binding = 0,
                Buffer = _uniformBuffer,
                Offset = 0,
                Size = (ulong)sizeof(SkyboxUniform)
            };
            entries[1] = new BindGroupEntry { Binding = 1, Sampler = clampSampler };
            entries[2] = new BindGroupEntry { Binding = 2, TextureView = environmentView };

            var descriptor = new BindGroupDescriptor
            {
                Layout = _bindGroupLayout,
                EntryCount = 3,
                Entries = entries
            };

            _bindGroup = _api.DeviceCreateBindGroup(_device, &descriptor);
            if (_bindGroup == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox bind group.");
            }

            _boundTextureView = environmentView;
        }

        private ShaderModule* CreateShaderModule(string shaderPath)
        {
            var source = ManaWgslPreprocessor.LoadResolved(shaderPath);
            var sourcePointer = Marshal.StringToCoTaskMemUTF8(source);

            try
            {
                var wgslDescriptor = new ShaderModuleWGSLDescriptor
                {
                    Chain = new ChainedStruct
                    {
                        SType = SType.ShaderModuleWgslDescriptor
                    },
                    Code = (byte*)sourcePointer
                };

                var shaderDescriptor = new ShaderModuleDescriptor
                {
                    NextInChain = &wgslDescriptor.Chain
                };

                var shaderModule = _api.DeviceCreateShaderModule(_device, &shaderDescriptor);
                if (shaderModule == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU shader module from {shaderPath}.");
                }

                return shaderModule;
            }
            finally
            {
                Marshal.FreeCoTaskMem(sourcePointer);
            }
        }

        public void Dispose()
        {
            if (_bindGroup != null)
            {
                _api.BindGroupRelease(_bindGroup);
                _bindGroup = null;
            }

            _boundTextureView = null;

            if (_uniformBuffer != null)
            {
                _api.BufferRelease(_uniformBuffer);
                _uniformBuffer = null;
            }

            if (_pipeline != null)
            {
                _api.RenderPipelineRelease(_pipeline);
                _pipeline = null;
            }

            if (_pipelineLayout != null)
            {
                _api.PipelineLayoutRelease(_pipelineLayout);
                _pipelineLayout = null;
            }

            if (_bindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_bindGroupLayout);
                _bindGroupLayout = null;
            }

            if (_shaderModule != null)
            {
                _api.ShaderModuleRelease(_shaderModule);
                _shaderModule = null;
            }
        }
    }
}

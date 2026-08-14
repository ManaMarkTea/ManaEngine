using System.Numerics;
using System.Runtime.InteropServices;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext
    {
        private BindGroupLayout* _skyboxBindGroupLayout;
        private PipelineLayout* _skyboxPipelineLayout;
        private RenderPipeline* _skyboxPipeline;
        private ShaderModule* _skyboxShaderModule;
        private WebGpuBuffer* _skyboxUniformBuffer;
        private BindGroup* _skyboxBindGroup;
        private TextureView* _skyboxBoundTextureView;

        /// <summary>
        /// True once the skybox has failed to build. Latched rather than retried every frame, the same as
        /// the ManaUI renderer's own first-use failure: a background that cannot draw should disappear
        /// once and quietly, not throw again on every subsequent frame.
        /// </summary>
        private bool _skyboxFailed;

        [StructLayout(LayoutKind.Sequential)]
        private struct SkyboxUniform
        {
            public Matrix4x4 InverseDirectionViewProjection;
        }

        /// <summary>
        /// The environment shown as a background, behind every mesh -- WebGPU's counterpart to what
        /// ManaIrradianceRenderer.RenderEnvironmentBackground already does on OpenGL. Drawn first in the
        /// scene pass, with depth write off and the compare forced to Always, so it can never block or be
        /// blocked by anything drawn after it: it is the one thing in the pass that is not geometry.
        /// <para>
        /// Skipped whenever there is nothing sensible to draw yet -- no active scene, no environment map --
        /// which is the same state a frame is in for the first few frames after startup while the real
        /// environment is still loading in the background. The scene pass's own clear is what shows through
        /// on those frames, exactly as it always has.
        /// </para>
        /// </summary>
        private void DrawSkybox(RenderPassEncoder* renderPass)
        {
            if (_skyboxFailed || _activeRenderScene == null || _clampSampler == null)
            {
                return;
            }

            var environment = _environmentReflectionMap;
            if (environment == null || environment.EnvironmentMapResource.TextureView == null)
            {
                return;
            }

            try
            {
                EnsureSkyboxPipeline();
                EnsureSkyboxBindGroup(environment.EnvironmentMapResource.TextureView);
                UpdateSkyboxUniform();
            }
            catch (Exception ex)
            {
                _skyboxFailed = true;
                ManaConsole.WriteDiagnostic($"WebGPU skybox failed to initialise, disabling it: {ex.Message}");
                return;
            }

            if (_skyboxPipeline == null || _skyboxBindGroup == null)
            {
                return;
            }

            _api.RenderPassEncoderSetPipeline(renderPass, _skyboxPipeline);
            _api.RenderPassEncoderSetBindGroup(renderPass, 0, _skyboxBindGroup, 0, null);
            _api.RenderPassEncoderDraw(renderPass, 3, 1, 0, 0);
        }

        private void UpdateSkyboxUniform()
        {
            var camera = _activeRenderScene!.Camera;

            // Same trick ManaIrradianceRenderer.RenderEnvironmentBackground uses on OpenGL: the view's
            // translation is zeroed before combining with the projection, so unprojecting a screen position
            // through the inverse yields a direction straight away rather than a point that would still
            // need the camera's position subtracted back out of it.
            var directionOnlyView = camera.View;
            directionOnlyView.Translation = Vector3.Zero;

            var directionViewProjection = directionOnlyView * GetAspectCorrectedProjection(camera);
            if (!Matrix4x4.Invert(directionViewProjection, out var inverse))
            {
                // Degenerate only when the projection itself is degenerate (a zero far plane, say), which
                // means every other draw this frame is already in trouble. Leaving the last good matrix in
                // the uniform buffer is a safer failure than writing a NaN-filled one.
                return;
            }

            var uniform = new SkyboxUniform { InverseDirectionViewProjection = inverse };
            _api.QueueWriteBuffer(_queue, _skyboxUniformBuffer, 0, &uniform, (nuint)sizeof(SkyboxUniform));
        }

        private void EnsureSkyboxPipeline()
        {
            if (_skyboxPipeline != null)
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
            _skyboxBindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &layoutDescriptor);
            if (_skyboxBindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox bind group layout.");
            }

            var setLayout = _skyboxBindGroupLayout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &setLayout
            };

            _skyboxPipelineLayout = _api.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);
            if (_skyboxPipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox pipeline layout.");
            }

            _skyboxShaderModule = CreateShaderModule(Path.Combine("Assets", "Shaders", "WebGpu", "skybox.wgsl"));

            var uniformDescriptor = new BufferDescriptor
            {
                Size = (ulong)sizeof(SkyboxUniform),
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst
            };

            _skyboxUniformBuffer = _api.DeviceCreateBuffer(_device, &uniformDescriptor);
            if (_skyboxUniformBuffer == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox uniform buffer.");
            }

            var vertexEntryPoint = (byte*)Marshal.StringToCoTaskMemUTF8("vs_main");
            var fragmentEntryPoint = (byte*)Marshal.StringToCoTaskMemUTF8("fs_main");

            try
            {
                var colorTarget = new ColorTargetState
                {
                    Format = GetSceneColorFormat(),
                    Blend = null,
                    WriteMask = ColorWriteMask.All
                };

                var fragmentState = new FragmentState
                {
                    Module = _skyboxShaderModule,
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
                    Layout = _skyboxPipelineLayout,
                    Vertex = new VertexState
                    {
                        Module = _skyboxShaderModule,
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

                _skyboxPipeline = _api.DeviceCreateRenderPipeline(_device, &pipelineDescriptor);
                if (_skyboxPipeline == null)
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
        private void EnsureSkyboxBindGroup(TextureView* environmentView)
        {
            if (_skyboxBindGroup != null && _skyboxBoundTextureView == environmentView)
            {
                return;
            }

            if (_skyboxBindGroup != null)
            {
                _api.BindGroupRelease(_skyboxBindGroup);
                _skyboxBindGroup = null;
            }

            var entries = stackalloc BindGroupEntry[3];
            entries[0] = new BindGroupEntry
            {
                Binding = 0,
                Buffer = _skyboxUniformBuffer,
                Offset = 0,
                Size = (ulong)sizeof(SkyboxUniform)
            };
            entries[1] = new BindGroupEntry { Binding = 1, Sampler = _clampSampler };
            entries[2] = new BindGroupEntry { Binding = 2, TextureView = environmentView };

            var descriptor = new BindGroupDescriptor
            {
                Layout = _skyboxBindGroupLayout,
                EntryCount = 3,
                Entries = entries
            };

            _skyboxBindGroup = _api.DeviceCreateBindGroup(_device, &descriptor);
            if (_skyboxBindGroup == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox bind group.");
            }

            _skyboxBoundTextureView = environmentView;
        }

        private void ReleaseSkybox()
        {
            if (_skyboxBindGroup != null)
            {
                _api.BindGroupRelease(_skyboxBindGroup);
                _skyboxBindGroup = null;
            }

            _skyboxBoundTextureView = null;

            if (_skyboxUniformBuffer != null)
            {
                _api.BufferRelease(_skyboxUniformBuffer);
                _skyboxUniformBuffer = null;
            }

            if (_skyboxPipeline != null)
            {
                _api.RenderPipelineRelease(_skyboxPipeline);
                _skyboxPipeline = null;
            }

            if (_skyboxPipelineLayout != null)
            {
                _api.PipelineLayoutRelease(_skyboxPipelineLayout);
                _skyboxPipelineLayout = null;
            }

            if (_skyboxBindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_skyboxBindGroupLayout);
                _skyboxBindGroupLayout = null;
            }

            if (_skyboxShaderModule != null)
            {
                _api.ShaderModuleRelease(_skyboxShaderModule);
                _skyboxShaderModule = null;
            }
        }
    }
}

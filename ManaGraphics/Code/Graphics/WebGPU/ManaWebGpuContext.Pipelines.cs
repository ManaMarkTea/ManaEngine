using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        private RenderPipeline* CreateDemoRenderPipeline(
            ShaderModule* shaderModule,
            TextureFormat colorFormat,
            PipelineLayout* pipelineLayout,
            string? shaderName = null,
            bool isTransparent = false,
            bool disableDepthWrite = false,
            CompareFunction depthCompare = CompareFunction.Less)
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

                var useAdditiveBlend = shaderName == "ManaLightningBallShader";
                var blendState = new BlendState
                {
                    Color = new BlendComponent
                    {
                        Operation = BlendOperation.Add,
                        SrcFactor = useAdditiveBlend
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
                    Format = _depthFormat,
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
                    DepthBias = 0,
                    DepthBiasSlopeScale = 0,
                    DepthBiasClamp = 0
                };

                var pipelineDescriptor = new RenderPipelineDescriptor
                {
                    Layout = pipelineLayout,
                    Vertex = vertexState,
                    Primitive = new PrimitiveState
                    {
                        Topology = PrimitiveTopology.TriangleList,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        // Procedural meshes do not currently guarantee a single winding
                        // convention. The lightning volume must retain both sides so the
                        // visible entry surface always performs the interior ray march.
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

                ManaConsole.WriteDiagnostic("Creating WebGPU render pipeline object.");
                var pipeline = _api.DeviceCreateRenderPipeline(_device, &pipelineDescriptor);
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

        private void CreateSharedPipelineLayouts()
        {
            var samplerDescriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.Repeat,
                AddressModeV = AddressMode.Repeat,
                AddressModeW = AddressMode.Repeat,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Linear,
                LodMinClamp = 0.0f,
                LodMaxClamp = 32.0f,
                Compare = CompareFunction.Undefined,
                MaxAnisotropy = 1
            };
            _demoSampler = _api.DeviceCreateSampler(_device, &samplerDescriptor);
            if (_demoSampler == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU demo sampler.");
            }

            var clampSamplerDescriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Linear,
                LodMinClamp = 0.0f,
                LodMaxClamp = 32.0f,
                Compare = CompareFunction.Undefined,
                MaxAnisotropy = 1
            };
            _clampSampler = _api.DeviceCreateSampler(_device, &clampSamplerDescriptor);
            if (_clampSampler == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU clamp sampler.");
            }

            var bindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    HasDynamicOffset = false,
                    MinBindingSize = (ulong)sizeof(ManaWebGpuTransformUniform)
                }
            };

            var samplerBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 1,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout
                {
                    Type = SamplerBindingType.Filtering
                }
            };

            var textureBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 2,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2D,
                    Multisampled = false
                }
            };

            var environmentMapBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 3,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.DimensionCube,
                    Multisampled = false
                }
            };

            var irradianceMapBindGroupLayoutEntry = environmentMapBindGroupLayoutEntry;
            irradianceMapBindGroupLayoutEntry.Binding = 4;

            var prefilteredReflectionMapBindGroupLayoutEntry = environmentMapBindGroupLayoutEntry;
            prefilteredReflectionMapBindGroupLayoutEntry.Binding = 5;

            var brdfLutBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 6,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2D,
                    Multisampled = false
                }
            };

            var clampSamplerBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 7,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout
                {
                    Type = SamplerBindingType.Filtering
                }
            };

            var bindGroupLayoutEntries = stackalloc BindGroupLayoutEntry[8];
            bindGroupLayoutEntries[0] = bindGroupLayoutEntry;
            bindGroupLayoutEntries[1] = samplerBindGroupLayoutEntry;
            bindGroupLayoutEntries[2] = textureBindGroupLayoutEntry;
            bindGroupLayoutEntries[3] = environmentMapBindGroupLayoutEntry;
            bindGroupLayoutEntries[4] = irradianceMapBindGroupLayoutEntry;
            bindGroupLayoutEntries[5] = prefilteredReflectionMapBindGroupLayoutEntry;
            bindGroupLayoutEntries[6] = brdfLutBindGroupLayoutEntry;
            bindGroupLayoutEntries[7] = clampSamplerBindGroupLayoutEntry;

            var bindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 8,
                Entries = bindGroupLayoutEntries
            };
            _demoBindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &bindGroupLayoutDescriptor);
            if (_demoBindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU demo bind group layout.");
            }

            var sceneCaptureBindGroupLayoutEntries = stackalloc BindGroupLayoutEntry[3];
            sceneCaptureBindGroupLayoutEntries[0] = bindGroupLayoutEntry;
            sceneCaptureBindGroupLayoutEntries[1] = samplerBindGroupLayoutEntry;
            sceneCaptureBindGroupLayoutEntries[2] = textureBindGroupLayoutEntry;

            var sceneCaptureBindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 3,
                Entries = sceneCaptureBindGroupLayoutEntries
            };
            _sceneCaptureBindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &sceneCaptureBindGroupLayoutDescriptor);
            if (_sceneCaptureBindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU scene capture bind group layout.");
            }

            var bindGroupLayoutPointer = _demoBindGroupLayout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &bindGroupLayoutPointer
            };
            _demoPipelineLayout = _api.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);
            if (_demoPipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU demo pipeline layout.");
            }

            var sceneCaptureBindGroupLayoutPointer = _sceneCaptureBindGroupLayout;
            var sceneCapturePipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &sceneCaptureBindGroupLayoutPointer
            };
            _sceneCapturePipelineLayout = _api.DeviceCreatePipelineLayout(_device, &sceneCapturePipelineLayoutDescriptor);
            if (_sceneCapturePipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU scene capture pipeline layout.");
            }

            // Second bind group, used only by shaders that opt into per-material custom uniforms
            // (lightning_ball.wgsl, liquid_ball.wgsl, water.wgsl today). Additive to group 0, so
            // every other WGSL shader/pipeline is untouched. Visible to both stages because
            // water.wgsl reads custom uniforms in its vertex shader (wave/ripple displacement),
            // not just the fragment shader like the earlier shaders that only needed this for color.
            var customUniformBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    HasDynamicOffset = false,
                    MinBindingSize = 0
                }
            };

            var customUniformBindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 1,
                Entries = &customUniformBindGroupLayoutEntry
            };
            _customUniformBindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &customUniformBindGroupLayoutDescriptor);
            if (_customUniformBindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU custom material uniform bind group layout.");
            }

            var customUniformBindGroupLayouts = stackalloc BindGroupLayout*[2];
            customUniformBindGroupLayouts[0] = _demoBindGroupLayout;
            customUniformBindGroupLayouts[1] = _customUniformBindGroupLayout;
            var customUniformPipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 2,
                BindGroupLayouts = customUniformBindGroupLayouts
            };
            _customUniformPipelineLayout = _api.DeviceCreatePipelineLayout(_device, &customUniformPipelineLayoutDescriptor);
            if (_customUniformPipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU custom-uniform pipeline layout.");
            }
        }

        // Shared by GetOrCreatePipeline (which pipeline/shader file to compile) and
        // GetCustomUniformSlotNames in ManaWebGpuContext.Uniforms.cs (which file to parse for slot
        // names) so the two never drift apart.
        private static string ResolveShaderFileName(string shaderName)
        {
            return shaderName switch
            {
                "ManaLightningBallShader" => "lightning_ball.wgsl",
                "ManaLiquidBallShader" => "liquid_ball.wgsl",
                "ManaLiquidShader" => "liquid.wgsl",
                "ManaGridShader" => "grid.wgsl",
                "ManaEnvironmentIrradianceShader" => "environment_irradiance.wgsl",
                "ManaPbrShader" => "pbr_irradiance.wgsl",
                "ManaWaterShader" => "water.wgsl",
                _ => "pbr_irradiance.wgsl"
            };
        }

        private RenderPipeline* GetOrCreatePipeline(string? shaderName, bool isTransparent = false, bool disableDepthWrite = false)
        {
            shaderName ??= "ManaPbrShader";
            
            string pipelineKey = $"{shaderName}_T{(isTransparent ? 1 : 0)}_D{(disableDepthWrite ? 1 : 0)}";
            if (_pipelines.TryGetValue(pipelineKey, out var cachedPipeline))
            {
                return (RenderPipeline*)cachedPipeline;
            }

            string shaderFile = ResolveShaderFileName(shaderName);

            var shaderPath = Path.Combine("Assets", "Shaders", "WebGpu", shaderFile);
            
            ShaderModule* shaderModule = null;
            try
            {
                shaderModule = CreateShaderModule(shaderPath);
            }
            catch (Exception ex)
            {
                ManaConsole.WriteDiagnostic($"Failed to load WGSL shader '{shaderPath}', falling back to default colored_mesh.wgsl. Error: {ex.Message}");
                shaderFile = "colored_mesh.wgsl";
                shaderPath = Path.Combine("Assets", "Shaders", "WebGpu", shaderFile);
                shaderModule = CreateShaderModule(shaderPath);
            }

            _shaderModules[shaderName] = (nint)shaderModule;
            // Every material pipeline gets group 1 (per-material custom uniforms) available, whether
            // or not a given .wgsl file actually declares it — same "declare only what you use"
            // precedent already relied on for group 0's TransformUniform.
            var pipeline = CreateDemoRenderPipeline(shaderModule, GetSceneColorFormat(), _customUniformPipelineLayout, shaderName, isTransparent, disableDepthWrite);
            _pipelines[pipelineKey] = (nint)pipeline;

            ManaConsole.WriteDiagnostic($"WebGPU render pipeline for '{shaderName}' created successfully using '{shaderFile}'.");
            return pipeline;
        }

        private RenderPipeline* GetOrCreateSceneCapturePipeline()
        {
            if (_sceneCapturePipeline != null)
            {
                return _sceneCapturePipeline;
            }

            var shaderModule = CreateShaderModule(Path.Combine("Assets", "Shaders", "WebGpu", "scene_capture.wgsl"));
            _sceneCapturePipeline = CreateDemoRenderPipeline(shaderModule, TextureFormat.Rgba8UnormSrgb, _sceneCapturePipelineLayout, "SceneCapture");
            _shaderModules["SceneCapture"] = (nint)shaderModule;
            ManaConsole.WriteDiagnostic("WebGPU scene capture pipeline created successfully.");
            return _sceneCapturePipeline;
        }

        private RenderPipeline* GetOrCreateOutlineMaskPipeline()
        {
            if (_outlineMaskPipeline != null)
            {
                return _outlineMaskPipeline;
            }

            var shaderModule = CreateShaderModule(Path.Combine("Assets", "Shaders", "WebGpu", "outline_mask.wgsl"));
            _outlineMaskPipeline = CreateDemoRenderPipeline(
                shaderModule,
                TextureFormat.Rgba8Unorm,
                _demoPipelineLayout,
                "OutlineMask",
                disableDepthWrite: true,
                depthCompare: CompareFunction.LessEqual);
            _shaderModules["OutlineMask"] = (nint)shaderModule;
            ManaConsole.WriteDiagnostic("WebGPU outline-mask pipeline created successfully.");
            return _outlineMaskPipeline;
        }

        private ShaderModule* CreateShaderModule(string relativePath)
        {
            var path = ResolveAssetPath(relativePath);
            var source = File.ReadAllText(path);
            nint sourcePointer = Marshal.StringToCoTaskMemUTF8(source);

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
                    throw new InvalidOperationException($"Failed to create WebGPU shader module from {path}.");
                }

                return shaderModule;
            }
            finally
            {
                Marshal.FreeCoTaskMem(sourcePointer);
            }
        }

    }
}

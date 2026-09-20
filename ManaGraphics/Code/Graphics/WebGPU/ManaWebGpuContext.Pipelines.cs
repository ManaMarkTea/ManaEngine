using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Silk.NET.Maths;
using ManaGraphics.Code.Graphics.Overlays;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
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
            CompareFunction depthCompare = CompareFunction.Less,
            PrimitiveTopology primitiveTopology = PrimitiveTopology.TriangleList,
            bool cullBackFaces = false)
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
                        Topology = primitiveTopology,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        // Procedural meshes do not currently guarantee a single winding
                        // convention. The lightning volume must retain both sides so the
                        // visible entry surface always performs the interior ray march.
                        //
                        // cullBackFaces is the one exception, opted into only for OpacityMode.Blend
                        // materials (glTF glass etc, see DrawSceneMesh): a single glass draw call has no
                        // way to sort its own front and back faces against each other, so both surfaces
                        // fight for the same pixel through the transparency. Culling the far surface --
                        // exactly what real glass thickness would occlude anyway from outside -- removes
                        // the fight instead of trying to order it.
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
            // Must exist before the frame bind group below, which binds the shadow map array view and
            // comparison sampler this creates as its own two extra entries.
            CreateShadowMapResources();

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

            // Group 0: per-frame globals (camera/time/lights) -- one persistent buffer+bind group,
            // written once per frame, bound once per frame, shared by every mesh's draw call. See
            // UpdateFrameUniform (ManaWebGpuContext.Uniforms.cs) and DrawDemoMesh.
            var frameBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    HasDynamicOffset = false,
                    MinBindingSize = (ulong)sizeof(ManaWebGpuFrameUniform)
                }
            };

            // Shadow map array (one layer per cascade) + comparison sampler, sampled by pbr_irradiance.wgsl
            // alongside the frame uniform. Declared here (not group 2) so every existing pipeline layout
            // that already includes group 0 gets them for free -- see CreateShadowMapResources.
            var shadowMapBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 1,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Depth,
                    ViewDimension = TextureViewDimension.Dimension2DArray,
                    Multisampled = false
                }
            };

            var shadowSamplerBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 2,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout
                {
                    Type = SamplerBindingType.Comparison
                }
            };

            var frameBindGroupLayoutEntries = stackalloc BindGroupLayoutEntry[3];
            frameBindGroupLayoutEntries[0] = frameBindGroupLayoutEntry;
            frameBindGroupLayoutEntries[1] = shadowMapBindGroupLayoutEntry;
            frameBindGroupLayoutEntries[2] = shadowSamplerBindGroupLayoutEntry;

            var frameBindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 3,
                Entries = frameBindGroupLayoutEntries
            };
            _frameBindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &frameBindGroupLayoutDescriptor);
            if (_frameBindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU frame bind group layout.");
            }

            var frameBufferDescriptor = new BufferDescriptor
            {
                // CopySrc costs nothing functionally and lets mana_debug_readback_buffer (see
                // mana_webgpu.c) inspect this buffer's real GPU-side bytes during future debugging.
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst | BufferUsage.CopySrc,
                Size = (ulong)sizeof(ManaWebGpuFrameUniform)
            };
            _frameUniformBuffer = _api.DeviceCreateBuffer(_device, &frameBufferDescriptor);
            if (_frameUniformBuffer == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU frame uniform buffer.");
            }

            var frameBindGroupEntry = new BindGroupEntry
            {
                Binding = 0,
                Buffer = _frameUniformBuffer,
                Offset = 0,
                Size = (ulong)sizeof(ManaWebGpuFrameUniform)
            };
            var shadowMapBindGroupEntry = new BindGroupEntry
            {
                Binding = 1,
                TextureView = _shadowMapArrayView
            };
            var shadowSamplerBindGroupEntry = new BindGroupEntry
            {
                Binding = 2,
                Sampler = _shadowSampler
            };
            var frameBindGroupEntries = stackalloc BindGroupEntry[3];
            frameBindGroupEntries[0] = frameBindGroupEntry;
            frameBindGroupEntries[1] = shadowMapBindGroupEntry;
            frameBindGroupEntries[2] = shadowSamplerBindGroupEntry;

            var frameBindGroupDescriptor = new BindGroupDescriptor
            {
                Layout = _frameBindGroupLayout,
                EntryCount = 3,
                Entries = frameBindGroupEntries
            };
            _frameBindGroup = _api.DeviceCreateBindGroup(_device, &frameBindGroupDescriptor);
            if (_frameBindGroup == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU frame bind group.");
            }

            // Group 1: per-object data (model/material) -- sized for the much smaller per-object
            // struct now that per-frame globals live separately above.
            var bindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    HasDynamicOffset = false,
                    MinBindingSize = (ulong)sizeof(ManaWebGpuObjectUniform)
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

            // The rest of the PBR maps. Every one is present in the layout whether or not a material uses
            // it: a bind group must fill every binding its layout declares, so an unassigned channel is
            // bound to a neutral 1x1 rather than left out. A shader that reads none of them is fine --
            // a layout may declare more than the shader consumes.
            var bindGroupLayoutEntries = stackalloc BindGroupLayoutEntry[ObjectBindingCount];
            bindGroupLayoutEntries[0] = bindGroupLayoutEntry;
            bindGroupLayoutEntries[1] = samplerBindGroupLayoutEntry;
            bindGroupLayoutEntries[2] = textureBindGroupLayoutEntry;
            bindGroupLayoutEntries[3] = environmentMapBindGroupLayoutEntry;
            bindGroupLayoutEntries[4] = irradianceMapBindGroupLayoutEntry;
            bindGroupLayoutEntries[5] = prefilteredReflectionMapBindGroupLayoutEntry;
            bindGroupLayoutEntries[6] = brdfLutBindGroupLayoutEntry;
            bindGroupLayoutEntries[7] = clampSamplerBindGroupLayoutEntry;

            for (var binding = FirstMaterialMapBinding; binding < ObjectBindingCount; binding++)
            {
                var entry = textureBindGroupLayoutEntry;
                entry.Binding = (uint)binding;
                bindGroupLayoutEntries[binding] = entry;
            }

            var bindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = ObjectBindingCount,
                Entries = bindGroupLayoutEntries
            };
            _objectBindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &bindGroupLayoutDescriptor);
            if (_objectBindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU object bind group layout.");
            }

            // Scene capture (cubemap probe refresh) intentionally keeps its own, larger, unsplit
            // uniform layout -- see UpdateSceneCaptureUniform/ManaWebGpuSceneCaptureUniform in
            // ManaWebGpuContext.Uniforms.cs for why.
            var sceneCaptureBindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    HasDynamicOffset = false,
                    MinBindingSize = (ulong)sizeof(ManaWebGpuSceneCaptureUniform)
                }
            };

            var sceneCaptureBindGroupLayoutEntries = stackalloc BindGroupLayoutEntry[3];
            sceneCaptureBindGroupLayoutEntries[0] = sceneCaptureBindGroupLayoutEntry;
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

            // Outline mask pass: group 0 (frame) + group 1 (object) -- no custom-material group needed.
            var demoBindGroupLayouts = stackalloc BindGroupLayout*[2];
            demoBindGroupLayouts[0] = _frameBindGroupLayout;
            demoBindGroupLayouts[1] = _objectBindGroupLayout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 2,
                BindGroupLayouts = demoBindGroupLayouts
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

            // Third bind group, used only by shaders that opt into per-material custom uniforms
            // (lightning_ball.wgsl, liquid_ball.wgsl, water.wgsl today). Additive to groups 0/1, so
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

            var customUniformBindGroupLayouts = stackalloc BindGroupLayout*[3];
            customUniformBindGroupLayouts[0] = _frameBindGroupLayout;
            customUniformBindGroupLayouts[1] = _objectBindGroupLayout;
            customUniformBindGroupLayouts[2] = _customUniformBindGroupLayout;
            var customUniformPipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 3,
                BindGroupLayouts = customUniformBindGroupLayouts
            };
            _customUniformPipelineLayout = _api.DeviceCreatePipelineLayout(_device, &customUniformPipelineLayoutDescriptor);
            if (_customUniformPipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU custom-uniform pipeline layout.");
            }
        }

        // Shared with the inspector/schema cache so the .wgsl file used for rendering and the .wgsl
        // file parsed for CustomMaterialUniform metadata never drift apart.
        /// <summary>
        /// Where the material maps start in group(1). Bindings 0..7 are the object uniform, the two
        /// samplers, albedo and the environment set; the rest follow in the order below.
        /// </summary>
        internal const int FirstMaterialMapBinding = 8;

        /// <summary>Normal, emission, metallic, roughness, ambient occlusion and opacity.</summary>
        internal const int MaterialMapCount = 6;

        internal const int ObjectBindingCount = FirstMaterialMapBinding + MaterialMapCount;

        private static string ResolveShaderFileName(string shaderName)
        {
            return ManaWebGpuCustomUniformSchemaCache.ResolveShaderFileNameOrDefault(shaderName);
        }

        private RenderPipeline* GetOrCreatePipeline(
            string? shaderName, bool isTransparent = false, bool disableDepthWrite = false, bool cullBackFaces = false,
            bool alwaysOnTop = false)
        {
            shaderName ??= "ManaPbrShader";

            string pipelineKey =
                $"{shaderName}_T{(isTransparent ? 1 : 0)}_D{(disableDepthWrite ? 1 : 0)}_C{(cullBackFaces ? 1 : 0)}_A{(alwaysOnTop ? 1 : 0)}";
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
            var pipeline = CreateDemoRenderPipeline(
                shaderModule, GetSceneColorFormat(), _customUniformPipelineLayout, shaderName, isTransparent,
                disableDepthWrite || alwaysOnTop,
                depthCompare: alwaysOnTop ? CompareFunction.Always : CompareFunction.Less,
                cullBackFaces: cullBackFaces);
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

        private RenderPipeline* GetOrCreateOutlineMaskPipeline(bool alwaysOnTop = false)
        {
            if (alwaysOnTop)
            {
                return _outlineMaskAlwaysOnTopPipeline != null
                    ? _outlineMaskAlwaysOnTopPipeline
                    : _outlineMaskAlwaysOnTopPipeline = CreateOutlineMaskPipeline(CompareFunction.Always);
            }

            if (_outlineMaskPipeline != null)
            {
                return _outlineMaskPipeline;
            }

            _outlineMaskPipeline = CreateOutlineMaskPipeline(CompareFunction.LessEqual);
            return _outlineMaskPipeline;
        }

        // The mask is depth-tested against what the scene pass left behind, so a highlighted mesh only
        // shows where nothing is in front of it. Handles that are always on top compare Always instead, or
        // their hover highlight would vanish behind the model exactly as the handle used to.
        private RenderPipeline* CreateOutlineMaskPipeline(CompareFunction depthCompare)
        {
            var shaderModule = CreateShaderModule(Path.Combine("Assets", "Shaders", "WebGpu", "outline_mask.wgsl"));
            var pipeline = CreateDemoRenderPipeline(
                shaderModule,
                TextureFormat.Rgba8Unorm,
                _demoPipelineLayout,
                "OutlineMask",
                disableDepthWrite: true,
                depthCompare: depthCompare);
            _shaderModules[depthCompare == CompareFunction.Always ? "OutlineMaskAlwaysOnTop" : "OutlineMask"] = (nint)shaderModule;
            ManaConsole.WriteDiagnostic($"WebGPU outline-mask pipeline created successfully ({depthCompare}).");
            return pipeline;
        }

        private RenderPipeline* GetOrCreateOverlayPipeline(ManaWebGpuOverlayPass overlay)
        {
            if (_overlayPipelines.TryGetValue(overlay.Name, out var cached))
            {
                return (RenderPipeline*)cached;
            }

            var shaderModule = CreateShaderModule(Path.Combine("Assets", "Shaders", "WebGpu", overlay.ShaderFileName));
            var pipeline = CreateDemoRenderPipeline(
                shaderModule,
                GetSceneColorFormat(),
                _demoPipelineLayout,
                overlay.Name,
                disableDepthWrite: true,
                depthCompare: CompareFunction.LessEqual,
                primitiveTopology: overlay.Topology);

            _shaderModules[overlay.Name] = (nint)shaderModule;
            _overlayPipelines[overlay.Name] = (nint)pipeline;
            ManaConsole.WriteDiagnostic($"WebGPU {overlay.Name} overlay pipeline created successfully.");
            return pipeline;
        }

        private ShaderModule* CreateShaderModule(string relativePath)
        {
            var path = ResolveAssetPath(relativePath);
            var source = ManaWgslPreprocessor.LoadResolved(path);
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

#if MANA_BROWSER
                // Kept as a standing diagnostic: logs any WGSL compilation warnings/errors straight
                // to the browser console. See mana_webgpu.c's mana_log_shader_compilation_info_impl.
                ManaBrowserWebGpuCallbackBridge.LogShaderCompilationInfo((int)(nint)shaderModule);
#endif

                return shaderModule;
            }
            finally
            {
                Marshal.FreeCoTaskMem(sourcePointer);
            }
        }

    }
}

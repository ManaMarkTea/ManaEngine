using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>
    /// The layouts, samplers and frame uniform every scene pipeline shares: group 0 frame and shadows, group 1 one
    /// mesh's object data and textures, group 2 custom material uniforms, plus the scene-capture layout.
    /// </summary>
    internal sealed unsafe class ManaWebGpuSceneBindings : ManaWebGpuDeviceObject
    {
        /// <summary>Where the material maps start in group 1, after the object uniform, samplers, albedo and environment.</summary>
        public const int FirstMaterialMapBinding = 8;

        /// <summary>Normal, emission, metallic, roughness, ambient occlusion and opacity.</summary>
        public const int MaterialMapCount = 6;

        public const int ObjectBindingCount = FirstMaterialMapBinding + MaterialMapCount;

        /// <summary>Group 0: frame uniform, shadow map and sampler, three light buffers, probe harmonics and sampler, occlusion.</summary>
        public const int FrameBindingCount = 11;

        private TextureView* _shadowMapArrayView;
        private Sampler* _shadowSampler;
        private ManaWebGpuLightClusters? _lights;
        private Texture* _emptyProbeTexture;
        private TextureView* _emptyProbeView;
        private TextureView* _probeRed;
        private TextureView* _probeGreen;
        private TextureView* _probeBlue;
        private TextureView* _occlusion;
        private Texture* _openOcclusionTexture;
        private TextureView* _openOcclusionView;


        public Sampler* LinearSampler { get; private set; }
        public Sampler* NearestSampler { get; private set; }
        public Sampler* ClampSampler { get; private set; }
        public BindGroupLayout* FrameLayout { get; private set; }
        public WebGpuBuffer* FrameBuffer { get; private set; }
        public BindGroup* FrameGroup { get; private set; }
        public BindGroupLayout* ObjectLayout { get; private set; }

        /// <summary>
        /// Group 1 for an instanced draw: as <see cref="ObjectLayout"/>, but binding 0 is a storage array of object
        /// uniforms reached through a dynamic offset (pbr_irradiance.wgsl built with MANA_INSTANCED).
        /// </summary>
        public BindGroupLayout* InstancedObjectLayout { get; private set; }

        /// <summary>Frame group and <see cref="InstancedObjectLayout"/>, for the instanced material pipelines.</summary>
        public PipelineLayout* InstancedMaterialLayout { get; private set; }

        /// <summary>Instances one draw reaches from its dynamic offset; a longer run is split into several draws.</summary>
        public const int InstancesPerDraw = 256;
        public BindGroupLayout* CustomUniformGroupLayout { get; private set; }
        public BindGroupLayout* SceneCaptureGroupLayout { get; private set; }

        /// <summary>Groups 0 and 1: outlines, overlays.</summary>
        public PipelineLayout* MeshLayout { get; private set; }

        /// <summary>Groups 0, 1 and 2: every material pipeline, whether or not its shader declares group 2.</summary>
        public PipelineLayout* MaterialLayout { get; private set; }

        /// <summary>The capture uniform alone: reflection-probe faces and thumbnails.</summary>
        public PipelineLayout* SceneCaptureLayout { get; private set; }

        /// <param name="shadowMapArrayView">Bound into group 0; the shadow pass keeps it alive for as long as these bindings.</param>
        /// <param name="lights">The light cluster buffers, bound into group 0; the scene renderer keeps them as long.</param>
        public ManaWebGpuSceneBindings(ManaWebGpuDevice gpu, TextureView* shadowMapArrayView, Sampler* shadowSampler, ManaWebGpuLightClusters lights)
            : base(gpu)
        {
            var api = gpu.Api;
            var device = gpu.Device;
            try
            {
                Create(api, device, shadowMapArrayView, shadowSampler, lights);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Create(WebGPU api, Device* device, TextureView* shadowMapArrayView, Sampler* shadowSampler, ManaWebGpuLightClusters lights)
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
                    // Ground and floors seen at a low angle stay sharp instead of blurring to a far mip.
                    MaxAnisotropy = 8
                };
                LinearSampler = api.DeviceCreateSampler(device, &samplerDescriptor);
                if (LinearSampler == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU demo sampler.");
                }

                // Pixel art: texels stay square up close; mips still blend so it does not shimmer far away.
                samplerDescriptor.MagFilter = FilterMode.Nearest;
                samplerDescriptor.MinFilter = FilterMode.Nearest;
                // Anisotropy needs every filter linear.
                samplerDescriptor.MaxAnisotropy = 1;
                NearestSampler = api.DeviceCreateSampler(device, &samplerDescriptor);
                if (NearestSampler == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU nearest sampler.");
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
                ClampSampler = api.DeviceCreateSampler(device, &clampSamplerDescriptor);
                if (ClampSampler == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU clamp sampler.");
                }

                // Group 0: frame globals, written and bound once a frame.
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

                // The shadow map array and comparison sampler, in group 0 so every pipeline has them.
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

                // The placed lights, the clustered view's cells and each cell's light indices (ManaWebGpuLightClusters).
                var frameBindGroupLayoutEntries = stackalloc BindGroupLayoutEntry[FrameBindingCount];
                frameBindGroupLayoutEntries[0] = frameBindGroupLayoutEntry;
                frameBindGroupLayoutEntries[1] = shadowMapBindGroupLayoutEntry;
                frameBindGroupLayoutEntries[2] = shadowSamplerBindGroupLayoutEntry;
                for (var binding = 3; binding < 6; binding++)
                {
                    frameBindGroupLayoutEntries[binding] = new BindGroupLayoutEntry
                    {
                        Binding = (uint)binding,
                        Visibility = ShaderStage.Fragment,
                        Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage, HasDynamicOffset = false }
                    };
                }
                // The probe volume's light: three textures of L1 harmonics (red, green, blue) and their sampler.
                for (var binding = 6; binding < 9; binding++)
                {
                    frameBindGroupLayoutEntries[binding] = new BindGroupLayoutEntry
                    {
                        Binding = (uint)binding,
                        Visibility = ShaderStage.Fragment,
                        Texture = new TextureBindingLayout
                        {
                            SampleType = TextureSampleType.Float,
                            ViewDimension = TextureViewDimension.Dimension3D,
                            Multisampled = false
                        }
                    };
                }
                frameBindGroupLayoutEntries[9] = new BindGroupLayoutEntry
                {
                    Binding = 9,
                    Visibility = ShaderStage.Fragment,
                    Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering }
                };
                // Screen-space ambient occlusion (ManaWebGpuAmbientOcclusionPass), half resolution.
                frameBindGroupLayoutEntries[10] = new BindGroupLayoutEntry
                {
                    Binding = 10,
                    Visibility = ShaderStage.Fragment,
                    Texture = new TextureBindingLayout
                    {
                        SampleType = TextureSampleType.Float,
                        ViewDimension = TextureViewDimension.Dimension2D,
                        Multisampled = false
                    }
                };

                var frameBindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
                {
                    EntryCount = FrameBindingCount,
                    Entries = frameBindGroupLayoutEntries
                };
                FrameLayout = api.DeviceCreateBindGroupLayout(device, &frameBindGroupLayoutDescriptor);
                if (FrameLayout == null)
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
                FrameBuffer = api.DeviceCreateBuffer(device, &frameBufferDescriptor);
                if (FrameBuffer == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU frame uniform buffer.");
                }

                _shadowMapArrayView = shadowMapArrayView;
                _shadowSampler = shadowSampler;
                _lights = lights;
                CreateEmptyProbeTexture(api, device);
                CreateOpenOcclusionTexture(api, device);
                BuildFrameGroup();

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

                // Every map is in the layout whether a material uses it or not; an unused one binds a neutral 1x1.
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
                ObjectLayout = api.DeviceCreateBindGroupLayout(device, &bindGroupLayoutDescriptor);
                if (ObjectLayout == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU object bind group layout.");
                }

                bindGroupLayoutEntries[0] = new BindGroupLayoutEntry
                {
                    Binding = 0,
                    Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                    Buffer = new BufferBindingLayout
                    {
                        Type = BufferBindingType.ReadOnlyStorage,
                        HasDynamicOffset = true,
                        MinBindingSize = (ulong)(sizeof(ManaWebGpuObjectUniform) * InstancesPerDraw)
                    }
                };
                InstancedObjectLayout = api.DeviceCreateBindGroupLayout(device, &bindGroupLayoutDescriptor);
                if (InstancedObjectLayout == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU instanced object bind group layout.");
                }

                var instancedGroupLayouts = stackalloc BindGroupLayout*[2];
                instancedGroupLayouts[0] = FrameLayout;
                instancedGroupLayouts[1] = InstancedObjectLayout;
                var instancedPipelineLayoutDescriptor = new PipelineLayoutDescriptor { BindGroupLayoutCount = 2, BindGroupLayouts = instancedGroupLayouts };
                InstancedMaterialLayout = api.DeviceCreatePipelineLayout(device, &instancedPipelineLayoutDescriptor);
                if (InstancedMaterialLayout == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU instanced material pipeline layout.");
                }

                // Scene capture keeps its own unsplit uniform layout (ManaWebGpuSceneCaptureUniform).
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
                SceneCaptureGroupLayout = api.DeviceCreateBindGroupLayout(device, &sceneCaptureBindGroupLayoutDescriptor);
                if (SceneCaptureGroupLayout == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU scene capture bind group layout.");
                }

                // Outline mask pass: group 0 (frame) + group 1 (object) -- no custom-material group needed.
                var demoBindGroupLayouts = stackalloc BindGroupLayout*[2];
                demoBindGroupLayouts[0] = FrameLayout;
                demoBindGroupLayouts[1] = ObjectLayout;
                var pipelineLayoutDescriptor = new PipelineLayoutDescriptor
                {
                    BindGroupLayoutCount = 2,
                    BindGroupLayouts = demoBindGroupLayouts
                };
                MeshLayout = api.DeviceCreatePipelineLayout(device, &pipelineLayoutDescriptor);
                if (MeshLayout == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU demo pipeline layout.");
                }

                var sceneCaptureBindGroupLayoutPointer = SceneCaptureGroupLayout;
                var sceneCapturePipelineLayoutDescriptor = new PipelineLayoutDescriptor
                {
                    BindGroupLayoutCount = 1,
                    BindGroupLayouts = &sceneCaptureBindGroupLayoutPointer
                };
                SceneCaptureLayout = api.DeviceCreatePipelineLayout(device, &sceneCapturePipelineLayoutDescriptor);
                if (SceneCaptureLayout == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU scene capture pipeline layout.");
                }

                // Group 2: per-material custom uniforms for shaders that declare them (water, lightning, liquid). Vertex stage
                // too: water displaces with them.
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
                CustomUniformGroupLayout = api.DeviceCreateBindGroupLayout(device, &customUniformBindGroupLayoutDescriptor);
                if (CustomUniformGroupLayout == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU custom material uniform bind group layout.");
                }

                var customUniformBindGroupLayouts = stackalloc BindGroupLayout*[3];
                customUniformBindGroupLayouts[0] = FrameLayout;
                customUniformBindGroupLayouts[1] = ObjectLayout;
                customUniformBindGroupLayouts[2] = CustomUniformGroupLayout;
                var customUniformPipelineLayoutDescriptor = new PipelineLayoutDescriptor
                {
                    BindGroupLayoutCount = 3,
                    BindGroupLayouts = customUniformBindGroupLayouts
                };
                MaterialLayout = api.DeviceCreatePipelineLayout(device, &customUniformPipelineLayoutDescriptor);
                if (MaterialLayout == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU custom-uniform pipeline layout.");
                }
        }

        /// <summary>
        /// Binds the probe volume's harmonics (ManaWebGpuProbeVolume) into group 0, or the black placeholder for null.
        /// Rebuilds the frame group, so it is called when the volume's textures change, not every frame.
        /// </summary>
        public void SetProbeVolume(TextureView* red, TextureView* green, TextureView* blue)
        {
            _probeRed = red;
            _probeGreen = green;
            _probeBlue = blue;
            BuildFrameGroup();
        }

        /// <summary>Binds the ambient occlusion (ManaWebGpuAmbientOcclusionPass) into group 0, or the open placeholder for null.</summary>
        public void SetAmbientOcclusion(TextureView* occlusion)
        {
            _occlusion = occlusion;
            BuildFrameGroup();
        }

        private void BuildFrameGroup()
        {
            var red = _probeRed;
            var green = _probeGreen;
            var blue = _probeBlue;
            if (red == null || green == null || blue == null)
            {
                red = _emptyProbeView;
                green = _emptyProbeView;
                blue = _emptyProbeView;
            }
            var occlusion = _occlusion;
            if (occlusion == null)
            {
                occlusion = _openOcclusionView;
            }

            var entries = stackalloc BindGroupEntry[FrameBindingCount];
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = FrameBuffer, Offset = 0, Size = (ulong)sizeof(ManaWebGpuFrameUniform) };
            entries[1] = new BindGroupEntry { Binding = 1, TextureView = _shadowMapArrayView };
            entries[2] = new BindGroupEntry { Binding = 2, Sampler = _shadowSampler };
            entries[3] = new BindGroupEntry { Binding = 3, Buffer = _lights!.LightBuffer, Offset = 0, Size = _lights.LightBufferSize };
            entries[4] = new BindGroupEntry { Binding = 4, Buffer = _lights.CellBuffer, Offset = 0, Size = _lights.CellBufferSize };
            entries[5] = new BindGroupEntry { Binding = 5, Buffer = _lights.IndexBuffer, Offset = 0, Size = _lights.IndexBufferSize };
            entries[6] = new BindGroupEntry { Binding = 6, TextureView = red };
            entries[7] = new BindGroupEntry { Binding = 7, TextureView = green };
            entries[8] = new BindGroupEntry { Binding = 8, TextureView = blue };
            entries[9] = new BindGroupEntry { Binding = 9, Sampler = ClampSampler };
            entries[10] = new BindGroupEntry { Binding = 10, TextureView = occlusion };

            var descriptor = new BindGroupDescriptor { Layout = FrameLayout, EntryCount = FrameBindingCount, Entries = entries };
            var label = Marshal.StringToCoTaskMemUTF8("Scene frame and shadows");
            BindGroup* group;
            try
            {
                descriptor.Label = (byte*)label;
                group = Api.DeviceCreateBindGroup(Device, &descriptor);
            }
            finally
            {
                Marshal.FreeCoTaskMem(label);
            }
            if (group == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU frame bind group.");
            }
            // A frame already encoded holds its own reference; releasing ours only drops this owner's.
            if (FrameGroup != null)
            {
                Api.BindGroupRelease(FrameGroup);
            }
            FrameGroup = group;
        }

        // Black harmonics: what group 0 binds until a level has a probe volume (the shader skips it then anyway).
        private void CreateEmptyProbeTexture(WebGPU api, Device* device)
        {
            var descriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension3D,
                Size = new Extent3D { Width = 1, Height = 1, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba16float,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _emptyProbeTexture = api.DeviceCreateTexture(device, &descriptor);
            if (_emptyProbeTexture == null)
            {
                throw new InvalidOperationException("Failed to create the empty probe texture.");
            }
            var viewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba16float,
                Dimension = TextureViewDimension.Dimension3D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            _emptyProbeView = api.TextureCreateView(_emptyProbeTexture, &viewDescriptor);
        }

        // White: nothing occluded. What group 0 binds while ambient occlusion is off.
        private void CreateOpenOcclusionTexture(WebGPU api, Device* device)
        {
            var descriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = 1, Height = 1, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8Unorm,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _openOcclusionTexture = api.DeviceCreateTexture(device, &descriptor);
            if (_openOcclusionTexture == null)
            {
                throw new InvalidOperationException("Failed to create the open occlusion texture.");
            }
            var white = stackalloc byte[4] { 255, 255, 255, 255 };
            var destination = new ImageCopyTexture { Texture = _openOcclusionTexture, MipLevel = 0, Origin = new Origin3D(), Aspect = TextureAspect.All };
            var layout = new TextureDataLayout { Offset = 0, BytesPerRow = 4, RowsPerImage = 1 };
            var size = new Extent3D { Width = 1, Height = 1, DepthOrArrayLayers = 1 };
            api.QueueWriteTexture(api.DeviceGetQueue(device), &destination, white, 4, &layout, &size);
            var viewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba8Unorm,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            _openOcclusionView = api.TextureCreateView(_openOcclusionTexture, &viewDescriptor);
        }

        public void WriteFrame(Queue* queue, in ManaWebGpuFrameUniform uniform)
        {
            fixed (ManaWebGpuFrameUniform* data = &uniform)
            {
                Api.QueueWriteBuffer(queue, FrameBuffer, 0, data, (nuint)sizeof(ManaWebGpuFrameUniform));
            }
        }

        public override void Dispose()
        {
            if (SceneCaptureLayout != null) { Api.PipelineLayoutRelease(SceneCaptureLayout); SceneCaptureLayout = null; }
            if (SceneCaptureGroupLayout != null) { Api.BindGroupLayoutRelease(SceneCaptureGroupLayout); SceneCaptureGroupLayout = null; }
            if (MeshLayout != null) { Api.PipelineLayoutRelease(MeshLayout); MeshLayout = null; }
            if (MaterialLayout != null) { Api.PipelineLayoutRelease(MaterialLayout); MaterialLayout = null; }
            if (CustomUniformGroupLayout != null) { Api.BindGroupLayoutRelease(CustomUniformGroupLayout); CustomUniformGroupLayout = null; }
            if (LinearSampler != null) { Api.SamplerRelease(LinearSampler); LinearSampler = null; }
            if (NearestSampler != null) { Api.SamplerRelease(NearestSampler); NearestSampler = null; }
            if (ClampSampler != null) { Api.SamplerRelease(ClampSampler); ClampSampler = null; }
            if (InstancedMaterialLayout != null) { Api.PipelineLayoutRelease(InstancedMaterialLayout); InstancedMaterialLayout = null; }
            if (InstancedObjectLayout != null) { Api.BindGroupLayoutRelease(InstancedObjectLayout); InstancedObjectLayout = null; }
            if (ObjectLayout != null) { Api.BindGroupLayoutRelease(ObjectLayout); ObjectLayout = null; }
            if (FrameGroup != null) { Api.BindGroupRelease(FrameGroup); FrameGroup = null; }
            if (_emptyProbeView != null) { Api.TextureViewRelease(_emptyProbeView); _emptyProbeView = null; }
            if (_emptyProbeTexture != null) { Api.TextureRelease(_emptyProbeTexture); _emptyProbeTexture = null; }
            if (_openOcclusionView != null) { Api.TextureViewRelease(_openOcclusionView); _openOcclusionView = null; }
            if (_openOcclusionTexture != null) { Api.TextureRelease(_openOcclusionTexture); _openOcclusionTexture = null; }
            if (FrameBuffer != null) { Api.BufferRelease(FrameBuffer); FrameBuffer = null; }
            if (FrameLayout != null) { Api.BindGroupLayoutRelease(FrameLayout); FrameLayout = null; }
        }
    }
}

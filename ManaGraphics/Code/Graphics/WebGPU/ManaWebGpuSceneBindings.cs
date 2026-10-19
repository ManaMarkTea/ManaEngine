using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// The bind group layouts, samplers and frame uniform every scene pipeline shares: group 0 (frame globals and the
    /// shadow map), group 1 (one mesh's object uniform and textures), group 2 (a material's custom uniforms), and the
    /// separate scene-capture layout. Passes build their pipelines over <see cref="MeshLayout"/>,
    /// <see cref="MaterialLayout"/> or <see cref="SceneCaptureLayout"/>; scene meshes build their groups from these layouts.
    /// </summary>
    internal sealed unsafe class ManaWebGpuSceneBindings : IDisposable
    {
        /// <summary>
        /// Where the material maps start in group(1). Bindings 0..7 are the object uniform, the two
        /// samplers, albedo and the environment set; the rest follow in the order below.
        /// </summary>
        public const int FirstMaterialMapBinding = 8;

        /// <summary>Normal, emission, metallic, roughness, ambient occlusion and opacity.</summary>
        public const int MaterialMapCount = 6;

        public const int ObjectBindingCount = FirstMaterialMapBinding + MaterialMapCount;

        private readonly WebGPU _api;

        public Sampler* LinearSampler { get; private set; }
        public Sampler* NearestSampler { get; private set; }
        public Sampler* ClampSampler { get; private set; }
        public BindGroupLayout* FrameLayout { get; private set; }
        public WebGpuBuffer* FrameBuffer { get; private set; }
        public BindGroup* FrameGroup { get; private set; }
        public BindGroupLayout* ObjectLayout { get; private set; }
        public BindGroupLayout* CustomUniformGroupLayout { get; private set; }
        public BindGroupLayout* SceneCaptureGroupLayout { get; private set; }

        /// <summary>Groups 0 and 1: outlines, overlays.</summary>
        public PipelineLayout* MeshLayout { get; private set; }

        /// <summary>Groups 0, 1 and 2: every material pipeline, whether or not its shader declares group 2.</summary>
        public PipelineLayout* MaterialLayout { get; private set; }

        /// <summary>The capture uniform alone: reflection-probe faces and thumbnails.</summary>
        public PipelineLayout* SceneCaptureLayout { get; private set; }

        /// <param name="shadowMapArrayView">Bound into group 0; the shadow pass keeps it alive for as long as these bindings.</param>
        public ManaWebGpuSceneBindings(ManaWebGpuDevice gpu, TextureView* shadowMapArrayView, Sampler* shadowSampler)
        {
            _api = gpu.Api;
            var api = gpu.Api;
            var device = gpu.Device;
            try
            {
                Create(api, device, shadowMapArrayView, shadowSampler);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void Create(WebGPU api, Device* device, TextureView* shadowMapArrayView, Sampler* shadowSampler)
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
                LinearSampler = api.DeviceCreateSampler(device, &samplerDescriptor);
                if (LinearSampler == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU demo sampler.");
                }

                // Pixel art: texels stay square up close; mips still blend so it does not shimmer far away.
                samplerDescriptor.MagFilter = FilterMode.Nearest;
                samplerDescriptor.MinFilter = FilterMode.Nearest;
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

                // Group 0: per-frame globals (camera/time/lights) -- one persistent buffer+bind group,
                // written once per frame, bound once per frame, shared by every mesh's draw call. See
                // ManaWebGpuFrameUniform.For and the passes that bind it.
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

                var frameBindGroupEntry = new BindGroupEntry
                {
                    Binding = 0,
                    Buffer = FrameBuffer,
                    Offset = 0,
                    Size = (ulong)sizeof(ManaWebGpuFrameUniform)
                };
                var shadowMapBindGroupEntry = new BindGroupEntry
                {
                    Binding = 1,
                    TextureView = shadowMapArrayView
                };
                var shadowSamplerBindGroupEntry = new BindGroupEntry
                {
                    Binding = 2,
                    Sampler = shadowSampler
                };
                var frameBindGroupEntries = stackalloc BindGroupEntry[3];
                frameBindGroupEntries[0] = frameBindGroupEntry;
                frameBindGroupEntries[1] = shadowMapBindGroupEntry;
                frameBindGroupEntries[2] = shadowSamplerBindGroupEntry;

                var frameBindGroupDescriptor = new BindGroupDescriptor
                {
                    Layout = FrameLayout,
                    EntryCount = 3,
                    Entries = frameBindGroupEntries
                };
                var frameLabel = Marshal.StringToCoTaskMemUTF8("Scene frame and shadows");
                try
                {
                    frameBindGroupDescriptor.Label = (byte*)frameLabel;
                    FrameGroup = api.DeviceCreateBindGroup(device, &frameBindGroupDescriptor);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(frameLabel);
                }
                if (FrameGroup == null)
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
                ObjectLayout = api.DeviceCreateBindGroupLayout(device, &bindGroupLayoutDescriptor);
                if (ObjectLayout == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU object bind group layout.");
                }

                // Scene capture (cubemap probe refresh) intentionally keeps its own, larger, unsplit
                // uniform layout -- see UpdateSceneCaptureUniform/ManaWebGpuSceneCaptureUniform in
                // ManaWebGpuUniforms.cs for why.
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

        public void WriteFrame(Queue* queue, in ManaWebGpuFrameUniform uniform)
        {
            fixed (ManaWebGpuFrameUniform* data = &uniform)
            {
                _api.QueueWriteBuffer(queue, FrameBuffer, 0, data, (nuint)sizeof(ManaWebGpuFrameUniform));
            }
        }

        public void Dispose()
        {
            if (SceneCaptureLayout != null) { _api.PipelineLayoutRelease(SceneCaptureLayout); SceneCaptureLayout = null; }
            if (SceneCaptureGroupLayout != null) { _api.BindGroupLayoutRelease(SceneCaptureGroupLayout); SceneCaptureGroupLayout = null; }
            if (MeshLayout != null) { _api.PipelineLayoutRelease(MeshLayout); MeshLayout = null; }
            if (MaterialLayout != null) { _api.PipelineLayoutRelease(MaterialLayout); MaterialLayout = null; }
            if (CustomUniformGroupLayout != null) { _api.BindGroupLayoutRelease(CustomUniformGroupLayout); CustomUniformGroupLayout = null; }
            if (LinearSampler != null) { _api.SamplerRelease(LinearSampler); LinearSampler = null; }
            if (NearestSampler != null) { _api.SamplerRelease(NearestSampler); NearestSampler = null; }
            if (ClampSampler != null) { _api.SamplerRelease(ClampSampler); ClampSampler = null; }
            if (ObjectLayout != null) { _api.BindGroupLayoutRelease(ObjectLayout); ObjectLayout = null; }
            if (FrameGroup != null) { _api.BindGroupRelease(FrameGroup); FrameGroup = null; }
            if (FrameBuffer != null) { _api.BufferRelease(FrameBuffer); FrameBuffer = null; }
            if (FrameLayout != null) { _api.BindGroupLayoutRelease(FrameLayout); FrameLayout = null; }
        }
    }
}

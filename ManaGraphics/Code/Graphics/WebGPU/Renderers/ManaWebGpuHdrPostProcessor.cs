using System;
using System.IO;
using System.Runtime.InteropServices;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif

namespace ManaGraphics.Code.Graphics.Renderers
{
    internal unsafe sealed class ManaWebGpuHdrPostProcessor : ManaWebGpuDeviceObject
    {
        private readonly TextureFormat _surfaceFormat;
        private readonly TextureFormat _sceneFormat = TextureFormat.Rgba16float;
        private readonly ShaderModule* _shaderModule;
        private readonly Sampler* _sampler;

        // Shared Bind Group Layout and Pipeline Layout
        private readonly BindGroupLayout* _bindGroupLayout;
        private readonly PipelineLayout* _pipelineLayout;

        // Pipelines
        private readonly RenderPipeline* _brightPipeline;
        private readonly RenderPipeline* _blurHPipeline;
        private readonly RenderPipeline* _blurVPipeline;
        private readonly RenderPipeline* _combinePipeline;
        private readonly RenderPipeline* _plainPipeline;
        // SMAA's input: the tone map alone, into a texture of the window's format (made on first use).
        private RenderPipeline* _tonemapPipeline;

        // Textures & Views (Full Res Scene, 1/4 Res Bloom A & B)
        private Texture* _sceneTexture;
        private TextureView* _sceneTextureView;
        private Texture* _outlineMaskTexture;
        private TextureView* _outlineMaskView;
        private Texture* _bloomTextureA;
        private TextureView* _bloomViewA;
        private Texture* _bloomTextureB;
        private TextureView* _bloomViewB;

        // Bind Groups
        private BindGroup* _brightBindGroup;
        private BindGroup* _blurHBindGroup;
        private BindGroup* _blurVBindGroup;
        private BindGroup* _combineBindGroup;

        public ManaWebGpuHdrPostProcessor(ManaWebGpuDevice gpu, TextureFormat surfaceFormat, string shaderPath)
            : base(gpu)
        {
            _surfaceFormat = surfaceFormat;
            _sampler = CreateSampler();
            
            _bindGroupLayout = CreateBindGroupLayout();
            _pipelineLayout = CreatePipelineLayout();
            
            _shaderModule = CreateShaderModule(shaderPath);
            
            _brightPipeline = CreatePipeline(_pipelineLayout, "fs_bright", _sceneFormat);
            _blurHPipeline = CreatePipeline(_pipelineLayout, "fs_blur_h", _sceneFormat);
            _blurVPipeline = CreatePipeline(_pipelineLayout, "fs_blur_v", _sceneFormat);
            _combinePipeline = CreatePipeline(_pipelineLayout, "fs_main", _surfaceFormat);
            _plainPipeline = CreatePipeline(_pipelineLayout, "fs_plain", _surfaceFormat);
        }

        public TextureFormat SceneFormat => _sceneFormat;

        public TextureView* SceneTextureView => _sceneTextureView;
        public TextureView* OutlineMaskView => _outlineMaskView;

        public void Resize(Vector2D<int> framebufferSize)
        {
            if (framebufferSize.X <= 0 || framebufferSize.Y <= 0)
            {
                return;
            }

            ReleaseSceneTarget();
            CreateSceneTarget(framebufferSize);
            CreateBindGroups();
        }

        public void Process(CommandEncoder* commandEncoder)
        {
            if (commandEncoder == null || _brightPipeline == null || _brightBindGroup == null)
            {
                return;
            }

            var clearColor = new Color { R = 0.0, G = 0.0, B = 0.0, A = 1.0 };

            // Pass 1: Bright Pass -> Bloom A
            {
                var colorAttachment = new RenderPassColorAttachment
                {
                    View = _bloomViewA,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearValue = clearColor
                };
                var passDescriptor = new RenderPassDescriptor
                {
                    ColorAttachmentCount = 1,
                    ColorAttachments = &colorAttachment
                };
                var pass = Api.CommandEncoderBeginRenderPass(commandEncoder, &passDescriptor);
                Api.RenderPassEncoderSetPipeline(pass, _brightPipeline);
                Api.RenderPassEncoderSetBindGroup(pass, 0, _brightBindGroup, 0, null);
                Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
                Api.RenderPassEncoderEnd(pass);
                Api.RenderPassEncoderRelease(pass);
            }

            // Pass 2: Blur H -> Bloom B
            {
                var colorAttachment = new RenderPassColorAttachment
                {
                    View = _bloomViewB,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearValue = clearColor
                };
                var passDescriptor = new RenderPassDescriptor
                {
                    ColorAttachmentCount = 1,
                    ColorAttachments = &colorAttachment
                };
                var pass = Api.CommandEncoderBeginRenderPass(commandEncoder, &passDescriptor);
                Api.RenderPassEncoderSetPipeline(pass, _blurHPipeline);
                Api.RenderPassEncoderSetBindGroup(pass, 0, _blurHBindGroup, 0, null);
                Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
                Api.RenderPassEncoderEnd(pass);
                Api.RenderPassEncoderRelease(pass);
            }

            // Pass 3: Blur V -> Bloom A
            {
                var colorAttachment = new RenderPassColorAttachment
                {
                    View = _bloomViewA,
                    LoadOp = LoadOp.Clear,
                    StoreOp = StoreOp.Store,
                    ClearValue = clearColor
                };
                var passDescriptor = new RenderPassDescriptor
                {
                    ColorAttachmentCount = 1,
                    ColorAttachments = &colorAttachment
                };
                var pass = Api.CommandEncoderBeginRenderPass(commandEncoder, &passDescriptor);
                Api.RenderPassEncoderSetPipeline(pass, _blurVPipeline);
                Api.RenderPassEncoderSetBindGroup(pass, 0, _blurVBindGroup, 0, null);
                Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
                Api.RenderPassEncoderEnd(pass);
                Api.RenderPassEncoderRelease(pass);
            }
        }

        /// <summary>The tone-mapped scene, bloom and all, into <paramref name="target"/> (the window's format): SMAA's input.</summary>
        public void EncodeTonemap(CommandEncoder* encoder, TextureView* target)
        {
            if (_combineBindGroup == null)
            {
                return;
            }
            if (_tonemapPipeline == null)
            {
                _tonemapPipeline = CreatePipeline(_pipelineLayout, "fs_tonemap", _surfaceFormat);
            }
            var colorAttachment = new RenderPassColorAttachment
            {
                View = target,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color { R = 0.0, G = 0.0, B = 0.0, A = 1.0 }
            };
            var passDescriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &colorAttachment };
            var pass = Api.CommandEncoderBeginRenderPass(encoder, &passDescriptor);
            Api.RenderPassEncoderSetPipeline(pass, _tonemapPipeline);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _combineBindGroup, 0, null);
            Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
            Api.RenderPassEncoderEnd(pass);
            Api.RenderPassEncoderRelease(pass);
        }

        /// <param name="fxaa">FXAA on the way out; off when anti-aliasing is off or done elsewhere.</param>
        public void Draw(RenderPassEncoder* renderPass, bool fxaa = true)
        {
            if (renderPass == null || _combinePipeline == null || _plainPipeline == null || _combineBindGroup == null)
            {
                return;
            }

            if (fxaa)
            {
                Api.RenderPassEncoderSetPipeline(renderPass, _combinePipeline);
            }
            else
            {
                Api.RenderPassEncoderSetPipeline(renderPass, _plainPipeline);
            }
            Api.RenderPassEncoderSetBindGroup(renderPass, 0, _combineBindGroup, 0, null);
            Api.RenderPassEncoderDraw(renderPass, 3, 1, 0, 0);
        }

        public override void Dispose()
        {
            ReleaseSceneTarget();

            if (_combinePipeline != null) Api.RenderPipelineRelease(_combinePipeline);
            if (_plainPipeline != null) Api.RenderPipelineRelease(_plainPipeline);
            if (_tonemapPipeline != null) Api.RenderPipelineRelease(_tonemapPipeline);
            if (_blurVPipeline != null) Api.RenderPipelineRelease(_blurVPipeline);
            if (_blurHPipeline != null) Api.RenderPipelineRelease(_blurHPipeline);
            if (_brightPipeline != null) Api.RenderPipelineRelease(_brightPipeline);

            if (_pipelineLayout != null) Api.PipelineLayoutRelease(_pipelineLayout);
            if (_bindGroupLayout != null) Api.BindGroupLayoutRelease(_bindGroupLayout);

            if (_sampler != null) Api.SamplerRelease(_sampler);
            if (_shaderModule != null) Api.ShaderModuleRelease(_shaderModule);
        }

        private void CreateSceneTarget(Vector2D<int> framebufferSize)
        {
            // Full res scene texture
            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D
                {
                    Width = (uint)framebufferSize.X,
                    Height = (uint)framebufferSize.Y,
                    DepthOrArrayLayers = 1
                },
                Format = _sceneFormat,
                MipLevelCount = 1,
                SampleCount = 1
            };

            _sceneTexture = Api.DeviceCreateTexture(Device, &textureDescriptor);
            if (_sceneTexture == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU HDR scene texture.");
            }

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = _sceneFormat,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            _sceneTextureView = Api.TextureCreateView(_sceneTexture, &viewDescriptor);
            if (_sceneTextureView == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU HDR scene texture view.");
            }

            var outlineMaskDescriptor = textureDescriptor;
            outlineMaskDescriptor.Format = TextureFormat.Rgba8Unorm;
            _outlineMaskTexture = Api.DeviceCreateTexture(Device, &outlineMaskDescriptor);
            if (_outlineMaskTexture == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU outline mask texture.");
            }

            var outlineMaskViewDescriptor = viewDescriptor;
            outlineMaskViewDescriptor.Format = TextureFormat.Rgba8Unorm;
            _outlineMaskView = Api.TextureCreateView(_outlineMaskTexture, &outlineMaskViewDescriptor);
            if (_outlineMaskView == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU outline mask texture view.");
            }

            // 1/4 res bloom textures
            var bloomWidth = (uint)Math.Max(1, framebufferSize.X / 4);
            var bloomHeight = (uint)Math.Max(1, framebufferSize.Y / 4);

            var bloomDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D
                {
                    Width = bloomWidth,
                    Height = bloomHeight,
                    DepthOrArrayLayers = 1
                },
                Format = _sceneFormat,
                MipLevelCount = 1,
                SampleCount = 1
            };

            _bloomTextureA = Api.DeviceCreateTexture(Device, &bloomDescriptor);
            if (_bloomTextureA == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU bloom texture A.");
            }

            _bloomTextureB = Api.DeviceCreateTexture(Device, &bloomDescriptor);
            if (_bloomTextureB == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU bloom texture B.");
            }

            _bloomViewA = Api.TextureCreateView(_bloomTextureA, &viewDescriptor);
            if (_bloomViewA == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU bloom view A.");
            }

            _bloomViewB = Api.TextureCreateView(_bloomTextureB, &viewDescriptor);
            if (_bloomViewB == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU bloom view B.");
            }
        }

        private void ReleaseSceneTarget()
        {
            ReleaseBindGroups();

            if (_outlineMaskView != null)
            {
                Api.TextureViewRelease(_outlineMaskView);
                _outlineMaskView = null;
            }

            if (_outlineMaskTexture != null)
            {
                Api.TextureRelease(_outlineMaskTexture);
                _outlineMaskTexture = null;
            }

            if (_bloomViewB != null)
            {
                Api.TextureViewRelease(_bloomViewB);
                _bloomViewB = null;
            }

            if (_bloomTextureB != null)
            {
                Api.TextureRelease(_bloomTextureB);
                _bloomTextureB = null;
            }

            if (_bloomViewA != null)
            {
                Api.TextureViewRelease(_bloomViewA);
                _bloomViewA = null;
            }

            if (_bloomTextureA != null)
            {
                Api.TextureRelease(_bloomTextureA);
                _bloomTextureA = null;
            }

            if (_sceneTextureView != null)
            {
                Api.TextureViewRelease(_sceneTextureView);
                _sceneTextureView = null;
            }

            if (_sceneTexture != null)
            {
                Api.TextureRelease(_sceneTexture);
                _sceneTexture = null;
            }
        }

        private Sampler* CreateSampler()
        {
            var descriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Nearest,
                LodMinClamp = 0.0f,
                LodMaxClamp = 1.0f,
                Compare = CompareFunction.Undefined,
                MaxAnisotropy = 1
            };

            var sampler = Api.DeviceCreateSampler(Device, &descriptor);
            if (sampler == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU tone-map sampler.");
            }

            return sampler;
        }

        private BindGroupLayout* CreateBindGroupLayout()
        {
            var samplerEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout
                {
                    Type = SamplerBindingType.Filtering
                }
            };

            var textureEntry1 = new BindGroupLayoutEntry
            {
                Binding = 1,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2D,
                    Multisampled = false
                }
            };

            var textureEntry2 = new BindGroupLayoutEntry
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

            var outlineMaskEntry = textureEntry2;
            outlineMaskEntry.Binding = 3;

            var entries = stackalloc BindGroupLayoutEntry[4];
            entries[0] = samplerEntry;
            entries[1] = textureEntry1;
            entries[2] = textureEntry2;
            entries[3] = outlineMaskEntry;

            var descriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 4,
                Entries = entries
            };

            var layout = Api.DeviceCreateBindGroupLayout(Device, &descriptor);
            if (layout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU tone-map bind group layout.");
            }

            return layout;
        }

        private PipelineLayout* CreatePipelineLayout()
        {
            var bindGroupLayout = _bindGroupLayout;
            var descriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &bindGroupLayout
            };

            var layout = Api.DeviceCreatePipelineLayout(Device, &descriptor);
            if (layout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU tone-map pipeline layout.");
            }

            return layout;
        }

        private ShaderModule* CreateShaderModule(string shaderPath)
        {
            var source = ManaWgslPreprocessor.LoadResolved(shaderPath);
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

                var shaderModule = Api.DeviceCreateShaderModule(Device, &shaderDescriptor);
                if (shaderModule == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU tone-map shader module from {shaderPath}.");
                }

                return shaderModule;
            }
            finally
            {
                Marshal.FreeCoTaskMem(sourcePointer);
            }
        }

        private RenderPipeline* CreatePipeline(
            PipelineLayout* layout,
            string entryPoint,
            TextureFormat outputFormat)
        {
            nint vertexEntryPoint = Marshal.StringToCoTaskMemUTF8("vs_main");
            nint fragmentEntryPoint = Marshal.StringToCoTaskMemUTF8(entryPoint);
            try
            {
                var vertexState = new VertexState
                {
                    Module = _shaderModule,
                    EntryPoint = (byte*)vertexEntryPoint,
                    BufferCount = 0,
                    Buffers = null
                };

                var blendState = new BlendState
                {
                    Color = new BlendComponent
                    {
                        Operation = BlendOperation.Add,
                        SrcFactor = BlendFactor.One,
                        DstFactor = BlendFactor.Zero
                    },
                    Alpha = new BlendComponent
                    {
                        Operation = BlendOperation.Add,
                        SrcFactor = BlendFactor.One,
                        DstFactor = BlendFactor.Zero
                    }
                };

                var colorTarget = new ColorTargetState
                {
                    Format = outputFormat,
                    Blend = &blendState,
                    WriteMask = ColorWriteMask.All
                };

                var fragmentState = new FragmentState
                {
                    Module = _shaderModule,
                    EntryPoint = (byte*)fragmentEntryPoint,
                    TargetCount = 1,
                    Targets = &colorTarget
                };

                var descriptor = new RenderPipelineDescriptor
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
                    DepthStencil = null,
                    Fragment = &fragmentState
                };

                var pipeline = Api.DeviceCreateRenderPipeline(Device, &descriptor);
                if (pipeline == null)
                {
                    throw new InvalidOperationException($"Failed to create WebGPU pipeline for '{entryPoint}'.");
                }

                return pipeline;
            }
            finally
            {
                Marshal.FreeCoTaskMem(vertexEntryPoint);
                Marshal.FreeCoTaskMem(fragmentEntryPoint);
            }
        }

        private void CreateBindGroups()
        {
            if (_sceneTextureView == null || _bloomViewA == null || _bloomViewB == null || _outlineMaskView == null)
            {
                return;
            }

            ReleaseBindGroups();

            // 1. Bright Bind Group (samples _sceneTextureView)
            {
                var entries = stackalloc BindGroupEntry[4];
                entries[0] = new BindGroupEntry { Binding = 0, Sampler = _sampler };
                entries[1] = new BindGroupEntry { Binding = 1, TextureView = _sceneTextureView };
                entries[2] = new BindGroupEntry { Binding = 2, TextureView = _sceneTextureView }; // dummy
                entries[3] = new BindGroupEntry { Binding = 3, TextureView = _outlineMaskView };
                var desc = new BindGroupDescriptor { Layout = _bindGroupLayout, EntryCount = 4, Entries = entries };
                _brightBindGroup = CreateLabeledBindGroup("HDR bloom extraction", &desc);
            }

            // 2. Blur H Bind Group (samples _bloomViewA)
            {
                var entries = stackalloc BindGroupEntry[4];
                entries[0] = new BindGroupEntry { Binding = 0, Sampler = _sampler };
                entries[1] = new BindGroupEntry { Binding = 1, TextureView = _bloomViewA };
                entries[2] = new BindGroupEntry { Binding = 2, TextureView = _bloomViewA }; // dummy
                entries[3] = new BindGroupEntry { Binding = 3, TextureView = _outlineMaskView };
                var desc = new BindGroupDescriptor { Layout = _bindGroupLayout, EntryCount = 4, Entries = entries };
                _blurHBindGroup = CreateLabeledBindGroup("HDR bloom horizontal blur", &desc);
            }

            // 3. Blur V Bind Group (samples _bloomViewB)
            {
                var entries = stackalloc BindGroupEntry[4];
                entries[0] = new BindGroupEntry { Binding = 0, Sampler = _sampler };
                entries[1] = new BindGroupEntry { Binding = 1, TextureView = _bloomViewB };
                entries[2] = new BindGroupEntry { Binding = 2, TextureView = _bloomViewB }; // dummy
                entries[3] = new BindGroupEntry { Binding = 3, TextureView = _outlineMaskView };
                var desc = new BindGroupDescriptor { Layout = _bindGroupLayout, EntryCount = 4, Entries = entries };
                _blurVBindGroup = CreateLabeledBindGroup("HDR bloom vertical blur", &desc);
            }

            // 4. Combine Bind Group (samples _sceneTextureView and _bloomViewA)
            {
                var entries = stackalloc BindGroupEntry[4];
                entries[0] = new BindGroupEntry { Binding = 0, Sampler = _sampler };
                entries[1] = new BindGroupEntry { Binding = 1, TextureView = _sceneTextureView };
                entries[2] = new BindGroupEntry { Binding = 2, TextureView = _bloomViewA };
                entries[3] = new BindGroupEntry { Binding = 3, TextureView = _outlineMaskView };
                var desc = new BindGroupDescriptor { Layout = _bindGroupLayout, EntryCount = 4, Entries = entries };
                _combineBindGroup = CreateLabeledBindGroup("HDR tone map and FXAA", &desc);
            }
        }

        private BindGroup* CreateLabeledBindGroup(string name, BindGroupDescriptor* descriptor)
        {
            var label = Marshal.StringToCoTaskMemUTF8(name);
            try
            {
                descriptor->Label = (byte*)label;
                return Api.DeviceCreateBindGroup(Device, descriptor);
            }
            finally
            {
                Marshal.FreeCoTaskMem(label);
            }
        }

        private void ReleaseBindGroups()
        {
            if (_combineBindGroup != null)
            {
                Api.BindGroupRelease(_combineBindGroup);
                _combineBindGroup = null;
            }

            if (_blurVBindGroup != null)
            {
                Api.BindGroupRelease(_blurVBindGroup);
                _blurVBindGroup = null;
            }

            if (_blurHBindGroup != null)
            {
                Api.BindGroupRelease(_blurHBindGroup);
                _blurHBindGroup = null;
            }

            if (_brightBindGroup != null)
            {
                Api.BindGroupRelease(_brightBindGroup);
                _brightBindGroup = null;
            }
        }
    }
}

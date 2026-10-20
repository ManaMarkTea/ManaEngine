using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Core.Native;

namespace ManaGraphics.Code.Graphics.Renderers
{
    /// <summary>
    /// A blurred snapshot of the frame for frosted UI panels: copied out between the scene and the UI, then halved down a
    /// mip chain, so a blur radius picks a level. Repeated halving approximates a gaussian at a fraction of its cost.
    /// </summary>
    internal sealed unsafe class ManaWebGpuUiBackdrop : ManaWebGpuDeviceObject
    {
        /// <summary>Levels of blur: six halvings take 4K to about 60 pixels, past where more blur reads as anything but grey.</summary>
        private const uint MaxLevels = 6;

        private readonly TextureFormat _format;

        private Texture* _texture;
        private TextureView* _view;
        private TextureView*[] _levelViews = new TextureView*[0];

        private BindGroupLayout* _bindGroupLayout;
        private PipelineLayout* _pipelineLayout;
        private RenderPipeline* _pipeline;
        private ShaderModule* _shader;
        private Sampler* _sampler;

        private BindGroup*[] _bindGroups = new BindGroup*[0];

        private Vector2D<int> _size;
        private uint _levels;

        public ManaWebGpuUiBackdrop(ManaWebGpuDevice gpu, TextureFormat format)
            : base(gpu)
        {
            _format = format;
        }

        /// <summary>The blurred chain, or null before anything has been captured.</summary>
        public TextureView* View => _view;

        /// <summary>Bumped when the texture is remade: a new view can reuse the freed one's address.</summary>
        public int Generation { get; private set; }

        /// <summary>Copies the frame out (hence the surface's CopySrc) and reduces it.</summary>
        public void Capture(CommandEncoder* encoder, Texture* frame, Vector2D<int> size)
        {
            if (size.X <= 0 || size.Y <= 0)
            {
                return;
            }

            EnsureResources(size);

            if (_texture == null)
            {
                return;
            }

            var source = new ImageCopyTexture
            {
                Texture = frame,
                MipLevel = 0,
                Origin = new Origin3D(0, 0, 0),
                Aspect = TextureAspect.All
            };

            var destination = new ImageCopyTexture
            {
                Texture = _texture,
                MipLevel = 0,
                Origin = new Origin3D(0, 0, 0),
                Aspect = TextureAspect.All
            };

            var extent = new Extent3D((uint)size.X, (uint)size.Y, 1);
            Api.CommandEncoderCopyTextureToTexture(encoder, &source, &destination, &extent);

            // Each level is drawn from the one above it, so the blur compounds down the chain.
            for (var level = 1u; level < _levels; level++)
            {
                Reduce(encoder, level);
            }
        }

        private void Reduce(CommandEncoder* encoder, uint level)
        {
            var attachment = new RenderPassColorAttachment
            {
                View = _levelViews[level],
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color(0.0, 0.0, 0.0, 1.0)
            };

            var descriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &attachment
            };

            var pass = Api.CommandEncoderBeginRenderPass(encoder, &descriptor);
            Api.RenderPassEncoderSetPipeline(pass, _pipeline);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroups[level], 0, null);
            Api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
            Api.RenderPassEncoderEnd(pass);
            Api.RenderPassEncoderRelease(pass);
        }

        private void EnsureResources(Vector2D<int> size)
        {
            if (_texture != null && _size == size)
            {
                return;
            }

            ReleaseTextures();
            _size = size;

            // Stops at 1x1 or the cap, whichever comes first.
            _levels = 1;
            var width = (uint)size.X;
            var height = (uint)size.Y;

            while (_levels < MaxLevels && width > 1 && height > 1)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
                _levels++;
            }

            var descriptor = new TextureDescriptor
            {
                Size = new Extent3D((uint)size.X, (uint)size.Y, 1),
                MipLevelCount = _levels,
                SampleCount = 1,
                Dimension = TextureDimension.Dimension2D,
                Format = _format,

                // CopyDst for the snapshot, RenderAttachment to reduce into, TextureBinding to sample from.
                Usage = TextureUsage.CopyDst | TextureUsage.RenderAttachment | TextureUsage.TextureBinding
            };

            _texture = Api.DeviceCreateTexture(Device, &descriptor);

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = _format,
                Dimension = TextureViewDimension.Dimension2D,
                BaseMipLevel = 0,
                MipLevelCount = _levels,
                BaseArrayLayer = 0,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            _view = Api.TextureCreateView(_texture, &viewDescriptor);
            Generation++;

            EnsurePipeline();

            _levelViews = new TextureView*[_levels];
            _bindGroups = new BindGroup*[_levels];

            for (var level = 0u; level < _levels; level++)
            {
                var single = new TextureViewDescriptor
                {
                    Format = _format,
                    Dimension = TextureViewDimension.Dimension2D,
                    BaseMipLevel = level,
                    MipLevelCount = 1,
                    BaseArrayLayer = 0,
                    ArrayLayerCount = 1,
                    Aspect = TextureAspect.All
                };

                _levelViews[level] = Api.TextureCreateView(_texture, &single);
            }

            // Each level reads the one above it, so the bind group for level N samples level N-1.
            var entries = stackalloc BindGroupEntry[2];
            for (var level = 1u; level < _levels; level++)
            {
                entries[0] = new BindGroupEntry { Binding = 0, Sampler = _sampler };
                entries[1] = new BindGroupEntry { Binding = 1, TextureView = _levelViews[level - 1] };

                var bindGroup = new BindGroupDescriptor
                {
                    Layout = _bindGroupLayout,
                    EntryCount = 2,
                    Entries = entries
                };

                _bindGroups[level] = Api.DeviceCreateBindGroup(Device, &bindGroup);
            }
        }

        private void EnsurePipeline()
        {
            if (_pipeline != null)
            {
                return;
            }

            var samplerDescriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Linear,
                LodMinClamp = 0.0f,
                LodMaxClamp = 32.0f,
                MaxAnisotropy = 1
            };

            _sampler = Api.DeviceCreateSampler(Device, &samplerDescriptor);

            var entries = stackalloc BindGroupLayoutEntry[2];
            entries[0] = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering }
            };

            entries[1] = new BindGroupLayoutEntry
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

            var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 2, Entries = entries };
            _bindGroupLayout = Api.DeviceCreateBindGroupLayout(Device, &layoutDescriptor);

            var layout = _bindGroupLayout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &layout
            };

            _pipelineLayout = Api.DeviceCreatePipelineLayout(Device, &pipelineLayoutDescriptor);
            _shader = CreateShaderModule();

            var blend = new BlendState
            {
                Color = new BlendComponent
                {
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.Zero,
                    Operation = BlendOperation.Add
                },
                Alpha = new BlendComponent
                {
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.Zero,
                    Operation = BlendOperation.Add
                }
            };

            var target = new ColorTargetState
            {
                Format = _format,
                Blend = &blend,
                WriteMask = ColorWriteMask.All
            };

            var entryPointVertex = (byte*)SilkMarshal.StringToPtr("vs_main");
            var entryPointFragment = (byte*)SilkMarshal.StringToPtr("fs_main");

            var fragment = new FragmentState
            {
                Module = _shader,
                EntryPoint = entryPointFragment,
                TargetCount = 1,
                Targets = &target
            };

            var pipelineDescriptor = new RenderPipelineDescriptor
            {
                Layout = _pipelineLayout,
                Vertex = new VertexState
                {
                    Module = _shader,
                    EntryPoint = entryPointVertex,
                    BufferCount = 0
                },
                Primitive = new PrimitiveState
                {
                    Topology = PrimitiveTopology.TriangleList,
                    CullMode = CullMode.None
                },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue },
                Fragment = &fragment
            };

            _pipeline = Api.DeviceCreateRenderPipeline(Device, &pipelineDescriptor);

            SilkMarshal.Free((nint)entryPointVertex);
            SilkMarshal.Free((nint)entryPointFragment);
        }

        private ShaderModule* CreateShaderModule()
        {
            var sourcePointer = Marshal.StringToCoTaskMemUTF8(ShaderSource);

            try
            {
                var wgslDescriptor = new ShaderModuleWGSLDescriptor
                {
                    Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                    Code = (byte*)sourcePointer
                };

                var descriptor = new ShaderModuleDescriptor { NextInChain = &wgslDescriptor.Chain };
                var module = Api.DeviceCreateShaderModule(Device, &descriptor);

                if (module == null)
                {
                    throw new InvalidOperationException("Failed to create the ManaUI backdrop shader module.");
                }

                return module;
            }
            finally
            {
                Marshal.FreeCoTaskMem(sourcePointer);
            }
        }

        /// <summary>A full-screen triangle averaging four bilinear taps at pixel corners: sixteen texels for four samples.</summary>
        private const string ShaderSource = @"
@group(0) @binding(0) var source_sampler : sampler;
@group(0) @binding(1) var source : texture_2d<f32>;

struct VertexOutput {
    @builtin(position) position : vec4<f32>,
    @location(0) uv : vec2<f32>,
};

@vertex
fn vs_main(@builtin(vertex_index) index : u32) -> VertexOutput {
    // One oversized triangle rather than two triangles: no seam down the diagonal, one fewer vertex.
    var output : VertexOutput;
    let uv = vec2<f32>(f32((index << 1u) & 2u), f32(index & 2u));
    output.uv = uv;
    output.position = vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);
    output.position.y = -output.position.y;
    return output;
}

@fragment
fn fs_main(input : VertexOutput) -> @location(0) vec4<f32> {
    let texel = 1.0 / vec2<f32>(textureDimensions(source, 0));
    let offset = texel * 0.5;

    var total = textureSample(source, source_sampler, input.uv + vec2<f32>(-offset.x, -offset.y));
    total = total + textureSample(source, source_sampler, input.uv + vec2<f32>(offset.x, -offset.y));
    total = total + textureSample(source, source_sampler, input.uv + vec2<f32>(-offset.x, offset.y));
    total = total + textureSample(source, source_sampler, input.uv + vec2<f32>(offset.x, offset.y));

    return total * 0.25;
}
";

        private void ReleaseTextures()
        {
            foreach (var group in _bindGroups)
            {
                if (group != null)
                {
                    Api.BindGroupRelease(group);
                }
            }

            _bindGroups = new BindGroup*[0];

            foreach (var view in _levelViews)
            {
                if (view != null)
                {
                    Api.TextureViewRelease(view);
                }
            }

            _levelViews = new TextureView*[0];

            if (_view != null)
            {
                Api.TextureViewRelease(_view);
                _view = null;
            }

            if (_texture != null)
            {
                Api.TextureDestroy(_texture);
                Api.TextureRelease(_texture);
                _texture = null;
            }
        }

        public override void Dispose()
        {
            ReleaseTextures();

            if (_pipeline != null)
            {
                Api.RenderPipelineRelease(_pipeline);
                _pipeline = null;
            }

            if (_pipelineLayout != null)
            {
                Api.PipelineLayoutRelease(_pipelineLayout);
                _pipelineLayout = null;
            }

            if (_bindGroupLayout != null)
            {
                Api.BindGroupLayoutRelease(_bindGroupLayout);
                _bindGroupLayout = null;
            }

            if (_shader != null)
            {
                Api.ShaderModuleRelease(_shader);
                _shader = null;
            }

            if (_sampler != null)
            {
                Api.SamplerRelease(_sampler);
                _sampler = null;
            }
        }
    }
}

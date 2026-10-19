using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.Core.Native;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// A blurred snapshot of the frame, for UI panels that want the scene softened behind them.
    /// <para>
    /// A fragment cannot read the target it is writing, so the blur cannot happen inside the UI pass. The
    /// frame is copied out after the scene has drawn and before ManaUI does, then reduced down a
    /// mip chain; each level is blurrier than the last, so a panel's blur radius picks a level rather than
    /// paying for a filter of its own.
    /// </para>
    /// <para>
    /// A mip chain rather than a separable gaussian because the cost is what matters here: this runs every
    /// frame a blurred panel is on screen. Successive halving is a box blur repeated, which converges on a
    /// gaussian quickly enough that nobody looking at a frosted panel can tell, and each level costs a
    /// quarter of the one before it.
    /// </para>
    /// </summary>
    public sealed unsafe class ManaWebGpuUiBackdrop : IDisposable
    {
        /// <summary>
        /// Levels of blur. Six halvings take a 4K frame down to about 60 pixels across, which is far past
        /// the point where more blur reads as anything but grey.
        /// </summary>
        private const uint MaxLevels = 6;

        private readonly Silk.NET.WebGPU.WebGPU _api;
        private readonly Device* _device;
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

        public ManaWebGpuUiBackdrop(Silk.NET.WebGPU.WebGPU api, Device* device, TextureFormat format)
        {
            _api = api;
            _device = device;
            _format = format;
        }

        /// <summary>The blurred chain, or null before anything has been captured.</summary>
        public TextureView* View => _view;

        /// <summary>
        /// Bumped each time the texture is made anew (the window's size changed). A replacement view can come
        /// back at the very address of the one just freed, so a pointer comparison cannot tell them apart.
        /// </summary>
        public int Generation { get; private set; }

        /// <summary>
        /// Copies the frame out and reduces it. The copy is the reason the surface is configured with
        /// CopySrc; without that the frame would have to be rendered twice.
        /// </summary>
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
            _api.CommandEncoderCopyTextureToTexture(encoder, &source, &destination, &extent);

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

            var pass = _api.CommandEncoderBeginRenderPass(encoder, &descriptor);
            _api.RenderPassEncoderSetPipeline(pass, _pipeline);
            _api.RenderPassEncoderSetBindGroup(pass, 0, _bindGroups[level], 0, null);
            _api.RenderPassEncoderDraw(pass, 3, 1, 0, 0);
            _api.RenderPassEncoderEnd(pass);
            _api.RenderPassEncoderRelease(pass);
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

            _texture = _api.DeviceCreateTexture(_device, &descriptor);

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

            _view = _api.TextureCreateView(_texture, &viewDescriptor);
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

                _levelViews[level] = _api.TextureCreateView(_texture, &single);
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

                _bindGroups[level] = _api.DeviceCreateBindGroup(_device, &bindGroup);
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

            _sampler = _api.DeviceCreateSampler(_device, &samplerDescriptor);

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
            _bindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &layoutDescriptor);

            var layout = _bindGroupLayout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &layout
            };

            _pipelineLayout = _api.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);
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

            _pipeline = _api.DeviceCreateRenderPipeline(_device, &pipelineDescriptor);

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
                var module = _api.DeviceCreateShaderModule(_device, &descriptor);

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

        /// <summary>
        /// A full-screen triangle that averages four taps of the level above. The taps sit at pixel corners
        /// so the bilinear filter folds four texels into each, giving a sixteen-texel average for four
        /// samples -- the standard trick, and the reason this is cheap enough to run every frame.
        /// </summary>
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
                    _api.BindGroupRelease(group);
                }
            }

            _bindGroups = new BindGroup*[0];

            foreach (var view in _levelViews)
            {
                if (view != null)
                {
                    _api.TextureViewRelease(view);
                }
            }

            _levelViews = new TextureView*[0];

            if (_view != null)
            {
                _api.TextureViewRelease(_view);
                _view = null;
            }

            if (_texture != null)
            {
                _api.TextureDestroy(_texture);
                _api.TextureRelease(_texture);
                _texture = null;
            }
        }

        public void Dispose()
        {
            ReleaseTextures();

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

            if (_shader != null)
            {
                _api.ShaderModuleRelease(_shader);
                _shader = null;
            }

            if (_sampler != null)
            {
                _api.SamplerRelease(_sampler);
                _sampler = null;
            }
        }
    }
}

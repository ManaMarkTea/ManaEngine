using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using ManaGraphics.Code.Diagnostics;
using ManaUI;
using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Renderers
{
    /// <summary>
    /// Draws a <see cref="ManaUiDrawList"/> as one instanced quad draw; clipping happens in the fragment shader, so
    /// nothing splits the batch.
    /// </summary>
    internal unsafe sealed class ManaWebGpuUiRenderer : ManaWebGpuDeviceObject
    {
        // Must match sizeof(ManaUiInstance). Checked at start-up rather than trusted, because a mismatch
        // reads every instance at the wrong offset and produces a frame of noise rather than an error.
        private const int InstanceStride = 92;


        private readonly ShaderModule* _shaderModule;
        private readonly BindGroupLayout* _bindGroupLayout;
        private readonly PipelineLayout* _pipelineLayout;
        private readonly RenderPipeline* _pipeline;
        private readonly Sampler* _sampler;
        private readonly Buffer* _uniformBuffer;

        private Texture* _glyphTexture;
        private TextureView* _glyphTextureView;
        private int _imageAtlasWidth;
        private int _imageAtlasHeight;
        private long _imageRevision = -1;
        private Texture* _imageTexture;
        private TextureView* _imageTextureView;
        private BindGroup* _bindGroup;

        private Buffer* _instanceBuffer;
        private ulong _instanceBufferSize;

        private int _glyphAtlasWidth;
        private int _glyphAtlasHeight;
        private bool _loggedFirstFrame;
        private bool _disposed;

        public ManaWebGpuUiRenderer(ManaWebGpuDevice gpu, TextureFormat surfaceFormat)
            : base(gpu)
        {

            // Next to the executable, not the working directory; the phone apps carry it in Game/.
            var shaderPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "WebGpu", "manaui.wgsl");
            if (!File.Exists(shaderPath))
            {
                shaderPath = Path.Combine(Environment.CurrentDirectory, "Assets", "Shaders", "WebGpu", "manaui.wgsl");
            }
            _shaderModule = CreateShaderModule(shaderPath);
            _sampler = CreateSampler();
            _backdropSampler = CreateBackdropSampler();
            _bindGroupLayout = CreateBindGroupLayout();
            _pipelineLayout = CreatePipelineLayout();
            _uniformBuffer = CreateUniformBuffer();
            _pipeline = CreatePipeline(surfaceFormat);

            // A 1x1 opaque white stand-in so the image binding is always valid; real images replace it.
            CreateImagePlaceholder();
        }

        /// <summary>Uploads atlas changes and draws the list.</summary>
        public void Render(
            RenderPassEncoder* renderPass,
            ManaUiDrawList drawList,
            ManaGlyphAtlas atlas,
            ManaImageAtlas? images,
            Vector2 logicalSize,
            uint framebufferWidth,
            uint framebufferHeight)
        {
            if (renderPass == null || _pipeline == null || drawList.InstanceCount == 0)
            {
                return;
            }

            if (logicalSize.X <= 0.0f || logicalSize.Y <= 0.0f)
            {
                return;
            }

            SyncGlyphAtlas(atlas);

            if (images != null)
            {
                SyncImageAtlas(images);
            }

            if (_bindGroup == null)
            {
                return;
            }

            if (!_loggedFirstFrame)
            {
                _loggedFirstFrame = true;
                ManaConsole.WriteDiagnostic(
                    $"ManaUI first frame: {drawList.InstanceCount} instances in {drawList.Batches.Count} batch(es), "
                    + $"logical={logicalSize.X}x{logicalSize.Y}, atlas={atlas.Width}x{atlas.Height}.");
            }

            UploadUniforms(logicalSize, atlas);
            UploadInstances(drawList);

            // Scissor persists within a pass; the UI clips in the shader and wants the whole target.
            if (framebufferWidth > 0 && framebufferHeight > 0)
            {
                Api.RenderPassEncoderSetScissorRect(renderPass, 0, 0, framebufferWidth, framebufferHeight);
            }

            Api.RenderPassEncoderSetPipeline(renderPass, _pipeline);
            Api.RenderPassEncoderSetBindGroup(renderPass, 0, _bindGroup, 0, null);
            Api.RenderPassEncoderSetVertexBuffer(renderPass, 0, _instanceBuffer, 0, _instanceBufferSize);

            foreach (var batch in drawList.Batches)
            {
                // Four vertices as a triangle strip; the quad corners come from the vertex index.
                Api.RenderPassEncoderDraw(
                    renderPass,
                    4,
                    (uint)batch.InstanceCount,
                    0,
                    (uint)batch.InstanceOffset);
            }
        }

        // ----- resource sync ---------------------------------------------------------------------------

        /// <summary>(Re)creates the glyph texture when the atlas resized, then uploads only its dirty rows.</summary>
        private void SyncGlyphAtlas(ManaGlyphAtlas atlas)
        {
            if (_glyphTexture == null || _glyphAtlasWidth != atlas.Width || _glyphAtlasHeight != atlas.Height)
            {
                ReleaseGlyphTexture();
                CreateGlyphTexture(atlas.Width, atlas.Height);
                CreateBindGroup();

                // A fresh texture has no contents, so the whole atlas has to go up regardless of the
                // dirty rect that was tracked against the old one.
                UploadGlyphRegion(atlas, 0, atlas.Height);
                atlas.ClearDirty();
                return;
            }

            if (!atlas.IsDirty)
            {
                return;
            }

            var (_, minY, _, maxY) = atlas.DirtyRegion;
            UploadGlyphRegion(atlas, minY, maxY);
            atlas.ClearDirty();
        }

        /// <summary>The colour atlas, uploaded like the glyph one; replaces the 1x1 placeholder on first use.</summary>
        private void SyncImageAtlas(ManaImageAtlas atlas)
        {
            if (_imageTexture == null || _imageAtlasWidth != atlas.Width || _imageAtlasHeight != atlas.Height)
            {
                ReleaseImageTexture();
                CreateImageTexture(atlas.Width, atlas.Height);
                CreateBindGroup();

                UploadImageRegion(atlas, 0, atlas.Height);
                _imageRevision = atlas.Revision;
                atlas.ClearDirty();
                return;
            }

            if (_imageRevision == atlas.Revision)
            {
                return;
            }

            // Another window may already have consumed the dirty region. Each renderer must
            // still see every change to the shared image atlas, including cached thumbnail UVs.
            var (_, minY, _, maxY) = atlas.DirtyRegion;
            var completeDirtyRegion = atlas.IsDirty && _imageRevision == atlas.DirtyBaseRevision;
            UploadImageRegion(atlas, completeDirtyRegion ? minY : 0, completeDirtyRegion ? maxY : atlas.Height);
            _imageRevision = atlas.Revision;
            atlas.ClearDirty();
        }

        /// <summary>
        /// The colour atlas as sRGB, so sampling decodes the PNG bytes; as Unorm they were encoded twice and emoji looked
        /// washed out. Linear content would need its own texture.
        /// </summary>
        private void CreateImageTexture(int width, int height)
        {
            var descriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8UnormSrgb,
                MipLevelCount = 1,
                SampleCount = 1
            };

            _imageTexture = Api.DeviceCreateTexture(Device, &descriptor);

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba8UnormSrgb,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            _imageTextureView = Api.TextureCreateView(_imageTexture, &viewDescriptor);
            _imageAtlasWidth = width;
            _imageAtlasHeight = height;
        }

        private void ReleaseImageTexture()
        {
            if (_imageTextureView != null)
            {
                Api.TextureViewRelease(_imageTextureView);
                _imageTextureView = null;
            }

            if (_imageTexture != null)
            {
                Api.TextureRelease(_imageTexture);
                _imageTexture = null;
            }

            _imageAtlasWidth = 0;
            _imageAtlasHeight = 0;
        }

        private void UploadImageRegion(ManaImageAtlas atlas, int startRow, int endRow)
        {
            startRow = Math.Clamp(startRow, 0, atlas.Height);
            endRow = Math.Clamp(endRow, startRow, atlas.Height);

            var rowCount = endRow - startRow;
            if (rowCount <= 0)
            {
                return;
            }

            var destination = new ImageCopyTexture
            {
                Texture = _imageTexture,
                MipLevel = 0,
                Origin = new Origin3D { X = 0, Y = (uint)startRow, Z = 0 },
                Aspect = TextureAspect.All
            };

            var layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = (uint)(atlas.Width * 4),
                RowsPerImage = (uint)rowCount
            };

            var extent = new Extent3D
            {
                Width = (uint)atlas.Width,
                Height = (uint)rowCount,
                DepthOrArrayLayers = 1
            };

            fixed (byte* pixels = &atlas.Pixels[startRow * atlas.Width * 4])
            {
                Api.QueueWriteTexture(
                    Queue,
                    &destination,
                    pixels,
                    (nuint)(rowCount * atlas.Width * 4),
                    &layout,
                    &extent);
            }
        }

        /// <summary>Whole rows: WriteTexture wants a fixed stride, so this avoids a staging copy.</summary>
        private void UploadGlyphRegion(ManaGlyphAtlas atlas, int startRow, int endRow)
        {
            startRow = Math.Clamp(startRow, 0, atlas.Height);
            endRow = Math.Clamp(endRow, startRow, atlas.Height);

            var rowCount = endRow - startRow;
            if (rowCount <= 0)
            {
                return;
            }

            var destination = new ImageCopyTexture
            {
                Texture = _glyphTexture,
                MipLevel = 0,
                Origin = new Origin3D { X = 0, Y = (uint)startRow, Z = 0 },
                Aspect = TextureAspect.All
            };

            var layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = (uint)atlas.Width,
                RowsPerImage = (uint)rowCount
            };

            var extent = new Extent3D
            {
                Width = (uint)atlas.Width,
                Height = (uint)rowCount,
                DepthOrArrayLayers = 1
            };

            fixed (byte* pixels = &atlas.Pixels[startRow * atlas.Width])
            {
                Api.QueueWriteTexture(
                    Queue,
                    &destination,
                    pixels,
                    (nuint)(rowCount * atlas.Width),
                    &layout,
                    &extent);
            }
        }

        private void UploadUniforms(Vector2 logicalSize, ManaGlyphAtlas atlas)
        {
            // Matches the WGSL Uniforms struct: two vec2<f32>.
            Span<float> uniforms = stackalloc float[4];
            uniforms[0] = logicalSize.X;
            uniforms[1] = logicalSize.Y;
            uniforms[2] = atlas.Width;
            uniforms[3] = atlas.Height;

            fixed (float* data = uniforms)
            {
                Api.QueueWriteBuffer(Queue, _uniformBuffer, 0, data, 16);
            }
        }

        private void UploadInstances(ManaUiDrawList drawList)
        {
            var required = (ulong)(drawList.InstanceCount * InstanceStride);

            if (_instanceBuffer == null || _instanceBufferSize < required)
            {
                if (_instanceBuffer != null)
                {
                    Api.BufferDestroy(_instanceBuffer);
                    Api.BufferRelease(_instanceBuffer);
                }

                // Grown with headroom so a UI that adds a few elements per frame is not reallocating
                // every frame.
                _instanceBufferSize = Math.Max(required + required / 2, 64UL * InstanceStride);
                _instanceBufferSize = (_instanceBufferSize + 3UL) & ~3UL;

                var descriptor = new BufferDescriptor
                {
                    Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                    Size = _instanceBufferSize
                };
                _instanceBuffer = Api.DeviceCreateBuffer(Device, &descriptor);
            }

            var instances = drawList.Instances;
            fixed (ManaUiInstance* data = instances)
            {
                Api.QueueWriteBuffer(Queue, _instanceBuffer, 0, data, (nuint)required);
            }
        }

        // ----- creation --------------------------------------------------------------------------------

        private ShaderModule* CreateShaderModule(string shaderPath)
        {
            var source = ManaWgslPreprocessor.LoadResolved(shaderPath);
            var sourcePointer = Marshal.StringToCoTaskMemUTF8(source);

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
                    throw new InvalidOperationException($"Failed to create the ManaUI shader module from {shaderPath}.");
                }

                return module;
            }
            finally
            {
                Marshal.FreeCoTaskMem(sourcePointer);
            }
        }

        private Sampler* CreateSampler()
        {
            // Linear filtering with clamped edges: glyph quads are drawn at their exact bitmap size, so
            // filtering is effectively a no-op for text while still smoothing scaled images.
            var descriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Linear,
                LodMinClamp = 0.0f,
                LodMaxClamp = 1.0f,
                Compare = CompareFunction.Undefined,
                MaxAnisotropy = 1
            };

            return Api.DeviceCreateSampler(Device, &descriptor);
        }

        private TextureView* _backdropTextureView;
        private Sampler* _backdropSampler;

        /// <summary>The backdrop's sampler: the atlas one clamps LOD to 1, and the backdrop is all mips.</summary>
        private Sampler* CreateBackdropSampler()
        {
            var descriptor = new SamplerDescriptor
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

            return Api.DeviceCreateSampler(Device, &descriptor);
        }

        private BindGroupLayout* CreateBindGroupLayout()
        {
            var entries = stackalloc BindGroupLayoutEntry[6];

            entries[0] = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 16 }
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
                    ViewDimension = TextureViewDimension.Dimension2D,
                    Multisampled = false
                }
            };
            entries[3] = new BindGroupLayoutEntry
            {
                Binding = 3,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2D,
                    Multisampled = false
                }
            };

            // Always declared, so turning blur on never rebuilds the pipeline.
            entries[4] = new BindGroupLayoutEntry
            {
                Binding = 4,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2D,
                    Multisampled = false
                }
            };

            // Its own sampler: the atlas one clamps LOD to 1, which turns a deep blur into one halving.
            entries[5] = new BindGroupLayoutEntry
            {
                Binding = 5,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering }
            };

            var descriptor = new BindGroupLayoutDescriptor { EntryCount = 6, Entries = entries };
            return Api.DeviceCreateBindGroupLayout(Device, &descriptor);
        }

        private PipelineLayout* CreatePipelineLayout()
        {
            var layouts = stackalloc BindGroupLayout*[1];
            layouts[0] = _bindGroupLayout;

            var descriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = layouts
            };

            return Api.DeviceCreatePipelineLayout(Device, &descriptor);
        }

        private Buffer* CreateUniformBuffer()
        {
            var descriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = 16
            };

            return Api.DeviceCreateBuffer(Device, &descriptor);
        }

        private RenderPipeline* CreatePipeline(TextureFormat surfaceFormat)
        {
            // One instance-stepped buffer; the quad's own corners come from @builtin(vertex_index).
            var attributes = stackalloc VertexAttribute[11];
            attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 0, ShaderLocation = 0 };
            attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 16, ShaderLocation = 1 };
            attributes[2] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 32, ShaderLocation = 2 };
            attributes[3] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 48, ShaderLocation = 3 };
            attributes[4] = new VertexAttribute { Format = VertexFormat.Uint32, Offset = 64, ShaderLocation = 4 };
            attributes[5] = new VertexAttribute { Format = VertexFormat.Uint32, Offset = 68, ShaderLocation = 5 };
            attributes[6] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 72, ShaderLocation = 6 };
            attributes[7] = new VertexAttribute { Format = VertexFormat.Uint32, Offset = 76, ShaderLocation = 7 };
            attributes[8] = new VertexAttribute { Format = VertexFormat.Uint32, Offset = 80, ShaderLocation = 8 };
            attributes[9] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 84, ShaderLocation = 9 };
            attributes[10] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 88, ShaderLocation = 10 };

            var vertexBufferLayout = new VertexBufferLayout
            {
                ArrayStride = InstanceStride,
                StepMode = VertexStepMode.Instance,
                AttributeCount = 11,
                Attributes = attributes
            };

            var vertexEntry = Marshal.StringToHGlobalAnsi("vs_main");
            var fragmentEntry = Marshal.StringToHGlobalAnsi("fs_main");

            try
            {
                // Premultiplied alpha: the shader outputs colour already scaled by coverage, which is what
                // keeps an antialiased border ring from double-blending against the fill underneath it.
                var blendState = new BlendState
                {
                    Color = new BlendComponent
                    {
                        SrcFactor = BlendFactor.One,
                        DstFactor = BlendFactor.OneMinusSrcAlpha,
                        Operation = BlendOperation.Add
                    },
                    Alpha = new BlendComponent
                    {
                        SrcFactor = BlendFactor.One,
                        DstFactor = BlendFactor.OneMinusSrcAlpha,
                        Operation = BlendOperation.Add
                    }
                };

                var colorTarget = new ColorTargetState
                {
                    Format = surfaceFormat,
                    Blend = &blendState,
                    WriteMask = ColorWriteMask.All
                };

                var fragmentState = new FragmentState
                {
                    Module = _shaderModule,
                    EntryPoint = (byte*)fragmentEntry,
                    TargetCount = 1,
                    Targets = &colorTarget
                };

                var descriptor = new RenderPipelineDescriptor
                {
                    Layout = _pipelineLayout,
                    Vertex = new VertexState
                    {
                        Module = _shaderModule,
                        EntryPoint = (byte*)vertexEntry,
                        BufferCount = 1,
                        Buffers = &vertexBufferLayout
                    },
                    Primitive = new PrimitiveState
                    {
                        Topology = PrimitiveTopology.TriangleStrip,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = CullMode.None
                    },
                    Multisample = new MultisampleState
                    {
                        Count = 1,
                        Mask = uint.MaxValue,
                        AlphaToCoverageEnabled = false
                    },
                    Fragment = &fragmentState,
                    DepthStencil = null
                };

                var pipeline = Api.DeviceCreateRenderPipeline(Device, &descriptor);
                if (pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create the ManaUI render pipeline.");
                }

                return pipeline;
            }
            finally
            {
                Marshal.FreeHGlobal(vertexEntry);
                Marshal.FreeHGlobal(fragmentEntry);
            }
        }

        private void CreateGlyphTexture(int width, int height)
        {
            var descriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
                Format = TextureFormat.R8Unorm,
                MipLevelCount = 1,
                SampleCount = 1
            };

            _glyphTexture = Api.DeviceCreateTexture(Device, &descriptor);

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.R8Unorm,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            _glyphTextureView = Api.TextureCreateView(_glyphTexture, &viewDescriptor);
            _glyphAtlasWidth = width;
            _glyphAtlasHeight = height;
        }

        /// <summary>Matches the real atlas's format, so replacing one with the other changes nothing else.</summary>
        private void CreateImagePlaceholder()
        {
            var descriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = 1, Height = 1, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8UnormSrgb,
                MipLevelCount = 1,
                SampleCount = 1
            };

            _imageTexture = Api.DeviceCreateTexture(Device, &descriptor);

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba8UnormSrgb,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            _imageTextureView = Api.TextureCreateView(_imageTexture, &viewDescriptor);

            var white = stackalloc byte[4] { 255, 255, 255, 255 };
            var destination = new ImageCopyTexture
            {
                Texture = _imageTexture,
                MipLevel = 0,
                Origin = new Origin3D(),
                Aspect = TextureAspect.All
            };
            var layout = new TextureDataLayout { Offset = 0, BytesPerRow = 4, RowsPerImage = 1 };
            var extent = new Extent3D { Width = 1, Height = 1, DepthOrArrayLayers = 1 };

            Api.QueueWriteTexture(Queue, &destination, white, 4, &layout, &extent);
        }

        /// <summary>The blurred frame under the UI, or null before one is captured.</summary>
        /// <param name="generation">A recreated texture can reuse the freed one's address; the generation keeps a stale
        /// bind group from naming the freed view.</param>
        public void SetBackdrop(TextureView* view, int generation)
        {
            if (_backdropTextureView == view && _backdropGeneration == generation)
            {
                return;
            }

            _backdropTextureView = view;
            _backdropGeneration = generation;
            CreateBindGroup();
        }

        private int _backdropGeneration = -1;

        private void CreateBindGroup()
        {
            if (_bindGroup != null)
            {
                Api.BindGroupRelease(_bindGroup);
                _bindGroup = null;
            }

            if (_glyphTextureView == null || _imageTextureView == null)
            {
                return;
            }

            var entries = stackalloc BindGroupEntry[6];
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = _uniformBuffer, Offset = 0, Size = 16 };
            entries[1] = new BindGroupEntry { Binding = 1, Sampler = _sampler };
            entries[2] = new BindGroupEntry { Binding = 2, TextureView = _glyphTextureView };
            entries[3] = new BindGroupEntry { Binding = 3, TextureView = _imageTextureView };

            // Falls back to the image atlas when no backdrop has been captured. Nothing samples it in that
            // case -- no instance carries the flag -- but the binding still has to be filled.
            entries[4] = new BindGroupEntry
            {
                Binding = 4,
                TextureView = _backdropTextureView != null ? _backdropTextureView : _imageTextureView
            };

            entries[5] = new BindGroupEntry { Binding = 5, Sampler = _backdropSampler };

            var descriptor = new BindGroupDescriptor
            {
                Layout = _bindGroupLayout,
                EntryCount = 6,
                Entries = entries
            };

            var label = Marshal.StringToCoTaskMemUTF8("ManaUI atlas and backdrop");
            try
            {
                descriptor.Label = (byte*)label;
                _bindGroup = Api.DeviceCreateBindGroup(Device, &descriptor);
            }
            finally
            {
                Marshal.FreeCoTaskMem(label);
            }
        }

        private void ReleaseGlyphTexture()
        {
            if (_glyphTextureView != null)
            {
                Api.TextureViewRelease(_glyphTextureView);
                _glyphTextureView = null;
            }

            if (_glyphTexture != null)
            {
                Api.TextureDestroy(_glyphTexture);
                Api.TextureRelease(_glyphTexture);
                _glyphTexture = null;
            }
        }

        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_bindGroup != null)
            {
                Api.BindGroupRelease(_bindGroup);
            }

            ReleaseGlyphTexture();

            if (_imageTextureView != null)
            {
                Api.TextureViewRelease(_imageTextureView);
            }

            if (_imageTexture != null)
            {
                Api.TextureDestroy(_imageTexture);
                Api.TextureRelease(_imageTexture);
            }

            if (_instanceBuffer != null)
            {
                Api.BufferDestroy(_instanceBuffer);
                Api.BufferRelease(_instanceBuffer);
            }

            if (_uniformBuffer != null)
            {
                Api.BufferDestroy(_uniformBuffer);
                Api.BufferRelease(_uniformBuffer);
            }

            if (_pipeline != null)
            {
                Api.RenderPipelineRelease(_pipeline);
            }

            if (_pipelineLayout != null)
            {
                Api.PipelineLayoutRelease(_pipelineLayout);
            }

            if (_bindGroupLayout != null)
            {
                Api.BindGroupLayoutRelease(_bindGroupLayout);
            }

            if (_sampler != null)
            {
                Api.SamplerRelease(_sampler);
            }

            if (_backdropSampler != null)
            {
                Api.SamplerRelease(_backdropSampler);
            }

            if (_shaderModule != null)
            {
                Api.ShaderModuleRelease(_shaderModule);
            }
        }
    }
}

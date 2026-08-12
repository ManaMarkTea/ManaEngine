using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using ManaGraphics.Code.Diagnostics;
using ManaUI;
using Silk.NET.WebGPU;
using Buffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Draws a <see cref="ManaUiDrawList"/> with WebGPU.
    /// <para>
    /// The whole UI is one instanced draw of a unit quad -- there is no vertex or index buffer, only a
    /// per-instance stream of <see cref="ManaUiInstance"/>. Clipping is handled in the fragment shader
    /// rather than by scissor rects, so nothing forces a batch split and a typical frame is a single
    /// <c>Draw</c> call regardless of how many panels and glyphs it contains.
    /// </para>
    /// </summary>
    internal unsafe sealed class ManaWebGpuUiRenderer : IDisposable
    {
        private const int InstanceStride = 80;

        private readonly WebGPU _api;
        private readonly Device* _device;
        private readonly Queue* _queue;

        private readonly ShaderModule* _shaderModule;
        private readonly BindGroupLayout* _bindGroupLayout;
        private readonly PipelineLayout* _pipelineLayout;
        private readonly RenderPipeline* _pipeline;
        private readonly Sampler* _sampler;
        private readonly Buffer* _uniformBuffer;

        private Texture* _glyphTexture;
        private TextureView* _glyphTextureView;
        private Texture* _imageTexture;
        private TextureView* _imageTextureView;
        private BindGroup* _bindGroup;

        private Buffer* _instanceBuffer;
        private ulong _instanceBufferSize;

        private int _glyphAtlasWidth;
        private int _glyphAtlasHeight;
        private bool _loggedFirstFrame;
        private bool _disposed;

        public ManaWebGpuUiRenderer(WebGPU api, Device* device, Queue* queue, TextureFormat surfaceFormat)
        {
            _api = api;
            _device = device;
            _queue = queue;

            // Resolved against the output directory the .wgsl is copied to, not the working directory --
            // `dotnet run` from the repo root leaves the CWD somewhere else entirely.
            _shaderModule = CreateShaderModule(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "WebGpu", "manaui.wgsl"));
            _sampler = CreateSampler();
            _bindGroupLayout = CreateBindGroupLayout();
            _pipelineLayout = CreatePipelineLayout();
            _uniformBuffer = CreateUniformBuffer();
            _pipeline = CreatePipeline(surfaceFormat);

            // A 1x1 opaque white stand-in so the image binding is always valid; real images replace it.
            CreateImagePlaceholder();
        }

        /// <summary>
        /// Uploads any atlas changes and draws the list. Safe to call with an empty list.
        /// </summary>
        public void Render(
            RenderPassEncoder* renderPass,
            ManaUiDrawList drawList,
            ManaGlyphAtlas atlas,
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

            // Scissor state persists across draws within a render pass, and ImGui -- which draws earlier in
            // this same pass -- sets one per command. Without resetting it here, the whole UI ends up
            // clipped to whatever ImGui's last widget happened to be. ManaUI does its own clipping in the
            // fragment shader, so full-target scissor is what it wants.
            if (framebufferWidth > 0 && framebufferHeight > 0)
            {
                _api.RenderPassEncoderSetScissorRect(renderPass, 0, 0, framebufferWidth, framebufferHeight);
            }

            _api.RenderPassEncoderSetPipeline(renderPass, _pipeline);
            _api.RenderPassEncoderSetBindGroup(renderPass, 0, _bindGroup, 0, null);
            _api.RenderPassEncoderSetVertexBuffer(renderPass, 0, _instanceBuffer, 0, _instanceBufferSize);

            foreach (var batch in drawList.Batches)
            {
                // Four vertices as a triangle strip; the quad corners come from the vertex index.
                _api.RenderPassEncoderDraw(
                    renderPass,
                    4,
                    (uint)batch.InstanceCount,
                    0,
                    (uint)batch.InstanceOffset);
            }
        }

        // ----- resource sync ---------------------------------------------------------------------------

        /// <summary>
        /// (Re)creates the glyph texture if the atlas was resized or reset, then uploads its dirty region.
        /// Only the changed rows are copied, so adding one glyph does not re-upload a megabyte.
        /// </summary>
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

        /// <summary>
        /// Uploads whole rows rather than a tight rectangle: WriteTexture wants a contiguous source with a
        /// fixed row stride, and the atlas is row-major, so full rows avoid a staging copy entirely.
        /// </summary>
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
                _api.QueueWriteTexture(
                    _queue,
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
                _api.QueueWriteBuffer(_queue, _uniformBuffer, 0, data, 16);
            }
        }

        private void UploadInstances(ManaUiDrawList drawList)
        {
            var required = (ulong)(drawList.InstanceCount * InstanceStride);

            if (_instanceBuffer == null || _instanceBufferSize < required)
            {
                if (_instanceBuffer != null)
                {
                    _api.BufferDestroy(_instanceBuffer);
                    _api.BufferRelease(_instanceBuffer);
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
                _instanceBuffer = _api.DeviceCreateBuffer(_device, &descriptor);
            }

            var instances = drawList.Instances;
            fixed (ManaUiInstance* data = instances)
            {
                _api.QueueWriteBuffer(_queue, _instanceBuffer, 0, data, (nuint)required);
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
                var module = _api.DeviceCreateShaderModule(_device, &descriptor);

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

            return _api.DeviceCreateSampler(_device, &descriptor);
        }

        private BindGroupLayout* CreateBindGroupLayout()
        {
            var entries = stackalloc BindGroupLayoutEntry[4];

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

            var descriptor = new BindGroupLayoutDescriptor { EntryCount = 4, Entries = entries };
            return _api.DeviceCreateBindGroupLayout(_device, &descriptor);
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

            return _api.DeviceCreatePipelineLayout(_device, &descriptor);
        }

        private Buffer* CreateUniformBuffer()
        {
            var descriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = 16
            };

            return _api.DeviceCreateBuffer(_device, &descriptor);
        }

        private RenderPipeline* CreatePipeline(TextureFormat surfaceFormat)
        {
            // One instance-stepped buffer; the quad's own corners come from @builtin(vertex_index).
            var attributes = stackalloc VertexAttribute[8];
            attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 0, ShaderLocation = 0 };
            attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 16, ShaderLocation = 1 };
            attributes[2] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 32, ShaderLocation = 2 };
            attributes[3] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 48, ShaderLocation = 3 };
            attributes[4] = new VertexAttribute { Format = VertexFormat.Uint32, Offset = 64, ShaderLocation = 4 };
            attributes[5] = new VertexAttribute { Format = VertexFormat.Uint32, Offset = 68, ShaderLocation = 5 };
            attributes[6] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 72, ShaderLocation = 6 };
            attributes[7] = new VertexAttribute { Format = VertexFormat.Uint32, Offset = 76, ShaderLocation = 7 };

            var vertexBufferLayout = new VertexBufferLayout
            {
                ArrayStride = InstanceStride,
                StepMode = VertexStepMode.Instance,
                AttributeCount = 8,
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

                var pipeline = _api.DeviceCreateRenderPipeline(_device, &descriptor);
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

            _glyphTexture = _api.DeviceCreateTexture(_device, &descriptor);

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.R8Unorm,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            _glyphTextureView = _api.TextureCreateView(_glyphTexture, &viewDescriptor);
            _glyphAtlasWidth = width;
            _glyphAtlasHeight = height;
        }

        private void CreateImagePlaceholder()
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

            _imageTexture = _api.DeviceCreateTexture(_device, &descriptor);

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba8Unorm,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            _imageTextureView = _api.TextureCreateView(_imageTexture, &viewDescriptor);

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

            _api.QueueWriteTexture(_queue, &destination, white, 4, &layout, &extent);
        }

        private void CreateBindGroup()
        {
            if (_bindGroup != null)
            {
                _api.BindGroupRelease(_bindGroup);
                _bindGroup = null;
            }

            if (_glyphTextureView == null || _imageTextureView == null)
            {
                return;
            }

            var entries = stackalloc BindGroupEntry[4];
            entries[0] = new BindGroupEntry { Binding = 0, Buffer = _uniformBuffer, Offset = 0, Size = 16 };
            entries[1] = new BindGroupEntry { Binding = 1, Sampler = _sampler };
            entries[2] = new BindGroupEntry { Binding = 2, TextureView = _glyphTextureView };
            entries[3] = new BindGroupEntry { Binding = 3, TextureView = _imageTextureView };

            var descriptor = new BindGroupDescriptor
            {
                Layout = _bindGroupLayout,
                EntryCount = 4,
                Entries = entries
            };

            _bindGroup = _api.DeviceCreateBindGroup(_device, &descriptor);
        }

        private void ReleaseGlyphTexture()
        {
            if (_glyphTextureView != null)
            {
                _api.TextureViewRelease(_glyphTextureView);
                _glyphTextureView = null;
            }

            if (_glyphTexture != null)
            {
                _api.TextureDestroy(_glyphTexture);
                _api.TextureRelease(_glyphTexture);
                _glyphTexture = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_bindGroup != null)
            {
                _api.BindGroupRelease(_bindGroup);
            }

            ReleaseGlyphTexture();

            if (_imageTextureView != null)
            {
                _api.TextureViewRelease(_imageTextureView);
            }

            if (_imageTexture != null)
            {
                _api.TextureDestroy(_imageTexture);
                _api.TextureRelease(_imageTexture);
            }

            if (_instanceBuffer != null)
            {
                _api.BufferDestroy(_instanceBuffer);
                _api.BufferRelease(_instanceBuffer);
            }

            if (_uniformBuffer != null)
            {
                _api.BufferDestroy(_uniformBuffer);
                _api.BufferRelease(_uniformBuffer);
            }

            if (_pipeline != null)
            {
                _api.RenderPipelineRelease(_pipeline);
            }

            if (_pipelineLayout != null)
            {
                _api.PipelineLayoutRelease(_pipelineLayout);
            }

            if (_bindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_bindGroupLayout);
            }

            if (_sampler != null)
            {
                _api.SamplerRelease(_sampler);
            }

            if (_shaderModule != null)
            {
                _api.ShaderModuleRelease(_shaderModule);
            }
        }
    }
}

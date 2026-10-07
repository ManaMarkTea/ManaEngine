using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// The environment shown as a background, behind every mesh -- WebGPU's counterpart to what
    /// ManaIrradianceRenderer.RenderEnvironmentBackground already does on OpenGL.
    /// <para>
    /// Standalone rather than another slice of <see cref="ManaWebGpuContext"/>, the same shape as
    /// <see cref="ManaWebGpuUiBackdrop"/>: everything it touches (api/device/queue/depth format, and a
    /// clamp sampler and environment view passed in per draw) is a value the context can hand over once,
    /// not a private field it needs to reach back into.
    /// </para>
    /// </summary>
    public unsafe sealed class ManaWebGpuSkybox : IDisposable
    {
        private readonly WebGPU _api;
        private readonly Device* _device;
        private readonly Queue* _queue;
        private readonly TextureFormat? _depthFormat;
        private readonly string _shaderPath;
        private readonly string? _moonPath;
        private readonly int _slots;

        // The moon's map (NASA's CGI Moon Kit), sampled across its disc as a sphere.
        private Texture* _moonTexture;
        private TextureView* _moonView;

        private BindGroupLayout* _bindGroupLayout;
        private PipelineLayout* _pipelineLayout;
        private RenderPipeline* _pipeline;
        private ShaderModule* _shaderModule;
        // One uniform per slot: a queue write lands before the whole command buffer runs, so six cube faces
        // drawn in one frame from one buffer would all see the last face's camera.
        private readonly nint[] _uniformBuffers;
        private readonly nint[] _bindGroups;
        private readonly nint[] _boundTextureViews;

        /// <summary>
        /// True once the skybox has failed to build. Latched rather than retried every frame, the same as
        /// the ManaUI renderer's own first-use failure: a background that cannot draw should disappear
        /// once and quietly, not throw again on every subsequent frame.
        /// </summary>
        private bool _failed;

        [StructLayout(LayoutKind.Sequential)]
        private struct SkyboxUniform
        {
            public Matrix4x4 InverseDirectionViewProjection;

            // xyz = direction towards the sun; w = 0 the environment image, 1 a day sky, 2 space.
            public Vector4 Sun;

            // xyz = direction towards the moon; w = 1 when the dated sky clock has a moon.
            public Vector4 Moon;

            // x = seconds, for the stars' twinkle; y = daylight, 0..1; z = cloud drift; w = lunar phase, 0..1.
            public Vector4 Parameters;
        }

        /// <param name="depthFormat">The pass's depth format, or null for a pass with no depth (a cube face).</param>
        /// <param name="slots">Draws a frame can hold with different cameras -- six for the faces of a cube.</param>
        public ManaWebGpuSkybox(WebGPU api, Device* device, Queue* queue, TextureFormat? depthFormat, string shaderPath, int slots = 1, string? moonPath = null)
        {
            _moonPath = moonPath;
            _api = api;
            _device = device;
            _queue = queue;
            _depthFormat = depthFormat;
            _shaderPath = shaderPath;
            _slots = Math.Max(1, slots);
            _uniformBuffers = new nint[_slots];
            _bindGroups = new nint[_slots];
            _boundTextureViews = new nint[_slots];
        }

        /// <summary>
        /// Drawn first in the scene pass, with depth write off and the compare forced to Always, so it can
        /// never block or be blocked by anything drawn after it: it is the one thing in the pass that is
        /// not geometry.
        /// </summary>
        public void Draw(
            RenderPassEncoder* renderPass,
            TextureFormat colorFormat,
            Sampler* clampSampler,
            TextureView* environmentView,
            ManaCameraData camera,
            Matrix4x4 aspectCorrectedProjection,
            Vector3 towardSun,
            ManaSky.State sky,
            float seconds,
            int slot = 0)
        {
            if (_failed || clampSampler == null || environmentView == null || slot < 0 || slot >= _slots)
            {
                return;
            }

            try
            {
                EnsurePipeline(colorFormat);
                EnsureBindGroup(slot, clampSampler, environmentView);
                UpdateUniform(slot, camera, aspectCorrectedProjection, towardSun, sky, seconds);
            }
            catch (Exception ex)
            {
                _failed = true;
                ManaConsole.WriteDiagnostic($"WebGPU skybox failed to initialise, disabling it: {ex.Message}");
                return;
            }

            var bindGroup = (BindGroup*)_bindGroups[slot];
            if (_pipeline == null || bindGroup == null)
            {
                return;
            }

            _api.RenderPassEncoderSetPipeline(renderPass, _pipeline);
            _api.RenderPassEncoderSetBindGroup(renderPass, 0, bindGroup, 0, null);
            _api.RenderPassEncoderDraw(renderPass, 3, 1, 0, 0);
        }

        private void UpdateUniform(int slot, ManaCameraData camera, Matrix4x4 aspectCorrectedProjection, Vector3 towardSun, ManaSky.State sky, float seconds)
        {
            // Same trick ManaIrradianceRenderer.RenderEnvironmentBackground uses on OpenGL: the view's
            // translation is zeroed before combining with the projection, so unprojecting a screen position
            // through the inverse yields a direction straight away rather than a point that would still
            // need the camera's position subtracted back out of it.
            var directionOnlyView = camera.View;
            directionOnlyView.Translation = Vector3.Zero;

            // An orthographic view has one direction for every pixel, which would flatten the sky to a single
            // colour; a stand-in perspective of the same shape gives it back its gradient and clouds.
            var projection = aspectCorrectedProjection;
            if (MathF.Abs(projection.M44 - 1.0f) < 1e-6f && MathF.Abs(projection.M11) > 1e-9f)
            {
                projection = Matrix4x4.CreatePerspectiveFieldOfView(55.0f * MathF.PI / 180.0f, projection.M22 / projection.M11, 0.1f, 1000.0f);
            }

            var directionViewProjection = directionOnlyView * projection;
            if (!Matrix4x4.Invert(directionViewProjection, out var inverse))
            {
                // Degenerate only when the projection itself is degenerate (a zero far plane, say), which
                // means every other draw this frame is already in trouble. Leaving the last good matrix in
                // the uniform buffer is a safer failure than writing a NaN-filled one.
                return;
            }

            var sun = towardSun.LengthSquared() > 1e-6f ? Vector3.Normalize(towardSun) : Vector3.UnitY;
            var uniform = new SkyboxUniform
            {
                InverseDirectionViewProjection = inverse,
                Sun = new Vector4(sun, sky.Mode switch { ManaSkyMode.Photo => 0.0f, ManaSkyMode.Space => 2.0f, _ => 1.0f }),
                Moon = new Vector4(sky.TowardMoon, sky.Mode == ManaSkyMode.DayNightCycle ? 1.0f : 0.0f),
                Parameters = new Vector4(seconds, sky.Daylight, sky.CloudTime, sky.MoonPhase)
            };
            _api.QueueWriteBuffer(_queue, (WebGpuBuffer*)_uniformBuffers[slot], 0, &uniform, (nuint)sizeof(SkyboxUniform));
        }

        private void EnsurePipeline(TextureFormat colorFormat)
        {
            if (_pipeline != null)
            {
                return;
            }

            var entries = stackalloc BindGroupLayoutEntry[4];
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

            var layoutDescriptor = new BindGroupLayoutDescriptor { EntryCount = 4, Entries = entries };
            _bindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &layoutDescriptor);
            if (_bindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox bind group layout.");
            }

            var setLayout = _bindGroupLayout;
            var pipelineLayoutDescriptor = new PipelineLayoutDescriptor
            {
                BindGroupLayoutCount = 1,
                BindGroupLayouts = &setLayout
            };

            _pipelineLayout = _api.DeviceCreatePipelineLayout(_device, &pipelineLayoutDescriptor);
            if (_pipelineLayout == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox pipeline layout.");
            }

            _shaderModule = CreateShaderModule(_shaderPath);
            CreateMoonTexture();

            var uniformDescriptor = new BufferDescriptor
            {
                Size = (ulong)sizeof(SkyboxUniform),
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst
            };

            for (var slot = 0; slot < _slots; slot++)
            {
                var buffer = _api.DeviceCreateBuffer(_device, &uniformDescriptor);
                if (buffer == null)
                {
                    throw new InvalidOperationException("Failed to create the WebGPU skybox uniform buffer.");
                }

                _uniformBuffers[slot] = (nint)buffer;
            }

            var vertexEntryPoint = (byte*)Marshal.StringToCoTaskMemUTF8("vs_main");
            var fragmentEntryPoint = (byte*)Marshal.StringToCoTaskMemUTF8("fs_main");

            try
            {
                var colorTarget = new ColorTargetState
                {
                    Format = colorFormat,
                    Blend = null,
                    WriteMask = ColorWriteMask.All
                };

                var fragmentState = new FragmentState
                {
                    Module = _shaderModule,
                    EntryPoint = fragmentEntryPoint,
                    TargetCount = 1,
                    Targets = &colorTarget
                };

                // Never writes depth and always passes the compare -- a background cannot occlude anything
                // and cannot be occluded by whatever the depth buffer held before this draw, which is the
                // whole point of drawing it first rather than sorting it against real geometry.
                var depthStencilState = new DepthStencilState
                {
                    Format = _depthFormat ?? TextureFormat.Depth24Plus,
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
                    Layout = _pipelineLayout,
                    Vertex = new VertexState
                    {
                        Module = _shaderModule,
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
                    DepthStencil = _depthFormat == null ? null : &depthStencilState,
                    Fragment = &fragmentState
                };

                _pipeline = _api.DeviceCreateRenderPipeline(_device, &pipelineDescriptor);
                if (_pipeline == null)
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
        private void EnsureBindGroup(int slot, Sampler* clampSampler, TextureView* environmentView)
        {
            if (_bindGroups[slot] != 0 && _boundTextureViews[slot] == (nint)environmentView)
            {
                return;
            }

            if (_bindGroups[slot] != 0)
            {
                _api.BindGroupRelease((BindGroup*)_bindGroups[slot]);
                _bindGroups[slot] = 0;
            }

            var entries = stackalloc BindGroupEntry[4];
            entries[0] = new BindGroupEntry
            {
                Binding = 0,
                Buffer = (WebGpuBuffer*)_uniformBuffers[slot],
                Offset = 0,
                Size = (ulong)sizeof(SkyboxUniform)
            };
            entries[1] = new BindGroupEntry { Binding = 1, Sampler = clampSampler };
            entries[2] = new BindGroupEntry { Binding = 2, TextureView = environmentView };
            entries[3] = new BindGroupEntry { Binding = 3, TextureView = _moonView };

            var descriptor = new BindGroupDescriptor
            {
                Layout = _bindGroupLayout,
                EntryCount = 4,
                Entries = entries
            };

            var bindGroup = _api.DeviceCreateBindGroup(_device, &descriptor);
            if (bindGroup == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU skybox bind group.");
            }

            _bindGroups[slot] = (nint)bindGroup;
            _boundTextureViews[slot] = (nint)environmentView;
        }

        /// <summary>The moon's map, or a grey pixel when the file is missing -- the moon then draws as a plain disc.</summary>
        private void CreateMoonTexture()
        {
            byte[] pixels = { 200, 200, 200, 255 };
            uint width = 1;
            uint height = 1;
            if (_moonPath != null && File.Exists(_moonPath))
            {
                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(_moonPath);
                pixels = new byte[image.Width * image.Height * 4];
                image.CopyPixelDataTo(pixels);
                width = (uint)image.Width;
                height = (uint)image.Height;
            }

            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8UnormSrgb,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _moonTexture = _api.DeviceCreateTexture(_device, &textureDescriptor);
            if (_moonTexture == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU moon texture.");
            }

            fixed (byte* pixelPointer = pixels)
            {
                var destination = new ImageCopyTexture { Texture = _moonTexture, MipLevel = 0, Origin = new Origin3D(), Aspect = TextureAspect.All };
                var dataLayout = new TextureDataLayout { Offset = 0, BytesPerRow = width * 4, RowsPerImage = height };
                var writeSize = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 };
                _api.QueueWriteTexture(_queue, &destination, pixelPointer, (nuint)pixels.Length, &dataLayout, &writeSize);
            }

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba8UnormSrgb,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            _moonView = _api.TextureCreateView(_moonTexture, &viewDescriptor);
            if (_moonView == null)
            {
                throw new InvalidOperationException("Failed to create the WebGPU moon texture view.");
            }
        }

        private ShaderModule* CreateShaderModule(string shaderPath)
        {
            var source = ManaWgslPreprocessor.LoadResolved(shaderPath);
            var sourcePointer = Marshal.StringToCoTaskMemUTF8(source);

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
                    throw new InvalidOperationException($"Failed to create WebGPU shader module from {shaderPath}.");
                }

                return shaderModule;
            }
            finally
            {
                Marshal.FreeCoTaskMem(sourcePointer);
            }
        }

        public void Dispose()
        {
            for (var slot = 0; slot < _slots; slot++)
            {
                if (_bindGroups[slot] != 0)
                {
                    _api.BindGroupRelease((BindGroup*)_bindGroups[slot]);
                    _bindGroups[slot] = 0;
                }

                _boundTextureViews[slot] = 0;

                if (_uniformBuffers[slot] != 0)
                {
                    _api.BufferRelease((WebGpuBuffer*)_uniformBuffers[slot]);
                    _uniformBuffers[slot] = 0;
                }
            }

            if (_pipeline != null)
            {
                _api.RenderPipelineRelease(_pipeline);
                _pipeline = null;
            }

            if (_moonView != null)
            {
                _api.TextureViewRelease(_moonView);
                _moonView = null;
            }

            if (_moonTexture != null)
            {
                _api.TextureRelease(_moonTexture);
                _moonTexture = null;
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

            if (_shaderModule != null)
            {
                _api.ShaderModuleRelease(_shaderModule);
                _shaderModule = null;
            }
        }
    }
}

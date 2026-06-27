using System.Runtime.InteropServices;
using Mana.Code.Diagnostics;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.Shapes.Loaders;
using SixLabors.ImageSharp.PixelFormats;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;
using Mana;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace Mana.Code.Graphics
{
    public unsafe sealed class ManaWebGpuContext : IDisposable
    {
        private readonly WebGPU _api;
        private readonly IWindow _window;
        private readonly RequestAdapterCallback _requestAdapterCallback;
        private readonly RequestDeviceCallback _requestDeviceCallback;
        private readonly ErrorCallback _errorCallback;

        private Instance* _instance;
        private Surface* _surface;
        private Adapter* _adapter;
        private Device* _device;
        private Queue* _queue;
        private TextureFormat _surfaceFormat;
        private bool _configured;
        private ManaWebGpuMesh? _demoMesh;
            private Mana.Code.Nodes.ManaSceneNode? _demoNode;
        private Vector3 _demoMeshCenter;
        private float _demoMeshScale = 1.0f;
        private Silk.NET.WebGPU.Buffer* _demoUniformBuffer;
        private BindGroupLayout* _demoBindGroupLayout;
        private BindGroup* _demoBindGroup;
        private PipelineLayout* _demoPipelineLayout;
        private Texture* _demoTexture;
        private TextureView* _demoTextureView;
        private Sampler* _demoSampler;
        private ShaderModule* _demoShaderModule;
        private RenderPipeline* _demoPipeline;

        public ManaWebGpuContext(IWindow window, string? requestedBackend) 
        {
            _window = window;
            ManaConsole.WriteDiagnostic("Creating Silk WebGPU API.");
            _api = WebGPU.GetApi();
            _requestAdapterCallback = OnRequestAdapter;
            _requestDeviceCallback = OnRequestDevice;
            _errorCallback = OnError;

            ManaConsole.WriteDiagnostic("Creating WebGPU instance.");
            var instanceBackend = ParseInstanceBackend(requestedBackend);
            var instanceExtras = new InstanceExtras
            {
                Chain = new ChainedStruct
                {
                    SType = (SType)NativeSType.STypeInstanceExtras
                },
                Backends = instanceBackend
            };

            var instanceDescriptor = new InstanceDescriptor
            {
                NextInChain = &instanceExtras.Chain
            };

            _instance = _api.CreateInstance(&instanceDescriptor);
            if (_instance == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU instance.");
            }

            ManaConsole.WriteDiagnostic("Creating WebGPU surface.");
            _surface = WebGPUSurface.CreateWebGPUSurface(window, _api, _instance);
            if (_surface == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU surface.");
            }

            ManaConsole.WriteDiagnostic("Requesting WebGPU adapter.");
            RequestAdapter(requestedBackend);
            ManaConsole.WriteDiagnostic("Requesting WebGPU device.");
            RequestDevice();

            ManaConsole.WriteDiagnostic("Configuring WebGPU queue and surface.");
            _queue = _api.DeviceGetQueue(_device);
            _api.DeviceSetUncapturedErrorCallback(_device, _errorCallback, null);
            _surfaceFormat = _api.SurfaceGetPreferredFormat(_surface, _adapter);
            ConfigureSurface(window.FramebufferSize);
            if (IsDemoMeshPipelineEnabled())
            {
                if (CanCreateDemoMeshPipeline(requestedBackend))
                {
                    CreateDemoMeshPipeline();
                }
                else
                {
                    ManaConsole.WriteDiagnostic("WebGPU demo mesh pipeline disabled on this backend. On Windows, the default backend is now Vulkan; explicit DX11/DX12 requests still keep the demo pipeline off while those paths remain unstable.");
                }
            }
            else
            {
                ManaConsole.WriteDiagnostic("WebGPU demo mesh pipeline disabled. Set MANA_WEBGPU_DEMO_MESH=1 to test the demo pipeline on the current backend.");
            }
            ManaConsole.WriteDiagnostic($"WebGPU surface configured: {window.FramebufferSize.X}x{window.FramebufferSize.Y}, format={_surfaceFormat}.");
        }

        public void Resize(Vector2D<int> framebufferSize)
        {
            ConfigureSurface(framebufferSize);
        }

        public void ClearFrame(Color clearColor)
        {
            if (!_configured)
            {
                return;
            }

            var surfaceTexture = new SurfaceTexture();
            _api.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
            if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success || surfaceTexture.Texture == null)
            {
                return;
            }

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = _surfaceFormat,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };

            var textureView = _api.TextureCreateView(surfaceTexture.Texture, &viewDescriptor);
            var colorAttachment = new RenderPassColorAttachment
            {
                View = textureView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = clearColor
            };

            var renderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment
            };

            var commandEncoderDescriptor = new CommandEncoderDescriptor();
            var commandEncoder = _api.DeviceCreateCommandEncoder(_device, &commandEncoderDescriptor);
            var renderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &renderPassDescriptor);
            DrawDemoMesh(renderPass);
            _api.RenderPassEncoderEnd(renderPass);

            var commandBufferDescriptor = new CommandBufferDescriptor();
            var commandBuffer = _api.CommandEncoderFinish(commandEncoder, &commandBufferDescriptor);
            _api.QueueSubmit(_queue, 1, &commandBuffer);
            _api.SurfacePresent(_surface);

            _api.CommandBufferRelease(commandBuffer);
            _api.CommandEncoderRelease(commandEncoder);
            _api.RenderPassEncoderRelease(renderPass);
            _api.TextureViewRelease(textureView);
        }

        public void Dispose()
        {
            if (_demoPipeline != null)
            {
                _api.RenderPipelineRelease(_demoPipeline);
                _demoPipeline = null;
            }

            if (_demoPipelineLayout != null)
            {
                _api.PipelineLayoutRelease(_demoPipelineLayout);
                _demoPipelineLayout = null;
            }

            if (_demoSampler != null)
            {
                _api.SamplerRelease(_demoSampler);
                _demoSampler = null;
            }

            if (_demoTextureView != null)
            {
                _api.TextureViewRelease(_demoTextureView);
                _demoTextureView = null;
            }

            if (_demoTexture != null)
            {
                _api.TextureRelease(_demoTexture);
                _demoTexture = null;
            }

            if (_demoBindGroup != null)
            {
                _api.BindGroupRelease(_demoBindGroup);
                _demoBindGroup = null;
            }

            if (_demoBindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_demoBindGroupLayout);
                _demoBindGroupLayout = null;
            }

            if (_demoUniformBuffer != null)
            {
                _api.BufferRelease(_demoUniformBuffer);
                _demoUniformBuffer = null;
            }

            if (_demoMesh != null)
            {
                _demoMesh.Dispose();
                _demoMesh = null;
            }

            _demoNode = null;

            if (_demoShaderModule != null)
            {
                _api.ShaderModuleRelease(_demoShaderModule);
                _demoShaderModule = null;
            }

            if (_surface != null && _configured)
            {
                _api.SurfaceUnconfigure(_surface);
                _configured = false;
            }

            if (_queue != null)
            {
                _api.QueueRelease(_queue);
                _queue = null;
            }

            if (_device != null)
            {
                _api.DeviceRelease(_device);
                _device = null;
            }

            if (_adapter != null)
            {
                _api.AdapterRelease(_adapter);
                _adapter = null;
            }

            if (_surface != null)
            {
                _api.SurfaceRelease(_surface);
                _surface = null;
            }

            if (_instance != null)
            {
                _api.InstanceRelease(_instance);
                _instance = null;
            }
        }

        private void ConfigureSurface(Vector2D<int> framebufferSize)
        {
            if (_device == null || _surface == null || framebufferSize.X <= 0 || framebufferSize.Y <= 0)
            {
                return;
            }

            if (_configured)
            {
                _api.SurfaceUnconfigure(_surface);
            }

            var configuration = new SurfaceConfiguration
            {
                Device = _device,
                Format = _surfaceFormat,
                Usage = TextureUsage.RenderAttachment,
                Width = (uint)framebufferSize.X,
                Height = (uint)framebufferSize.Y,
                PresentMode = PresentMode.Fifo,
                AlphaMode = CompositeAlphaMode.Auto
            };

            _api.SurfaceConfigure(_surface, &configuration);
            _configured = true;
        }

        private void CreateDemoMeshPipeline()
        {
            ManaConsole.WriteDiagnostic("Creating WebGPU demo mesh pipeline.");

            var demoMesh = CreateDemoManaMesh();
            _demoNode = new Mana.Code.Nodes.ManaSceneNode(demoMesh.Context, Matrix4x4.Identity)
            {
                Name = "WebGPU Demo Node",
                Position = Vector3.Zero,
                Scale = Vector3.One
            };
            _demoNode.UpdateTransform();
            var cpuData = demoMesh.GetOrCreateCpuData();
            UpdateDemoMeshBounds(cpuData);
            _demoMesh = ManaWebGpuMesh.CreateFromManaMesh(_api, _device, _queue, demoMesh);
            ManaConsole.WriteDiagnostic($"WebGPU demo mesh uploaded: {cpuData.Positions.Length} vertices, {cpuData.Indexes.Length} indexes from ManaMesh '{demoMesh.Name}'.");
            CreateDemoTextureResources(cpuData);
            CreateDemoUniformResources();
            _demoShaderModule = CreateShaderModule(Path.Combine("Assets", "Shaders", "WebGpu", "colored_mesh.wgsl"));
            ManaConsole.WriteDiagnostic("WebGPU demo shader module created.");
            _demoPipeline = CreateDemoRenderPipeline(_demoShaderModule);
            ManaConsole.WriteDiagnostic("WebGPU demo mesh pipeline ready.");
        }

        private static ManaMesh CreateDemoManaMesh()
        {
            var context = new ManaContext((BaseGameMode)null!);
            var loader = new ManaGltfLoader(context);
            var flightHelmetPath = ResolveAssetPath(Path.Combine("Assets", "Models", "FlightHelmet", "glTF", "FlightHelmet.gltf"));

            try
            {
                var mesh = loader.LoadFirstMeshCpuOnly(flightHelmetPath);
                mesh.Name = $"WebGPU Demo {mesh.Name}";
                return mesh;
            }
            catch
            {
                var fallbackMesh = new ManaCube(context, new Vector4(0.12f, 0.95f, 1.0f, 1.0f), new Vector3(0.85f, 0.85f, 0.85f))
                {
                    Name = "WebGPU Demo Cube"
                };

                return fallbackMesh;
            }
        }

        private static string ResolveAssetPath(string relativePath)
        {
            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, relativePath),
                Path.Combine(Environment.CurrentDirectory, "Mana", relativePath),
                Path.Combine(Environment.CurrentDirectory, "..", "Mana", relativePath),
                Path.Combine(AppContext.BaseDirectory, relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", relativePath)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            throw new FileNotFoundException($"Could not locate asset file '{relativePath}'.");
        }

        private void DrawDemoMesh(RenderPassEncoder* renderPass)
        {
            if (_demoPipeline == null || _demoMesh == null)
            {
                return;
            }

            UpdateDemoUniforms();
            _api.RenderPassEncoderSetPipeline(renderPass, _demoPipeline);
            if (_demoBindGroup != null)
            {
                _api.RenderPassEncoderSetBindGroup(renderPass, 0, _demoBindGroup, 0, null);
            }
            _demoMesh.Draw(renderPass);
        }

        private RenderPipeline* CreateDemoRenderPipeline(ShaderModule* shaderModule)
        {
            nint vertexEntryPoint = Marshal.StringToCoTaskMemUTF8("vs_main");
            nint fragmentEntryPoint = Marshal.StringToCoTaskMemUTF8("fs_main");

            try
            {
                var vertexAttributes = stackalloc VertexAttribute[3];
                vertexAttributes[0] = new VertexAttribute
                {
                    Format = VertexFormat.Float32x3,
                    Offset = 0,
                    ShaderLocation = 0
                };
                vertexAttributes[1] = new VertexAttribute
                {
                    Format = VertexFormat.Float32x4,
                    Offset = (ulong)sizeof(Vector3),
                    ShaderLocation = 1
                };
                vertexAttributes[2] = new VertexAttribute
                {
                    Format = VertexFormat.Float32x2,
                    Offset = (ulong)(sizeof(float) * (3 + 4)),
                    ShaderLocation = 2
                };

                var vertexBufferLayout = new VertexBufferLayout
                {
                    ArrayStride = ManaWebGpuMesh.VertexStride,
                    StepMode = VertexStepMode.Vertex,
                    AttributeCount = 3,
                    Attributes = vertexAttributes
                };

                var vertexState = new VertexState
                {
                    Module = shaderModule,
                    EntryPoint = (byte*)vertexEntryPoint,
                    BufferCount = 1,
                    Buffers = &vertexBufferLayout
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
                    Format = _surfaceFormat,
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

                var pipelineDescriptor = new RenderPipelineDescriptor
                {
                    Layout = _demoPipelineLayout,
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
                    Fragment = &fragmentState
                };

                ManaConsole.WriteDiagnostic("Creating WebGPU render pipeline object.");
                var pipeline = _api.DeviceCreateRenderPipeline(_device, &pipelineDescriptor);
                if (pipeline == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU demo render pipeline.");
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

        private void UpdateDemoMeshBounds(ManaMeshCpuData cpuData)
        {
            if (cpuData.Positions.Length == 0)
            {
                _demoMeshCenter = Vector3.Zero;
                _demoMeshScale = 1.0f;
                return;
            }

            var min = cpuData.Positions[0];
            var max = cpuData.Positions[0];
            foreach (var position in cpuData.Positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            _demoMeshCenter = (min + max) * 0.5f;
            var extents = max - min;
            var maxExtent = MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z));
            _demoMeshScale = maxExtent <= 0.0001f ? 1.0f : 2.0f / maxExtent;
        }

        private void CreateDemoUniformResources()
        {
            var bufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = (ulong)sizeof(ManaWebGpuTransformUniform)
            };
            _demoUniformBuffer = _api.DeviceCreateBuffer(_device, &bufferDescriptor);
            if (_demoUniformBuffer == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU demo uniform buffer.");
            }

            var bindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
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

            var bindGroupLayoutEntries = stackalloc BindGroupLayoutEntry[3];
            bindGroupLayoutEntries[0] = bindGroupLayoutEntry;
            bindGroupLayoutEntries[1] = samplerBindGroupLayoutEntry;
            bindGroupLayoutEntries[2] = textureBindGroupLayoutEntry;

            var bindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 3,
                Entries = bindGroupLayoutEntries
            };
            _demoBindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &bindGroupLayoutDescriptor);
            if (_demoBindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU demo bind group layout.");
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

            var bindGroupEntry = new BindGroupEntry
            {
                Binding = 0,
                Buffer = _demoUniformBuffer,
                Offset = 0,
                Size = (ulong)sizeof(ManaWebGpuTransformUniform)
            };

            var samplerBindGroupEntry = new BindGroupEntry
            {
                Binding = 1,
                Sampler = _demoSampler
            };

            var textureBindGroupEntry = new BindGroupEntry
            {
                Binding = 2,
                TextureView = _demoTextureView
            };

            var bindGroupEntries = stackalloc BindGroupEntry[3];
            bindGroupEntries[0] = bindGroupEntry;
            bindGroupEntries[1] = samplerBindGroupEntry;
            bindGroupEntries[2] = textureBindGroupEntry;
            var bindGroupDescriptor = new BindGroupDescriptor
            {
                Layout = _demoBindGroupLayout,
                EntryCount = 3,
                Entries = bindGroupEntries
            };
            _demoBindGroup = _api.DeviceCreateBindGroup(_device, &bindGroupDescriptor);
            if (_demoBindGroup == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU demo bind group.");
            }

            UpdateDemoUniforms();
            ManaConsole.WriteDiagnostic("WebGPU demo uniform resources created.");
        }

        private void CreateDemoTextureResources(ManaMeshCpuData cpuData)
        {
            byte[] pixels;
            uint width;
            uint height;

            if (!string.IsNullOrWhiteSpace(cpuData.AlbedoTexturePath) && File.Exists(cpuData.AlbedoTexturePath))
            {
                using var image = ImageSharpImage.Load<Rgba32>(cpuData.AlbedoTexturePath);
                width = (uint)image.Width;
                height = (uint)image.Height;
                pixels = new byte[image.Width * image.Height * 4];
                image.CopyPixelDataTo(pixels);
                ManaConsole.WriteDiagnostic($"WebGPU demo texture loaded: {cpuData.AlbedoTexturePath}");
            }
            else
            {
                var color = cpuData.AlbedoFactor ?? new Vector4(1, 1, 1, 1);
                width = 1;
                height = 1;
                pixels = new[]
                {
                    (byte)(Math.Clamp(color.X, 0.0f, 1.0f) * 255.0f),
                    (byte)(Math.Clamp(color.Y, 0.0f, 1.0f) * 255.0f),
                    (byte)(Math.Clamp(color.Z, 0.0f, 1.0f) * 255.0f),
                    (byte)(Math.Clamp(color.W, 0.0f, 1.0f) * 255.0f)
                };
                ManaConsole.WriteDiagnostic("WebGPU demo texture fallback: solid albedo color.");
            }

            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8Unorm,
                MipLevelCount = 1,
                SampleCount = 1
            };
            _demoTexture = _api.DeviceCreateTexture(_device, &textureDescriptor);
            if (_demoTexture == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU demo texture.");
            }

            fixed (byte* pixelPointer = pixels)
            {
                var destination = new ImageCopyTexture
                {
                    Texture = _demoTexture,
                    MipLevel = 0,
                    Origin = new Origin3D(),
                    Aspect = TextureAspect.All
                };
                var dataLayout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = width * 4,
                    RowsPerImage = height
                };
                var writeSize = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 };
                _api.QueueWriteTexture(_queue, &destination, pixelPointer, (nuint)pixels.Length, &dataLayout, &writeSize);
            }

            var textureViewDescriptor = new TextureViewDescriptor
            {
                Format = TextureFormat.Rgba8Unorm,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            _demoTextureView = _api.TextureCreateView(_demoTexture, &textureViewDescriptor);
            if (_demoTextureView == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU demo texture view.");
            }

            var samplerDescriptor = new SamplerDescriptor
            {
                AddressModeU = AddressMode.Repeat,
                AddressModeV = AddressMode.Repeat,
                AddressModeW = AddressMode.Repeat,
                MagFilter = FilterMode.Linear,
                MinFilter = FilterMode.Linear,
                MipmapFilter = MipmapFilterMode.Linear,
                LodMinClamp = 0,
                LodMaxClamp = 1,
                Compare = CompareFunction.Undefined,
                MaxAnisotropy = 1
            };
            _demoSampler = _api.DeviceCreateSampler(_device, &samplerDescriptor);
            if (_demoSampler == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU demo sampler.");
            }
        }

        private void UpdateDemoUniforms()
        {
            if (_demoUniformBuffer == null)
            {
                return;
            }

            var time = (float)ManaTime.GlobalTime.GameTime.TotalSeconds;
            var aspect = _window.FramebufferSize.Y == 0 ? 1.0f : _window.FramebufferSize.X / (float)_window.FramebufferSize.Y;
            var normalization = Matrix4x4.CreateTranslation(-_demoMeshCenter) * Matrix4x4.CreateScale(_demoMeshScale);
            var animation = Matrix4x4.CreateRotationY(time * 0.45f) * Matrix4x4.CreateRotationX(-0.35f);
            var projectionFit = Matrix4x4.CreateScale(0.7f / MathF.Max(aspect, 1.0f), 0.7f, 0.7f);

            if (_demoNode != null)
            {
                _demoNode.Rotation = new Vector3(-0.35f, time * 0.45f, 0.0f);
                _demoNode.UpdateTransform();
                animation = _demoNode.WorldTransform;
            }

            var modelProjection = normalization * animation * projectionFit;
            var uniform = new ManaWebGpuTransformUniform
            {
                ModelProjection = Matrix4x4.Transpose(modelProjection)
            };

            _api.QueueWriteBuffer(_queue, _demoUniformBuffer, 0, &uniform, (nuint)sizeof(ManaWebGpuTransformUniform));
        }

        private ShaderModule* CreateShaderModule(string relativePath)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
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

        private void RequestAdapter(string? requestedBackend)
        {
            var options = new RequestAdapterOptions
            {
                CompatibleSurface = _surface,
                PowerPreference = PowerPreference.HighPerformance
            };

            _api.InstanceRequestAdapter(_instance, &options, _requestAdapterCallback, null);
            if (_adapter == null)
            {
                throw new InvalidOperationException("Failed to request WebGPU adapter.");
            }
        }

        private void RequestDevice()
        {
            var descriptor = new DeviceDescriptor();
            _api.AdapterRequestDevice(_adapter, &descriptor, _requestDeviceCallback, null);
            if (_device == null)
            {
                throw new InvalidOperationException("Failed to request WebGPU device.");
            }
        }

        private void OnRequestAdapter(RequestAdapterStatus status, Adapter* adapter, byte* message, void* userdata)
        {
            if (status == RequestAdapterStatus.Success)
            {
                _adapter = adapter;
                return;
            }

            throw new InvalidOperationException($"WebGPU adapter request failed: {status} {ReadMessage(message)}");
        }

        private void OnRequestDevice(RequestDeviceStatus status, Device* device, byte* message, void* userdata)
        {
            if (status == RequestDeviceStatus.Success)
            {
                _device = device;
                return;
            }

            throw new InvalidOperationException($"WebGPU device request failed: {status} {ReadMessage(message)}");
        }

        private void OnError(ErrorType type, byte* message, void* userdata)
        {
            ManaConsole.WriteDiagnostic($"[WebGPU] {type}: {ReadMessage(message)}");
        }

        private static InstanceBackend ParseInstanceBackend(string? requestedBackend)
        {
            if (!string.IsNullOrWhiteSpace(requestedBackend))
            {
                return requestedBackend.Trim().ToLowerInvariant() switch
                {
                    "d3d12" or "dx12" => InstanceBackend.DX12,
                    "d3d11" or "dx11" => InstanceBackend.DX11,
                    "metal" => InstanceBackend.Metal,
                    "vulkan" or "vk" => InstanceBackend.Vulkan,
                    "opengl" or "gl" => InstanceBackend.GL,
                    "all" => InstanceBackend.All,
                    "primary" => InstanceBackend.Primary,
                    "secondary" => InstanceBackend.Secondary,
                    _ => InstanceBackend.Primary
                };
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return InstanceBackend.Metal;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return InstanceBackend.Vulkan;
                //return InstanceBackend.DX12;
            }

            return InstanceBackend.Vulkan;
        }

        private static string ReadMessage(byte* message)
        {
            return message == null ? string.Empty : Marshal.PtrToStringUTF8((nint)message) ?? string.Empty;
        }

        private static bool CanCreateDemoMeshPipeline(string? requestedBackend)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(requestedBackend))
            {
                return true;
            }

            return requestedBackend.Trim().ToLowerInvariant() is "vulkan" or "vk";
        }

        private static bool IsDemoMeshPipelineEnabled()
        {
            var value = Environment.GetEnvironmentVariable("MANA_WEBGPU_DEMO_MESH") ??
                Environment.GetEnvironmentVariable("MANA_WEBGPU_UNSAFE_DEMO_MESH");
            return value == "1" ||
                value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
                value?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ManaWebGpuTransformUniform
        {
            public Matrix4x4 ModelProjection;
        }
    }
}

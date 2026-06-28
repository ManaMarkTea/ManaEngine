using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;
using Silk.NET.Input;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed class ManaWebGpuContext : IDisposable
    {
        public bool UseSceneCapture = false;

        private readonly WebGPU _api;
        private readonly IWindow _window;
        private readonly Func<ManaRenderScene>? _renderSceneProvider;
        private readonly RequestAdapterCallback _requestAdapterCallback;
        private readonly RequestDeviceCallback _requestDeviceCallback;
        private readonly ErrorCallback _errorCallback;

        private Instance* _instance;
        private Surface* _surface;
        private Adapter* _adapter;
        private Device* _device;
        private Queue* _queue;
        private Texture* _depthTexture;
        private TextureView* _depthTextureView;
        private TextureFormat _surfaceFormat;
        private readonly TextureFormat _depthFormat = TextureFormat.Depth24Plus;
        private Vector2D<int> _framebufferSize;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private bool _configured;
        private readonly List<ManaWebGpuSceneMesh> _sceneMeshes = new();
        private readonly Dictionary<string, ManaWebGpuSceneMesh> _sceneMeshMap = new();
        private readonly Dictionary<string, ManaWebGpuMeshTexture> _meshTextureCache = new(StringComparer.OrdinalIgnoreCase);
        private ManaRenderScene? _activeRenderScene;
        private Vector3 _demoMeshCenter;
        private float _demoMeshScale = 1.0f;
        private BindGroupLayout* _demoBindGroupLayout;
        private PipelineLayout* _demoPipelineLayout;
        private BindGroupLayout* _sceneCaptureBindGroupLayout;
        private PipelineLayout* _sceneCapturePipelineLayout;
        private RenderPipeline* _sceneCapturePipeline;
        private ManaWebGpuHdrPostProcessor? _hdrPostProcessor;
        private ManaWebGpuEnvironmentPrefilterCompute? _environmentPrefilterCompute;
        private Texture* _sceneCaptureDepthTexture;
        private TextureView* _sceneCaptureDepthTextureView;
        private uint _sceneCaptureDepthSize;
        private Sampler* _demoSampler;
        private Sampler* _clampSampler;
        private ManaWebGpuEnvironmentReflectionMap? _environmentReflectionMap;
        private int _framesSinceEnvironmentCapture = int.MaxValue;
        private readonly Dictionary<string, nint> _shaderModules = new();
        private readonly Dictionary<string, nint> _pipelines = new();
        private ManaWebGpuImGuiController? _imGuiController;

        public ManaWebGpuContext(IWindow window, string? requestedBackend, Func<ManaRenderScene>? renderSceneProvider = null) 
        {
            _window = window;
            _renderSceneProvider = renderSceneProvider;

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
            if (_renderSceneProvider != null || IsDemoMeshPipelineEnabled())
            {
                if (CanCreateDemoMeshPipeline(requestedBackend))
                {
                    try
                    {
                        CreateDemoMeshPipeline();
                    }
                    catch (Exception ex)
                    {
                        ManaConsole.WriteDiagnostic("WebGPU mesh pipeline failed; continuing with clear-only renderer.");
                        ManaConsole.WriteDiagnostic(ex);
                        ReleaseDemoResources();
                    }
                }
                else
                {
                    ManaConsole.WriteDiagnostic("WebGPU mesh pipeline disabled on this backend. On Windows, the default backend is now Vulkan; explicit DX11/DX12 requests still keep the mesh pipeline off while those paths remain unstable.");
                }
            }
            else
            {
                ManaConsole.WriteDiagnostic("WebGPU mesh pipeline disabled. Set MANA_WEBGPU_DEMO_MESH=1 to test the fallback demo pipeline on the current backend.");
            }
            ManaConsole.WriteDiagnostic($"WebGPU surface configured: {window.FramebufferSize.X}x{window.FramebufferSize.Y}, format={_surfaceFormat}.");
        }

        public void Resize(Vector2D<int> framebufferSize)
        {
            ConfigureSurface(framebufferSize);
        }

        public IManaImGuiController CreateImGuiController(IInputContext input)
        {
            _imGuiController = new ManaWebGpuImGuiController(_api, _device, _queue, _surfaceFormat, _window, input);
            return _imGuiController;
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
            var hdrSceneTextureView = _hdrPostProcessor?.SceneTextureView;
            if (textureView == null || hdrSceneTextureView == null)
            {
                if (textureView != null)
                {
                    _api.TextureViewRelease(textureView);
                }

                return;
            }

            var sceneColorAttachment = new RenderPassColorAttachment
            {
                View = hdrSceneTextureView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = clearColor
            };

            var depthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                View = _depthTextureView,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
                StencilClearValue = 0,
                StencilReadOnly = true
            };

            var sceneRenderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &sceneColorAttachment,
                DepthStencilAttachment = &depthStencilAttachment
            };

            var commandEncoderDescriptor = new CommandEncoderDescriptor();
            var commandEncoder = _api.DeviceCreateCommandEncoder(_device, &commandEncoderDescriptor);
            RefreshRenderScene();
            CaptureSceneEnvironmentIfNeeded(commandEncoder);

            var sceneRenderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &sceneRenderPassDescriptor);
            DrawDemoMesh(sceneRenderPass);
            _api.RenderPassEncoderEnd(sceneRenderPass);

            _hdrPostProcessor?.Process(commandEncoder);

            var surfaceColorAttachment = new RenderPassColorAttachment
            {
                View = textureView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = clearColor
            };

            var toneMapRenderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &surfaceColorAttachment
            };

            var toneMapRenderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &toneMapRenderPassDescriptor);
            _hdrPostProcessor?.Draw(toneMapRenderPass);
            _imGuiController?.Render(toneMapRenderPass);
            _api.RenderPassEncoderEnd(toneMapRenderPass);

            var commandBufferDescriptor = new CommandBufferDescriptor();
            var commandBuffer = _api.CommandEncoderFinish(commandEncoder, &commandBufferDescriptor);
            _api.QueueSubmit(_queue, 1, &commandBuffer);
            _api.SurfacePresent(_surface);

            _api.CommandBufferRelease(commandBuffer);
            _api.CommandEncoderRelease(commandEncoder);
            _api.RenderPassEncoderRelease(sceneRenderPass);
            _api.RenderPassEncoderRelease(toneMapRenderPass);
            _api.TextureViewRelease(textureView);
        }

        public void Dispose()
        {
            ReleaseDemoResources();

            if (_surface != null && _configured)
            {
                _api.SurfaceUnconfigure(_surface);
                _configured = false;
            }

            ReleaseDepthResources();

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

        private void ReleaseDemoResources()
        {
            foreach (var entry in _pipelines)
            {
                if (entry.Value != 0)
                {
                    _api.RenderPipelineRelease((RenderPipeline*)entry.Value);
                }
            }
            _pipelines.Clear();

            foreach (var entry in _shaderModules)
            {
                if (entry.Value != 0)
                {
                    _api.ShaderModuleRelease((ShaderModule*)entry.Value);
                }
            }
            _shaderModules.Clear();

            if (_sceneCapturePipeline != null)
            {
                _api.RenderPipelineRelease(_sceneCapturePipeline);
                _sceneCapturePipeline = null;
            }

            _hdrPostProcessor?.Dispose();
            _hdrPostProcessor = null;

            ReleaseSceneCaptureDepthResources();

            if (_sceneCapturePipelineLayout != null)
            {
                _api.PipelineLayoutRelease(_sceneCapturePipelineLayout);
                _sceneCapturePipelineLayout = null;
            }

            if (_sceneCaptureBindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_sceneCaptureBindGroupLayout);
                _sceneCaptureBindGroupLayout = null;
            }

            _environmentPrefilterCompute?.Dispose();
            _environmentPrefilterCompute = null;

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

            if (_clampSampler != null)
            {
                _api.SamplerRelease(_clampSampler);
                _clampSampler = null;
            }

            if (_demoBindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_demoBindGroupLayout);
                _demoBindGroupLayout = null;
            }

            ReleaseSceneMeshes();
            ReleaseEnvironmentReflectionMap();

            foreach (var entry in _shaderModules)
            {
                if (entry.Value != 0)
                {
                    _api.ShaderModuleRelease((ShaderModule*)entry.Value);
                }
            }
            _shaderModules.Clear();
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
                AlphaMode = CompositeAlphaMode.Opaque
            };

            _api.SurfaceConfigure(_surface, &configuration);
            _framebufferSize = framebufferSize;
            CreateDepthResources(framebufferSize);
            _hdrPostProcessor?.Resize(framebufferSize);
            _configured = true;
        }

        private void ReleaseDepthResources()
        {
            if (_depthTextureView != null)
            {
                _api.TextureViewRelease(_depthTextureView);
                _depthTextureView = null;
            }

            if (_depthTexture != null)
            {
                _api.TextureRelease(_depthTexture);
                _depthTexture = null;
            }
        }

        private void CreateDepthResources(Vector2D<int> framebufferSize)
        {
            ReleaseDepthResources();

            var depthTextureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D
                {
                    Width = (uint)framebufferSize.X,
                    Height = (uint)framebufferSize.Y,
                    DepthOrArrayLayers = 1
                },
                Format = _depthFormat,
                MipLevelCount = 1,
                SampleCount = 1
            };

            _depthTexture = _api.DeviceCreateTexture(_device, &depthTextureDescriptor);
            if (_depthTexture == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU depth texture.");
            }

            var depthTextureViewDescriptor = new TextureViewDescriptor
            {
                Format = _depthFormat,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.DepthOnly
            };

            _depthTextureView = _api.TextureCreateView(_depthTexture, &depthTextureViewDescriptor);
            if (_depthTextureView == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU depth texture view.");
            }
        }

        private void ReleaseSceneCaptureDepthResources()
        {
            if (_sceneCaptureDepthTextureView != null)
            {
                _api.TextureViewRelease(_sceneCaptureDepthTextureView);
                _sceneCaptureDepthTextureView = null;
            }

            if (_sceneCaptureDepthTexture != null)
            {
                _api.TextureRelease(_sceneCaptureDepthTexture);
                _sceneCaptureDepthTexture = null;
            }

            _sceneCaptureDepthSize = 0;
        }

        private void EnsureSceneCaptureDepthResources(uint size)
        {
            if (_sceneCaptureDepthTexture != null &&
                _sceneCaptureDepthTextureView != null &&
                _sceneCaptureDepthSize == size)
            {
                return;
            }

            ReleaseSceneCaptureDepthResources();

            var depthTextureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.RenderAttachment,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D
                {
                    Width = size,
                    Height = size,
                    DepthOrArrayLayers = 1
                },
                Format = _depthFormat,
                MipLevelCount = 1,
                SampleCount = 1
            };

            _sceneCaptureDepthTexture = _api.DeviceCreateTexture(_device, &depthTextureDescriptor);
            if (_sceneCaptureDepthTexture == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU scene capture depth texture.");
            }

            var depthTextureViewDescriptor = new TextureViewDescriptor
            {
                Format = _depthFormat,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.DepthOnly
            };

            _sceneCaptureDepthTextureView = _api.TextureCreateView(_sceneCaptureDepthTexture, &depthTextureViewDescriptor);
            if (_sceneCaptureDepthTextureView == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU scene capture depth texture view.");
            }

            _sceneCaptureDepthSize = size;
        }

        private void CreateDemoMeshPipeline()
        {
            ManaConsole.WriteDiagnostic("Creating WebGPU mesh pipeline.");

            var renderScene = _renderSceneProvider?.Invoke() ?? CreateDemoRenderScene();
            if (renderScene.Meshes.Count == 0)
            {
                throw new InvalidOperationException("WebGPU render scene did not contain any mesh instances.");
            }

            _activeRenderScene = renderScene;
            CreateSharedPipelineLayouts();
            CreateHdrPostProcessor();
            
            // Pre-compile default pipeline
            GetOrCreatePipeline("ManaPbrShader");
            
            ManaConsole.WriteDiagnostic("WebGPU mesh pipeline ready.");
            UploadSceneMeshes(renderScene);
        }

        private static ManaRenderScene CreateDemoRenderScene()
        {
            var meshData = CreateDemoMeshData();
            var worldTransform = CreateNormalizedWorldTransform(meshData);

            return new ManaRenderScene
            {
                Camera = new ManaCameraData(
                    Matrix4x4.CreateLookAt(new Vector3(0.0f, 0.55f, 4.25f), new Vector3(0.0f, 0.1f, 0.0f), Vector3.UnitY),
                    Matrix4x4.CreatePerspectiveFieldOfView(DegreesToRadians(55.0f), 16.0f / 9.0f, 0.05f, 100.0f),
                    new Vector3(0.0f, 0.55f, 4.25f),
                    0.05f,
                    100.0f),
                Meshes = new[]
                {
                    new ManaRenderMeshInstance
                    {
                        Name = "WebGPU Core Demo Cube",
                        MeshData = meshData,
                        WorldTransform = worldTransform
                    }
                }
            };
        }

        private static ManaMeshCpuData CreateDemoMeshData()
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Vector4>();
            var texCoords = new List<Vector2>();
            var indexes = new List<uint>();

            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector4(0.12f, 0.95f, 1.0f, 1.0f));
            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(0, 0, -1), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector4(0.25f, 0.55f, 1.0f, 1.0f));
            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0), new Vector4(0.95f, 0.35f, 0.22f, 1.0f));
            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0), new Vector4(0.95f, 0.8f, 0.22f, 1.0f));
            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector4(0.65f, 1.0f, 0.3f, 1.0f));
            AddCubeFace(positions, normals, colors, texCoords, indexes, new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector4(0.9f, 0.45f, 1.0f, 1.0f));

            return new ManaMeshCpuData
            {
                Positions = positions.ToArray(),
                Normals = normals.ToArray(),
                Colors = colors.ToArray(),
                TextureCoordinates0 = texCoords.ToArray(),
                TextureCoordinates1 = Array.Empty<Vector2>(),
                Indexes = indexes.ToArray(),
                AlbedoFactor = Vector4.One,
                AlbedoTexturePath = null
            };
        }

        private static void AddCubeFace(
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector4> colors,
            List<Vector2> texCoords,
            List<uint> indexes,
            Vector3 normal,
            Vector3 tangent,
            Vector3 bitangent,
            Vector4 color)
        {
            var start = (uint)positions.Count;
            var center = normal * 0.5f;
            var halfTangent = tangent * 0.5f;
            var halfBitangent = bitangent * 0.5f;

            positions.Add(center - halfTangent - halfBitangent);
            positions.Add(center + halfTangent - halfBitangent);
            positions.Add(center + halfTangent + halfBitangent);
            positions.Add(center - halfTangent + halfBitangent);

            for (var i = 0; i < 4; i++)
            {
                normals.Add(normal);
                colors.Add(color);
            }

            texCoords.Add(new Vector2(0, 1));
            texCoords.Add(new Vector2(1, 1));
            texCoords.Add(new Vector2(1, 0));
            texCoords.Add(new Vector2(0, 0));

            indexes.Add(start);
            indexes.Add(start + 1);
            indexes.Add(start + 2);
            indexes.Add(start);
            indexes.Add(start + 2);
            indexes.Add(start + 3);
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
            if (_sceneMeshes.Count == 0)
            {
                return;
            }

            foreach (var sceneMesh in _sceneMeshes)
            {
                UpdateDemoUniforms(sceneMesh.Instance, sceneMesh.UniformBuffer);
                
                var pipeline = GetOrCreatePipeline(sceneMesh.Instance.ShaderName, sceneMesh.Instance.IsTransparent, sceneMesh.Instance.DisableDepthWrite);
                if (pipeline != null)
                {
                    _api.RenderPassEncoderSetPipeline(renderPass, pipeline);
                }

                if (sceneMesh.BindGroup != null)
                {
                    _api.RenderPassEncoderSetBindGroup(renderPass, 0, sceneMesh.BindGroup, 0, null);
                }
                sceneMesh.Mesh.Draw(renderPass);
            }
        }

        private void CreateHdrPostProcessor()
        {
            if (_hdrPostProcessor != null)
            {
                return;
            }

            var shaderPath = ResolveAssetPath(Path.Combine("Assets", "Shaders", "WebGpu", "hdr_tonemap.wgsl"));
            _hdrPostProcessor = new ManaWebGpuHdrPostProcessor(_api, _device, _surfaceFormat, shaderPath);
            _hdrPostProcessor.Resize(_framebufferSize);
        }

        private TextureFormat GetSceneColorFormat()
        {
            return _hdrPostProcessor?.SceneFormat ?? _surfaceFormat;
        }

        private void CaptureSceneEnvironmentIfNeeded(CommandEncoder* commandEncoder)
        {
            if (!UseSceneCapture)
            {
                return;
            }

            if (_activeRenderScene == null ||
                _sceneMeshes.Count == 0)
            {
                return;
            }

            var source = GetEffectiveEnvironmentReflectionSource(_activeRenderScene.EnvironmentReflectionSource);
            if (source.SourceKind != ManaEnvironmentSourceKind.SceneCapture)
            {
                return;
            }

            var refreshInterval = Math.Max(1, source.SceneCaptureRefreshIntervalFrames);
            if (_framesSinceEnvironmentCapture < refreshInterval)
            {
                _framesSinceEnvironmentCapture++;
                return;
            }

            var environmentReflectionMap = GetOrCreateEnvironmentReflectionMap();
            var environmentMap = environmentReflectionMap.EnvironmentMapResource;
            if (environmentMap.RenderTargetViews.Count < 6)
            {
                return;
            }

            var prefilteredMap = environmentReflectionMap.PrefilteredReflectionMapResource;
            var irradianceMap = environmentReflectionMap.IrradianceMapResource;
            var pipeline = GetOrCreateSceneCapturePipeline();
            ManaConsole.WriteDiagnostic($"WebGPU scene environment capture: position={source.SceneCapturePosition}, meshes={_sceneMeshes.Count(mesh => mesh.Instance.IncludeInEnvironmentCapture)}.");

            for (var face = 0; face < 6; face++)
            {
                var captureCamera = CreateSceneCaptureCamera(source, face);
                EnsureSceneCaptureDepthResources(environmentMap.Width);
                RenderSceneCaptureFace(commandEncoder, (TextureView*)environmentMap.RenderTargetViews[face], pipeline, captureCamera, face);

                if (irradianceMap.RenderTargetViews.Count >= 6)
                {
                    EnsureSceneCaptureDepthResources(irradianceMap.Width);
                    RenderSceneCaptureFace(commandEncoder, (TextureView*)irradianceMap.RenderTargetViews[face], pipeline, captureCamera, face);
                }
            }

            DispatchEnvironmentPrefilter(commandEncoder, environmentMap, prefilteredMap);
            _framesSinceEnvironmentCapture = 0;
        }

        private void RenderSceneCaptureFace(
            CommandEncoder* commandEncoder,
            TextureView* faceView,
            RenderPipeline* pipeline,
            ManaCameraData captureCamera,
            int face)
        {
            if (faceView == null)
            {
                return;
            }

            var colorAttachment = new RenderPassColorAttachment
            {
                View = faceView,
                LoadOp = LoadOp.Load,
                StoreOp = StoreOp.Store,
                ClearValue = new Color(0.0, 0.0, 0.0, 1.0)
            };

            var depthStencilAttachment = new RenderPassDepthStencilAttachment
            {
                View = _sceneCaptureDepthTextureView,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
                StencilClearValue = 0,
                StencilReadOnly = true
            };

            var renderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
                DepthStencilAttachment = &depthStencilAttachment
            };

            var renderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &renderPassDescriptor);

            _api.RenderPassEncoderSetPipeline(renderPass, pipeline);
            foreach (var sceneMesh in _sceneMeshes)
            {
                if (!sceneMesh.Instance.IncludeInEnvironmentCapture)
                {
                    continue;
                }

                var captureUniformBuffer = sceneMesh.GetSceneCaptureUniformBuffer(face);
                var captureBindGroup = sceneMesh.GetSceneCaptureBindGroup(face);
                UpdateDemoUniforms(sceneMesh.Instance, captureUniformBuffer, captureCamera);
                _api.RenderPassEncoderSetBindGroup(renderPass, 0, captureBindGroup, 0, null);
                sceneMesh.Mesh.Draw(renderPass);
            }

            _api.RenderPassEncoderEnd(renderPass);
            _api.RenderPassEncoderRelease(renderPass);
        }

        private void DispatchEnvironmentPrefilter(
            CommandEncoder* commandEncoder,
            ManaWebGpuTextureResource sourceMap,
            ManaWebGpuTextureResource destinationMap)
        {
            GetOrCreateEnvironmentPrefilterCompute().Dispatch(commandEncoder, sourceMap, destinationMap);
        }

        private void DispatchEnvironmentPrefilterNow(ManaWebGpuEnvironmentReflectionMap environmentReflectionMap)
        {
            GetOrCreateEnvironmentPrefilterCompute().DispatchNow(
                environmentReflectionMap.EnvironmentMapResource,
                environmentReflectionMap.PrefilteredReflectionMapResource);
        }

        private ManaWebGpuEnvironmentPrefilterCompute GetOrCreateEnvironmentPrefilterCompute()
        {
            if (_environmentPrefilterCompute != null)
            {
                return _environmentPrefilterCompute;
            }

            var shaderPath = ResolveAssetPath(Path.Combine("Assets", "Shaders", "WebGpu", "environment_prefilter_compute.wgsl"));
            _environmentPrefilterCompute = new ManaWebGpuEnvironmentPrefilterCompute(_api, _device, _queue, shaderPath);
            return _environmentPrefilterCompute;
        }

        private RenderPipeline* CreateDemoRenderPipeline(
            ShaderModule* shaderModule,
            TextureFormat colorFormat,
            PipelineLayout* pipelineLayout,
            string? shaderName = null,
            bool isTransparent = false,
            bool disableDepthWrite = false)
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

                var blendState = new BlendState
                {
                    Color = new BlendComponent
                    {
                        Operation = BlendOperation.Add,
                        SrcFactor = isTransparent ? BlendFactor.SrcAlpha : BlendFactor.One,
                        DstFactor = isTransparent ? BlendFactor.OneMinusSrcAlpha : BlendFactor.Zero
                    },
                    Alpha = new BlendComponent
                    {
                        Operation = BlendOperation.Add,
                        SrcFactor = isTransparent ? BlendFactor.One : BlendFactor.One,
                        DstFactor = isTransparent ? BlendFactor.OneMinusSrcAlpha : BlendFactor.Zero
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
                    DepthCompare = CompareFunction.Less,
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

        private void UploadSceneMeshes(ManaRenderScene renderScene)
        {
            ReleaseSceneMeshes();

            var renderableMeshes = GetRenderableMeshes(renderScene);
            for (var i = 0; i < renderableMeshes.Count; i++)
            {
                var instance = renderableMeshes[i];
                var sceneMesh = CreateSceneMesh(instance);
                _sceneMeshes.Add(sceneMesh);
                _sceneMeshMap[GetSceneMeshKey(instance, i)] = sceneMesh;

                ManaConsole.WriteDiagnostic($"WebGPU scene mesh uploaded: {instance.WorldTransform.Translation.ToString()}: {instance.MeshData.Positions.Length} vertices, {instance.MeshData.Indexes.Length} indexes from render packet '{instance.Name}'.");
            }

            if (renderableMeshes.Count > 4)
            {
                var instance = renderableMeshes[3];
                var meshData = instance.MeshData;
                ManaConsole.WriteDiagnostic($"WebGPU scene mesh uploaded: {instance.WorldTransform.Translation.ToString()}: {meshData.Positions.Length} vertices, {meshData.Indexes.Length} indexes from render packet '{instance.Name}'.");
            }                        

            var totalIndexes = _sceneMeshes.Sum(mesh => mesh.Mesh.IndexCount);
            ManaConsole.WriteDiagnostic($"WebGPU scene upload complete: meshes={_sceneMeshes.Count}, indexes={totalIndexes}.");
        }

        private ManaWebGpuSceneMesh CreateSceneMesh(ManaRenderMeshInstance instance)
        {
            var meshData = instance.MeshData;
            var mesh = ManaWebGpuMesh.CreateFromMeshData(_api, _device, _queue, meshData, instance.Name);

            var bufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
                Size = (ulong)sizeof(ManaWebGpuTransformUniform)
            };
            var uniformBuffer = _api.DeviceCreateBuffer(_device, &bufferDescriptor);
            if (uniformBuffer == null)
            {
                mesh.Dispose();
                throw new InvalidOperationException($"Failed to create WebGPU uniform buffer for mesh '{instance.Name}'.");
            }

            var textureResource = AcquireMeshTexture(meshData);
            var environmentReflectionMap = GetOrCreateEnvironmentReflectionMap();

            var bindGroupEntry = new BindGroupEntry
            {
                Binding = 0,
                Buffer = uniformBuffer,
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
                TextureView = textureResource.TextureView
            };

            var environmentMapBindGroupEntry = new BindGroupEntry
            {
                Binding = 3,
                TextureView = environmentReflectionMap.EnvironmentMapResource.TextureView
            };

            var irradianceMapBindGroupEntry = new BindGroupEntry
            {
                Binding = 4,
                TextureView = environmentReflectionMap.IrradianceMapResource.TextureView
            };

            var prefilteredReflectionMapBindGroupEntry = new BindGroupEntry
            {
                Binding = 5,
                TextureView = environmentReflectionMap.PrefilteredReflectionMapResource.TextureView
            };

            var brdfLutBindGroupEntry = new BindGroupEntry
            {
                Binding = 6,
                TextureView = environmentReflectionMap.BrdfLutResource.TextureView
            };

            var clampSamplerBindGroupEntry = new BindGroupEntry
            {
                Binding = 7,
                Sampler = _clampSampler
            };

            var bindGroupEntries = new BindGroupEntry[8];
            bindGroupEntries[0] = bindGroupEntry;
            bindGroupEntries[1] = samplerBindGroupEntry;
            bindGroupEntries[2] = textureBindGroupEntry;
            bindGroupEntries[3] = environmentMapBindGroupEntry;
            bindGroupEntries[4] = irradianceMapBindGroupEntry;
            bindGroupEntries[5] = prefilteredReflectionMapBindGroupEntry;
            bindGroupEntries[6] = brdfLutBindGroupEntry;
            bindGroupEntries[7] = clampSamplerBindGroupEntry;

            BindGroup* bindGroup = null;
            fixed (BindGroupEntry* bindGroupEntriesPointer = bindGroupEntries)
            {
                var bindGroupDescriptor = new BindGroupDescriptor
                {
                    Layout = _demoBindGroupLayout,
                    EntryCount = 8,
                    Entries = bindGroupEntriesPointer
                };
                bindGroup = _api.DeviceCreateBindGroup(_device, &bindGroupDescriptor);
            }

            if (bindGroup == null)
            {
                ReleaseMeshTexture(textureResource);
                _api.BufferRelease(uniformBuffer);
                mesh.Dispose();
                throw new InvalidOperationException($"Failed to create WebGPU bind group for mesh '{instance.Name}'.");
            }

            var sceneCaptureUniformBuffers = new nint[6];
            var sceneCaptureBindGroups = new nint[6];
            for (var face = 0; face < 6; face++)
            {
                var sceneCaptureUniformBuffer = _api.DeviceCreateBuffer(_device, &bufferDescriptor);
                if (sceneCaptureUniformBuffer == null)
                {
                    ReleaseSceneCaptureResources(sceneCaptureUniformBuffers, sceneCaptureBindGroups);
                    _api.BindGroupRelease(bindGroup);
                    ReleaseMeshTexture(textureResource);
                    _api.BufferRelease(uniformBuffer);
                    mesh.Dispose();
                    throw new InvalidOperationException($"Failed to create WebGPU scene capture uniform buffer for mesh '{instance.Name}'.");
                }

                sceneCaptureUniformBuffers[face] = (nint)sceneCaptureUniformBuffer;
                var sceneCaptureBufferBindGroupEntry = bindGroupEntry;
                sceneCaptureBufferBindGroupEntry.Buffer = sceneCaptureUniformBuffer;

                var sceneCaptureBindGroupEntries = new BindGroupEntry[3];
                sceneCaptureBindGroupEntries[0] = sceneCaptureBufferBindGroupEntry;
                sceneCaptureBindGroupEntries[1] = samplerBindGroupEntry;
                sceneCaptureBindGroupEntries[2] = textureBindGroupEntry;

                BindGroup* sceneCaptureBindGroup = null;
                fixed (BindGroupEntry* sceneCaptureBindGroupEntriesPointer = sceneCaptureBindGroupEntries)
                {
                    var sceneCaptureBindGroupDescriptor = new BindGroupDescriptor
                    {
                        Layout = _sceneCaptureBindGroupLayout,
                        EntryCount = 3,
                        Entries = sceneCaptureBindGroupEntriesPointer
                    };
                    sceneCaptureBindGroup = _api.DeviceCreateBindGroup(_device, &sceneCaptureBindGroupDescriptor);
                }

                if (sceneCaptureBindGroup == null)
                {
                    ReleaseSceneCaptureResources(sceneCaptureUniformBuffers, sceneCaptureBindGroups);
                    _api.BindGroupRelease(bindGroup);
                    ReleaseMeshTexture(textureResource);
                    _api.BufferRelease(uniformBuffer);
                    mesh.Dispose();
                    throw new InvalidOperationException($"Failed to create WebGPU scene capture bind group for mesh '{instance.Name}'.");
                }

                sceneCaptureBindGroups[face] = (nint)sceneCaptureBindGroup;
            }

            var sceneMesh = new ManaWebGpuSceneMesh(_api, instance, mesh, uniformBuffer, textureResource, ReleaseMeshTexture, bindGroup, sceneCaptureUniformBuffers, sceneCaptureBindGroups);
            UpdateDemoUniforms(instance, uniformBuffer);
            return sceneMesh;
        }

        private void ReleaseSceneCaptureResources(IReadOnlyList<nint> uniformBuffers, IReadOnlyList<nint> bindGroups)
        {
            foreach (var bindGroup in bindGroups)
            {
                if (bindGroup != 0)
                {
                    _api.BindGroupRelease((BindGroup*)bindGroup);
                }
            }

            foreach (var uniformBuffer in uniformBuffers)
            {
                if (uniformBuffer != 0)
                {
                    _api.BufferRelease((Silk.NET.WebGPU.Buffer*)uniformBuffer);
                }
            }
        }

        private void ReleaseSceneMeshes()
        {
            foreach (var sceneMesh in _sceneMeshes)
            {
                sceneMesh.Dispose();
            }

            _sceneMeshes.Clear();
            _sceneMeshMap.Clear();
        }

        private void RefreshRenderScene()
        {
            var renderScene = _renderSceneProvider?.Invoke();
            if (renderScene == null)
            {
                return;
            }

            var renderableMeshes = GetRenderableMeshes(renderScene);
            _activeRenderScene = renderScene;
            ReconcileSceneMeshes(renderableMeshes);
        }

        private void ReconcileSceneMeshes(IReadOnlyList<ManaRenderMeshInstance> renderableMeshes)
        {
            var nextSceneMeshes = new List<ManaWebGpuSceneMesh>(renderableMeshes.Count);
            var activeKeys = new HashSet<string>(StringComparer.Ordinal);
            var uploadedCount = 0;
            var removedCount = 0;
            ulong uploadedIndexes = 0;
            ulong removedIndexes = 0;
            List<string>? changedMeshSummaries = null;

            for (var i = 0; i < renderableMeshes.Count; i++)
            {
                var instance = renderableMeshes[i];
                var key = GetSceneMeshKey(instance, i);
                activeKeys.Add(key);

                if (_sceneMeshMap.TryGetValue(key, out var existing))
                {
                    if (CanReuseSceneMesh(existing.Instance, instance))
                    {
                        existing.Instance = instance;
                        nextSceneMeshes.Add(existing);
                        continue;
                    }

                    existing.Dispose();
                    _sceneMeshMap.Remove(key);
                    removedCount++;
                    removedIndexes += existing.Mesh.IndexCount;
                }

                var created = CreateSceneMesh(instance);
                _sceneMeshMap[key] = created;
                nextSceneMeshes.Add(created);
                uploadedCount++;
                uploadedIndexes += created.Mesh.IndexCount;
                changedMeshSummaries ??= new List<string>();
                changedMeshSummaries.Add(FormatSceneMeshDelta(instance, key, existing?.Mesh.IndexCount, created.Mesh.IndexCount));
            }

            var staleKeys = _sceneMeshMap.Keys
                .Where(key => !activeKeys.Contains(key))
                .ToArray();
            foreach (var staleKey in staleKeys)
            {
                if (_sceneMeshMap.Remove(staleKey, out var staleMesh))
                {
                    staleMesh.Dispose();
                    removedCount++;
                    removedIndexes += staleMesh.Mesh.IndexCount;
                    changedMeshSummaries ??= new List<string>();
                    changedMeshSummaries.Add(FormatSceneMeshDelta(staleMesh.Instance, staleKey, staleMesh.Mesh.IndexCount, null));
                }
            }

            _sceneMeshes.Clear();
            _sceneMeshes.AddRange(nextSceneMeshes);

            if (uploadedCount > 0 || removedCount > 0)
            {
                var residentIndexes = _sceneMeshes.Sum(mesh => mesh.Mesh.IndexCount);
                ManaConsole.WriteDiagnostic(
                    $"WebGPU scene mesh delta: uploaded={uploadedCount} (indexes={uploadedIndexes}), " +
                    $"removed={removedCount} (indexes={removedIndexes}), residentMeshes={_sceneMeshes.Count}, " +
                    $"residentIndexes={residentIndexes}.");

                if (changedMeshSummaries is { Count: > 0 })
                {
                    ManaConsole.WriteDiagnostic($"WebGPU changed meshes: {string.Join("; ", changedMeshSummaries)}.");
                }
            }
        }

        private static string FormatSceneMeshDelta(ManaRenderMeshInstance instance, string key, uint? oldIndexes, uint? newIndexes)
        {
            var builder = new StringBuilder();
            builder.Append('\'');
            builder.Append(string.IsNullOrWhiteSpace(instance.Name) ? "<unnamed>" : instance.Name);
            builder.Append("' key=");
            builder.Append(key);

            if (oldIndexes.HasValue)
            {
                builder.Append(" oldIndexes=");
                builder.Append(oldIndexes.Value);
            }

            if (newIndexes.HasValue)
            {
                builder.Append(" newIndexes=");
                builder.Append(newIndexes.Value);
            }

            return builder.ToString();
        }

        private static bool CanReuseSceneMesh(ManaRenderMeshInstance current, ManaRenderMeshInstance incoming)
        {
            if (!string.Equals(current.Name, incoming.Name, StringComparison.Ordinal) ||
                !string.Equals(current.MeshKey, incoming.MeshKey, StringComparison.Ordinal) ||
                current.MeshData.Indexes.Length != incoming.MeshData.Indexes.Length ||
                current.MeshData.Positions.Length != incoming.MeshData.Positions.Length ||
                !string.Equals(current.MeshData.AlbedoTexturePath, incoming.MeshData.AlbedoTexturePath, StringComparison.Ordinal))
            {
                return false;
            }

            if (current.MeshVersion != 0 || incoming.MeshVersion != 0)
            {
                return current.MeshVersion == incoming.MeshVersion;
            }

            return ReferenceEquals(current.MeshData, incoming.MeshData);
        }

        private static string GetSceneMeshKey(ManaRenderMeshInstance instance, int index)
        {
            if (!string.IsNullOrWhiteSpace(instance.InstanceKey))
            {
                return instance.InstanceKey;
            }

            if (!string.IsNullOrWhiteSpace(instance.MeshKey))
            {
                return $"{instance.MeshKey}:{index}";
            }

            return $"{instance.Name}:{index}";
        }

        private static List<ManaRenderMeshInstance> GetRenderableMeshes(ManaRenderScene renderScene)
        {
            return renderScene.Meshes
                .Where(instance => instance.MeshData.Positions.Length > 0 && instance.MeshData.Indexes.Length > 0)
                .ToList();
        }

        private void CreateSharedPipelineLayouts()
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

            var bindGroupLayoutEntry = new BindGroupLayoutEntry
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
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

            var bindGroupLayoutEntries = stackalloc BindGroupLayoutEntry[8];
            bindGroupLayoutEntries[0] = bindGroupLayoutEntry;
            bindGroupLayoutEntries[1] = samplerBindGroupLayoutEntry;
            bindGroupLayoutEntries[2] = textureBindGroupLayoutEntry;
            bindGroupLayoutEntries[3] = environmentMapBindGroupLayoutEntry;
            bindGroupLayoutEntries[4] = irradianceMapBindGroupLayoutEntry;
            bindGroupLayoutEntries[5] = prefilteredReflectionMapBindGroupLayoutEntry;
            bindGroupLayoutEntries[6] = brdfLutBindGroupLayoutEntry;
            bindGroupLayoutEntries[7] = clampSamplerBindGroupLayoutEntry;

            var bindGroupLayoutDescriptor = new BindGroupLayoutDescriptor
            {
                EntryCount = 8,
                Entries = bindGroupLayoutEntries
            };
            _demoBindGroupLayout = _api.DeviceCreateBindGroupLayout(_device, &bindGroupLayoutDescriptor);
            if (_demoBindGroupLayout == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU demo bind group layout.");
            }

            var sceneCaptureBindGroupLayoutEntries = stackalloc BindGroupLayoutEntry[3];
            sceneCaptureBindGroupLayoutEntries[0] = bindGroupLayoutEntry;
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
        }

        private void CreateMeshTexture(ManaMeshCpuData cpuData, out Texture* meshTexture, out TextureView* meshTextureView)
        {
            byte[] pixels;
            uint width;
            uint height;

            if (!string.IsNullOrWhiteSpace(cpuData.AlbedoTexturePath) && File.Exists(cpuData.AlbedoTexturePath))
            {
                using var image = ImageSharpImage.Load<Rgba32>(cpuData.AlbedoTexturePath);
                image.Mutate(x => x.Flip(FlipMode.Vertical));
                width = (uint)image.Width;
                height = (uint)image.Height;
                pixels = new byte[image.Width * image.Height * 4];
                image.CopyPixelDataTo(pixels);
                ManaConsole.WriteDiagnostic($"WebGPU mesh texture loaded: {cpuData.AlbedoTexturePath}");
            }
            else
            {
                width = 1;
                height = 1;
                pixels = new[] { byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue };
                ManaConsole.WriteDiagnostic("WebGPU mesh texture fallback: solid albedo color.");
            }

            CreateRgba8SrgbTexture(pixels, width, height, "mesh", out meshTexture, out meshTextureView);
        }

        private void CreateImageTexture(string texturePath, bool flipVertical, string diagnosticName, out Texture* texture, out TextureView* textureView)
        {
            using var image = ImageSharpImage.Load<Rgba32>(texturePath);
            if (flipVertical)
            {
                image.Mutate(x => x.Flip(FlipMode.Vertical));
            }

            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);
            CreateRgba8SrgbTexture(pixels, (uint)image.Width, (uint)image.Height, diagnosticName, out texture, out textureView);
            ManaConsole.WriteDiagnostic($"WebGPU {diagnosticName} texture loaded: {texturePath}");
        }

        private void CreateRgba8SrgbTexture(byte[] pixels, uint width, uint height, string diagnosticName, out Texture* texture, out TextureView* textureView)
        {
            var textureDescriptor = new TextureDescriptor
            {
                Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
                Dimension = TextureDimension.Dimension2D,
                Size = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 },
                Format = TextureFormat.Rgba8UnormSrgb,
                MipLevelCount = 1,
                SampleCount = 1
            };
            texture = _api.DeviceCreateTexture(_device, &textureDescriptor);
            if (texture == null)
            {
                throw new InvalidOperationException($"Failed to create WebGPU {diagnosticName} texture.");
            }

            fixed (byte* pixelPointer = pixels)
            {
                var destination = new ImageCopyTexture
                {
                    Texture = texture,
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
                Format = TextureFormat.Rgba8UnormSrgb,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            textureView = _api.TextureCreateView(texture, &textureViewDescriptor);
            if (textureView == null)
            {
                _api.TextureRelease(texture);
                texture = null;
                throw new InvalidOperationException($"Failed to create WebGPU {diagnosticName} texture view.");
            }
        }

        private ManaWebGpuEnvironmentReflectionMap GetOrCreateEnvironmentReflectionMap()
        {
            var source = GetEffectiveEnvironmentReflectionSource(_activeRenderScene?.EnvironmentReflectionSource);
            var environmentPath = ResolveAssetPath(source.SourcePath);
            if (_environmentReflectionMap != null &&
                HasSameEnvironmentSource(_environmentReflectionMap.Source, source))
            {
                return _environmentReflectionMap;
            }

            ReleaseEnvironmentReflectionMap();
            _environmentReflectionMap = ManaWebGpuEnvironmentReflectionMap.Create(_api, _device, _queue, source, environmentPath);
            DispatchEnvironmentPrefilterNow(_environmentReflectionMap);
            ManaConsole.WriteDiagnostic($"WebGPU environment reflection targets loaded: {environmentPath}");
            return _environmentReflectionMap;
        }

        private static ManaEnvironmentReflectionSource GetEffectiveEnvironmentReflectionSource(ManaEnvironmentReflectionSource? source)
        {
            source ??= new ManaEnvironmentReflectionSource();

            return new ManaEnvironmentReflectionSource
            {
                SourceKind = ManaEnvironmentSourceKind.SceneCapture,
                SourcePath = source.SourcePath,
                Projection = source.Projection,
                BuildReflectionCubeMap = source.BuildReflectionCubeMap,
                EnvironmentCubeSize = source.EnvironmentCubeSize,
                IrradianceCubeSize = source.IrradianceCubeSize,
                PrefilterCubeSize = source.PrefilterCubeSize,
                BrdfLutSize = source.BrdfLutSize,
                SceneCapturePosition = source.SceneCapturePosition,
                SceneCaptureNearPlane = source.SceneCaptureNearPlane,
                SceneCaptureFarPlane = source.SceneCaptureFarPlane,
                SceneCaptureRefreshIntervalFrames = source.SceneCaptureRefreshIntervalFrames
            };
        }

        private static bool IsEnvironmentFlagEnabled(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrWhiteSpace(value) &&
                !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSameEnvironmentSource(ManaEnvironmentReflectionSource current, ManaEnvironmentReflectionSource incoming)
        {
            return current.SourceKind == incoming.SourceKind &&
                string.Equals(current.SourcePath, incoming.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                current.Projection == incoming.Projection &&
                current.BuildReflectionCubeMap == incoming.BuildReflectionCubeMap &&
                current.EnvironmentCubeSize == incoming.EnvironmentCubeSize &&
                current.IrradianceCubeSize == incoming.IrradianceCubeSize &&
                current.PrefilterCubeSize == incoming.PrefilterCubeSize &&
                current.BrdfLutSize == incoming.BrdfLutSize &&
                current.SceneCapturePosition == incoming.SceneCapturePosition &&
                Math.Abs(current.SceneCaptureNearPlane - incoming.SceneCaptureNearPlane) < 0.0001f &&
                Math.Abs(current.SceneCaptureFarPlane - incoming.SceneCaptureFarPlane) < 0.0001f &&
                current.SceneCaptureRefreshIntervalFrames == incoming.SceneCaptureRefreshIntervalFrames;
        }

        private void ReleaseEnvironmentReflectionMap()
        {
            if (_environmentReflectionMap == null)
            {
                return;
            }

            _environmentReflectionMap.Dispose();
            _environmentReflectionMap = null;
            _framesSinceEnvironmentCapture = int.MaxValue;
        }

        private ManaWebGpuMeshTexture AcquireMeshTexture(ManaMeshCpuData cpuData)
        {
            var textureKey = GetMeshTextureKey(cpuData);
            if (_meshTextureCache.TryGetValue(textureKey, out var cachedTexture))
            {
                cachedTexture.RefCount++;
                return cachedTexture;
            }

            CreateMeshTexture(cpuData, out var texture, out var textureView);
            var textureResource = new ManaWebGpuMeshTexture(textureKey, texture, textureView)
            {
                RefCount = 1
            };
            _meshTextureCache[textureKey] = textureResource;
            return textureResource;
        }

        private void ReleaseMeshTexture(ManaWebGpuMeshTexture textureResource)
        {
            if (textureResource.RefCount <= 0)
            {
                return;
            }

            textureResource.RefCount--;
            if (textureResource.RefCount > 0)
            {
                return;
            }

            _meshTextureCache.Remove(textureResource.Key);
            if (textureResource.TextureView != null)
            {
                _api.TextureViewRelease(textureResource.TextureView);
                textureResource.TextureView = null;
            }

            if (textureResource.Texture != null)
            {
                _api.TextureRelease(textureResource.Texture);
                textureResource.Texture = null;
            }
        }

        private static string GetMeshTextureKey(ManaMeshCpuData cpuData)
        {
            if (!string.IsNullOrWhiteSpace(cpuData.AlbedoTexturePath) && File.Exists(cpuData.AlbedoTexturePath))
            {
                return $"file:{Path.GetFullPath(cpuData.AlbedoTexturePath)}";
            }

            return "fallback:white";
        }

        private void UpdateDemoUniforms(ManaRenderMeshInstance meshInstance, Silk.NET.WebGPU.Buffer* uniformBuffer)
        {
            if (_activeRenderScene == null)
            {
                return;
            }

            UpdateDemoUniforms(meshInstance, uniformBuffer, _activeRenderScene.Camera);
        }

        private void UpdateDemoUniforms(ManaRenderMeshInstance meshInstance, Silk.NET.WebGPU.Buffer* uniformBuffer, ManaCameraData camera)
        {
            if (uniformBuffer == null || _activeRenderScene == null)
            {
                return;
            }

            var model = meshInstance.WorldTransform;
            var view = camera.View;
            var projection = GetAspectCorrectedProjection(camera);
            var modelProjection = model * view * projection;
            if (!Matrix4x4.Invert(model, out var inverseModel))
            {
                inverseModel = Matrix4x4.Identity;
            }
            var normalMatrix = Matrix4x4.Transpose(inverseModel);
            var elapsedSeconds = (float)(DateTime.UtcNow - _startTime).TotalSeconds;
            var irradianceSettings = _activeRenderScene.IrradianceSettings;
            var lightDirection = irradianceSettings.LightDirection.LengthSquared() > 0.0001f
                ? Vector3.Normalize(irradianceSettings.LightDirection)
                : Vector3.Normalize(new Vector3(0.35f, 0.82f, 0.45f));
            
            var uniform = new ManaWebGpuTransformUniform
            {
                ModelProjection = modelProjection,
                Model = model,
                NormalMatrix = normalMatrix,
                CameraPosition = new Vector4(camera.Position, 1.0f),
                MaterialAlbedo = meshInstance.Albedo,
                MaterialFactors = new Vector4(
                    Math.Clamp(meshInstance.Metallic, 0.0f, 1.0f),
                    Math.Clamp(meshInstance.Roughness, 0.0f, 1.0f),
                    Math.Clamp(meshInstance.AmbientOcclusion, 0.0f, 1.0f),
                    1.0f),
                MaterialEmission = new Vector4(
                    meshInstance.Emission.X,
                    meshInstance.Emission.Y,
                    meshInstance.Emission.Z,
                    meshInstance.EmissionStrength),
                IrradianceFactors = new Vector4(
                    Math.Clamp(irradianceSettings.Exposure, 0.0f, 8.0f),
                    Math.Clamp(irradianceSettings.IblIntensity, 0.0f, 8.0f),
                    Math.Clamp(irradianceSettings.LightIntensity, 0.0f, 64.0f),
                    1.0f),
                LightPositions0 = _activeRenderScene.LightPositions[0],
                LightPositions1 = _activeRenderScene.LightPositions[1],
                LightPositions2 = _activeRenderScene.LightPositions[2],
                LightPositions3 = _activeRenderScene.LightPositions[3],
                LightColors0 = _activeRenderScene.LightColors[0],
                LightColors1 = _activeRenderScene.LightColors[1],
                LightColors2 = _activeRenderScene.LightColors[2],
                LightColors3 = _activeRenderScene.LightColors[3],
                Time = elapsedSeconds,
                WebGpuDebugMode = meshInstance.PbrDebugMode ?? irradianceSettings.WebGpuDebugMode,
                _padding1 = 0.0f,
                _padding2 = 0.0f
            };

            _api.QueueWriteBuffer(_queue, uniformBuffer, 0, &uniform, (nuint)sizeof(ManaWebGpuTransformUniform));
        }

        private RenderPipeline* GetOrCreatePipeline(string? shaderName, bool isTransparent = false, bool disableDepthWrite = false)
        {
            shaderName ??= "ManaPbrShader";
            
            string pipelineKey = $"{shaderName}_T{(isTransparent ? 1 : 0)}_D{(disableDepthWrite ? 1 : 0)}";
            if (_pipelines.TryGetValue(pipelineKey, out var cachedPipeline))
            {
                return (RenderPipeline*)cachedPipeline;
            }

            string shaderFile = shaderName switch
            {
                "ManaLightningBallShader" => "lightning_ball.wgsl",
                "ManaLiquidBallShader" => "liquid_ball.wgsl",
                "ManaLiquidShader" => "liquid.wgsl",
                "ManaGridShader" => "grid.wgsl",
                "ManaEnvironmentIrradianceShader" => "environment_irradiance.wgsl",
                "ManaPbrShader" => "pbr_irradiance.wgsl",
                _ => "pbr_irradiance.wgsl"
            };

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
            var pipeline = CreateDemoRenderPipeline(shaderModule, GetSceneColorFormat(), _demoPipelineLayout, shaderName, isTransparent, disableDepthWrite);
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

        private ShaderModule* CreateShaderModule(string relativePath)
        {
            var path = ResolveAssetPath(relativePath);
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

        private static float DegreesToRadians(float degrees)
        {
            return degrees * MathF.PI / 180.0f;
        }

        private static ManaCameraData CreateSceneCaptureCamera(ManaEnvironmentReflectionSource source, int face)
        {
            var position = source.SceneCapturePosition;
            var nearPlane = Math.Max(0.001f, source.SceneCaptureNearPlane);
            var farPlane = Math.Max(nearPlane + 1.0f, source.SceneCaptureFarPlane);
            var direction = face switch
            {
                0 => Vector3.UnitX,
                1 => -Vector3.UnitX,
                2 => Vector3.UnitY,
                3 => -Vector3.UnitY,
                4 => Vector3.UnitZ,
                _ => -Vector3.UnitZ
            };
            var up = face switch
            {
                2 => -Vector3.UnitZ,
                3 => Vector3.UnitZ,
                _ => Vector3.UnitY
            };

            var view = Matrix4x4.CreateLookAt(position, position + direction, up);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI * 0.5f, 1.0f, nearPlane, farPlane);
            return new ManaCameraData(view, projection, position, nearPlane, farPlane);
        }

        private Matrix4x4 GetAspectCorrectedProjection(ManaCameraData camera)
        {
            if (_framebufferSize.Y <= 0)
            {
                return camera.Projection;
            }

            return camera.Projection;
        }

        private static Matrix4x4 CreateNormalizedWorldTransform(ManaMeshCpuData meshData)
        {
            if (meshData.Positions.Length == 0)
            {
                return Matrix4x4.Identity;
            }

            var min = meshData.Positions[0];
            var max = meshData.Positions[0];
            foreach (var position in meshData.Positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }

            var center = (min + max) * 0.5f;
            var extents = max - min;
            var maxExtent = MathF.Max(extents.X, MathF.Max(extents.Y, extents.Z));
            var scale = maxExtent <= 0.0001f ? 1.0f : 2.0f / maxExtent;
            return Matrix4x4.CreateTranslation(-center) * Matrix4x4.CreateScale(scale);
        }

        private unsafe sealed class ManaWebGpuMeshTexture
        {
            public ManaWebGpuMeshTexture(string key, Texture* texture, TextureView* textureView)
            {
                Key = key;
                Texture = texture;
                TextureView = textureView;
            }

            public string Key { get; }
            public Texture* Texture { get; set; }
            public TextureView* TextureView { get; set; }
            public int RefCount { get; set; }
        }

        private sealed class ManaWebGpuSceneMesh : IDisposable
        {
            private readonly WebGPU _api;
            private readonly Action<ManaWebGpuMeshTexture> _releaseTexture;

            public ManaWebGpuSceneMesh(
                WebGPU api,
                ManaRenderMeshInstance instance,
                ManaWebGpuMesh mesh,
                Silk.NET.WebGPU.Buffer* uniformBuffer,
                ManaWebGpuMeshTexture texture,
                Action<ManaWebGpuMeshTexture> releaseTexture,
                BindGroup* bindGroup,
                nint[] sceneCaptureUniformBuffers,
                nint[] sceneCaptureBindGroups)
            {
                _api = api;
                _releaseTexture = releaseTexture;
                Instance = instance;
                Mesh = mesh;
                UniformBuffer = uniformBuffer;
                Texture = texture;
                BindGroup = bindGroup;
                SceneCaptureUniformBuffers = sceneCaptureUniformBuffers;
                SceneCaptureBindGroups = sceneCaptureBindGroups;
            }

            public ManaRenderMeshInstance Instance { get; set; }
            public ManaWebGpuMesh Mesh { get; }
            public Silk.NET.WebGPU.Buffer* UniformBuffer { get; }
            public ManaWebGpuMeshTexture Texture { get; }
            public BindGroup* BindGroup { get; }
            public nint[] SceneCaptureUniformBuffers { get; private set; }
            public nint[] SceneCaptureBindGroups { get; private set; }

            public Silk.NET.WebGPU.Buffer* GetSceneCaptureUniformBuffer(int face)
            {
                return (Silk.NET.WebGPU.Buffer*)SceneCaptureUniformBuffers[Math.Clamp(face, 0, SceneCaptureUniformBuffers.Length - 1)];
            }

            public BindGroup* GetSceneCaptureBindGroup(int face)
            {
                return (BindGroup*)SceneCaptureBindGroups[Math.Clamp(face, 0, SceneCaptureBindGroups.Length - 1)];
            }

            public void Dispose()
            {
                Mesh.Dispose();

                foreach (var sceneCaptureBindGroup in SceneCaptureBindGroups)
                {
                    if (sceneCaptureBindGroup != 0)
                    {
                        _api.BindGroupRelease((BindGroup*)sceneCaptureBindGroup);
                    }
                }
                SceneCaptureBindGroups = Array.Empty<nint>();

                foreach (var sceneCaptureUniformBuffer in SceneCaptureUniformBuffers)
                {
                    if (sceneCaptureUniformBuffer != 0)
                    {
                        _api.BufferRelease((Silk.NET.WebGPU.Buffer*)sceneCaptureUniformBuffer);
                    }
                }
                SceneCaptureUniformBuffers = Array.Empty<nint>();

                if (BindGroup != null)
                {
                    _api.BindGroupRelease(BindGroup);
                }
                _releaseTexture(Texture);
                if (UniformBuffer != null)
                {
                    _api.BufferRelease(UniformBuffer);
                }
            }
        }

        private static ManaRenderMeshInstance SelectPrimaryMesh(ManaRenderScene renderScene)
        {
            var startupVoxels = renderScene.Meshes.FirstOrDefault(mesh =>
                mesh.Name.Contains("Startup Voxels", StringComparison.OrdinalIgnoreCase) ||
                mesh.Name.Contains("Voxel", StringComparison.OrdinalIgnoreCase));

            if (startupVoxels != null)
            {
                return startupVoxels;
            }

            return renderScene.Meshes
                .OrderByDescending(mesh => mesh.MeshData.Indexes.Length)
                .First();
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
            public Matrix4x4 Model;
            public Matrix4x4 NormalMatrix;
            public Vector4 CameraPosition;
            public Vector4 MaterialAlbedo;
            public Vector4 MaterialFactors;
            public Vector4 MaterialEmission;
            public Vector4 IrradianceFactors;
            public Vector4 LightPositions0;
            public Vector4 LightPositions1;
            public Vector4 LightPositions2;
            public Vector4 LightPositions3;
            public Vector4 LightColors0;
            public Vector4 LightColors1;
            public Vector4 LightColors2;
            public Vector4 LightColors3;
            public float Time;
            public float WebGpuDebugMode;
            public float _padding1;
            public float _padding2;
        }
    }
}

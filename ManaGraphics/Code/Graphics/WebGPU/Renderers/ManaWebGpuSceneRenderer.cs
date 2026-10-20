using System.Diagnostics;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaGraphics.Code.Graphics.Pipelines;
using ManaGraphics.Code.Graphics.Pipelines.Overlays;
using ManaGraphics.Code.Graphics.Resources;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Renderers
{
    /// <summary>
    /// Draws the scene: owns the shadow map, the shared scene bindings, HDR post-processing and the registered passes, and
    /// encodes the scene half of a frame -- passes' preparation, the scene pass, what runs after it, and HDR post. The
    /// context then tone-maps into the surface (<see cref="DrawToneMap"/>) and draws the UI over it.
    /// </summary>
    internal sealed unsafe class ManaWebGpuSceneRenderer : ManaWebGpuDeviceObject
    {
        private readonly ManaWebGpuEnvironment _environment;
        private readonly ManaWebGpuSceneMeshes _meshes;
        private readonly ManaWebGpuShadowMap _shadowMap;
        private readonly ManaWebGpuHdrPostProcessor _hdr;
        private readonly ManaWebGpuParticlePass _particles;
        // In frame order; each owns its pipelines (see ManaWebGpuRenderPass).
        private readonly List<ManaWebGpuRenderPass> _passes = new();
        private readonly ManaWebGpuFrame _frame = new();
        private ManaWebGpuSceneBindings? _bindings;

        public ManaWebGpuSceneRenderer(
            ManaWebGpuDevice gpu, ManaWebGpuEnvironment environment, ManaWebGpuSceneMeshes meshes, Vector2D<int> size,
            Func<bool> sceneCaptureEnabled)
            : base(gpu)
        {
            _environment = environment;
            _meshes = meshes;
            ManaWebGpuShadowMap? shadowMap = null;
            ManaWebGpuHdrPostProcessor? hdr = null;
            try
            {
                // The frame group binds the shadow map, so the map comes first.
                shadowMap = new ManaWebGpuShadowMap(gpu);
                _bindings = new ManaWebGpuSceneBindings(gpu, shadowMap.ArrayView, shadowMap.Sampler);
                hdr = new ManaWebGpuHdrPostProcessor(gpu, gpu.SurfaceFormat,
                    ManaWebGpuDevice.ResolveAssetPath(ManaWebGpuDevice.ShaderPath("hdr_tonemap.wgsl")));
                hdr.Resize(size);
                var sceneColor = hdr.SceneFormat;

                // Before the shadows, as before anything samples the reflection cubes.
                AddPass(new ManaWebGpuEnvironmentCapturePass(gpu, environment, sceneCaptureEnabled));
                AddPass(new ManaWebGpuShadowPass(gpu, _bindings, shadowMap));
                // First in the scene pass, so ordinary geometry draws over it: the sky is a background.
                AddPass(new ManaWebGpuSkyPass(gpu));
                AddPass(new ManaWebGpuMaterialPass(gpu, _bindings, sceneColor));
                _particles = AddPass(new ManaWebGpuParticlePass(gpu, sceneColor));
                AddPass(new ManaWebGpuWireframeOverlay(gpu, _bindings, sceneColor));
                AddPass(new ManaWebGpuNormalLinesOverlay(gpu, _bindings, sceneColor));
                AddPass(new ManaWebGpuOutlinePass(gpu, _bindings));
            }
            catch
            {
                _shadowMap = shadowMap!;
                _hdr = hdr!;
                Dispose();
                throw;
            }
            _shadowMap = shadowMap;
            _hdr = hdr;
        }

        /// <summary>Whether the scene pipelines run on <paramref name="requestedBackend"/>: on Windows only Vulkan, while the
        /// Direct3D paths remain unstable.</summary>
        public static bool SupportsBackend(string? requestedBackend)
        {
            return !System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
                || string.IsNullOrWhiteSpace(requestedBackend)
                || requestedBackend.Trim().ToLowerInvariant() is "vulkan" or "vk";
        }

        /// <summary>MANA_WEBGPU_DEMO_MESH=1: build the scene pipelines over a demo mesh when the app supplies no scene.</summary>
        public static bool DemoRequested
        {
            get
            {
                var value = Environment.GetEnvironmentVariable("MANA_WEBGPU_DEMO_MESH") ??
                    Environment.GetEnvironmentVariable("MANA_WEBGPU_UNSAFE_DEMO_MESH");
                return value == "1" ||
                    value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ||
                    value?.Equals("yes", StringComparison.OrdinalIgnoreCase) == true;
            }
        }

        public ManaWebGpuSceneBindings Bindings => _bindings!;
        public TextureFormat SceneColorFormat => _hdr.SceneFormat;
        public long ParticlesDrawn => _particles.Drawn;
        public long ParticlesRequested => _particles.Requested;
        public int PipelineCount => _passes.Sum(pass => pass.PipelineCount);
        public int ShaderModuleCount => _passes.Sum(pass => pass.ShaderModuleCount);

        /// <summary>CPU time spent encoding the scene pass and what follows it, last frame.</summary>
        public double LastDrawMs { get; private set; }

        private T AddPass<T>(T pass) where T : ManaWebGpuRenderPass
        {
            _passes.Add(pass);
            return pass;
        }

        public void Resize(Vector2D<int> size)
        {
            _hdr.Resize(size);
        }

        /// <summary>Fits the shadow cascades to the camera and writes the frame uniform every pass binds.</summary>
        public void UpdateFrame(ManaRenderScene scene, float seconds)
        {
            _shadowMap.UpdateCascades(scene);
            _bindings!.WriteFrame(Queue, ManaWebGpuFrameUniform.For(scene, _shadowMap, seconds));
        }

        /// <summary>
        /// Encodes the scene into the HDR target, or straight into <paramref name="surfaceView"/> when HDR has no target
        /// yet. Returns true when it drew into the surface, so the tone map must load rather than clear it. While
        /// <paramref name="suspended"/> (loading) the scene pass only clears.
        /// </summary>
        public bool Encode(
            CommandEncoder* encoder, ManaRenderScene scene, ManaWebGpuWindowSurface surface, TextureView* surfaceView,
            Color clearColor, bool suspended, float seconds)
        {
            var hdrView = _hdr.SceneTextureView;
            var useHdr = hdrView != null;
            var sceneTarget = useHdr ? hdrView : surfaceView;
            var frame = FillFrame(encoder, scene, surface, sceneTarget, useHdr, suspended, seconds);

            if (!suspended)
            {
                foreach (var pass in _passes)
                {
                    using (ManaFrameProfiler.Measure(pass.ProfileName))
                    {
                        pass.Prepare(frame);
                    }
                }
            }

            var colorAttachment = new RenderPassColorAttachment
            {
                View = sceneTarget,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = clearColor
            };
            var depthAttachment = new RenderPassDepthStencilAttachment
            {
                View = surface.Depth,
                DepthLoadOp = LoadOp.Clear,
                DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1.0f,
                DepthReadOnly = false,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
                StencilClearValue = 0,
                StencilReadOnly = true
            };
            var descriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
                DepthStencilAttachment = &depthAttachment
            };

            var drawStopwatch = Stopwatch.StartNew();
            var scenePass = Api.CommandEncoderBeginRenderPass(encoder, &descriptor);
            try
            {
                if (!suspended)
                {
                    foreach (var pass in _passes)
                    {
                        pass.DrawScene(frame, scenePass);
                    }
                }

                Api.RenderPassEncoderEnd(scenePass);
            }
            finally
            {
                Api.RenderPassEncoderRelease(scenePass);
            }

            foreach (var pass in _passes)
            {
                pass.AfterScene(frame);
            }
            LastDrawMs = drawStopwatch.Elapsed.TotalMilliseconds;

            if (useHdr)
            {
                using (ManaFrameProfiler.Measure("GPU: Outline + HDR Post"))
                {
                    _hdr.Process(encoder);
                }
            }

            return !useHdr;
        }

        /// <summary>Tone-maps the HDR scene into the surface pass the UI then draws over.</summary>
        public void DrawToneMap(RenderPassEncoder* pass)
        {
            _hdr.Draw(pass);
        }

        private ManaWebGpuFrame FillFrame(
            CommandEncoder* encoder, ManaRenderScene scene, ManaWebGpuWindowSurface surface, TextureView* sceneTarget,
            bool useHdr, bool suspended, float seconds)
        {
            _frame.Encoder = encoder;
            _frame.Scene = scene;
            _frame.Meshes = _meshes.Meshes;
            _frame.Bindings = _bindings!;
            _frame.SceneColorFormat = _hdr.SceneFormat;
            _frame.Depth = surface.Depth;
            _frame.HandleDepth = surface.HandleDepth;
            _frame.Projection = scene.Camera.Projection;
            _frame.Time = seconds;
            _frame.SceneTarget = sceneTarget;
            _frame.OutlineMask = useHdr ? _hdr.OutlineMaskView : null;
            var environment = _environment.Map;
            _frame.EnvironmentMap = environment == null ? null : environment.EnvironmentMapResource.TextureView;
            _frame.SceneSuspended = suspended;
            return _frame;
        }

        public override void Dispose()
        {
            // Passes build over the shared bindings, and the frame group binds the shadow map.
            for (var i = _passes.Count - 1; i >= 0; i--)
            {
                _passes[i].Dispose();
            }
            _passes.Clear();
            _hdr?.Dispose();
            _bindings?.Dispose();
            _bindings = null;
            _shadowMap?.Dispose();
        }
    }
}

using System.Numerics;
using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using ManaGraphics.Code.Graphics.Renderers;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>
    /// Redraws the reflection cubes before anything samples them: the computed sky whenever it moves, and the scene when
    /// scene capture is on. Each redraw is prefiltered.
    /// </summary>
    internal sealed unsafe class ManaWebGpuEnvironmentCapturePass : ManaWebGpuRenderPass
    {
        // A moving sky is redrawn at most this often.
        private const int SkyRedrawFrames = 20;

        private readonly ManaWebGpuEnvironment _environment;
        private readonly Func<bool> _sceneCaptureEnabled;
        private ManaWebGpuSkybox? _skybox;
        private ShaderModule* _captureShader;
        private RenderPipeline* _capturePipeline;
        private ManaWebGpuDepthTarget? _captureDepth;
        private ManaWebGpuEnvironmentReflectionMap? _capturedMap;
        private int _framesSinceCapture = int.MaxValue;

        public ManaWebGpuEnvironmentCapturePass(ManaWebGpuDevice gpu, ManaWebGpuEnvironment environment, Func<bool> sceneCaptureEnabled)
            : base(gpu)
        {
            _environment = environment;
            _sceneCaptureEnabled = sceneCaptureEnabled;
        }

        public override string ProfileName => "GPU: Environment Capture";
        public override int PipelineCount => (_skybox != null ? 1 : 0) + (_capturePipeline != null ? 1 : 0);
        public override int ShaderModuleCount => _captureShader != null ? 1 : 0;

        public override void Prepare(ManaWebGpuFrame frame)
        {
            DrawSky(frame);
            CaptureScene(frame);
        }

        private void DrawSky(ManaWebGpuFrame frame)
        {
            var scene = frame.Scene;
            var map = _environment.Map;
            if (map == null || scene.Sky.Mode == ManaSkyMode.Photo)
            {
                return;
            }

            var sky = scene.Sky with { TowardSun = ManaWebGpuSkyPass.TowardSun(scene) };
            map.FramesSinceSkyDrawn++;
            if (map.DrawnSky is { } drawn && (!SkyMoved(drawn, sky) || map.FramesSinceSkyDrawn < SkyRedrawFrames))
            {
                return;
            }

            var environmentMap = map.EnvironmentMapResource;
            var prefilteredMap = map.PrefilteredReflectionMapResource;
            if (environmentMap.RenderTargetViews.Count < 6 || prefilteredMap.TextureView == null)
            {
                return;
            }

            _skybox ??= new ManaWebGpuSkybox(
                Gpu, null, ManaWebGpuSkyPass.ShaderPath, 6, ManaWebGpuSkyPass.MoonTexturePath,
                starCatalogue: ManaWebGpuSkyPass.Mode >= 1);

            for (var face = 0; face < 6; face++)
            {
                // Bound only to fill the layout -- a computed sky never samples it -- and not a cube drawn into here.
                DrawSkyFace(frame, (TextureView*)environmentMap.RenderTargetViews[face], CubeFaceCamera(face), sky,
                    (TextureView*)prefilteredMap.TextureView, face);
            }

            _environment.Prefilter(frame.Encoder, map);
            map.DrawnSky = sky;
            map.HasPhoto = false;
            map.FramesSinceSkyDrawn = 0;
        }

        private static bool SkyMoved(ManaSky.State drawn, ManaSky.State sky)
        {
            var moonPhaseDelta = MathF.Abs(drawn.MoonPhase - sky.MoonPhase);
            moonPhaseDelta = MathF.Min(moonPhaseDelta, 1.0f - moonPhaseDelta);
            return drawn.Mode != sky.Mode ||
                Vector3.Dot(drawn.TowardSun, sky.TowardSun) < 0.99985f ||
                (sky.Mode == ManaSkyMode.DayNightCycle && Vector3.Dot(drawn.TowardMoon, sky.TowardMoon) < 0.99985f) ||
                MathF.Abs(drawn.Daylight - sky.Daylight) > 0.01f ||
                drawn.ConstellationLines != sky.ConstellationLines ||
                (sky.Mode == ManaSkyMode.DayNightCycle && Vector3.Dot(drawn.CelestialEast, sky.CelestialEast) < 0.99985f) ||
                moonPhaseDelta > 0.001f;
        }

        private void DrawSkyFace(ManaWebGpuFrame frame, TextureView* faceView, ManaCameraData camera, ManaSky.State sky, TextureView* unusedView, int face)
        {
            var colorAttachment = new RenderPassColorAttachment
            {
                View = faceView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color(0.0, 0.0, 0.0, 1.0)
            };

            var renderPassDescriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment
            };

            var renderPass = Api.CommandEncoderBeginRenderPass(frame.Encoder, &renderPassDescriptor);
            try
            {
                _skybox!.Draw(
                    renderPass, TextureFormat.Rgba8UnormSrgb, frame.Bindings.ClampSampler, unusedView, camera, camera.Projection,
                    sky.TowardSun, sky, ManaSky.Seconds, face);
                Api.RenderPassEncoderEnd(renderPass);
            }
            finally
            {
                Api.RenderPassEncoderRelease(renderPass);
            }
        }

        /// <summary>A camera looking out of one cube face, in WebGPU's face order and orientation (+X -X +Y -Y +Z -Z).</summary>
        private static ManaCameraData CubeFaceCamera(int face)
        {
            var (forward, right, up) = face switch
            {
                0 => (Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),
                1 => (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
                2 => (Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ),
                3 => (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
                4 => (Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
                _ => (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY)
            };

            // View space looks down -Z: rows of the basis, written as System.Numerics' row-vector matrix.
            var back = -forward;
            var view = new Matrix4x4(
                right.X, up.X, back.X, 0.0f,
                right.Y, up.Y, back.Y, 0.0f,
                right.Z, up.Z, back.Z, 0.0f,
                0.0f, 0.0f, 0.0f, 1.0f);
            var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI * 0.5f, 1.0f, 0.1f, 100.0f);
            return new ManaCameraData(view, projection, Vector3.Zero, 0.1f, 100.0f);
        }

        private void CaptureScene(ManaWebGpuFrame frame)
        {
            if (!_sceneCaptureEnabled() || frame.Meshes.Count == 0)
            {
                return;
            }

            var source = ManaWebGpuEnvironment.EffectiveSource(frame.Scene.EnvironmentReflectionSource);
            if (source.SourceKind != ManaEnvironmentSourceKind.SceneCapture)
            {
                return;
            }

            var map = _environment.EnsureMap(frame.Scene);
            if (!ReferenceEquals(map, _capturedMap))
            {
                // A new map has never been captured into: do it now rather than after the interval.
                _capturedMap = map;
                _framesSinceCapture = int.MaxValue;
            }

            var refreshInterval = Math.Max(1, source.SceneCaptureRefreshIntervalFrames);
            if (_framesSinceCapture < refreshInterval)
            {
                _framesSinceCapture++;
                return;
            }

            var environmentMap = map.EnvironmentMapResource;
            if (environmentMap.RenderTargetViews.Count < 6)
            {
                return;
            }

            var pipeline = GetOrCreateCapturePipeline(frame.Bindings);
            ManaConsole.WriteDiagnostic($"WebGPU scene environment capture: position={source.SceneCapturePosition}, meshes={frame.Meshes.Count(mesh => mesh.Instance.IncludeInEnvironmentCapture)}.");

            if (_captureDepth == null || _captureDepth.Width != environmentMap.Width)
            {
                _captureDepth?.Dispose();
                _captureDepth = null;
                _captureDepth = new ManaWebGpuDepthTarget(Gpu, environmentMap.Width, environmentMap.Width, "scene capture depth");
            }

            for (var face = 0; face < 6; face++)
            {
                CaptureFace(frame, (TextureView*)environmentMap.RenderTargetViews[face], pipeline, CaptureCamera(source, face), face);
            }

            _environment.Prefilter(frame.Encoder, map);
            _framesSinceCapture = 0;
        }

        private void CaptureFace(ManaWebGpuFrame frame, TextureView* faceView, RenderPipeline* pipeline, ManaCameraData camera, int face)
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
                View = _captureDepth!.View,
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

            var renderPass = Api.CommandEncoderBeginRenderPass(frame.Encoder, &renderPassDescriptor);
            try
            {
                Api.RenderPassEncoderSetPipeline(renderPass, pipeline);
                foreach (var sceneMesh in frame.Meshes)
                {
                    if (!sceneMesh.Instance.IncludeInEnvironmentCapture)
                    {
                        continue;
                    }

                    var uniform = ManaWebGpuSceneCaptureUniform.For(sceneMesh.Instance, camera, frame.Scene, frame.Time);
                    var buffer = sceneMesh.GetSceneCaptureUniformBuffer(face);
                    if (buffer != null)
                    {
                        Api.QueueWriteBuffer(Queue, buffer, 0, &uniform, (nuint)sizeof(ManaWebGpuSceneCaptureUniform));
                    }
                    Api.RenderPassEncoderSetBindGroup(renderPass, 0, sceneMesh.GetSceneCaptureBindGroup(face), 0, null);
                    sceneMesh.Mesh.Draw(renderPass);
                }

                Api.RenderPassEncoderEnd(renderPass);
            }
            finally
            {
                Api.RenderPassEncoderRelease(renderPass);
            }
        }

        private static ManaCameraData CaptureCamera(ManaEnvironmentReflectionSource source, int face)
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

        private RenderPipeline* GetOrCreateCapturePipeline(ManaWebGpuSceneBindings bindings)
        {
            if (_capturePipeline != null)
            {
                return _capturePipeline;
            }

            _captureShader = Gpu.CreateShaderModule("scene_capture.wgsl");
            _capturePipeline = ManaWebGpuMeshPipeline.Create(Gpu, _captureShader, TextureFormat.Rgba8UnormSrgb, bindings.SceneCaptureLayout, "SceneCapture");
            ManaConsole.WriteDiagnostic("WebGPU scene capture pipeline created successfully.");
            return _capturePipeline;
        }

        public override void Dispose()
        {
            _skybox?.Dispose();
            _skybox = null;
            _captureDepth?.Dispose();
            _captureDepth = null;

            if (_capturePipeline != null)
            {
                Api.RenderPipelineRelease(_capturePipeline);
                _capturePipeline = null;
            }

            if (_captureShader != null)
            {
                Api.ShaderModuleRelease(_captureShader);
                _captureShader = null;
            }
        }
    }
}

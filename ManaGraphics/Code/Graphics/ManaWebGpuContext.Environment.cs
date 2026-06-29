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
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
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

    }
}

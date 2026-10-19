using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        private void ReleaseDemoResources()
        {
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
            ReleaseThumbnailResources();

            _environmentPrefilterCompute?.Dispose();
            _environmentPrefilterCompute = null;

            // Passes build over the shared bindings, and the frame group binds the shadow map.
            ReleasePasses();
            _bindings?.Dispose();
            _bindings = null;
            _shadowMap?.Dispose();
            _shadowMap = null;

            ReleaseSceneMeshes(immediate: true);
            ReleasePendingEnvironmentWork();
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
                // CopySrc as well, so the composited frame can be snapshotted for the UI's blurred backdrop.
                // Nothing copies it unless a panel asks for blur; the usage flag alone costs nothing.
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
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
            _handleDepth?.Dispose();
            _handleDepth = null;
            _depth?.Dispose();
            _depth = null;
        }

        private void CreateDepthResources(Vector2D<int> framebufferSize)
        {
            ReleaseDepthResources();

            var width = (uint)framebufferSize.X;
            var height = (uint)framebufferSize.Y;
            // Assigned one at a time: if the second cannot be made the first is still released with the rest.
            _depth = new ManaWebGpuDepthTarget(_api, _device, _depthFormat, width, height, "depth");
            _handleDepth = new ManaWebGpuDepthTarget(_api, _device, _depthFormat, width, height, "handle depth");
        }

        private void ReleaseSceneCaptureDepthResources()
        {
            _sceneCaptureDepth?.Dispose();
            _sceneCaptureDepth = null;
        }

        private void EnsureSceneCaptureDepthResources(uint size)
        {
            if (_sceneCaptureDepth != null && _sceneCaptureDepth.Width == size)
            {
                return;
            }

            ReleaseSceneCaptureDepthResources();
            _sceneCaptureDepth = new ManaWebGpuDepthTarget(_api, _device, _depthFormat, size, size, "scene capture depth");
        }

    }
}

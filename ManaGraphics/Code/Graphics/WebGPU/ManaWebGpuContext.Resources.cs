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

            if (_outlineMaskPipeline != null)
            {
                _api.RenderPipelineRelease(_outlineMaskPipeline);
                _outlineMaskPipeline = null;
            }

            if (_outlineMaskAlwaysOnTopPipeline != null)
            {
                _api.RenderPipelineRelease(_outlineMaskAlwaysOnTopPipeline);
                _outlineMaskAlwaysOnTopPipeline = null;
            }

            foreach (var overlayPipeline in _overlayPipelines.Values)
            {
                if (overlayPipeline != 0)
                {
                    _api.RenderPipelineRelease((RenderPipeline*)overlayPipeline);
                }
            }

            _overlayPipelines.Clear();

            _hdrPostProcessor?.Dispose();
            _hdrPostProcessor = null;

            ReleaseSceneCaptureDepthResources();
            ReleaseThumbnailResources();

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

            if (_customUniformPipelineLayout != null)
            {
                _api.PipelineLayoutRelease(_customUniformPipelineLayout);
                _customUniformPipelineLayout = null;
            }

            if (_customUniformBindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_customUniformBindGroupLayout);
                _customUniformBindGroupLayout = null;
            }

            if (_demoSampler != null)
            {
                _api.SamplerRelease(_demoSampler);
                _demoSampler = null;
            }

            if (_nearestSampler != null)
            {
                _api.SamplerRelease(_nearestSampler);
                _nearestSampler = null;
            }

            if (_clampSampler != null)
            {
                _api.SamplerRelease(_clampSampler);
                _clampSampler = null;
            }

            if (_objectBindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_objectBindGroupLayout);
                _objectBindGroupLayout = null;
            }

            if (_frameBindGroup != null)
            {
                _api.BindGroupRelease(_frameBindGroup);
                _frameBindGroup = null;
            }

            if (_frameUniformBuffer != null)
            {
                _api.BufferRelease(_frameUniformBuffer);
                _frameUniformBuffer = null;
            }

            if (_frameBindGroupLayout != null)
            {
                _api.BindGroupLayoutRelease(_frameBindGroupLayout);
                _frameBindGroupLayout = null;
            }

            ReleaseShadowResources();

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

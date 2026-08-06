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

    }
}

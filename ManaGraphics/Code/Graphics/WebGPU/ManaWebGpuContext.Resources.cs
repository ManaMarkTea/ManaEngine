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
        // Shutdown and failed pipeline initialization both come through here; anything may still be null.
        private void ReleaseDemoResources()
        {
            _hdrPostProcessor?.Dispose();
            _hdrPostProcessor = null;
            _thumbnails?.Dispose();
            _thumbnails = null;

            // Meshes first: they bind the environment and shadow views the owners below release.
            _meshes?.ReleaseAll(immediate: true);
            // Passes build over the shared bindings, and the frame group binds the shadow map.
            ReleasePasses();
            _bindings?.Dispose();
            _bindings = null;
            _shadowMap?.Dispose();
            _shadowMap = null;
            _environment?.Release();
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
    }
}

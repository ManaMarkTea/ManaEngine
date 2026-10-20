using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>
    /// The window's swap chain: configured at the framebuffer size with the scene's depth targets alongside, acquired
    /// once a frame and presented. The surface handle itself is the context's (made before the device, released after).
    /// </summary>
    internal sealed unsafe class ManaWebGpuWindowSurface : ManaWebGpuDeviceObject
    {
        private readonly Surface* _surface;
        private ManaWebGpuDepthTarget? _depth;
        private ManaWebGpuDepthTarget? _handleDepth;

        public ManaWebGpuWindowSurface(ManaWebGpuDevice gpu, Surface* surface)
            : base(gpu)
        {
            _surface = surface;
        }

        public bool IsConfigured { get; private set; }

        /// <summary>The size the surface was configured at, which is the frame's: mid-resize the window reports its new
        /// size a frame before the surface catches up.</summary>
        public Vector2D<int> Size { get; private set; }

        public TextureView* Depth => _depth == null ? null : _depth.View;

        /// <summary>The always-on-top handles depth-test against one another here, not against the scene.</summary>
        public TextureView* HandleDepth => _handleDepth == null ? null : _handleDepth.View;

        /// <summary>(Re)configures for <paramref name="size"/>; false for an empty size, which leaves it as it was.</summary>
        public bool Configure(Vector2D<int> size)
        {
            if (size.X <= 0 || size.Y <= 0)
            {
                return false;
            }

            if (IsConfigured)
            {
                Api.SurfaceUnconfigure(_surface);
            }

            var configuration = new SurfaceConfiguration
            {
                Device = Device,
                Format = Gpu.SurfaceFormat,
                // CopySrc so the UI's blur can snapshot the frame; free unless used.
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
                Width = (uint)size.X,
                Height = (uint)size.Y,
                PresentMode = PresentMode.Fifo,
                AlphaMode = CompositeAlphaMode.Opaque
            };

            Api.SurfaceConfigure(_surface, &configuration);
            Size = size;
            ReleaseDepth();
            // One at a time: if the second cannot be made the first is still released with the rest.
            _depth = new ManaWebGpuDepthTarget(Gpu, (uint)size.X, (uint)size.Y, "depth");
            _handleDepth = new ManaWebGpuDepthTarget(Gpu, (uint)size.X, (uint)size.Y, "handle depth");
            IsConfigured = true;
            return true;
        }

        /// <summary>
        /// This frame's texture and a view of it, both the caller's to release; false when there is none this frame
        /// (acquire fails during startup or resize: reconfigure and try next frame).
        /// </summary>
        public bool TryAcquire(out Texture* texture, out TextureView* view)
        {
            view = null;
            var surfaceTexture = new SurfaceTexture();
            Api.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
            texture = surfaceTexture.Texture;
            if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success || texture == null)
            {
                if (texture != null)
                {
                    Api.TextureRelease(texture);
                    texture = null;
                }

                return false;
            }

            var viewDescriptor = new TextureViewDescriptor
            {
                Format = Gpu.SurfaceFormat,
                Dimension = TextureViewDimension.Dimension2D,
                MipLevelCount = 1,
                ArrayLayerCount = 1,
                Aspect = TextureAspect.All
            };
            view = Api.TextureCreateView(texture, &viewDescriptor);
            if (view == null)
            {
                Api.TextureRelease(texture);
                texture = null;
                return false;
            }

            return true;
        }

        public void Present()
        {
            Api.SurfacePresent(_surface);
        }

        private void ReleaseDepth()
        {
            _handleDepth?.Dispose();
            _handleDepth = null;
            _depth?.Dispose();
            _depth = null;
        }

        public override void Dispose()
        {
            if (IsConfigured)
            {
                Api.SurfaceUnconfigure(_surface);
                IsConfigured = false;
            }

            ReleaseDepth();
        }
    }
}

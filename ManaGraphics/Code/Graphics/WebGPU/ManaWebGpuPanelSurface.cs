#if !MANA_BROWSER
using ManaUI;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// A UI-only presentation surface for a native panel window, drawing on the editor's device and queue. Borrows the
    /// API, instance, adapter, device and queue; owns its surface and UI renderer. Made by
    /// <see cref="ManaWebGpuContext.CreateUiSurface"/>, which disposes any still open before it releases the device.
    /// </summary>
    public sealed unsafe class ManaWebGpuPanelSurface : IUiWindowRenderer
    {
        private readonly WebGPU _api;
        private readonly Device* _device;
        private readonly Queue* _queue;
        private readonly IWindow _window;
        private readonly Action<ManaWebGpuPanelSurface> _closed;
        private Surface* _surface;
        private readonly TextureFormat _format;
        private readonly PresentMode _presentMode;
        private ManaWebGpuUiRenderer? _renderer;
        private Vector2D<int> _size;
        private bool _configured;

        internal ManaWebGpuPanelSurface(
            WebGPU api, Instance* instance, Adapter* adapter, Device* device, Queue* queue, IWindow window,
            Action<ManaWebGpuPanelSurface> closed)
        {
            _api = api;
            _device = device;
            _queue = queue;
            _window = window;
            _closed = closed;
            try
            {
                _surface = WebGPUSurface.CreateWebGPUSurface(window, api, instance);
                if (_surface == null)
                {
                    throw new InvalidOperationException("Unable to create the panel surface.");
                }
                _format = api.SurfaceGetPreferredFormat(_surface, adapter);
                SurfaceCapabilities capabilities = default;
                api.SurfaceGetCapabilities(_surface, adapter, &capabilities);
                try
                {
                    _presentMode = PresentMode.Fifo;
                    for (nuint i = 0; i < capabilities.PresentModeCount; i++)
                    {
                        // The editor already paces frames; a secondary FIFO can stall the whole UI thread.
                        if (capabilities.PresentModes[i] == PresentMode.Immediate)
                        {
                            _presentMode = PresentMode.Immediate;
                            break;
                        }
                        if (capabilities.PresentModes[i] == PresentMode.Mailbox)
                        {
                            _presentMode = PresentMode.Mailbox;
                        }
                    }
                }
                finally
                {
                    api.SurfaceCapabilitiesFreeMembers(capabilities);
                }
                _renderer = new ManaWebGpuUiRenderer(api, device, queue, _format);
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Panel UI surface: present={_presentMode}.");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Render(UiRoot root)
        {
            var size = _window.FramebufferSize;
            if (_surface == null || size.X <= 0 || size.Y <= 0 || _window.WindowState == WindowState.Minimized)
            {
                return;
            }

            if (!_configured || _size != size)
            {
                if (_configured)
                {
                    _api.SurfaceUnconfigure(_surface);
                }
                var configuration = new SurfaceConfiguration
                {
                    Device = _device, Format = _format, Usage = TextureUsage.RenderAttachment,
                    Width = (uint)size.X, Height = (uint)size.Y,
                    PresentMode = _presentMode, AlphaMode = CompositeAlphaMode.Opaque
                };
                _api.SurfaceConfigure(_surface, &configuration);
                _configured = true;
                _size = size;
            }

            SurfaceTexture texture = default;
            using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("Panel UI: Acquire"))
            {
                _api.SurfaceGetCurrentTexture(_surface, &texture);
            }
            if (texture.Status != SurfaceGetCurrentTextureStatus.Success || texture.Texture == null)
            {
                if (texture.Texture != null)
                {
                    _api.TextureRelease(texture.Texture);
                }
                // A timeout leaves the surface valid; rebuilding it can wait for the entire shared queue.
                if (texture.Status != SurfaceGetCurrentTextureStatus.Timeout)
                {
                    _api.SurfaceUnconfigure(_surface);
                    _configured = false;
                }
                return;
            }

            TextureView* view = null;
            CommandEncoder* encoder = null;
            RenderPassEncoder* pass = null;
            CommandBuffer* commands = null;
            try
            {
                view = _api.TextureCreateView(texture.Texture, (TextureViewDescriptor*)null);
                if (view == null)
                {
                    return;
                }
                encoder = _api.DeviceCreateCommandEncoder(_device, (CommandEncoderDescriptor*)null);
                var attachment = new RenderPassColorAttachment
                {
                    View = view, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store,
                    ClearValue = new Color { R = 0.055, G = 0.06, B = 0.07, A = 1 }
                };
                var descriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
                pass = _api.CommandEncoderBeginRenderPass(encoder, &descriptor);
                _renderer!.Render(pass, root.DrawList, root.Atlas, root.Images, root.Size, (uint)size.X, (uint)size.Y);
                _api.RenderPassEncoderEnd(pass);
                commands = _api.CommandEncoderFinish(encoder, (CommandBufferDescriptor*)null);
                _api.QueueSubmit(_queue, 1, &commands);
                using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("Panel UI: Present"))
                {
                    _api.SurfacePresent(_surface);
                }
            }
            finally
            {
                if (commands != null) { _api.CommandBufferRelease(commands); }
                if (pass != null) { _api.RenderPassEncoderRelease(pass); }
                if (encoder != null) { _api.CommandEncoderRelease(encoder); }
                if (view != null) { _api.TextureViewRelease(view); }
                _api.TextureRelease(texture.Texture);
            }
        }

        public void Dispose()
        {
            _closed(this);
            _renderer?.Dispose();
            _renderer = null;
            if (_surface == null)
            {
                return;
            }
            if (_configured)
            {
                _api.SurfaceUnconfigure(_surface);
            }
            _api.SurfaceRelease(_surface);
            _surface = null;
        }
    }
}
#endif

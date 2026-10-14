#if !MANA_BROWSER
using ManaUI;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext
    {
        /// <summary>A UI-only presentation surface sharing the editor's device and queue.</summary>
        private readonly List<UiSurface> _uiSurfaces = new();
        public UiSurface CreateUiSurface(IWindow window)
        {
            var surface = new UiSurface(this, window);
            _uiSurfaces.Add(surface);
            return surface;
        }

        public sealed class UiSurface : IUiWindowRenderer
        {
            private readonly ManaWebGpuContext _owner;
            private readonly IWindow _window;
            private Surface* _surface;
            private readonly TextureFormat _format;
            private readonly PresentMode _presentMode;
            private ManaWebGpuUiRenderer? _renderer;
            private Vector2D<int> _size;
            private bool _configured;

            internal UiSurface(ManaWebGpuContext owner, IWindow window)
            {
                _owner = owner;
                _window = window;
                try
                {
                    _surface = WebGPUSurface.CreateWebGPUSurface(window, owner._api, owner._instance);
                    if (_surface == null) throw new InvalidOperationException("Unable to create the panel surface.");
                    _format = owner._api.SurfaceGetPreferredFormat(_surface, owner._adapter);
                    SurfaceCapabilities capabilities = default;
                    owner._api.SurfaceGetCapabilities(_surface, owner._adapter, &capabilities);
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
                        owner._api.SurfaceCapabilitiesFreeMembers(capabilities);
                    }
                    _renderer = new ManaWebGpuUiRenderer(owner._api, owner._device, owner._queue, _format);
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Panel UI surface: present={_presentMode}.");
                }
                catch { Dispose(); throw; }
            }

            public void Render(UiRoot root)
            {
                var size = _window.FramebufferSize;
                if (_surface == null || size.X <= 0 || size.Y <= 0 || _window.WindowState == WindowState.Minimized) return;
                var api = _owner._api;
                if (!_configured || _size != size)
                {
                    if (_configured) api.SurfaceUnconfigure(_surface);
                    var configuration = new SurfaceConfiguration
                    {
                        Device = _owner._device, Format = _format, Usage = TextureUsage.RenderAttachment,
                        Width = (uint)size.X, Height = (uint)size.Y,
                        PresentMode = _presentMode, AlphaMode = CompositeAlphaMode.Opaque
                    };
                    api.SurfaceConfigure(_surface, &configuration);
                    _configured = true;
                    _size = size;
                }

                SurfaceTexture texture = default;
                using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("Panel UI: Acquire"))
                {
                    api.SurfaceGetCurrentTexture(_surface, &texture);
                }
                if (texture.Status != SurfaceGetCurrentTextureStatus.Success || texture.Texture == null)
                {
                    if (texture.Texture != null) api.TextureRelease(texture.Texture);
                    // A timeout leaves the surface valid; rebuilding it can wait for the entire shared queue.
                    if (texture.Status != SurfaceGetCurrentTextureStatus.Timeout)
                    {
                        api.SurfaceUnconfigure(_surface);
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
                    view = api.TextureCreateView(texture.Texture, (TextureViewDescriptor*)null);
                    if (view == null) return;
                    encoder = api.DeviceCreateCommandEncoder(_owner._device, (CommandEncoderDescriptor*)null);
                    var attachment = new RenderPassColorAttachment
                    {
                        View = view, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store,
                        ClearValue = new Color { R = 0.055, G = 0.06, B = 0.07, A = 1 }
                    };
                    var descriptor = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &attachment };
                    pass = api.CommandEncoderBeginRenderPass(encoder, &descriptor);
                    _renderer!.Render(pass, root.DrawList, root.Atlas, root.Images, root.Size, (uint)size.X, (uint)size.Y);
                    api.RenderPassEncoderEnd(pass);
                    commands = api.CommandEncoderFinish(encoder, (CommandBufferDescriptor*)null);
                    api.QueueSubmit(_owner._queue, 1, &commands);
                    using (ManaCore.Diagnostics.ManaFrameProfiler.Measure("Panel UI: Present"))
                    {
                        api.SurfacePresent(_surface);
                    }
                }
                finally
                {
                    if (commands != null) api.CommandBufferRelease(commands);
                    if (pass != null) api.RenderPassEncoderRelease(pass);
                    if (encoder != null) api.CommandEncoderRelease(encoder);
                    if (view != null) api.TextureViewRelease(view);
                    api.TextureRelease(texture.Texture);
                }
            }

            public void Dispose()
            {
                _owner._uiSurfaces.Remove(this);
                _renderer?.Dispose();
                _renderer = null;
                if (_surface == null) return;
                if (_configured) _owner._api.SurfaceUnconfigure(_surface);
                _owner._api.SurfaceRelease(_surface);
                _surface = null;
            }
        }
    }
}
#endif


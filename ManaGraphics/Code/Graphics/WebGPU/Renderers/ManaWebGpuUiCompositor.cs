using ManaGraphics.Code.Diagnostics;
using ManaUI;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Renderers
{
    /// <summary>Draws ManaUI over the tone-mapped frame, with the blurred backdrop frosted panels sample. Built on first use.</summary>
    internal sealed unsafe class ManaWebGpuUiCompositor : ManaWebGpuDeviceObject
    {
        private ManaWebGpuUiRenderer? _renderer;
        private ManaWebGpuUiBackdrop? _backdrop;

        public ManaWebGpuUiCompositor(ManaWebGpuDevice gpu)
            : base(gpu)
        {
        }

        /// <summary>Whether the UI asked for blur; checked before splitting the tone-map pass for it.</summary>
        public static bool WantsBackdrop(UiRoot? root)
        {
            UiBackdropStats.Supported = true;
            if (root?.DrawList.BackdropRequested != true)
            {
                return false;
            }

            UiBackdropStats.Requested++;
            return true;
        }

        /// <param name="size">The size the surface was configured at, which is the frame's: mid-resize the window
        /// reports its new size a frame before the surface catches up, and copying that much overruns the frame.</param>
        public void CaptureBackdrop(CommandEncoder* encoder, Texture* frame, Vector2D<int> size)
        {
            _backdrop ??= new ManaWebGpuUiBackdrop(Gpu, Gpu.SurfaceFormat);
            _backdrop.Capture(encoder, frame, size);

            UiBackdropStats.Captured++;
            _renderer?.SetBackdrop(_backdrop.View, _backdrop.Generation);
        }

        private readonly Dictionary<int, nint> _textures = new();

        /// <summary>A texture batches can draw from by id (ManaUiDrawList.SetTexture); kept until the renderer exists.</summary>
        public void SetTexture(int id, TextureView* view)
        {
            _textures[id] = (nint)view;
            _renderer?.SetTexture(id, view);
        }

        /// <summary>Draws <paramref name="root"/>'s list. False when the UI renderer could not be built: drop the root.</summary>
        public bool Draw(RenderPassEncoder* pass, UiRoot root, Vector2D<int> size)
        {
            if (_renderer == null)
            {
                try
                {
                    // The first time anything asks to draw ManaUI, which for a mode with a loading overlay is the very
                    // first loading frame -- a candidate for the gap before the loading screen shows.
                    ManaConsole.WriteDiagnosticElapsed("Creating the ManaUI renderer (first draw request).");
                    _renderer = new ManaWebGpuUiRenderer(Gpu, Gpu.SurfaceFormat);
                    foreach (var (id, view) in _textures)
                    {
                        _renderer.SetTexture(id, (TextureView*)view);
                    }
                    ManaConsole.WriteDiagnosticElapsed("ManaUI renderer ready; submitting its first draw list.");
                }
                catch (Exception ex)
                {
                    // A failed UI pipeline must not take the whole renderer down; reported once, not every frame.
                    ManaConsole.WriteDiagnostic($"ManaUI renderer failed to initialise, disabling it: {ex.Message}");
                    return false;
                }
            }

            // Bound here as well as at capture time: the renderer is built after the first capture ran, so a workspace
            // that opens with blur already on would otherwise show one frame with nothing behind it.
            if (_backdrop != null)
            {
                _renderer.SetBackdrop(_backdrop.View, _backdrop.Generation);
                UiBackdropStats.Bound = _backdrop.View != null;
            }

            _renderer.Render(
                pass,
                root.DrawList,
                root.Atlas,
                root.Images,
                root.Size,
                (uint)Math.Max(0, size.X),
                (uint)Math.Max(0, size.Y));
            return true;
        }

        public override void Dispose()
        {
            _renderer?.Dispose();
            _renderer = null;
            _backdrop?.Dispose();
            _backdrop = null;
        }
    }
}

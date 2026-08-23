using Mana.Code.Graphics;
using ManaCore.Graphics;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Mana.Code
{
    public class ManaContext
    {
#if !MANA_BROWSER
        public GL gl => RequireOpenGl();
        public GL? OpenGl => (GraphicsDevice as IOpenGlGraphicsDevice)?.Gl;
#else
        // No OpenGL on the browser target at all (WebGPU-only; see WebPort-Plan.md) --
        // always null, matching the existing "this ManaContext has no OpenGL context"
        // case every caller already checks for on desktop.
        public GL? OpenGl => null;
#endif
        public IManaGraphicsDevice? GraphicsDevice { get; private set; }
        public ManaGraphicsBackendKind GraphicsBackend => GraphicsDevice?.Backend ?? ManaGraphicsBackendKind.None;
        public IInputContext Input { get; set; } = null!;
        public IWindow Window { get; set; } = null!;

        public ManaWorld World { get; private set; } = null!;

        public bool DeferGpuUploads { get; set; }

        public ManaPhysics Physics { get; set; } = null!;

        public BaseGameMode GameMode { get; set; } = null!;

        public ManaContext(BaseGameMode gameMode)
        {
            GameMode = gameMode;
        }

        public ManaContext(IWindow window, IInputContext input, ManaWorld world, IManaGraphicsDevice? graphicsDevice = null)
        {
            Window = window;
            Input = input;
            World = world;
            GraphicsDevice = graphicsDevice;
        }

#if !MANA_BROWSER
        public ManaContext(IWindow window, GL gl, IInputContext input, ManaWorld world)
            : this(window, input, world, new ManaOpenGlGraphicsDevice(gl))
        {
        }

        public GL RequireOpenGl()
        {
            return RequireOpenGlDevice().Gl;
        }

        public IOpenGlGraphicsDevice RequireOpenGlDevice()
        {
            return GraphicsDevice as IOpenGlGraphicsDevice ?? throw new InvalidOperationException(
                $"This ManaContext is using {GraphicsBackend} and does not have an OpenGL context. " +
                "Move this call behind a graphics backend abstraction or use an OpenGL-specific path.");
        }
#endif
    }
}

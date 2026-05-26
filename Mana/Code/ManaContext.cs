using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Mana.Code
{
    public class ManaContext
    {
        public GL gl = null!;
        public IInputContext Input { get; set; } = null!;
        public IWindow Window { get; set; } = null!;

        public ManaWorld World { get; private set; } = null!;

        public ManaPhysics Physics {  get; set; } = null!;

        public BaseGameMode GameMode { get; set; } = null!;

        public ManaContext(BaseGameMode gameMode)
        {
            GameMode = gameMode;
        }

        public ManaContext(IWindow window, GL gl, IInputContext input, ManaWorld world)
        {
            this.gl = gl;
            this.Window = window;
            this.Input = input;
            this.World = world;
        }
    }
}

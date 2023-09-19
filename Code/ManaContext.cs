using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Mana.Code
{
    public class ManaContext
    {
        public GL gl;
        public IInputContext Input { get; set; }
        public IWindow Window { get; set; }

        public ManaWorld World { get; private set; }

        public ManaContext()
        {
            //Empty Context
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

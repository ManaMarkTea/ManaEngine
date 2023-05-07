using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code
{
    public class ManaContext
    {
        public GL gl;
        public IInputContext Input { get; set; }

        public ManaWorld World { get; private set; }        

        public ManaContext()
        {
            //Empty Context
        }

        public ManaContext(GL gl, IInputContext input, ManaWorld world)
        {
            this.gl = gl;
            this.Input = input;
            this.World = world;
        }
    }
}

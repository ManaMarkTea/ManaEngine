using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Renderers
{
    public class ManaForwardRenderer : ManaRenderer
    {
        public ManaForwardRenderer(ManaContext context) : base(context) 
        { 

        }

        public override void Initialize() { }

        public override void Resize(int width, int height) { }

        public override void Clear() 
        {
            Context.gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        }

        public override void Render(Action drawScenes) 
        { 
            drawScenes();
        }
    }
}



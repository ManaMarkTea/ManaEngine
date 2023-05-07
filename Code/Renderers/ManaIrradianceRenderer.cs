using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Renderers
{
    public class ManaIrradianceRenderer : ManaRenderer
    {
        public ManaIrradianceRenderer(ManaContext context) : base(context) 
        { 

        }

        public ManaRenderTarget IrradianceFramebuffer;
        public ManaRenderTarget PrefilterFramebuffer;
        public ManaRenderTarget BrdfFramebuffer;


        public override void Initialize() 
        {
            IrradianceFramebuffer = new ManaRenderTarget(Context, 32, 32);
            IrradianceFramebuffer.AddFloatRgbCubeBuffer("Irradiance");
            IrradianceFramebuffer.Build();
            IrradianceFramebuffer.UnBind();

            PrefilterFramebuffer = new ManaRenderTarget(Context, 128, 128);
            PrefilterFramebuffer.AddFloatRgbCubeBuffer("PreFilter");
            PrefilterFramebuffer.Build();
            PrefilterFramebuffer.UnBind();

            BrdfFramebuffer = new ManaRenderTarget(Context, 32, 32);
            BrdfFramebuffer.AddFloatBuffer("BRDF");
            BrdfFramebuffer.Build();
            BrdfFramebuffer.UnBind();

        }

        public override void Resize(int width, int height) { }

        public override void Clear() 
        {            
            Context.gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        }

        public override void Render(Action drawScenes) 
        {
            IrradianceFramebuffer.Bind();
            Context.gl.ClearColor(Colors.Gray.X, Colors.Gray.Y, Colors.Gray.Z, Colors.Gray.W);
            Clear();            
            IrradianceFramebuffer.UnBind();

            PrefilterFramebuffer.Bind();
            Context.gl.ClearColor(Colors.Silver.X, Colors.Silver.Y, Colors.Silver.Z, Colors.Silver.W);
            Clear();
            PrefilterFramebuffer.UnBind();

            BrdfFramebuffer.Bind();
            Context.gl.ClearColor(Colors.Blue.X, Colors.Blue.Y, Colors.Blue.Z, Colors.Blue.W);
            Clear();
            BrdfFramebuffer.UnBind();

            drawScenes();
        }
    }
}



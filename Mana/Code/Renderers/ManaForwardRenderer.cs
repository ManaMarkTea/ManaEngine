using Silk.NET.OpenGL;

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
            Context.RequireOpenGl().Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        }

        public override void Render(Action drawScenes)
        {
            drawScenes();
        }
    }
}



using Mana.Code.Materials;
using Mana.Code.Nodes;
using Silk.NET.OpenGL;

namespace Mana.Code
{
    public class ManaLiquidBallShader : ManaShader
    {
        public override bool IsTransparent => true;
        public override bool DisableDepthWrite => true;
        static ManaLiquidBallShader()
        {
            CacheShader("Assets/ShaderToys/LightningBall.vert");
            CacheShader("Assets/ShaderToys/LiquidBall.frag");
        }

        public ManaLiquidBallShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["LightningBall.vert"], ShaderSources["LiquidBall.frag"]);
            BindShader();
            UnbindShader();
        }

        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            if (Context.OpenGl == null) return;
            Context.OpenGl!.Enable(EnableCap.Blend);
            Context.OpenGl!.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            Context.OpenGl!.DepthMask(false);
            Context.OpenGl!.Enable(EnableCap.CullFace);
            Context.OpenGl!.CullFace(TriangleFace.Back);

            base.BindShader(material, objectMatrix, camera, owner);
        }

        public override void UnbindShader()
        {
            if (Context.OpenGl == null) return;
            base.UnbindShader();

            Context.OpenGl!.DepthMask(true);
            Context.OpenGl!.Disable(EnableCap.CullFace);
            Context.OpenGl!.Disable(EnableCap.Blend);
        }
    }
}

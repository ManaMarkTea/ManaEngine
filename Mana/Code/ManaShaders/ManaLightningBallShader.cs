using Mana.Code.Materials;
using Mana.Code.Nodes;
using Silk.NET.OpenGL;

namespace Mana.Code
{
    public class ManaLightningBallShader : ManaShader
    {
        static ManaLightningBallShader()
        {
            CacheShader("Assets/ShaderToys/LightningBall.vert");
            CacheShader("Assets/ShaderToys/LightningBall.frag");
        }

        public ManaLightningBallShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["LightningBall.vert"], ShaderSources["LightningBall.frag"]);
            BindShader();
            UnbindShader();
        }

        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            if (Context.OpenGl == null) return;
            Context.RequireOpenGl().Enable(EnableCap.Blend);
            Context.RequireOpenGl().BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            Context.RequireOpenGl().DepthMask(false);
            Context.RequireOpenGl().Enable(EnableCap.CullFace);
            Context.RequireOpenGl().CullFace(TriangleFace.Back);

            base.BindShader(material, objectMatrix, camera, owner);
        }

        public override void UnbindShader()
        {
            if (Context.OpenGl == null) return;
            base.UnbindShader();

            Context.RequireOpenGl().DepthMask(true);
            Context.RequireOpenGl().Disable(EnableCap.CullFace);
            Context.RequireOpenGl().Disable(EnableCap.Blend);
        }
    }
}

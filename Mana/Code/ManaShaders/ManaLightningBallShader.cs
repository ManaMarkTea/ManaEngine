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
            Context.gl.Enable(EnableCap.Blend);
            Context.gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            Context.gl.DepthMask(false);
            Context.gl.Enable(EnableCap.CullFace);
            Context.gl.CullFace(TriangleFace.Back);

            base.BindShader(material, objectMatrix, camera, owner);
        }

        public override void UnbindShader()
        {
            base.UnbindShader();

            Context.gl.DepthMask(true);
            Context.gl.Disable(EnableCap.CullFace);
            Context.gl.Disable(EnableCap.Blend);
        }
    }
}

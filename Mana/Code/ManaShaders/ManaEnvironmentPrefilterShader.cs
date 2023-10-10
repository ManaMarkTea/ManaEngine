namespace Mana.Code.ManaShaders
{
    public class ManaEnvironmentPrefilterShader : ManaShader
    {
        static ManaEnvironmentPrefilterShader()
        {
            CacheShader("Assets/Shaders/LearnSpecular/cubemap.vert");
            CacheShader("Assets/Shaders/LearnSpecular/prefilter.frag");
        }

        public ManaEnvironmentPrefilterShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["cubemap.vert"], ShaderSources["prefilter.frag"]);
            BindShader();


            UnbindShader();
        }

        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            base.BindShader(material, objectMatrix, camera, owner);
        }
    }
}

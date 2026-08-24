using Mana.Code.Materials;
using Mana.Code.Nodes;

namespace Mana.Code.ManaShaders
{
    public class ManaEnvironmentCubeShader : ManaShader
    {
        static ManaEnvironmentCubeShader()
        {
            CacheShader("Assets/Shaders/LearnSpecular/cubemap.vert");
            CacheShader("Assets/Shaders/LearnSpecular/equirectangular_to_cubemap.frag");
        }

        public ManaTexture EnvironmentTexture;

        public ManaEnvironmentCubeShader(ManaContext context) : base(context)
        {
            var environmentPath = context.World.EnvironmentReflectionSource.SourcePath;
            //var environmentPath = "Assets/Textures/Environments/newport_loft.png";
            //var environmentPath = "Assets/Textures/Environments/chromatic.jpg";
            //var environmentPath = "Assets/Textures/Environments/field.jpg";
            EnvironmentTexture = TextureManager.GetInstance(context).CreateTexture(environmentPath);
            EnvironmentTexture.OriginalFilename = environmentPath;
            EnvironmentTexture.Load();

            LoadShaderSource(ShaderSources["cubemap.vert"], ShaderSources["equirectangular_to_cubemap.frag"]);
            BindShader();


            UnbindShader();
        }

        public override void BindShader(ManaMaterial material, Matrix4x4 objectMatrix, CameraComponent camera, ManaSceneNode? owner = null)
        {
            base.BindShader(material, objectMatrix, camera, owner);
        }

        public void BindEnvironmentTexture()
        {
            if (EnvironmentTexture != null)
            {
                EnvironmentTexture.Bind(0);
            }
        }
    }
}

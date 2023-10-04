namespace Mana.Code.ManaShaders
{
    public class ManaBasicTextureShader : ManaShader
    {
        static ManaBasicTextureShader()
        {
            CacheShader("Assets/Shaders/basic_pos_uv.vert");
            CacheShader("Assets/Shaders/basic_texture.frag");
            CacheShader("Assets/Shaders/basic_texture_cube.frag");
        }

        public ManaBasicTextureShader(ManaContext context, bool isCube) : base(context)
        {
            if (isCube)
            {
                LoadShaderSource(shaderSources["basic_pos_uv.vert"], shaderSources["basic_texture_cube.frag"]);
            }
            else
            {
                LoadShaderSource(shaderSources["basic_pos_uv.vert"], shaderSources["basic_texture.frag"]);
            }

            BindShader();
            UnbindShader();
        }
    }
}

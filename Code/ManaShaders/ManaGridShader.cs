namespace Mana.Code
{

    public class ManaGridShader : ManaShader
    {
        static ManaGridShader()
        {
            CacheShader("Assets/Shaders/Grids/grid.vert");
            CacheShader("Assets/Shaders/Grids/grid.frag");
        }

        public ManaGridShader(ManaContext context) : base(context)
        {
            LoadShaderSource(shaderSources["grid.vert"], shaderSources["grid.frag"]);
            BindShader();
            UnbindShader();
        }
    }
}

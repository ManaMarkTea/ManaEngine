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
            LoadShaderSource(ShaderSources["grid.vert"], ShaderSources["grid.frag"]);
            if (!context.DeferGpuUploads)
            {
                BindShader();
                UnbindShader();
            }
        }
    }
}

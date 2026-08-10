namespace Mana.Code.ManaShaders
{
    /// <summary>
    /// Overlay lines whose width follows camera distance -- see draped_line.wgsl and
    /// ManaLineProfile.ScreenRibbon. WebGPU-only; the OpenGL path falls back to the PBR source below,
    /// which draws the ribbon at whatever width the geometry was built with.
    /// </summary>
    public class ManaDrapedLineShader : ManaShader
    {
        static ManaDrapedLineShader()
        {
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.vert");
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.frag");
        }

        public ManaDrapedLineShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["pbr.vert"], ShaderSources["pbr.frag"]);
            BindShader();
            UnbindShader();
        }
    }
}

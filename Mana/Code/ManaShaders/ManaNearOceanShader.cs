namespace Mana.Code
{
    // WebGPU is the primary target for this shader (see near_ocean.wgsl, resolved by class name in
    // ManaWebGpuContext.Pipelines.cs / ManaWebGpuCustomUniformSchema.cs). The OpenGL path reuses the
    // existing planar liquid-fill GLSL pair as a stand-in rather than duplicating shader work, same
    // reasoning as ManaOceanShader and ManaWaterShader.
    public class ManaNearOceanShader : ManaShader
    {
        public override bool IsTransparent => true;

        // See ManaOceanShader.DisableDepthWrite -- the two tiers deliberately overlap, so neither may
        // depth-reject the other.
        public override bool DisableDepthWrite => true;

        static ManaNearOceanShader()
        {
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.vert");
            CacheShader("Assets/ShaderToys/LiquidFill.frag");
        }

        public ManaNearOceanShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["pbr.vert"], ShaderSources["LiquidFill.frag"]);
            BindShader();
            UnbindShader();
        }
    }
}

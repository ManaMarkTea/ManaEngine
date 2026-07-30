namespace Mana.Code
{
    // WebGPU is the primary target for this shader (see water.wgsl, resolved by class name in
    // ManaWebGpuContext.Pipelines.cs / ManaWebGpuCustomUniformSchema.cs). The OpenGL path reuses
    // the existing planar liquid-fill GLSL pair as a stand-in rather than duplicating shader work.
    public class ManaWaterShader : ManaShader
    {
        public override bool IsTransparent => true;

        static ManaWaterShader()
        {
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.vert");
            CacheShader("Assets/ShaderToys/LiquidFill.frag");
        }

        public ManaWaterShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["pbr.vert"], ShaderSources["LiquidFill.frag"]);
            BindShader();
            UnbindShader();
        }
    }
}

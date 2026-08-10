namespace Mana.Code
{
    // WebGPU is the primary target for this shader (see ocean.wgsl, resolved by class name in
    // ManaWebGpuContext.Pipelines.cs / ManaWebGpuCustomUniformSchema.cs). The OpenGL path reuses
    // the existing planar liquid-fill GLSL pair as a stand-in rather than duplicating shader work,
    // same reasoning as ManaWaterShader.
    public class ManaOceanShader : ManaShader
    {
        public override bool IsTransparent => true;

        // The two ocean tiers overlap by design (the near patch cross-fades over this shell), and both
        // sit within a few metres of the same sea-level sphere. With depth writes on, whichever drew
        // first would depth-reject the other: the shell first would clip the near patch's wave troughs,
        // the patch first would z-fight along the whole fade band. They still depth-TEST against the
        // opaque terrain, which is what actually matters -- so the cross-fade alone decides how they
        // combine, and it does so the same way regardless of draw order.
        public override bool DisableDepthWrite => true;

        static ManaOceanShader()
        {
            CacheShader("Assets/Shaders/LearnSpecularTextured/pbr.vert");
            CacheShader("Assets/ShaderToys/LiquidFill.frag");
        }

        public ManaOceanShader(ManaContext context) : base(context)
        {
            LoadShaderSource(ShaderSources["pbr.vert"], ShaderSources["LiquidFill.frag"]);
            BindShader();
            UnbindShader();
        }
    }
}

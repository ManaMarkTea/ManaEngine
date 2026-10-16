using Mana.Code;

namespace Mana
{
    // The WebGPU backend resolves the WGSL pipeline from this material shader's type.
    public class ManaShader : ManaObject
    {
        public virtual bool IsTransparent => false;
        public virtual bool DisableDepthWrite => false;

        public ManaShader(ManaContext context) : base(context) { }
    }
}

using Mana.Code;

namespace Mana
{
    public class ManaTextureDynamic : ManaTexture
    {
        protected float[] buffer;
        public ManaTexture? TextureValue { get; set; }

        public ManaTextureDynamic(ManaContext context, int floatCount) : base(context)
        {
            buffer = new float[floatCount];
        }

        protected virtual void LoadBuffer() { }
        public virtual bool IsDirty() => false;

        public override void Load()
        {
            LoadBuffer();
            IsLoaded = true;
        }
    }
}

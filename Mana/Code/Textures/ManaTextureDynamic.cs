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

        // Whether this texture has GPU storage behind it. Bind creates the texture object on demand but
        // allocates nothing, so the object existing says nothing about whether it can be written to.
        private bool _hasStorage;

        protected override void LoadTextureData()
        {
            var gl = Context.OpenGl!;
            SizeInBytes = (int)Width * (int)Height * buffer.Length;
            AllocateEmptyTextImage2D(gl, TextureTargetType, Width, Height);
            _hasStorage = true;
            PushToTexture();
        }

        protected virtual void LoadBuffer()
        {
        }

        /// <summary>The copy has no storage of its own, whatever the original had.</summary>
        protected override void OnCloned()
        {
            base.OnCloned();
            _hasStorage = false;
        }

        protected void PushToTexture()
        {
            if (Context.OpenGl == null)
            {
                return;
            }

            // Storage first. Binding a texture creates the GL object but leaves it with no image, and a
            // channel can reach Bind without ever having been loaded -- a cloned material resets its
            // textures, and a material can be built before the context exists. Writing into a texture
            // with no storage is a GL_INVALID_VALUE, which surfaced the moment the material sliders
            // started marking these dirty.
            if (!_hasStorage)
            {
                Load();
                return;
            }

            var gl = Context.OpenGl!;
            LoadBuffer();
            SubTextImage2D(gl, TextureTargetType, 0, 0, 1, 1, new ReadOnlySpan<float>(buffer));
        }

        public virtual bool IsDirty() { return false; }

        public override void Bind(int channel)
        {
            if (TextureValue != null)
            {
                TextureValue.Bind(channel);
                return;
            }

            base.Bind(channel);
            if (IsDirty())
            {
                PushToTexture();
            }
        }
    }
}

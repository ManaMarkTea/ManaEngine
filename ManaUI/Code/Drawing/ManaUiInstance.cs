using System.Runtime.InteropServices;

namespace ManaUI
{
    [Flags]
    public enum ManaUiInstanceFlags : uint
    {
        None = 0,

        /// <summary>Sample the glyph atlas and treat the red channel as coverage over FillColor.</summary>
        GlyphAtlas = 1 << 0,

        /// <summary>Sample the image atlas (or the batch's bound texture) and modulate by FillColor.</summary>
        Texture = 1 << 1,

        /// <summary>Render an outer drop shadow of the box instead of the box itself.</summary>
        Shadow = 1 << 2,

        /// <summary>
        /// Fill with a vertical gradient from FillColor at the top to GradientColor at the bottom.
        /// <para>
        /// Vertical only, and deliberately: every gradient a UI actually wants is a light source overhead.
        /// An angle would cost another field on every instance in the frame to serve the rare case.
        /// </para>
        /// </summary>
        Gradient = 1 << 3
    }

    /// <summary>
    /// One GPU instance: a rounded, optionally-bordered, optionally-textured, self-clipping box.
    /// <para>
    /// Everything the UI draws -- panels, borders, glyphs, images, focus rings -- is one of these, which is
    /// what lets a whole frame go out as a single instanced draw call. Clipping is per-instance rather than
    /// via scissor for the same reason: a scissor change would force a draw-call split, and rounded clips
    /// (scroll viewers with rounded corners) are not expressible as a scissor rect at all.
    /// </para>
    /// 84 bytes; field order and padding must match the WGSL/GLSL instance struct exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ManaUiInstance
    {
        /// <summary>minX, minY, maxX, maxY in logical UI pixels.</summary>
        public Vector4 Rect;

        /// <summary>u0, v0, u1, v1 into whichever atlas Flags selects.</summary>
        public Vector4 Uv;

        /// <summary>Corner radii, clockwise from top-left.</summary>
        public Vector4 Corners;

        /// <summary>minX, minY, maxX, maxY of the clip rect; fragments outside are discarded.</summary>
        public Vector4 Clip;

        public uint FillColor;
        public uint BorderColor;

        /// <summary>Inset border width in pixels; 0 disables the border. Doubles as shadow blur when Flags has Shadow.</summary>
        public float BorderWidth;

        public uint Flags;

        /// <summary>The bottom colour when Flags has Gradient. Ignored otherwise.</summary>
        public uint GradientColor;
    }

    /// <summary>
    /// A run of instances sharing one bound texture. Normal UI produces exactly one batch (the shared glyph
    /// and image atlases); a batch boundary only appears when something draws with its own texture, such as
    /// a viewport render target or a material thumbnail.
    /// </summary>
    public readonly struct ManaUiDrawBatch
    {
        public readonly int InstanceOffset;
        public readonly int InstanceCount;

        /// <summary>0 means "the shared atlases"; anything else is a renderer-owned texture handle.</summary>
        public readonly int TextureId;

        public ManaUiDrawBatch(int instanceOffset, int instanceCount, int textureId)
        {
            InstanceOffset = instanceOffset;
            InstanceCount = instanceCount;
            TextureId = textureId;
        }
    }
}

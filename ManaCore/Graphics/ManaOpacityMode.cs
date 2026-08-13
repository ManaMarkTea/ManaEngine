namespace ManaCore.Graphics
{
    /// <summary>
    /// How a material's alpha is turned into pixels.
    /// <para>
    /// Separate from the opacity value itself because the two answer different questions: how much of the
    /// surface is there, and what the renderer should do about it. Blending needs back-to-front ordering
    /// and costs depth writes; cutting out does not, and is what a leaf or a grate actually wants.
    /// </para>
    /// </summary>
    public enum ManaOpacityMode
    {
        /// <summary>
        /// Read from what the material asks for: a value below one is a fade, so Blend; a map with the
        /// value left alone is a mask, so Cutout; neither, so Opaque. The default, because assigning a
        /// map or moving the slider and seeing nothing change is not a useful answer.
        /// </summary>
        Auto,

        /// <summary>Alpha ignored entirely. The cheapest, and right for almost everything.</summary>
        Opaque,

        /// <summary>
        /// Pixels below the cutoff are discarded outright. No sorting, no depth-write cost, hard edges --
        /// foliage, chain link, decals with a clean border.
        /// </summary>
        Cutout,

        /// <summary>
        /// Real alpha blending. Needs back-to-front ordering to be correct, so it is worth choosing
        /// deliberately rather than falling into: glass, smoke, a fading object.
        /// </summary>
        Blend
    }
}

namespace ManaCore.Graphics
{
    /// <summary>
    /// What a material map's contents mean, which decides the colour space it must be uploaded in.
    /// <para>
    /// Getting this wrong is silent: a normal or roughness map uploaded as sRGB has the sampler apply a
    /// transfer curve to numbers that were never encoded with one, and the result merely looks a bit off.
    /// </para>
    /// </summary>
    public enum ManaMaterialMapKind
    {
        /// <summary>Albedo and emission -- authored as colour, so sRGB.</summary>
        Colour,

        /// <summary>A tangent-space direction. Linear, and its neutral is (0.5, 0.5, 1).</summary>
        Normal,

        /// <summary>A coefficient or coverage: metallic, roughness, occlusion, opacity. Linear.</summary>
        Linear
    }
}

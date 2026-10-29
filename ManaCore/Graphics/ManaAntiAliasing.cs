namespace ManaCore.Graphics
{
    /// <summary>How the scene's edges are smoothed (Scene Inspector > Lighting).</summary>
    public enum ManaAntiAliasing
    {
        /// <summary>Raw pixels.</summary>
        Off,

        /// <summary>A single post pass on the tone-mapped picture: cheapest, softens texture detail a little.</summary>
        Fxaa,

        /// <summary>Morphological post (edges, blend weights, blend): crisper than FXAA for a little more.</summary>
        Smaa,

        /// <summary>Four samples a pixel in the scene pass: true geometry edges, the costliest in memory and fill.</summary>
        Msaa
    }
}

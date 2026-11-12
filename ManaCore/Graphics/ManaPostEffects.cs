namespace ManaCore.Graphics
{
    /// <summary>What a post effect script (a RetroFilter...) puts through its effect.</summary>
    public enum ManaPostEffectScope
    {
        /// <summary>The whole screen, sky and all.</summary>
        Screen,
        /// <summary>Everything in the script's scene (its level), not the sky or other scenes.</summary>
        Scene,
        /// <summary>The script's node and everything beneath it.</summary>
        Children,
        /// <summary>Everything under the script's parent: the node, its siblings and theirs.</summary>
        Siblings
    }

    /// <summary>
    /// The screen effects' slots: each has a bit in <see cref="ManaRenderMeshInstance.PostEffects"/> (the meshes it covers)
    /// and in <see cref="ManaRenderScene.ScopedPostEffects"/> (when it covers only those), and the renderer draws a mask
    /// per scoped slot for its effect to read. A new effect takes the next free slot.
    /// </summary>
    public static class ManaPostEffects
    {
        public const int Retro = 0;

        public const int SlotCount = 8;

        public static byte Bit(int slot) => (byte)(1 << slot);

        public static bool Has(byte bits, int slot) => (bits & Bit(slot)) != 0;
    }
}

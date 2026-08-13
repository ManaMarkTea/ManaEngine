namespace ManaUI
{
    /// <summary>
    /// What the backend actually did with the backdrop request, for the one effect no headless test can see.
    /// <para>
    /// Blur fails silently in three different places -- the frame is never captured, the capture is never
    /// bound, or it is bound and sampled at a level the sampler clamps away -- and all three look identical
    /// from the outside: no blur. These counters are the difference between reading the screen and guessing
    /// at it.
    /// </para>
    /// <para>
    /// Static because it is diagnostics, and threading a reporting channel from a render backend up to a
    /// panel would be more machinery than the thing being diagnosed.
    /// </para>
    /// </summary>
    public static class UiBackdropStats
    {
        /// <summary>Frames whose draw list asked for a blurred backdrop.</summary>
        public static long Requested;

        /// <summary>Frames where the backend snapshotted and reduced the frame.</summary>
        public static long Captured;

        /// <summary>True once a captured backdrop has been bound for the UI to sample.</summary>
        public static bool Bound;

        /// <summary>Set by a backend that implements the effect at all. False means the flag is ignored.</summary>
        public static bool Supported;

        public static string Summary =>
            !Supported
                ? "this backend does not implement blur"
                : $"requested {Requested}, captured {Captured}, bound {(Bound ? "yes" : "no")}";
    }
}

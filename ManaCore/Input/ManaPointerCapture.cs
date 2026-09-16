namespace ManaCore.Input
{
    /// <summary>
    /// Whether something has grabbed the pointer for a mouselook drag -- hidden, locked in place, and
    /// reporting unbounded motion so a turn is not cut short by the edge of the screen.
    /// <para>
    /// A flag here rather than a parameter on anything because the two halves live in projects that
    /// cannot see each other: the camera decides when to grab (Mana), and the ImGui controllers write
    /// the cursor mode from their own state every single frame (ManaGraphics). Without somewhere neutral
    /// to say "not this frame", whichever ran second would put the cursor straight back.
    /// </para>
    /// <para>
    /// Whoever sets this owns the cursor mode for as long as it is set, and must clear it -- releasing a
    /// grab nobody clears leaves the pointer invisible with no way to get it back.
    /// </para>
    /// </summary>
    public static class ManaPointerCapture
    {
        public static bool IsActive { get; set; }
    }
}

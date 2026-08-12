namespace ManaUI
{
    /// <summary>
    /// Cursor shapes the UI can ask for. ManaUI cannot set a cursor itself -- it has no windowing
    /// dependency -- so it reports the shape it wants through <see cref="UiRoot.RequestedCursor"/> and the
    /// host maps it onto the platform's own.
    /// </summary>
    public enum UiCursor
    {
        Arrow,
        IBeam,
        Hand,

        /// <summary>Horizontal resize.</summary>
        SizeWE,

        /// <summary>Vertical resize.</summary>
        SizeNS,

        /// <summary>Diagonal resize, top-left to bottom-right.</summary>
        SizeNWSE,

        /// <summary>Diagonal resize, top-right to bottom-left.</summary>
        SizeNESW,

        /// <summary>Move in any direction.</summary>
        SizeAll,

        NotAllowed
    }
}

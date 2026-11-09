using ManaCore.Graphics;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>
    /// Where the About panel finds the backend's start-up diagnostics.
    /// <para>
    /// A static hand-off rather than a parameter threaded through the workspace: the snapshot is captured
    /// by whichever backend happens to be running, long before any panel exists, and the workspace has no
    /// business knowing which backend that was.
    /// </para>
    /// </summary>
    public static class ManaUiDiagnostics
    {
        /// <summary>Null until a backend has captured one, which the panel reports rather than hiding.</summary>
        public static ManaGraphicsDiagnosticsSnapshot? Snapshot { get; set; }

        public static string BackendName { get; set; } = "Graphics";
    }
}

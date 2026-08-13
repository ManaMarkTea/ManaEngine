namespace ManaUI
{
    /// <summary>
    /// The clipboard the text controls copy and paste through. ManaUI cannot reach a real clipboard -- it
    /// has no windowing dependency -- so a host that has one supplies an implementation and everything
    /// else runs on <see cref="UiMemoryClipboard"/>.
    /// </summary>
    public interface IUiClipboard
    {
        /// <summary>
        /// Clipboard text. The getter reads through to wherever the contents actually live rather than a
        /// mirrored copy: the OS clipboard changes without telling anyone, and a copy made in another
        /// application has to be visible here on the next paste.
        /// </summary>
        string Text { get; set; }
    }

    /// <summary>
    /// A clipboard held in a field. The default, so copy and paste work headlessly, and what a test run
    /// uses -- a run swaps the platform implementation out, so no check can read or overwrite what the
    /// user had copied.
    /// </summary>
    public sealed class UiMemoryClipboard : IUiClipboard
    {
        public string Text { get; set; } = string.Empty;
    }
}

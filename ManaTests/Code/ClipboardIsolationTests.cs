using ManaUI;

namespace ManaTests;

/// <summary>
/// Runs the suite on an in-memory clipboard, putting the host's back afterwards.
/// <para>
/// The text box checks copy, paste and cut, which go through <see cref="TextBox.Clipboard"/> -- a static.
/// That is harmless while it is an in-memory one, and not harmless at all once a host has replaced it with
/// the platform's: running the suite from the GUI runner replaced whatever the user had copied with
/// "SeaSea", the leftovers of a cut test.
/// </para>
/// <para>
/// Swapping the whole clipboard rather than saving and restoring its contents is what makes this safe. The
/// contents are text, and a clipboard holding an image reads as empty, so a restore that wrote the value
/// back emptied it -- which is how screenshots were being erased by a test run.
/// </para>
/// </summary>
public sealed class ClipboardIsolation : IDisposable
{
    private readonly IUiClipboard _host;

    private ClipboardIsolation()
    {
        _host = TextBox.Clipboard;
        TextBox.Clipboard = new UiMemoryClipboard();
    }

    public static ClipboardIsolation Begin()
    {
        return new ClipboardIsolation();
    }

    public void Dispose()
    {
        TextBox.Clipboard = _host;
    }
}

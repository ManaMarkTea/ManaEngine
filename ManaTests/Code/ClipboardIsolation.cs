using ManaUI;

namespace ManaTests;

/// <summary>
/// Detaches ManaUI's clipboard from the platform's for the duration of a run, and puts it back after.
/// <para>
/// The text box checks copy, paste and cut, which means they write to <see cref="TextBox.Clipboard"/> --
/// a static. That is harmless while it is the library's own in-process string, and not harmless at all
/// once a host has pointed it at the real clipboard: running the suite from the GUI runner replaced
/// whatever the user had copied with "SeaSea", the leftovers of a cut test.
/// </para>
/// <para>
/// Isolating here rather than teaching each check to clean up after itself: the runner owns the
/// environment a run happens in, and a check that forgot would be a silent, occasional theft of somebody's
/// clipboard.
/// </para>
/// </summary>
public sealed class ClipboardIsolation : IDisposable
{
    private readonly Func<string>? _read;
    private readonly Action<string>? _write;
    private readonly string _contents;

    /// <summary>
    /// True while a run is isolated. Exists so the suite can assert that the runner actually applies this
    /// -- without it, a check could only verify that the class works when called, which says nothing about
    /// whether anything calls it.
    /// </summary>
    public static bool IsActive { get; private set; }

    private ClipboardIsolation()
    {
        _read = TextBox.ClipboardRead;
        _write = TextBox.ClipboardWrite;

        // Read through the hooks before removing them, so the value restored afterwards is the one the
        // user actually had rather than whatever a check left behind.
        _contents = TextBox.Clipboard;

        TextBox.ClipboardRead = null;
        TextBox.ClipboardWrite = null;
        TextBox.Clipboard = string.Empty;
        IsActive = true;
    }

    public static ClipboardIsolation Begin()
    {
        return new ClipboardIsolation();
    }

    public void Dispose()
    {
        // The hooks go back first, so assigning the contents writes through to the platform and the user's
        // clipboard is left holding what it held before the run.
        IsActive = false;
        TextBox.ClipboardRead = _read;
        TextBox.ClipboardWrite = _write;
        TextBox.Clipboard = _contents;
    }
}

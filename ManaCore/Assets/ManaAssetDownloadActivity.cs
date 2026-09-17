namespace ManaCore.Assets;

/// <summary>
/// Live view of ManaAssetCatalog's downloads, polled by ManaUi rather than pushed to it (see the
/// BindPolled call sites throughout ManaVoxel's UI for the established convention) -- a corner readout
/// in the editor reads this every frame rather than the catalog handing it events to buffer.
/// <para>
/// A static hand-off like ManaUiDiagnostics.Snapshot: ManaAssetCatalog.Current is the only thing that
/// ever populates it, is null until a browser build initializes one, and this activity is likewise
/// permanently empty on desktop without needing a browser-only guard anywhere that reads it.
/// </para>
/// </summary>
public static class ManaAssetDownloadActivity
{
    private readonly record struct Entry(string Path, long Size);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Active = new(StringComparer.Ordinal);
    private static long _totalBytesDownloaded;
    private static int _totalFilesDownloaded;

    public static bool IsDownloading { get { lock (Gate) return Active.Count > 0; } }

    public static long TotalBytesDownloaded { get { lock (Gate) return _totalBytesDownloaded; } }

    public static int TotalFilesDownloaded { get { lock (Gate) return _totalFilesDownloaded; } }

    /// <summary>File names (not full catalog paths) currently downloading, for a status line.</summary>
    public static IReadOnlyList<string> ActiveNames
    {
        get
        {
            lock (Gate)
            {
                return Active.Count == 0
                    ? Array.Empty<string>()
                    : Active.Values.Select(e => System.IO.Path.GetFileName(e.Path)).ToArray();
            }
        }
    }

    /// <summary>Whether the exact catalog path named is downloading right now -- for a per-tile indicator,
    /// as opposed to <see cref="IsDownloading"/>'s whole-session view.</summary>
    public static bool IsActive(string path)
    {
        lock (Gate) return Active.ContainsKey(path);
    }

    internal static void Started(string path, long size)
    {
        lock (Gate) Active[path] = new Entry(path, size);
    }

    internal static void Finished(string path, long size, bool succeeded)
    {
        lock (Gate)
        {
            Active.Remove(path);
            if (succeeded)
            {
                _totalBytesDownloaded += size;
                _totalFilesDownloaded++;
            }
        }
    }
}

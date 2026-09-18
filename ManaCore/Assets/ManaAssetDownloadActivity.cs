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
    private readonly record struct Entry(string Path, long Size, System.Diagnostics.Stopwatch Stopwatch);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Active = new(StringComparer.Ordinal);

    /// <summary>How long the most recent successful download of each path took, for an info tooltip. Never
    /// trimmed -- one TimeSpan per distinct file a session ever downloaded is not worth an eviction policy.</summary>
    private static readonly Dictionary<string, TimeSpan> CompletedDurations = new(StringComparer.Ordinal);

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

    /// <summary>How long the path's most recent successful download took, or null if it has never
    /// completed one this session (including "still in progress" -- a partial time is not a duration).</summary>
    public static TimeSpan? DurationOf(string path)
    {
        lock (Gate) return CompletedDurations.TryGetValue(path, out var duration) ? duration : null;
    }

    internal static void Started(string path, long size)
    {
        lock (Gate) Active[path] = new Entry(path, size, System.Diagnostics.Stopwatch.StartNew());
    }

    internal static void Finished(string path, long size, bool succeeded)
    {
        lock (Gate)
        {
            if (Active.Remove(path, out var entry) && succeeded)
            {
                CompletedDurations[path] = entry.Stopwatch.Elapsed;
                _totalBytesDownloaded += size;
                _totalFilesDownloaded++;
            }
        }
    }
}

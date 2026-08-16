using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ManaGeoTools.Code.GeoTiff.Terrarium;
using ManaGeoTools.Code.Planet;
using ManaGraphics.Code.Diagnostics;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// How much disk the planet's downloaded elevation tiles are holding, with a way to clear it.
    /// <para>
    /// The scan walks every file under the cache root, which is too slow to do on every poll -- so it runs
    /// once in the background the first time this widget is built, and again only when asked to. A stale
    /// number beats blocking the inspector on a directory walk.
    /// </para>
    /// </summary>
    public sealed class TerrainCacheWidget : InspectorWidget<PlanetNode>
    {
        private readonly object _lock = new();
        private Task? _refreshTask;
        private int _generation;
        private long? _bytes;
        private DateTimeOffset? _updatedAt;
        private string? _error;

        protected override UiElement Build(PlanetNode planet, InspectorContext context)
        {
            var theme = context.Theme;
            EnsureStarted();

            var sizeText = new TextBlock().Foreground(theme.Text);
            sizeText.BindTextPolled(() => $"Cache size: {DescribeSize()}");

            var updatedText = new TextBlock().Foreground(theme.TextMuted);
            updatedText.BindTextPolled(DescribeStatus);
            updatedText.BindPolled(t => t.Visibility,
                () => string.IsNullOrEmpty(DescribeStatus()) ? Visibility.Collapsed : Visibility.Visible);

            var pathText = new TextBlock().Text(TerrariumTileFetcher.CacheRoot).Wrap().Foreground(theme.TextMuted);

            var refreshButton = new Button().Label("Refresh").OnClick(() => RequestRefresh(clearFirst: false));
            refreshButton.BindPolled(b => b.IsEnabled, () => !IsRefreshing());

            var openButton = new Button().Label("Open Cache Folder").OnClick(() => OpenFolder(TerrariumTileFetcher.CacheRoot));

            var clearButton = new Button().Label("Clear Cache").OnClick(() => RequestRefresh(clearFirst: true));
            clearButton.BindPolled(b => b.IsEnabled, () => !IsRefreshing());

            return new Expander()
                .Header("Terrain Cache")
                .Section(theme.Spacing,
                    sizeText,
                    updatedText,
                    pathText,
                    new StackPanel().Horizontal(theme.Spacing).Children(refreshButton, openButton, clearButton));
        }

        private void EnsureStarted()
        {
            lock (_lock)
            {
                if (_bytes == null && _refreshTask == null)
                {
                    StartRefreshLocked(clearFirst: false);
                }
            }
        }

        private bool IsRefreshing()
        {
            lock (_lock)
            {
                return _refreshTask is { IsCompleted: false };
            }
        }

        private string DescribeSize()
        {
            lock (_lock)
            {
                if (_bytes.HasValue)
                {
                    return FormatByteCount(_bytes.Value);
                }

                return _refreshTask is { IsCompleted: false } ? "Calculating..." : "Unknown";
            }
        }

        private string DescribeStatus()
        {
            lock (_lock)
            {
                if (_refreshTask is { IsCompleted: false })
                {
                    return "Refreshing...";
                }

                if (!string.IsNullOrWhiteSpace(_error))
                {
                    return $"Cache scan failed: {_error}";
                }

                return _updatedAt.HasValue ? $"Updated {_updatedAt.Value:HH:mm:ss}" : string.Empty;
            }
        }

        private void RequestRefresh(bool clearFirst)
        {
            lock (_lock)
            {
                if (_refreshTask is { IsCompleted: false })
                {
                    return;
                }

                StartRefreshLocked(clearFirst);
            }
        }

        /// <summary>Caller must hold <see cref="_lock"/>.</summary>
        private void StartRefreshLocked(bool clearFirst)
        {
            var generation = ++_generation;
            _refreshTask = Task.Run(() =>
            {
                long? bytes = null;
                string? error = null;
                try
                {
                    if (clearFirst)
                    {
                        TerrariumTileFetcher.ClearCache();
                    }

                    bytes = TerrariumTileFetcher.GetCacheSizeBytes();
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                lock (_lock)
                {
                    // A newer refresh started while this one was running; let it win instead of
                    // clobbering its result with a stale one.
                    if (generation != _generation)
                    {
                        return;
                    }

                    _bytes = bytes;
                    _error = error;
                    _updatedAt = DateTimeOffset.Now;
                }
            });
        }

        private static string FormatByteCount(long bytes)
        {
            const double kibibyte = 1024.0;
            const double mebibyte = kibibyte * 1024.0;
            const double gibibyte = mebibyte * 1024.0;

            return bytes switch
            {
                >= 1_073_741_824 => $"{bytes / gibibyte:N2} GiB",
                >= 1_048_576 => $"{bytes / mebibyte:N2} MiB",
                >= 1_024 => $"{bytes / kibibyte:N2} KiB",
                _ => $"{bytes:N0} B"
            };
        }

        private static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);

                var startInfo = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true }
                    : new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true };

                Process.Start(startInfo);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException)
            {
                ManaConsole.WriteDiagnostic($"Failed to open the terrain cache folder: {ex.Message}");
            }
        }
    }
}

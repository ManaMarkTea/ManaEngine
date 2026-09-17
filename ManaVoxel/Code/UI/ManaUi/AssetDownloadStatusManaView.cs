using ManaCore.Assets;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// A small corner readout of ManaAssetCatalog's activity: what is downloading right now, and how much
    /// has come down this session. Native replacement for the browser build's own status text (see
    /// tools/WebAssets/asset-downloads.js's status()) for the routine progress case; that script still
    /// owns surfacing a download failure, which this view does not attempt to duplicate.
    /// <para>
    /// ManaAssetCatalog.Current is null on desktop and stays null until a browser build initializes one,
    /// so ManaAssetDownloadActivity reads as permanently idle there -- this collapses to nothing without
    /// needing a browser-only guard of its own.
    /// </para>
    /// </summary>
    public static class AssetDownloadStatusManaView
    {
        public const float Width = 260.0f;
        public const float Height = 26.0f;

        public static UiElement Build(UiTheme theme)
        {
            var text = new TextBlock()
                .FontFamily(theme.FontFamily)
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.Text)
                .Ellipsis();

            var root = new Border()
                .Fill(theme.SurfaceRaised)
                .Rounded(theme.CornerRadius)
                .PaddingOf(8.0f, 4.0f);
            root.Child = text;

            root.BindPolled(el => el.Visibility, () => Status() == null ? Visibility.Collapsed : Visibility.Visible);
            text.BindPolled(t => t.Text, () => Status() ?? string.Empty);

            return root;
        }

        /// <summary>Null hides the widget entirely -- nothing has downloaded yet and nothing is downloading.</summary>
        private static string? Status()
        {
            if (ManaAssetDownloadActivity.IsDownloading)
            {
                var names = ManaAssetDownloadActivity.ActiveNames;
                var label = names.Count switch
                {
                    0 => "Downloading…",
                    1 => $"Downloading {names[0]}…",
                    _ => $"Downloading {names[0]} +{names.Count - 1} more…"
                };
                return $"{label} {FormatBytes(ManaAssetDownloadActivity.TotalBytesDownloaded)} so far";
            }

            return ManaAssetDownloadActivity.TotalFilesDownloaded > 0
                ? $"{FormatBytes(ManaAssetDownloadActivity.TotalBytesDownloaded)} downloaded"
                    + $" ({ManaAssetDownloadActivity.TotalFilesDownloaded} file{(ManaAssetDownloadActivity.TotalFilesDownloaded == 1 ? "" : "s")})"
                : null;
        }

        private static string FormatBytes(long bytes) =>
            bytes >= 1024 * 1024 ? $"{bytes / (1024.0 * 1024.0):0.0} MB" : $"{bytes / 1024.0:0} KB";
    }
}

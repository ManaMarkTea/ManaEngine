namespace ManaLidar;

/// <summary>Where scans are saved, and handing one to AirDrop, Files or Mail.</summary>
public static class ScanFiles
{
    /// <summary>Documents, which Info.plist shares with the Files app and with Finder over USB.</summary>
    public static string Folder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public static string PathFor(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ':' ? '-' : c));
        var path = Path.Combine(Folder, safe + ".glb");
        for (var n = 2; File.Exists(path); n++)
        {
            path = Path.Combine(Folder, $"{safe} {n}.glb");
        }
        return path;
    }

    public static FileInfo[] List() => new DirectoryInfo(Folder).GetFiles("*.glb")
        .OrderByDescending(f => f.LastWriteTimeUtc).ToArray();

    public static void Share(UIViewController from, string path, UIView anchor)
    {
        var share = new UIActivityViewController(new NSObject[] { NSUrl.FromFilename(path) }, null);
        if (share.PopoverPresentationController is { } popover)
        {
            // iPad shows the share sheet as a popover, which needs something to point at.
            popover.SourceView = anchor;
            popover.SourceRect = anchor.Bounds;
        }
        from.PresentViewController(share, true, null);
    }
}

/// <summary>Every exported scan, newest first: tap to view and measure, (i) to share (AirDrop it to the Mac), swipe to delete.</summary>
public sealed class ScansViewController : UITableViewController
{
    private const string CellId = "scan";
    private FileInfo[] _files = Array.Empty<FileInfo>();

    public ScansViewController() : base(UITableViewStyle.InsetGrouped)
    {
        Title = "Scans";
    }

    public override void ViewWillAppear(bool animated)
    {
        base.ViewWillAppear(animated);
        _files = ScanFiles.List();
        TableView.ReloadData();
    }

    public override nint RowsInSection(UITableView tableView, nint section) => _files.Length;

    public override string? TitleForFooter(UITableView tableView, nint section) => _files.Length == 0
        ? "No scans yet. Export one from the scanner."
        : "Open a scan in Mana by dropping the .glb onto the ManaVoxel window.";

    public override UITableViewCell GetCell(UITableView tableView, NSIndexPath indexPath)
    {
        var cell = tableView.DequeueReusableCell(CellId) ?? new UITableViewCell(UITableViewCellStyle.Subtitle, CellId);
        var file = _files[indexPath.Row];
        cell.TextLabel!.Text = Path.GetFileNameWithoutExtension(file.Name);
        cell.DetailTextLabel!.Text = $"{file.LastWriteTime:g}  ·  {file.Length / (1024.0 * 1024.0):0.0} MB";
        cell.Accessory = UITableViewCellAccessory.DetailButton;
        return cell;
    }

    // Tap opens the scan to look round and measure; the (i) button shares it.
    public override void RowSelected(UITableView tableView, NSIndexPath indexPath)
    {
        tableView.DeselectRow(indexPath, true);
        NavigationController?.PushViewController(new ScanViewerViewController(_files[indexPath.Row].FullName), true);
    }

    public override void AccessoryButtonTapped(UITableView tableView, NSIndexPath indexPath)
    {
        var cell = tableView.CellAt(indexPath) ?? (UIView)tableView;
        ScanFiles.Share(this, _files[indexPath.Row].FullName, cell);
    }

    public override bool CanEditRow(UITableView tableView, NSIndexPath indexPath) => true;

    public override void CommitEditingStyle(UITableView tableView, UITableViewCellEditingStyle editingStyle, NSIndexPath indexPath)
    {
        if (editingStyle != UITableViewCellEditingStyle.Delete)
        {
            return;
        }
        _files[indexPath.Row].Delete();
        _files = ScanFiles.List();
        tableView.DeleteRows(new[] { indexPath }, UITableViewRowAnimation.Automatic);
    }
}

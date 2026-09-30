using System.IO.Compression;
using System.Text;
using System.Numerics;
using System.Runtime.Versioning;
using Mana.Code.Shapes.Loaders;

namespace ManaVoxel.Code.Assets
{
    /// <summary>
    /// The mesh assets on disk, as a filtered tree.
    /// <para>
    /// This was all inside the ImGui import panel, rebuilt from scratch on the frame anything changed. It is
    /// here instead because none of it is drawing: scanning, format filters, grouping a model's formats into
    /// one entry and collapsing empty folder chains are decisions about what the list contains, and they are
    /// the part worth testing. A view on top of this only has to render rows.
    /// </para>
    /// </summary>
    public sealed partial class MeshAssetLibrary : IDisposable
    {
        /// <summary>
        /// Extensions worth scanning for. Used to cover whatever Assimp would open; narrowed to what
        /// ManaWorld.LoadFromGltf's SharpGLTF backend actually reads once Assimp was dropped entirely (see
        /// that file's comment) -- real content was glTF/GLB only anyway, so nothing was lost in practice.
        /// </summary>
        public static readonly string[] SupportedExtensions =
        {
            ".gltf",
            ".glb"
        };

        /// <summary>
        /// Which format wins when one model ships several. glTF first because it is the one that carries
        /// materials the renderer actually understands.
        /// </summary>
        private static readonly string[] FormatPreference =
        {
            ".glb",
            ".gltf"
        };


        /// <summary>
        /// How long to wait after the last file-system event before actually rescanning. A copy or unzip
        /// touches a folder many times in a burst; without this a rescan would fire, and rebuild the panel,
        /// once per file instead of once for the whole drop.
        /// </summary>
        private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);

        private readonly List<string> _assets = new();

        private MeshAssetFolder? _tree;
        private bool _treeDirty = true;
        private bool _flatDirty = true;
        private List<MeshAssetEntry>? _flat;
        private MeshAssetFolder? _folderTree;
        private bool _folderTreeDirty = true;

        private Task<MeshScanResult>? _scan;

        // Starts true so the first RequestScan (there is no other way to populate the list) always runs.
        // Set again only by a watcher event or an explicit force -- there is no timer rescanning on a clock
        // any more, because a panel open the whole time was rebuilding its tile grid, and killing whatever
        // tooltip happened to be open, every couple of seconds whether anything on disk had changed or not.
        private volatile bool _dirty = true;

        private readonly object _watchGate = new();
        private List<FileSystemWatcher>? _watchers;
        private Timer? _debounceTimer;
        private bool _disposed;

        private string _filter = string.Empty;
        private MeshAssetFormats _formats = MeshAssetFormats.Default;

        /// <summary>Raised when the tree changes shape, so a view can rebuild its rows.</summary>
        public event EventHandler? Changed;

        public IReadOnlyList<string> Assets => _assets;

        public bool IsScanning => _scan is { IsCompleted: false };

        /// <summary>Set when archives could not be read. A warning rather than a failure: the rest still scanned.</summary>
        public string? ScanError { get; private set; }

        private string? _selectedAsset;
        private readonly List<string> _selection = new();

        /// <summary>
        /// The asset last clicked -- the one single-asset actions (Place, Use selected) act on. Setting it
        /// selects it alone.
        /// </summary>
        public string? SelectedAsset
        {
            get => _selectedAsset;
            set
            {
                _selectedAsset = value;
                _selection.Clear();
                if (value != null)
                {
                    _selection.Add(value);
                }
            }
        }

        /// <summary>Every selected asset, in the order the grid shows them. Holds <see cref="SelectedAsset"/> when it is set.</summary>
        public IReadOnlyList<string> SelectedAssets => _selection;

        public bool IsSelected(string path) => _selection.Contains(path, StringComparer.Ordinal);

        /// <summary>
        /// A click on an asset: alone it selects just that one; <paramref name="toggle"/> (Ctrl) adds or removes
        /// it; <paramref name="range"/> (Shift) selects everything shown from the last one clicked to this.
        /// </summary>
        public void Click(string path, bool toggle, bool range)
        {
            if (range && _selectedAsset != null)
            {
                var order = FilteredEntries.Select(entry => entry.PrimaryPath).ToList();
                var from = order.IndexOf(_selectedAsset);
                var to = order.IndexOf(path);
                if (from >= 0 && to >= 0)
                {
                    var span = order.GetRange(Math.Min(from, to), Math.Abs(to - from) + 1);
                    if (!toggle)
                    {
                        _selection.Clear();
                    }

                    foreach (var item in span.Where(item => !_selection.Contains(item, StringComparer.Ordinal)))
                    {
                        _selection.Add(item);
                    }

                    // The anchor stays where it was, so a second shift-click re-draws the range from it.
                    SortSelection(order);
                    return;
                }
            }

            if (toggle)
            {
                if (_selection.Remove(path))
                {
                    _selectedAsset = _selection.LastOrDefault();
                }
                else
                {
                    _selection.Add(path);
                    _selectedAsset = path;
                    SortSelection(FilteredEntries.Select(entry => entry.PrimaryPath).ToList());
                }

                return;
            }

            SelectedAsset = path;
        }

        private void SortSelection(List<string> order)
        {
            var sorted = _selection.OrderBy(item => order.IndexOf(item) is var index and >= 0 ? index : int.MaxValue).ToList();
            _selection.Clear();
            _selection.AddRange(sorted);
        }

        public string Filter
        {
            get => _filter;
            set
            {
                var text = value ?? string.Empty;
                if (_filter == text)
                {
                    return;
                }

                _filter = text;
                Invalidate();
            }
        }

        public MeshAssetFormats Formats
        {
            get => _formats;
            set
            {
                if (_formats == value)
                {
                    return;
                }

                _formats = value;
                Invalidate();
                _folderTreeDirty = true;
            }
        }

        /// <summary>The filtered tree. Built on demand, because most frames change nothing.</summary>
        public MeshAssetFolder Tree
        {
            get
            {
                if (_treeDirty || _tree == null)
                {
                    _tree = BuildTree(_filter);
                    _treeDirty = false;
                    _flatDirty = true;
                }

                return _tree;
            }
        }

        /// <summary>
        /// Every entry the filter admits, flattened and in tree order.
        /// <para>
        /// A grid of thumbnails wants a list, not a hierarchy: the folder each asset came from is already
        /// shown by the sidebar and by the filter box, so repeating it as nesting inside the grid would spend
        /// a row per folder to say what is on screen twice already. Cached with the tree it flattens, since
        /// the grid asks for it every frame it lays out.
        /// </para>
        /// </summary>
        public IReadOnlyList<MeshAssetEntry> FilteredEntries
        {
            get
            {
                if (_flatDirty || _flat == null)
                {
                    var flat = new List<MeshAssetEntry>();
                    Flatten(Tree, flat);
                    _flat = flat;
                    _flatDirty = false;
                }

                return _flat;
            }
        }

        private static void Flatten(MeshAssetFolder folder, List<MeshAssetEntry> output)
        {
            output.AddRange(folder.Entries);
            foreach (var child in folder.Folders)
            {
                Flatten(child, output);
            }
        }

        /// <summary>
        /// The folder hierarchy alone, ignoring the text filter -- an Unreal-style folder sidebar should
        /// not shrink just because something is typed into the search box above the main list.
        /// <para>
        /// Cached like <see cref="Tree"/>, and for a sharper reason than "avoid rebuilding": the folder
        /// sidebar's TreeView tracks which nodes are expanded by the <see cref="MeshAssetFolder"/> object's
        /// own identity (deliberately -- see TreeView's own comment on why it uses reference equality, not
        /// value equality, for that). Handing back a freshly built tree on every call -- as this used to --
        /// means every node is a new instance each time, so a TreeView rebuild triggered by nothing more
        /// than expanding or collapsing one node would silently forget every expand/collapse state, because
        /// none of the old objects it remembered still exist. Invalidated on exactly what
        /// ManaVoxel.Code.UI.ManaUi.MeshImportManaView's TreeView.StructureSignature already keys its own
        /// rebuilds on -- Assets and Formats, not Filter -- so identity survives across everything that
        /// does not actually change which folders exist.
        /// </para>
        /// </summary>
        public MeshAssetFolder FolderTree
        {
            get
            {
                if (_folderTreeDirty || _folderTree == null)
                {
                    _folderTree = BuildTree(string.Empty);
                    _folderTreeDirty = false;
                }

                return _folderTree;
            }
        }

        public bool IsFormatEnabled(MeshAssetFormats format) => (_formats & format) != 0;

        public void SetFormatEnabled(MeshAssetFormats format, bool enabled)
        {
            Formats = enabled ? _formats | format : _formats & ~format;
        }

        /// <summary>
        /// Starts a scan if one is due -- a watcher saw a real change under one of the asset roots, or the
        /// caller forced it. Cheap to call every frame: with nothing dirty it is a bool check, and it will
        /// not stack a second scan on top of one already running.
        /// </summary>
        public void RequestScan(bool force = false)
        {
            EnsureWatching();

            if (!force && !_dirty)
            {
                return;
            }

            if (IsScanning)
            {
                return;
            }

            _dirty = false;
            _scan = Task.Run(Scan);
        }

        /// <summary>Takes the results of a finished scan. Returns true if the asset list changed.</summary>
        public bool PollScan()
        {
            if (_scan is not { IsCompleted: true } task)
            {
                return false;
            }

            _scan = null;
            var result = task.GetAwaiter().GetResult();
            var changed = SetAssets(result.Assets, markClean: false);
            ScanError = result.Error;
            return changed;
        }

        /// <summary>
        /// Replaces the asset list outright. The seam the tests use -- everything below this point is
        /// decisions about a list of paths, and none of it needs a disk.
        /// <para>
        /// Manual callers mark the library clean by default; scan results pass false so a watcher event that
        /// happened during the scan can still trigger another one.
        /// </para>
        /// <para>
        /// Returns false, and touches nothing, when the new list is identical to what is already held --
        /// a watcher can fire for a change that nets out to nothing (a save that rewrites a file with the
        /// same content, a rename and rename back), and a rebuild the list does not actually need is exactly
        /// the kind of surprise this replaced the polling rescan to avoid.
        /// </para>
        /// </summary>
        public bool SetAssets(IEnumerable<string> assets, bool markClean = true)
        {
            var incoming = assets as ICollection<string> ?? assets.ToList();
            if (markClean)
            {
                _dirty = false;
            }

            if (incoming.Count == _assets.Count && incoming.SequenceEqual(_assets, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            _assets.Clear();
            _assets.AddRange(incoming);

            // A selection that survived a rescan but not the files is worse than none: the buttons acting on
            // it would still be enabled.
            if (SelectedAsset != null && !_assets.Contains(SelectedAsset, StringComparer.OrdinalIgnoreCase))
            {
                SelectedAsset = null;
            }

            _selection.RemoveAll(item => !_assets.Contains(item, StringComparer.OrdinalIgnoreCase));

            Invalidate();
            _folderTreeDirty = true;
            return true;
        }

        private void Invalidate()
        {
            _treeDirty = true;
            _flatDirty = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // ----- tree ---------------------------------------------------------------------------------------

        private MeshAssetFolder BuildTree(string filterOverride)
        {
            var root = new MutableFolder("Assets");
            var filter = filterOverride.Trim();

            foreach (var asset in _assets)
            {
                if (!IsEnabled(asset))
                {
                    continue;
                }

                var label = Label(asset);

                // Filtered on the full workspace-relative path rather than the file name, so typing a folder
                // narrows to it -- which is how anyone with a deep asset tree actually searches.
                if (filter.Length > 0 && !label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Place(root, label, asset);
            }

            return Freeze(root, collapse: false);
        }

        private static void Place(MutableFolder root, string label, string asset)
        {
            var parts = label.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                root.Assets.Add(asset);
                return;
            }

            var folder = root;

            // The last part is the file name, so everything before it is the path.
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!folder.Children.TryGetValue(parts[i], out var child))
                {
                    child = new MutableFolder(parts[i]);
                    folder.Children.Add(parts[i], child);
                }

                folder = child;
            }

            folder.Assets.Add(asset);
        }

        /// <summary>
        /// Turns the working tree into the immutable one, collapsing runs of single-child folders into one
        /// row along the way -- "Assets/Models/Kenney/Nature" is one thing to click past, not four.
        /// </summary>
        private static MeshAssetFolder Freeze(MutableFolder folder, bool collapse)
        {
            var name = folder.Name;
            var node = folder;

            while (collapse && node.Assets.Count == 0 && node.Children.Count == 1)
            {
                var only = node.Children.Values.First();
                name = $"{name}/{only.Name}";
                node = only;
            }

            var folders = new List<MeshAssetFolder>(node.Children.Count);
            foreach (var child in node.Children.Values)
            {
                folders.Add(Freeze(child, collapse: true));
            }

            return new MeshAssetFolder(name, folders, GroupVariants(node.Assets));
        }

        /// <summary>
        /// Collapses one model's formats into a single row. A sample asset shipping .glb, .gltf and .obj is
        /// one model, and listing it three times is three chances to pick the worst one.
        /// </summary>
        private static IReadOnlyList<MeshAssetEntry> GroupVariants(IEnumerable<string> assets)
        {
            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var asset in assets)
            {
                var key = VariantGroupKey(asset);
                if (!groups.TryGetValue(key, out var variants))
                {
                    variants = new List<string>();
                    groups.Add(key, variants);
                }

                variants.Add(asset);
            }

            var entries = new List<MeshAssetEntry>(groups.Count);
            foreach (var group in groups)
            {
                group.Value.Sort(static (a, b) =>
                {
                    var order = Priority(a).CompareTo(Priority(b));
                    return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(DisplayName(a), DisplayName(b));
                });

                entries.Add(new MeshAssetEntry(group.Value[0], DisplayName(group.Value[0]), group.Value));
            }

            entries.Sort(static (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.DisplayName, b.DisplayName));
            return entries;
        }

        private static string VariantGroupKey(string asset)
        {
            if (TryDecodeZip(asset, out var zip, out var entry))
            {
                return $"{zip}|{PathWithoutExtension(entry.Replace('\\', '/'))}";
            }

            return Path.GetFileNameWithoutExtension(FileName(asset));
        }

        private static int Priority(string asset)
        {
            var extension = Extension(asset);
            for (var i = 0; i < FormatPreference.Length; i++)
            {
                if (extension.Equals(FormatPreference[i], StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            // Unlisted formats keep a deterministic order after the preferred ones.
            return FormatPreference.Length + 10;
        }

        private bool IsEnabled(string asset) => (_formats & FormatOf(asset)) != 0;

        // ----- asset references ---------------------------------------------------------------------------

        public static MeshAssetFormats FormatOf(string asset)
        {
            return Extension(asset).ToLowerInvariant() switch
            {
                ".glb" => MeshAssetFormats.Glb,
                ".gltf" => MeshAssetFormats.Gltf,
                ".fbx" => MeshAssetFormats.Fbx,
                ".dae" => MeshAssetFormats.Dae,
                ".obj" => MeshAssetFormats.Obj,
                ".stl" => MeshAssetFormats.Stl,
                ".ply" => MeshAssetFormats.Ply,
                ".png" => MeshAssetFormats.Png,
                _ => MeshAssetFormats.None
            };
        }

        /// <summary>A short format tag and its colour, so a row's format is readable without parsing the name.</summary>
        public static (string Label, Vector4 Color) Badge(string asset)
        {
            return Extension(asset).ToLowerInvariant() switch
            {
                ".glb" => ("GLB", new Vector4(0.22f, 0.67f, 0.95f, 1.0f)),
                ".gltf" => ("GLTF", new Vector4(0.32f, 0.78f, 0.56f, 1.0f)),
                ".obj" => ("OBJ", new Vector4(0.95f, 0.62f, 0.24f, 1.0f)),
                ".fbx" => ("FBX", new Vector4(0.86f, 0.39f, 0.39f, 1.0f)),
                ".dae" => ("DAE", new Vector4(0.62f, 0.48f, 0.90f, 1.0f)),
                ".stl" => ("STL", new Vector4(0.54f, 0.84f, 0.42f, 1.0f)),
                ".ply" => ("PLY", new Vector4(0.48f, 0.77f, 0.86f, 1.0f)),
                ".png" => ("PNG", new Vector4(0.95f, 0.45f, 0.70f, 1.0f)),
                _ => ("MESH", new Vector4(0.75f, 0.75f, 0.75f, 1.0f))
            };
        }

        public static string FileName(string asset)
        {
            return TryDecodeZip(asset, out _, out var entry) ? Path.GetFileName(entry) : Path.GetFileName(asset);
        }

        public static string DisplayName(string asset)
        {
            if (!TryDecodeZip(asset, out _, out var entry))
            {
                return Path.GetFileName(asset);
            }

            var normalized = entry.Replace('\\', '/').Trim('/');
            var file = Path.GetFileName(normalized);
            var stem = Path.GetFileNameWithoutExtension(file);

            // A lot of asset packs put every mesh in its own folder as "model.glb" or "scene.gltf". Showing
            // only that file name makes a grid of distinct assets read as duplicates, so generic names keep
            // enough archive path to identify what they really are.
            return IsGenericArchiveFileName(stem) ? normalized : file;
        }

        public static string Extension(string asset)
        {
            return TryDecodeZip(asset, out _, out var entry) ? Path.GetExtension(entry) : Path.GetExtension(asset);
        }

        private static bool IsGenericArchiveFileName(string stem)
        {
            return stem.Equals("model", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("scene", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("asset", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("mesh", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("untitled", StringComparison.OrdinalIgnoreCase);
        }

        private static string PathWithoutExtension(string path)
        {
            var extension = Path.GetExtension(path);
            return extension.Length == 0 ? path : path[..^extension.Length];
        }

        /// <summary>The path as shown in the tree -- workspace relative, forward slashed, archives prefixed.</summary>
        public static string Label(string asset)
        {
            if (TryDecodeZip(asset, out var zip, out var entry))
            {
                return $"zip/{Relative(zip)}/{entry.Replace('\\', '/')}";
            }

            return Relative(asset);
        }

        /// <summary>The full path, for a tooltip. Which file a row means is not obvious from a relative label.</summary>
        public static string Tooltip(string asset)
        {
            if (TryDecodeZip(asset, out var zip, out var entry))
            {
                return $"{zip}{Environment.NewLine}{entry.Replace('\\', '/')}";
            }

            return asset;
        }

        /// <summary>
        /// A reference to a mesh inside an archive.
        /// <para>
        /// Both halves are base64 rather than written plainly, because a Windows path contains a colon and
        /// an archive entry can contain very nearly anything -- there is no punctuation left to separate
        /// them with. This is the format the importer decodes, and the two have to stay identical: a
        /// reference it does not recognise is passed on as a filename, and the load fails on a path that is
        /// not one.
        /// </para>
        /// </summary>
        public static string EncodeZip(string zipPath, string entryPath)
        {
            var zip = Convert.ToBase64String(Encoding.UTF8.GetBytes(Path.GetFullPath(zipPath)));
            var entry = Convert.ToBase64String(Encoding.UTF8.GetBytes(entryPath.Replace('\\', '/')));
            return $"{ZipAssetPrefix}{zip}:{entry}";
        }

        /// <summary>
        /// A zip-packed mesh reference that stays valid when a built-in level is moved with the repo.
        /// Runtime resolution tries the working directory and the executable's project roots.
        /// </summary>
        public static string EncodeRelativeZip(string zipPath, string entryPath)
        {
            var zip = Convert.ToBase64String(Encoding.UTF8.GetBytes(zipPath.Replace('\\', '/')));
            var entry = Convert.ToBase64String(Encoding.UTF8.GetBytes(entryPath.Replace('\\', '/')));
            return $"{RelativeZipAssetPrefix}{zip}:{entry}";
        }


        private static string Relative(string path)
        {
            var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
            return string.IsNullOrWhiteSpace(relative) ? Path.GetFileName(path) : relative.Replace('\\', '/');
        }

        /// <summary>
        /// Whether a PNG is offered as a sprite picture: a loose one inside a Sprites folder, or one in a pack that
        /// has no models -- not at the pack's top (previews), not a packed sheet, and only one size of a pack that
        /// ships two (Kenney's Double folder repeats Default at twice the pixels).
        /// </summary>
        public static bool IsSpritePicture(string path, bool packHasModels, bool looseFile)
        {
            var normalized = path.Replace('\\', '/');
            if (!normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            var folders = normalized.Split('/')[..^1];
            if (looseFile)
            {
                return folders.Any(folder => folder.Equals("Sprites", StringComparison.OrdinalIgnoreCase));
            }
            return !packHasModels && folders.Length > 0
                && !folders.Any(folder => folder.Equals("Double", StringComparison.OrdinalIgnoreCase)
                    || folder.Equals("Spritesheets", StringComparison.OrdinalIgnoreCase)
                    || folder.Equals("Vector", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// What a Sprite stores for a picture listed here: a pack picture as "zip:pack.zip/path", a loose one by its
        /// path from the project, so a level still finds it on another machine.
        /// </summary>
        public static string SpritePicture(string asset)
        {
            if (TryDecodeZip(asset, out var zipPath, out var entryPath))
            {
                return EncodeReadableZip(Path.GetFileName(zipPath), entryPath);
            }
            var project = Path.GetDirectoryName(Mana.Code.Content.ManaContentAssets.Root) ?? Environment.CurrentDirectory;
            var full = Path.GetFullPath(asset);
            return full.StartsWith(project, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(project, full).Replace(Path.DirectorySeparatorChar, '/')
                : full;
        }

        public static bool IsSupported(string extension)
        {
            foreach (var candidate in SupportedExtensions)
            {
                if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // ----- watching -------------------------------------------------------------------------------------

        [UnsupportedOSPlatformGuard("browser")]
        private static bool IsFileWatchingSupported()
        {
            return !OperatingSystem.IsBrowser();
        }

        /// <summary>
        /// Starts a <see cref="FileSystemWatcher"/> on every asset root that exists, the first time a scan is
        /// asked for. Lazy, and not in the constructor, for the same reason scanning itself is driven from the
        /// panel rather than the render loop -- a library nobody has opened a picker for yet should not be
        /// asking the OS to watch a folder tree.
        /// </summary>
        private void EnsureWatching()
        {
            if (_watchers != null)
            {
                return;
            }

            // FileSystemWatcher throws PlatformNotSupportedException on browser-wasm; there is nothing
            // resembling a live directory tree to watch there anyway (assets are a fixed, publish-time
            // bundle -- see WasmFilesToIncludeInFileSystem in ManaVoxel.Editor.Web.csproj). Scanning
            // itself (Roots()/directory enumeration, called separately) still works against whatever the
            // bundle contains; this only skips the "notice new files without a rescan" half.
            if (!IsFileWatchingSupported())
            {
                return;
            }

            lock (_watchGate)
            {
                if (_watchers != null)
                {
                    return;
                }

                var watchers = new List<FileSystemWatcher>();

                foreach (var root in Roots())
                {
                    if (!Directory.Exists(root))
                    {
                        continue;
                    }

                    FileSystemWatcher watcher;

                    try
                    {
                        watcher = new FileSystemWatcher(root)
                        {
                            IncludeSubdirectories = true,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
                        };
                    }
                    catch (ArgumentException)
                    {
                        // A root that resolved to a path the watcher itself rejects (case differences, an
                        // inaccessible drive) -- skip it rather than take the panel down over a folder nobody
                        // needed watched anyway.
                        continue;
                    }

                    watcher.Changed += OnFileSystemEvent;
                    watcher.Created += OnFileSystemEvent;
                    watcher.Deleted += OnFileSystemEvent;
                    watcher.Renamed += OnFileSystemEvent;
                    watcher.Error += OnWatcherError;

                    try
                    {
                        watcher.EnableRaisingEvents = true;
                    }
                    catch
                    {
                        watcher.Dispose();
                        continue;
                    }

                    watchers.Add(watcher);
                }

                _watchers = watchers;
            }
        }

        /// <summary>
        /// A file changed under a watched root. Filtered to extensions the library cares about -- a watcher
        /// with no filter set fires on every touch of every file in the tree, and most of those (a texture
        /// saved next to a model, an editor's swap file) have nothing to do with what this list shows.
        /// </summary>
        [UnsupportedOSPlatform("browser")]
        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            if (!IsRelevant(e))
            {
                return;
            }

            // Debounced rather than marking dirty straight away: a copy or an unzip fires this many times in
            // quick succession, and without a delay the first one lands mid-copy -- a scan that finds half
            // the files and misses the rest.
            lock (_watchGate)
            {
                _debounceTimer ??= new Timer(_ => _dirty = true, null, Timeout.Infinite, Timeout.Infinite);
                _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }

        [UnsupportedOSPlatform("browser")]
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            // The watcher itself died (an overflowed internal buffer, a removable drive pulled out from under
            // it). The list it was watching is not wrong yet, but the next real change would go unseen, so
            // mark dirty once rather than leave the panel silently stale forever.
            _dirty = true;
        }

        [UnsupportedOSPlatform("browser")]
        private static bool IsRelevant(FileSystemEventArgs e)
        {
            if (e is RenamedEventArgs renamed && IsRelevantPath(renamed.OldFullPath))
            {
                return true;
            }

            return IsRelevantPath(e.FullPath);
        }

        [UnsupportedOSPlatform("browser")]
        private static bool IsRelevantPath(string path)
        {
            var extension = Path.GetExtension(path);
            return IsSupported(extension) || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            lock (_watchGate)
            {
                if (_watchers != null)
                {
                    foreach (var watcher in _watchers)
                    {
                        watcher.Dispose();
                    }

                    _watchers = null;
                }

                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
        }

        // ----- scanning -----------------------------------------------------------------------------------

        /// <summary>
        /// Where assets might live. The working directory differs between running from the IDE and from a
        /// published build, so every plausible root is tried and the missing ones skipped.
        /// </summary>
        public static IEnumerable<string> Roots()
        {
            yield return Path.Combine(Environment.CurrentDirectory, "Assets");
            yield return Path.Combine(Environment.CurrentDirectory, "Mana", "Assets");

            yield return Path.Combine(AppContext.BaseDirectory, "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "Mana", "Assets");

            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Assets");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Mana", "Assets");
        }

        private static MeshScanResult Scan()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ManaCore.Assets.ManaAssetCatalog.Current is { } catalog)
                found.UnionWith(catalog.Models);
            var unreadable = 0;

            foreach (var root in Roots())
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var extension in SupportedExtensions)
                {
                    foreach (var file in Directory.EnumerateFiles(root, $"*{extension}", SearchOption.AllDirectories))
                    {
                        var fullPath = Path.GetFullPath(file);
                        if (IsUnloadableGltf(fullPath))
                        {
                            continue;
                        }

                        found.Add(fullPath);
                    }
                }

                foreach (var picture in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
                {
                    if (IsSpritePicture(Path.GetRelativePath(root, picture), packHasModels: false, looseFile: true))
                    {
                        found.Add(Path.GetFullPath(picture));
                    }
                }

                foreach (var zip in Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories))
                {
                    if (!ScanZip(zip, found))
                    {
                        unreadable++;
                    }
                }
            }

            var sorted = found.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToList();
            return new MeshScanResult(sorted, unreadable > 0 ? $"{unreadable} archive(s) could not be read" : null);
        }

        /// <summary>
        /// True for a glTF/GLB that requires an extension this engine cannot read (Draco compression above
        /// all) and has no loadable sibling to fall back to. ManaSharpGltfLoader already substitutes a
        /// sibling transparently when one exists (see ManaGltfSupport), so a file that would load fine that
        /// way stays in the list; only a genuine dead end -- one that would throw the moment it was picked
        /// -- is hidden here instead of shown just to fail.
        /// </summary>
        private static bool IsUnloadableGltf(string path) =>
            ManaGltfSupport.DescribeUnsupported(path) != null && ManaGltfSupport.FindSupportedAlternative(path) == null;

        /// <summary>How much of a zip entry to buffer before asking ManaGltfSupport about it -- matches the
        /// cap ManaGltfSupport.DescribeUnsupported itself applies when reading a plain glTF's JSON directly
        /// off a seekable stream; extensionsRequired sits well inside this for any real export.</summary>
        private const int HeaderScanCap = 512 * 1024;

        private static bool ScanZip(string zipPath, HashSet<string> found)
        {
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var modelsBefore = found.Count;

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Name) || !IsSupported(Path.GetExtension(entry.FullName)))
                    {
                        continue;
                    }

                    // No on-disk sibling folder to fall back to inside an archive entry, unlike a loose
                    // file (see IsUnloadableGltf) -- an unsupported entry here has no way to load at all.
                    // DescribeUnsupported needs to seek, which the entry's own (typically deflate-compressed)
                    // stream cannot do, so a capped prefix is buffered first -- the same amount
                    // DescribeUnsupported itself would read out of a seekable one.
                    using (var entryStream = entry.Open())
                    using (var buffered = new MemoryStream())
                    {
                        var header = new byte[HeaderScanCap];
                        var read = 0;
                        int chunk;
                        while (read < header.Length
                               && (chunk = entryStream.Read(header, read, header.Length - read)) > 0)
                        {
                            read += chunk;
                        }

                        buffered.Write(header, 0, read);
                        buffered.Position = 0;
                        if (ManaGltfSupport.DescribeUnsupported(buffered) != null)
                        {
                            continue;
                        }
                    }

                    found.Add(EncodeZip(zipPath, entry.FullName));
                }

                // A pack's pictures are sprites only when it has no models: a 3D pack's PNGs are the models' textures.
                var hasModels = found.Count > modelsBefore;
                foreach (var entry in archive.Entries)
                {
                    if (IsSpritePicture(entry.FullName, hasModels, looseFile: false))
                    {
                        found.Add(EncodeZip(zipPath, entry.FullName));
                    }
                }

                return true;
            }
            catch
            {
                // One locked or malformed archive should not take the whole scan down with it.
                return false;
            }
        }

        private readonly record struct MeshScanResult(List<string> Assets, string? Error);

        private sealed class MutableFolder
        {
            public MutableFolder(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public SortedDictionary<string, MutableFolder> Children { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public List<string> Assets { get; } = new();
        }
    }

    [Flags]
    public enum MeshAssetFormats
    {
        None = 0,
        Glb = 1 << 0,
        Gltf = 1 << 1,
        Fbx = 1 << 2,
        Dae = 1 << 3,
        Obj = 1 << 4,
        Stl = 1 << 5,
        Ply = 1 << 6,

        /// <summary>Pictures for sprites: a 2D pack's, or anything in a Sprites folder.</summary>
        Png = 1 << 7,

        /// <summary>What the importer handles best, and so what the panel opens showing.</summary>
        Default = Glb | Gltf | Png,

        All = Glb | Gltf | Fbx | Dae | Obj | Stl | Ply | Png
    }

    /// <summary>A folder of assets. Names may span several real folders where a chain of them was collapsed.</summary>
    public sealed record MeshAssetFolder(
        string Name,
        IReadOnlyList<MeshAssetFolder> Folders,
        IReadOnlyList<MeshAssetEntry> Entries)
    {
        public bool IsEmpty => Folders.Count == 0 && Entries.Count == 0;

        /// <summary>Assets anywhere below here, for a count beside a collapsed folder.</summary>
        public int TotalEntries
        {
            get
            {
                var total = Entries.Count;
                foreach (var folder in Folders)
                {
                    total += folder.TotalEntries;
                }

                return total;
            }
        }
    }

    /// <summary>
    /// One model. <see cref="Variants"/> holds every format it ships in, best first, and
    /// <see cref="PrimaryPath"/> is the one that gets imported unless another is chosen.
    /// </summary>
    public sealed record MeshAssetEntry(
        string PrimaryPath,
        string DisplayName,
        IReadOnlyList<string> Variants)
    {
        public bool HasVariants => Variants.Count > 1;
    }
}

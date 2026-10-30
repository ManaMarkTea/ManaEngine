using System.Text;
using System.Text.Json;
using ManaCore.Diagnostics;
using ManaCore.Imaging;

namespace ManaCore.Assets;

/// <summary>Catalog-backed downloads staged into the ordinary filesystem before synchronous import.</summary>
public sealed class ManaAssetCatalog
{
    public static ManaAssetCatalog? Current { get; set; }
    private sealed record Asset(string Path, string Url, string Hash, long Size, string[] Dependencies);
    private readonly Dictionary<string, Asset> files = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim downloadSlots = new(2);
    private readonly Func<string, string, long, Task<byte[]>> download;
    private readonly string root;
    private readonly Dictionary<string, string> modelThumbnails = new(StringComparer.Ordinal);
    public IReadOnlyList<string> Models { get; }
    public IEnumerable<string> MaterialZips => files.Keys.Where(path =>
        path.StartsWith("Assets/Materials/", StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A catalog whose blob URLs may point at <paramref name="externalAssetBaseUrl"/> instead of only the
    /// local <c>AssetLibrary/</c> the catalog itself ships beside -- the web editor's external library is
    /// too large for itch.io's browser-embed size cap, so its blobs are hosted on GitHub Pages instead while
    /// the small catalog file stays local. Content is SHA256-verified after download regardless of origin;
    /// this only limits which origin a well-formed catalog is allowed to name; it does not accept an
    /// arbitrary attacker-supplied host.
    /// </summary>
    public ManaAssetCatalog(string json, string root, Func<string, string, long, Task<byte[]>> download,
        string? externalAssetBaseUrl = null)
    {
        this.root = Path.GetFullPath(root);
        this.download = download;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.GetProperty("version").GetInt32() != 1)
            throw new InvalidDataException("Unsupported asset catalog version.");
        foreach (var item in document.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = item.GetProperty("path").GetString()!;
            ValidatePath(path);
            var url = item.GetProperty("url").GetString()!;
            ValidateUrl(url, externalAssetBaseUrl);
            var dependencies = item.GetProperty("dependencies").EnumerateArray().Select(x => x.GetString()!).ToArray();
            foreach (var dependency in dependencies)
                ValidatePath(dependency);
            files.Add(path, new Asset(path, url, item.GetProperty("sha256").GetString()!,
                item.GetProperty("size").GetInt64(), dependencies));
        }
        foreach (var file in files.Values)
            foreach (var dependency in file.Dependencies)
                if (!files.ContainsKey(dependency))
                    throw new InvalidDataException($"Missing catalog dependency: {dependency}");

        var models = new List<string>();
        foreach (var item in document.RootElement.GetProperty("models").EnumerateArray())
        {
            var path = item.GetProperty("path").GetString()!;
            if (!files.ContainsKey(path))
                throw new InvalidDataException($"Missing catalog model: {path}");
            var local = LocalPath(path);
            var reference = item.TryGetProperty("entry", out var entry)
                ? "zipasset:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(local)) + ":"
                    + Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.GetString()!))
                : local;
            models.Add(reference);

            // Pre-built by Tools/ThumbnailBaker at publish time (see split_for_itch.py for the same idea
            // applied to blob urls); optional, so an older catalog with no baker run over it still works,
            // just without an instant preview before the model itself downloads.
            if (item.TryGetProperty("thumbnail", out var thumbnail) && thumbnail.GetString() is { Length: > 0 } thumbnailPath)
            {
                ValidatePath(thumbnailPath);
                if (!files.ContainsKey(thumbnailPath))
                    throw new InvalidDataException($"Missing catalog thumbnail: {thumbnailPath}");
                modelThumbnails[reference] = thumbnailPath;
            }
        }
        Models = models;
    }

    private static void ValidatePath(string path, string? prefix = null)
    {
        // Games/: a game's own models and sounds, published under their repo paths.
        var allowed = path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Library/Imported/", StringComparison.Ordinal)
            || path.StartsWith("Games/", StringComparison.Ordinal);
        if (prefix != null)
        {
            allowed = path.StartsWith(prefix, StringComparison.Ordinal);
        }
        if (!allowed || path.Contains('\\') || path.Contains(':')
            || path.Split('/').Any(part => part is ".." or "." or ""))
            throw new InvalidDataException($"Invalid catalog path: {path}");
    }

    /// <summary>A blob URL is either the local relative form, or -- only when the caller named one --
    /// exactly the trusted external host, checked as a real absolute HTTPS URL rather than by prefix text
    /// alone so a lookalike (e.g. the prefix followed by "@attacker.example") cannot sneak through.</summary>
    private static void ValidateUrl(string url, string? externalAssetBaseUrl)
    {
        if (url.StartsWith("AssetLibrary/", StringComparison.Ordinal))
        {
            ValidatePath(url, "AssetLibrary/");
            return;
        }

        if (externalAssetBaseUrl != null
            && url.StartsWith(externalAssetBaseUrl, StringComparison.Ordinal)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && Uri.TryCreate(externalAssetBaseUrl, UriKind.Absolute, out var baseUri)
            && uri.Host == baseUri.Host
            && !url.Contains(".."))
        {
            return;
        }

        throw new InvalidDataException($"Invalid catalog path: {url}");
    }

    private string LocalPath(string path) => Path.GetFullPath(Path.Combine(root, path));

    private string? Key(string reference)
    {
        // "zip:kenney_pack.zip/Models/x.glb" -- the readable form names the zip by file name only, so the
        // catalog file is the one whose path ends with that name, wherever it is filed.
        if (reference.StartsWith("zip:", StringComparison.OrdinalIgnoreCase))
        {
            var body = reference[4..].Replace('\\', '/');
            var split = body.IndexOf(".zip/", StringComparison.OrdinalIgnoreCase);
            if (split < 0) return null;
            var zipName = Path.GetFileName(body[..(split + 4)]);
            return files.Keys.FirstOrDefault(key => string.Equals(Path.GetFileName(key), zipName, StringComparison.OrdinalIgnoreCase));
        }

        if (reference.StartsWith("zipasset:", StringComparison.Ordinal)
            || reference.StartsWith("zipasset-rel:", StringComparison.Ordinal))
        {
            var start = reference.IndexOf(':') + 1;
            var end = reference.IndexOf(':', start);
            if (end < 0) throw new InvalidDataException("Malformed zip asset reference.");
            reference = Encoding.UTF8.GetString(Convert.FromBase64String(reference[start..end]));
        }
        var normalized = reference.Replace('\\', '/');
        if (normalized.StartsWith("Mana/Assets/", StringComparison.Ordinal))
            normalized = normalized[5..];
        if (files.ContainsKey(normalized)) return normalized;
        if (Path.IsPathRooted(reference))
        {
            var relative = Path.GetRelativePath(root, reference).Replace('\\', '/');
            if (files.ContainsKey(relative)) return relative;
        }
        return null;
    }

    public bool IsReady(string reference)
    {
        var key = Key(reference);
        if (key == null) return true; // Local user imports are not catalog assets.
        return Closure(key).All(path => File.Exists(LocalPath(path)));
    }

    /// <summary>Whether this asset or one of its dependencies (its buffers/textures) is downloading right
    /// now -- for a per-tile progress indicator, as opposed to a global "something is downloading" flag.</summary>
    public bool IsDownloading(string reference)
    {
        var key = Key(reference);
        if (key == null) return false; // Local user imports are never in the catalog to begin with.
        return Closure(key).Any(ManaAssetDownloadActivity.IsActive);
    }

    /// <summary>Just this reference's own catalog entry -- not its dependencies' sizes -- or null for a
    /// local user import the catalog never heard of. For an info tooltip, not for deciding what to fetch.</summary>
    public long? SizeOf(string reference)
    {
        var key = Key(reference);
        return key == null ? null : files[key].Size;
    }

    /// <summary>Where this reference's own bytes come from -- the local relative form, or the trusted
    /// external host, whichever the catalog that loaded this instance actually named. Null for a local
    /// user import.</summary>
    public string? UrlOf(string reference)
    {
        var key = Key(reference);
        return key == null ? null : files[key].Url;
    }

    /// <summary>
    /// The catalog path of a pre-built preview for this exact model reference, or null when the catalog
    /// was published without one (an older build, or a model the baker skipped). Looked up by the resolved
    /// model reference itself rather than through <see cref="Key"/> -- a zip-packed model's preview differs
    /// per entry, while Key() resolves only to the zip file's own (entry-less) catalog path.
    /// </summary>
    public string? ThumbnailPathOf(string modelReference) =>
        modelThumbnails.TryGetValue(modelReference, out var path) ? path : null;

    /// <summary>Where a catalog path lands once downloaded. Public so a caller that already has the exact
    /// catalog path (as <see cref="ThumbnailPathOf"/> hands back, not an arbitrary reference) can read the
    /// file straight off disk after <see cref="EnsureAsync"/> -- everything else here works in references,
    /// which need <see cref="Key"/> to resolve; this one already has the resolved path.</summary>
    public string LocalFilePath(string catalogPath) => LocalPath(catalogPath);

    /// <summary>Where a reference's own file lands once downloaded, or null when the catalog does not hold it.</summary>
    public string? LocalFileOf(string reference) => Key(reference) is { } key ? LocalPath(key) : null;

    private IEnumerable<string> Closure(string key)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(key);
        while (queue.TryDequeue(out var path))
        {
            if (!seen.Add(path)) continue;
            yield return path;
            foreach (var dependency in files[path].Dependencies) queue.Enqueue(dependency);
        }
    }

    public async Task EnsureAsync(string reference)
    {
        var key = Key(reference);
        if (key != null) await DownloadClosureAsync(key);
        await PreparePicturesAsync(reference, key);
    }

    /// <summary>
    /// The browser decodes pictures only asynchronously, so whatever was just made ready to load has its
    /// pictures decoded now, for the synchronous load that follows (<see cref="ManaImageCodec"/>).
    /// </summary>
    private async Task PreparePicturesAsync(string reference, string? key)
    {
        if (!OperatingSystem.IsBrowser()) return;
        try
        {
            foreach (var picture in PicturesOf(reference, key))
                await ManaImageCodec.PrepareAsync(picture);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or FormatException)
        {
            ManaLog.WriteDiagnostic($"[Images] could not decode the pictures of '{reference}' ahead: {exception.Message}");
        }
    }

    private IEnumerable<byte[]> PicturesOf(string reference, string? key)
    {
        if (reference.StartsWith("zip:", StringComparison.OrdinalIgnoreCase))
        {
            // A whole pack ("zip:pack.zip/") is too much to decode; a picture or model in one is not.
            var body = reference[4..].Replace('\\', '/');
            var split = body.IndexOf(".zip/", StringComparison.OrdinalIgnoreCase);
            var entry = split < 0 ? "" : body[(split + 5)..];
            return key == null || entry.Length == 0 ? [] : ManaModelPictures.OfZipEntry(LocalPath(key), entry);
        }

        if (reference.StartsWith("zipasset:", StringComparison.Ordinal) || reference.StartsWith("zipasset-rel:", StringComparison.Ordinal))
        {
            var start = reference.IndexOf(':') + 1;
            var end = reference.IndexOf(':', start);
            if (end < 0) return [];
            var zip = key != null ? LocalPath(key) : Encoding.UTF8.GetString(Convert.FromBase64String(reference[start..end]));
            var entry = Encoding.UTF8.GetString(Convert.FromBase64String(reference[(end + 1)..]));
            return File.Exists(zip) ? ManaModelPictures.OfZipEntry(zip, entry) : [];
        }

        // A model's own pictures, and those of a sibling it falls back to, which its closure holds.
        var files = key != null ? Closure(key).Select(LocalPath) : File.Exists(reference) ? [reference] : [];
        return files.Where(File.Exists).SelectMany(ManaModelPictures.OfFile);
    }

    private async Task DownloadClosureAsync(string key)
    {
        // Sequential dependency loads bound peak browser memory; requests for the same file share a task.
        foreach (var path in Closure(key))
        {
            Task task;
            lock (pending)
            {
                if (!pending.TryGetValue(path, out task!))
                    pending[path] = task = DownloadAsync(files[path]);
            }
            try { await task; }
            finally
            {
                lock (pending)
                    if (pending.TryGetValue(path, out var current) && ReferenceEquals(current, task))
                        pending.Remove(path);
            }
        }
    }

    private async Task DownloadAsync(Asset asset)
    {
        var path = LocalPath(asset.Path);
        if (File.Exists(path)) return;
        ManaAssetDownloadActivity.Started(asset.Path, asset.Size);
        var succeeded = false;
        try
        {
            byte[] bytes;
            await downloadSlots.WaitAsync();
            try { bytes = await download(asset.Url, asset.Hash, asset.Size); }
            finally { downloadSlots.Release(); }
            if (bytes.LongLength != asset.Size)
                throw new InvalidDataException($"Incomplete asset download: {asset.Path}");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".download";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            succeeded = true;
        }
        finally { ManaAssetDownloadActivity.Finished(asset.Path, asset.Size, succeeded); }
    }
}

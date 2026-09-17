using System.Text;
using System.Text.Json;

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
    public IReadOnlyList<string> Models { get; }

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

        Models = document.RootElement.GetProperty("models").EnumerateArray().Select(item =>
        {
            var path = item.GetProperty("path").GetString()!;
            if (!files.ContainsKey(path))
                throw new InvalidDataException($"Missing catalog model: {path}");
            var local = LocalPath(path);
            return item.TryGetProperty("entry", out var entry)
                ? "zipasset:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(local)) + ":"
                    + Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.GetString()!))
                : local;
        }).ToArray();
    }

    private static void ValidatePath(string path, string prefix = "Assets/")
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':')
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
        if (key == null) return;
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

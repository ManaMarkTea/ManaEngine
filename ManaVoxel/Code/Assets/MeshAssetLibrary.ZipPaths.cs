using System.IO.Compression;
using System.Text;

namespace ManaVoxel.Code.Assets
{
    // Encoding/decoding the "zipasset:" and "zipasset-rel:" references a level stores for a mesh
    // that lives inside a zip. Split into its own partial so the browser build can compile just
    // this half: the rest of MeshAssetLibrary is an editor concern (directory scanning,
    // FileSystemWatcher, thumbnail siblings) with no browser-wasm story, while level loading needs
    // only this decoding. Pure string/base64/path work.
    public sealed partial class MeshAssetLibrary
    {
        private const string ZipAssetPrefix = "zipasset:";
        private const string RelativeZipAssetPrefix = "zipasset-rel:";

        // The readable form: "zip:kenney_city-kit-roads.zip/Models/GLB format/construction-cone.glb". Located by
        // the zip's file name, wherever under an Assets folder it now lives, so moving the packs does not break
        // a level -- see LocateZip.
        private const string ReadableZipPrefix = "zip:";

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LocatedZips =
            new(StringComparer.OrdinalIgnoreCase);

        public static bool TryDecodeZip(string asset, out string zipPath, out string entryPath)
        {
            zipPath = string.Empty;
            entryPath = string.Empty;

            if (asset.StartsWith(ZipAssetPrefix, StringComparison.Ordinal))
            {
                return TryDecodeZipBody(asset[ZipAssetPrefix.Length..], resolveRelative: false, out zipPath, out entryPath);
            }

            if (asset.StartsWith(RelativeZipAssetPrefix, StringComparison.Ordinal))
            {
                return TryDecodeZipBody(asset[RelativeZipAssetPrefix.Length..], resolveRelative: true, out zipPath, out entryPath);
            }

            if (asset.StartsWith(ReadableZipPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return TryDecodeReadable(asset[ReadableZipPrefix.Length..], out zipPath, out entryPath);
            }

            return false;
        }

        /// <summary>
        /// A zip-packed mesh written so a person can read it in a level file: the zip's name, then the model's
        /// path inside it. The zip is matched by name, not by where it happens to be, so it survives the packs
        /// being moved to another folder.
        /// </summary>
        public static string EncodeReadableZip(string zipFileName, string entryPath)
        {
            return $"{ReadableZipPrefix}{zipFileName.Replace('\\', '/')}/{entryPath.Replace('\\', '/')}";
        }

        /// <summary>
        /// Rewrites a reference in the readable form when it can be, and returns it unchanged when it cannot --
        /// a zip outside every Assets folder has nothing to be found by but its full path, so it keeps the
        /// encoded form that carries one.
        /// </summary>
        public static string ToReadableReference(string asset)
        {
            if (asset.StartsWith(ReadableZipPrefix, StringComparison.OrdinalIgnoreCase)
                || !TryDecodeZip(asset, out var zipPath, out var entryPath))
            {
                return asset;
            }

            var name = Path.GetFileName(zipPath);
            var found = FindZipByFileName(name);
            return found != null && string.Equals(Path.GetFullPath(found), Path.GetFullPath(zipPath), StringComparison.OrdinalIgnoreCase)
                ? EncodeReadableZip(name, entryPath)
                : asset;
        }

        /// <summary>
        /// Opens an entry by the path a level wrote, and when the pack has been rearranged since, by its file
        /// name -- so a model moved between folders inside a zip is still found. Prefers a "GLB format" copy,
        /// which is the one the importer wants when a pack ships several formats of one model.
        /// </summary>
        public static ZipArchiveEntry? FindZipEntry(ZipArchive archive, string entryPath)
        {
            var normalized = entryPath.Replace('\\', '/');
            var exact = archive.GetEntry(normalized);
            if (exact != null)
            {
                return exact;
            }

            var byPath = archive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName, normalized, StringComparison.OrdinalIgnoreCase));
            if (byPath != null)
            {
                return byPath;
            }

            var fileName = Path.GetFileName(normalized);
            var sameName = archive.Entries
                .Where(entry => string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return sameName.FirstOrDefault(entry => entry.FullName.Contains("GLB format", StringComparison.OrdinalIgnoreCase))
                ?? sameName.FirstOrDefault();
        }

        private static bool TryDecodeReadable(string body, out string zipPath, out string entryPath)
        {
            zipPath = string.Empty;
            entryPath = string.Empty;

            var split = body.IndexOf(".zip/", StringComparison.OrdinalIgnoreCase);
            if (split < 0 || split + 5 >= body.Length)
            {
                return false;
            }

            zipPath = LocateZip(body[..(split + 4)]);
            entryPath = body[(split + 5)..].Replace('\\', '/');
            return !string.IsNullOrWhiteSpace(entryPath);
        }

        // Where the zip is now: the path as written first (a level saved next to its assets still works), then
        // the same under each root's Assets folder, then anywhere under an Assets folder by file name.
        private static string LocateZip(string written)
        {
            var normalized = written.Replace('\\', '/');
            var relative = normalized.Replace('/', Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(relative) && File.Exists(relative))
            {
                return Path.GetFullPath(relative);
            }

            foreach (var root in RelativeZipRoots())
            {
                foreach (var candidate in new[] { Path.Combine(root, relative), Path.Combine(root, "Assets", relative) })
                {
                    try
                    {
                        if (File.Exists(candidate))
                        {
                            return Path.GetFullPath(candidate);
                        }
                    }
                    catch (ArgumentException)
                    {
                    }
                }
            }

            return FindZipByFileName(Path.GetFileName(normalized))
                ?? Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Assets", Path.GetFileName(normalized)));
        }

        // Anywhere beneath an Assets folder, by file name. Only hits are remembered, so a pack dropped in after
        // a failed lookup is still found on the next one.
        private static string? FindZipByFileName(string fileName)
        {
            if (LocatedZips.TryGetValue(fileName, out var cached) && File.Exists(cached))
            {
                return cached;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in RelativeZipRoots())
            {
                try
                {
                    var assets = Path.GetFullPath(Path.Combine(root, "Assets"));
                    if (!seen.Add(assets) || !Directory.Exists(assets))
                    {
                        continue;
                    }

                    var hit = Directory.EnumerateFiles(assets, fileName, SearchOption.AllDirectories).FirstOrDefault();
                    if (hit != null)
                    {
                        return LocatedZips[fileName] = Path.GetFullPath(hit);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    // No searchable filesystem here (a browser build, a locked folder): try the next root.
                }
            }

            return null;
        }

        private static bool TryDecodeZipBody(
            string body,
            bool resolveRelative,
            out string zipPath,
            out string entryPath)
        {
            zipPath = string.Empty;
            entryPath = string.Empty;

            var separator = body.IndexOf(':');
            if (separator <= 0 || separator >= body.Length - 1)
            {
                return false;
            }

            try
            {
                var decodedZipPath = Encoding.UTF8.GetString(Convert.FromBase64String(body[..separator]));
                zipPath = resolveRelative ? ResolveRelativeZipPath(decodedZipPath) : decodedZipPath;
                entryPath = Encoding.UTF8.GetString(Convert.FromBase64String(body[(separator + 1)..]));
                return !string.IsNullOrWhiteSpace(zipPath) && !string.IsNullOrWhiteSpace(entryPath);
            }
            catch
            {
                // Not a reference we wrote. Treated as a plain path rather than thrown on, which is what a
                // stray colon in a filename should do.
                return false;
            }
        }

        private static string ResolveRelativeZipPath(string zipPath)
        {
            if (Path.IsPathRooted(zipPath))
            {
                return Path.GetFullPath(zipPath);
            }

            var normalized = zipPath.Replace('/', Path.DirectorySeparatorChar);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in RelativeZipRoots())
            {
                var fullRoot = Path.GetFullPath(root);
                if (!seen.Add(fullRoot))
                {
                    continue;
                }

                var candidate = Path.GetFullPath(Path.Combine(fullRoot, normalized));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // Not where the level said, so look for it by name -- which is what lets a pack be moved.
            return FindZipByFileName(Path.GetFileName(normalized))
                ?? Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, normalized));
        }

        private static IEnumerable<string> RelativeZipRoots()
        {
            yield return Environment.CurrentDirectory;
            yield return AppContext.BaseDirectory;
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
        }
    }
}

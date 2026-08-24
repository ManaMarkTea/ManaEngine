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

            return false;
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

            return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, normalized));
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

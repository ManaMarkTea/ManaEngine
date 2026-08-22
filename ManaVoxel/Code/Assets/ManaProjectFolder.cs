namespace ManaVoxel.Code.Assets
{
    /// <summary>
    /// Resolves a project-local folder that has to sit at a stable, discoverable place relative to the
    /// project -- levels, generated caches -- probed the same way <see cref="MeshAssetLibrary.Roots"/> probes
    /// for the Assets folder itself, and for the same reason: the working directory differs between
    /// <c>dotnet run</c>, a published build and the exe run straight out of <c>bin</c>, so a fixed relative
    /// path is wrong in at least two of the three.
    /// </summary>
    public static class ManaProjectFolder
    {
        /// <summary>
        /// The folder, created if this is the first time. Never throws on a read-only location -- it falls
        /// back to the executable's own folder, which is at worst a place the content can still be written.
        /// </summary>
        public static string Resolve(string folderName)
        {
            foreach (var root in Roots(folderName))
            {
                if (Directory.Exists(root))
                {
                    return Path.GetFullPath(root);
                }
            }

            // Beside the repo's Assets folder when there is one, so this sits with the content rather than
            // in a build output.
            foreach (var assetRoot in MeshAssetLibrary.Roots())
            {
                if (!Directory.Exists(assetRoot))
                {
                    continue;
                }

                var parent = Path.GetDirectoryName(Path.GetFullPath(assetRoot));
                if (string.IsNullOrEmpty(parent))
                {
                    continue;
                }

                var candidate = Path.Combine(parent, folderName);
                if (TryCreate(candidate))
                {
                    return candidate;
                }
            }

            var fallback = Path.Combine(AppContext.BaseDirectory, folderName);
            TryCreate(fallback);
            return fallback;
        }

        private static IEnumerable<string> Roots(string folderName)
        {
            yield return Path.Combine(Environment.CurrentDirectory, folderName);
            yield return Path.Combine(AppContext.BaseDirectory, folderName);
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", folderName);
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", folderName);
        }

        private static bool TryCreate(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}

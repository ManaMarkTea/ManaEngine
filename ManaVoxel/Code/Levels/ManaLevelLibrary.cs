namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// Where levels live on disk, and what is in there.
    /// <para>
    /// Probed the same way <see cref="Assets.MeshAssetLibrary.Roots"/> probes for assets, and for the same
    /// reason: the working directory is different under <c>dotnet run</c>, under a published build and
    /// under the exe run straight out of <c>bin</c>, so a fixed relative path is wrong in at least two of
    /// the three. The first folder that already exists wins, which means a repo checkout keeps its levels
    /// in the repo rather than beside a build output nobody would think to look in.
    /// </para>
    /// </summary>
    public static class ManaLevelLibrary
    {
        public const string FolderName = "Levels";

        private static IEnumerable<string> Roots()
        {
            yield return Path.Combine(Environment.CurrentDirectory, FolderName);
            yield return Path.Combine(AppContext.BaseDirectory, FolderName);
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", FolderName);
            yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", FolderName);
        }

        /// <summary>
        /// The folder levels are read from and written to, created if this is the first time. Never throws
        /// on a read-only location -- it falls back to the executable's own folder, which is at worst a
        /// place levels can still be saved.
        /// </summary>
        public static string ResolveFolder()
        {
            foreach (var root in Roots())
            {
                if (Directory.Exists(root))
                {
                    return Path.GetFullPath(root);
                }
            }

            // Beside the repo's Assets folder when there is one, so levels sit with the content rather than
            // in a build output.
            foreach (var assetRoot in Assets.MeshAssetLibrary.Roots())
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

                var candidate = Path.Combine(parent, FolderName);
                if (TryCreate(candidate))
                {
                    return candidate;
                }
            }

            var fallback = Path.Combine(AppContext.BaseDirectory, FolderName);
            TryCreate(fallback);
            return fallback;
        }

        /// <summary>Every level file in the folder, newest first -- the one you were just working on is on top.</summary>
        public static IReadOnlyList<string> List()
        {
            var folder = ResolveFolder();
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory
                    .EnumerateFiles(folder, "*" + ManaLevel.FileExtension)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .ToList();
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Turns a typed-in name into a full path, and leaves a path that is already one alone.</summary>
        public static string ResolvePath(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath))
            {
                nameOrPath = "Untitled";
            }

            if (Path.IsPathRooted(nameOrPath))
            {
                return Path.ChangeExtension(nameOrPath, ManaLevel.FileExtension);
            }

            var safeName = string.Concat(nameOrPath.Split(Path.GetInvalidFileNameChars())).Trim();
            if (safeName.Length == 0)
            {
                safeName = "Untitled";
            }

            return Path.Combine(ResolveFolder(), Path.ChangeExtension(safeName, ManaLevel.FileExtension));
        }

        public static string DisplayName(string path) => Path.GetFileNameWithoutExtension(path);

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

using ManaVoxel.Code.Assets;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// Where levels live on disk, and what is in there. Levels are authored content, unlike the caches
    /// <see cref="ManaProjectFolder"/> is also used for -- the "first folder that already exists wins" rule
    /// it applies here is what makes a repo checkout keep its levels in the repo, travelling with it, rather
    /// than beside a build output nobody would think to look in.
    /// </summary>
    public static class ManaLevelLibrary
    {
        public const string FolderName = "Levels";

        /// <summary>
        /// The folder levels are read from and written to, created if this is the first time. Never throws
        /// on a read-only location -- it falls back to the executable's own folder, which is at worst a
        /// place levels can still be saved.
        /// </summary>
        public static string ResolveFolder() => ManaProjectFolder.Resolve(FolderName);

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
    }
}

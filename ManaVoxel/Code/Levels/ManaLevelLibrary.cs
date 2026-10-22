using Mana.Code.Content;
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
        public const string FolderName = "Content/Levels";

        /// <summary>
        /// The folder levels are read from and written to, created if this is the first time. Never throws
        /// on a read-only location -- it falls back to the executable's own folder, which is at worst a
        /// place levels can still be saved.
        /// </summary>
        public static string ResolveFolder() => ManaProjectFolder.Resolve(FolderName);

        /// <summary>
        /// Every folder levels are kept in: the main one (no name), then each game's own
        /// (<c>Games/&lt;Name&gt;/Content/Levels</c>, the content mounts beside it), named by its game.
        /// </summary>
        public static IReadOnlyList<(string Game, string Folder)> Folders() => Folders(ResolveFolder());

        public static IReadOnlyList<(string Game, string Folder)> Folders(string mainFolder)
        {
            var main = Path.GetFullPath(mainFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folders = new List<(string, string)> { (string.Empty, main) };
            if (Path.GetDirectoryName(main) is { } content)
            {
                folders.AddRange(ManaContentMounts.For(content).Where(mount => !mount.IsMain)
                    .Select(mount => (mount.Name, Path.Combine(mount.Root, "Levels")))
                    .Where(game => Directory.Exists(game.Item2)));
            }
            return folders;
        }

        /// <summary>Every level file beneath the folders, newest first -- the one you were just working on is on top.</summary>
        public static IReadOnlyList<string> List() => List(ResolveFolder());

        public static IReadOnlyList<string> List(string mainFolder)
        {
            return Folders(mainFolder).SelectMany(folder => ListOne(folder.Folder))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }

        private static IEnumerable<string> ListOne(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory
                    .EnumerateFiles(folder, "*" + ManaLevel.FileExtension, new EnumerationOptions
                    {
                        RecurseSubdirectories = true, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    })
                    .ToList();
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Where a level sits among the folders, for menus and the scene browser: relative to the main folder, or
        /// <c>Games/&lt;Name&gt;/...</c> for a game's -- or null for a level in none of them.
        /// </summary>
        public static string? RelativePath(string path, string mainFolder)
        {
            var full = Path.GetFullPath(path);
            foreach (var (game, folder) in Folders(mainFolder))
            {
                var relative = Path.GetRelativePath(folder, full);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                {
                    continue;
                }
                return game.Length == 0 ? relative : Path.Combine("Games", game, relative);
            }
            return null;
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

            // A level by that name in any folder (a game's own included) is that one; otherwise a new one in the main folder.
            var file = Path.ChangeExtension(safeName, ManaLevel.FileExtension);
            return Folders().Select(folder => Path.Combine(folder.Folder, file)).FirstOrDefault(File.Exists)
                ?? Path.Combine(ResolveFolder(), file);
        }

        public static string DisplayName(string path) => Path.GetFileNameWithoutExtension(path);
    }
}

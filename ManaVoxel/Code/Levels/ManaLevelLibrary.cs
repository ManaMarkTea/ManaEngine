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

        /// <summary>The checkout's third-party benchmark scenes (gitignored, made by Tools/Benchmarks): listed in the editor only.</summary>
        public const string BenchmarksName = "Benchmarks";

        /// <summary>
        /// What the editor offers: <see cref="Folders(string)"/>, then <c>Library/Benchmarks</c> when a benchmark script
        /// has filled it. Kept apart so the content checks, the web selector and the phone never see those scenes.
        /// </summary>
        public static IReadOnlyList<(string Game, string Folder)> EditorFolders(string mainFolder)
        {
            var folders = Folders(mainFolder).ToList();
            if (BenchmarksFolder(mainFolder) is { } benchmarks)
            {
                folders.Add((BenchmarksName, benchmarks));
            }
            return folders;
        }

        // <checkout>/Library/Benchmarks beside <checkout>/Content/Levels, when it exists.
        private static string? BenchmarksFolder(string mainFolder)
        {
            var content = Path.GetDirectoryName(Path.GetFullPath(mainFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (Path.GetDirectoryName(content) is not { } checkout)
            {
                return null;
            }
            var folder = Path.Combine(checkout, "Library", BenchmarksName);
            if (Directory.Exists(folder))
            {
                return folder;
            }
            return null;
        }

        /// <summary>Every level the editor lists, newest first -- the one you were just working on is on top.</summary>
        public static IReadOnlyList<string> List()
        {
            return EditorFolders(ResolveFolder()).SelectMany(folder => ListOne(folder.Folder))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }

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
        /// Where a level sits among the folders, for menus and the scene browser: relative to the main folder,
        /// <c>Games/&lt;Name&gt;/...</c> for a game's, <c>Benchmarks/...</c> for a benchmark scene -- or null for none.
        /// </summary>
        public static string? RelativePath(string path, string mainFolder)
        {
            var full = Path.GetFullPath(path);
            foreach (var (game, folder) in EditorFolders(mainFolder))
            {
                var relative = Path.GetRelativePath(folder, full);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                {
                    continue;
                }
                if (game.Length == 0)
                {
                    return relative;
                }
                if (folder.EndsWith(Path.Combine("Library", BenchmarksName), StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(BenchmarksName, relative);
                }
                return Path.Combine("Games", game, relative);
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

namespace Mana.Code.Content
{
    /// <summary>A folder of content: the project's own Content (no name), or a game's (<c>Games/&lt;Name&gt;/Content</c>).</summary>
    public sealed record ManaContentMount(string Name, string Root)
    {
        public bool IsMain => Name.Length == 0;
    }

    /// <summary>
    /// Content lives in the project's Content folder and in each game's own <c>Games/&lt;Name&gt;/Content</c>, so a game
    /// carries its prefabs and assets with it. A path into a game's folder is written <c>@Name/rest</c>
    /// (<c>@Foundry/Prefabs/Conveyor.manaprefab</c>); any other path is under the main Content folder, as before.
    /// </summary>
    public static class ManaContentMounts
    {
        public const char Prefix = '@';

        /// <summary>The main folder first, then each game's, for the project whose Content folder is <paramref name="contentRoot"/>.</summary>
        public static IReadOnlyList<ManaContentMount> For(string contentRoot)
        {
            var main = Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var mounts = new List<ManaContentMount> { new(string.Empty, main) };
            var games = Path.Combine(Path.GetDirectoryName(main) ?? main, "Games");
            if (Directory.Exists(games))
            {
                foreach (var game in Directory.EnumerateDirectories(games).Order(StringComparer.OrdinalIgnoreCase))
                {
                    var content = Path.Combine(game, "Content");
                    if (Directory.Exists(content))
                    {
                        mounts.Add(new ManaContentMount(Path.GetFileName(game), content));
                    }
                }
            }
            return mounts;
        }

        /// <summary>The file a content-relative path names, or null for a mount that does not exist.</summary>
        public static string? FullPath(string contentRoot, string relative)
        {
            var normalized = relative.Replace('\\', '/');
            if (normalized.Length == 0 || normalized[0] != Prefix)
            {
                return Path.GetFullPath(Path.Combine(contentRoot, normalized));
            }
            var slash = normalized.IndexOf('/');
            var name = slash < 0 ? normalized[1..] : normalized[1..slash];
            var rest = slash < 0 ? string.Empty : normalized[(slash + 1)..];
            var mount = For(contentRoot).FirstOrDefault(m => !m.IsMain && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            return mount == null ? null : Path.GetFullPath(Path.Combine(mount.Root, rest));
        }

        /// <summary>The mount holding a file, or null when it is in none of them.</summary>
        public static ManaContentMount? MountOf(string contentRoot, string file)
        {
            var full = Path.GetFullPath(file);
            return For(contentRoot).FirstOrDefault(m => full.StartsWith(m.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The content-relative path of a file (with OS separators): <c>@Name/...</c> for a game's folder.</summary>
        public static string RelativePath(string contentRoot, string file)
        {
            var mount = MountOf(contentRoot, file);
            if (mount == null || mount.IsMain)
            {
                return Path.GetRelativePath(Path.GetFullPath(contentRoot), Path.GetFullPath(file));
            }
            return Prefix + mount.Name + Path.DirectorySeparatorChar + Path.GetRelativePath(mount.Root, Path.GetFullPath(file));
        }

        public static bool Contains(string contentRoot, string file) => MountOf(contentRoot, file) != null;
    }
}

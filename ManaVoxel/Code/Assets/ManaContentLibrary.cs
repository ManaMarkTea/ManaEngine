using System.Text.Json;
using Mana.Code.Content;

namespace ManaVoxel.Code.Assets
{
    public sealed record ManaContentEntry(string Path, string RelativePath, string Name, string Kind);

    public static class ManaContentLibrary
    {
        private sealed record ProjectRoot(string Path);
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mana.ManaWorld, ProjectRoot> ProjectRoots = new();

        public static string RootFor(Mana.ManaWorld world) => ProjectRoots.TryGetValue(world, out var root) ? root.Path : Root;

        /// <summary>Bind before loading or editing assets; each world keeps its own project location.</summary>
        public static void BindRoot(Mana.ManaWorld world, string root) =>
            ProjectRoots.Add(world, new ProjectRoot(Canonical(root)));

#if MANA_BROWSER
        // The browser bundles Content/ into its file system, including Content/Levels/ (see WebAssets.props).
        public static string Root => Path.GetFullPath("Content");
#else
        public static string Root => ManaProjectFolder.Resolve("Content");
#endif

#if MANA_BROWSER
        // The browser's in-memory file system has no links to resolve.
        private static string Canonical(string path) => Path.GetFullPath(path);
#else
        private static string Canonical(string path) => ManaProjectFolder.CanonicalPath(path);
#endif

        public static IReadOnlyList<ManaContentEntry> List(string? root = null)
        {
            root ??= Root;
            var contentRoot = root;
            // The main Content folder and each game's own (Games/<Name>/Content, listed as @Name/...).
            return ManaContentMounts.For(contentRoot).Where(mount => Directory.Exists(mount.Root))
                .SelectMany(mount => Directory.EnumerateFiles(mount.Root, "*", SearchOption.AllDirectories))
                .Select(p => (Path: p, Kind: ManaContentKinds.ForPath(p)))
                .Where(item => item.Kind != null)
                .Select(item => new ManaContentEntry(item.Path, ManaContentMounts.RelativePath(contentRoot, item.Path), Path.GetFileNameWithoutExtension(item.Path),
                    item.Kind?.Name ?? string.Empty))
                .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static string NewPath(string name, string folder, string extension, string? root = null)
        {
            var safe = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim().Trim('.');
            if (safe.Length == 0)
            {
                throw new InvalidDataException("Enter an asset name.");
            }
            var path = Path.Combine(ManaContentMounts.FullPath(root ?? Root, folder) ?? throw new DirectoryNotFoundException($"No content folder '{folder}'."), safe + extension);
            if (File.Exists(path))
            {
                throw new IOException("An asset with that name already exists. Choose a different name.");
            }
            return path;
        }

        public static string Reference(string path, string? root = null) =>
            ManaContentMounts.RelativePath(Canonical(root ?? Root), Canonical(path)).Replace('\\', '/');

        public static string ResolvePrefab(string id, string relativePath, string? root = null)
        {
            root = Canonical(root ?? Root);
            var mapped = ManaContentMounts.FullPath(root, relativePath);
            var candidate = mapped == null ? null : Canonical(mapped);
            if (candidate != null && !ManaContentMounts.Contains(root, candidate))
            {
                throw new InvalidDataException("Prefab reference must stay inside Content.");
            }
            if (candidate != null && File.Exists(candidate) && ManaPrefab.ReadId(candidate) == id)
            {
                return candidate;
            }
            var matches = new List<string>();
            foreach (var entry in List(root).Where(e => e.Kind == "Prefab"))
            {
                try
                {
                    if (ManaPrefab.ReadId(entry.Path) == id)
                    {
                        matches.Add(entry.Path);
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    // A broken unrelated asset must not prevent resolving a healthy one.
                }
            }
            return matches.Count switch
            {
                1 => matches[0],
                0 => throw new FileNotFoundException($"Missing prefab: {relativePath} ({id})."),
                _ => throw new InvalidDataException($"Several prefabs share asset ID {id}.")
            };
        }

        public static void Write(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}

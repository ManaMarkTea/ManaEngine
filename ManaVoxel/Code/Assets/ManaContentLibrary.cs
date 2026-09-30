using System.Text.Json;

namespace ManaVoxel.Code.Assets
{
    public sealed record ManaContentEntry(string Path, string RelativePath, string Name, string Kind);

    public static class ManaContentLibrary
    {
#if MANA_BROWSER
        // The browser bundles Content/ into its file system, including Content/Levels/ (see WebAssets.props).
        public static string Root => Path.GetFullPath("Content");

        private const string GridMapPresetExtension = ".managridpreset";
#else
        public static string Root => ManaProjectFolder.Resolve("Content");

        private const string GridMapPresetExtension = ManaGridMapPreset.Extension;
#endif

        public static IReadOnlyList<ManaContentEntry> List(string? root = null)
        {
            root ??= Root;
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(p => Path.GetExtension(p) is ManaPrefab.Extension or GridMapPresetExtension)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => new ManaContentEntry(p, Path.GetRelativePath(root, p), Path.GetFileNameWithoutExtension(p),
                    Path.GetExtension(p) == ManaPrefab.Extension ? "Prefab" : "Grid Map Preset")).ToList();
        }

        public static string NewPath(string name, string folder, string extension)
        {
            var safe = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim().Trim('.');
            if (safe.Length == 0)
            {
                throw new InvalidDataException("Enter an asset name.");
            }
            var path = Path.Combine(Root, folder, safe + extension);
            if (File.Exists(path))
            {
                throw new IOException("An asset with that name already exists. Choose a different name.");
            }
            return path;
        }

        public static string Reference(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

        public static string ResolvePrefab(string id, string relativePath, string? root = null)
        {
            root = Path.GetFullPath(root ?? Root);
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Prefab reference must stay inside Content.");
            }
            if (File.Exists(candidate) && ManaPrefab.ReadId(candidate) == id)
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

using System.Text.Json;

namespace ManaVoxel.Code.Imports
{
    /// <summary>
    /// Scene packs as downloaded: zips in the checkout's <c>Downloaded/</c> folder (gitignored, kept as they came), each
    /// converted on first open into <c>Library/Imported/&lt;zip name&gt;/</c> -- the private, regenerable side. An import is
    /// current while the zip is the one it was made from and the importer has not changed.
    /// </summary>
    public static class ManaDownloads
    {
        public const string FolderName = "Downloaded";

        /// <summary>Bump when an import would come out differently, so every pack converts again.</summary>
        public const int ImporterVersion = 3;

        private const string ManifestName = "import.json";

        /// <summary>The checkout holding <paramref name="levelsFolder"/> (its Content/Levels).</summary>
        public static string? Checkout(string levelsFolder)
        {
            var content = Path.GetDirectoryName(Path.GetFullPath(levelsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return Path.GetDirectoryName(content ?? "");
        }

        public static string Folder(string checkout) => Path.Combine(checkout, FolderName);

        public static string LibraryFolder(string checkout) => Path.Combine(checkout, "Library");

        public static string ImportedRoot(string checkout) => Path.Combine(LibraryFolder(checkout), "Imported");

        public static string ImportFolder(string checkout, string zip) => Path.Combine(ImportedRoot(checkout), Path.GetFileNameWithoutExtension(zip));

        public static bool IsPack(string path) => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        /// <summary>Every zip waiting in Downloaded/.</summary>
        public static IEnumerable<string> Packs(string checkout)
        {
            var folder = Folder(checkout);
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }
            return Directory.EnumerateFiles(folder, "*.zip", SearchOption.TopDirectoryOnly).ToList();
        }

        public sealed class Manifest
        {
            public int Version { get; set; }
            public long ZipLength { get; set; }
            public long ZipTicks { get; set; }

            /// <summary>The levels made, as file names in the import folder; the first is the one opening the zip opens.</summary>
            public List<string> Levels { get; set; } = new();
        }

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static Manifest? ReadManifest(string importFolder)
        {
            var path = Path.Combine(importFolder, ManifestName);
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static void WriteManifest(string importFolder, Manifest manifest) =>
            File.WriteAllText(Path.Combine(importFolder, ManifestName), JsonSerializer.Serialize(manifest, Json));

        public static Manifest Stamp(string zip) =>
            new() { Version = ImporterVersion, ZipLength = new FileInfo(zip).Length, ZipTicks = File.GetLastWriteTimeUtc(zip).Ticks };

        /// <summary>The import's levels when it is current for the zip as it is now; null when it must run (again).</summary>
        public static IReadOnlyList<string>? CurrentLevels(string checkout, string zip)
        {
            var folder = ImportFolder(checkout, zip);
            var manifest = ReadManifest(folder);
            var stamp = Stamp(zip);
            if (manifest == null || manifest.Version != stamp.Version || manifest.ZipLength != stamp.ZipLength || manifest.ZipTicks != stamp.ZipTicks)
            {
                return null;
            }
            var levels = manifest.Levels.Select(name => Path.Combine(folder, name)).ToList();
            if (levels.Count == 0 || !levels.All(File.Exists))
            {
                return null;
            }
            return levels;
        }
    }
}

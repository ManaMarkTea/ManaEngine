using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mana.Code.Content
{
    /// <summary>
    /// Finding and reading user-made assets in Content/ -- sprite sheets, 9-slices, and whatever kind comes next.
    /// Every one is a JSON file with a format, a version, an id and a name; a reference holds the id and the path,
    /// and the id wins when the file has moved. Reads are cached until the file changes.
    /// </summary>
    public static class ManaContentAssets
    {
        public static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters = { new JsonStringEnumConverter() }
        };

        private static string? _root;
        private static readonly ConcurrentDictionary<string, (DateTime Written, object Asset)> Loaded = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The Content folder: beside the working directory or the program, or in a folder above them (the program
        /// runs from a build folder several levels down in the repo). Settable, for an editor that knows better.
        /// </summary>
        public static string Root
        {
            get => _root ??= FindRoot();
            set => _root = Path.GetFullPath(value);
        }

        /// <summary>The asset's file, trusting the id over the path -- or null when there is no such asset.</summary>
        public static string? Resolve(ManaAssetRef reference, string extension) => Resolve(reference, extension, Root);

        public static string? Resolve(ManaAssetRef reference, string extension, string root)
        {
            root = Path.GetFullPath(root);
            if (reference.IsEmpty)
            {
                return null;
            }

            // Asked for every frame by every sprite, so a hit is remembered while its file is still there.
            var cacheKey = $"{root}|{reference.AssetId}|{reference.AssetPath}|{extension}";
            if (Found.TryGetValue(cacheKey, out var known) && File.Exists(known))
            {
                return known;
            }
            var found = Search(reference, extension, root);
            if (found != null)
            {
                Found[cacheKey] = found;
            }
            return found;
        }

        private static readonly ConcurrentDictionary<string, string> Found = new(StringComparer.OrdinalIgnoreCase);

        private static string? Search(ManaAssetRef reference, string extension, string root)
        {
            if (reference.AssetPath.Length > 0)
            {
                var candidate = Path.GetFullPath(Path.Combine(root, reference.AssetPath));
                if (candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate)
                    && (reference.AssetId.Length == 0 || ReadId(candidate) == reference.AssetId))
                {
                    return candidate;
                }
            }

            if (reference.AssetId.Length == 0 || !Directory.Exists(root))
            {
                return null;
            }
            return Directory.EnumerateFiles(root, "*" + extension, SearchOption.AllDirectories)
                .FirstOrDefault(file => ReadId(file) == reference.AssetId);
        }

        /// <summary>The asset, read and cached, or null when it is missing or unreadable.</summary>
        public static T? Load<T>(ManaAssetRef reference, string extension) where T : class
        {
            return Resolve(reference, extension) is { } file ? Load<T>(file) : null;
        }

        // The asset types are only ever made by JSON reflection, which a trimmed build (the web one) cannot see; these
        // keep their constructors and properties.
        [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties, typeof(ManaSpriteSheet))]
        [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties, typeof(ManaSpriteAnimation))]
        [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties, typeof(ManaSpriteSlice))]
        [System.Diagnostics.CodeAnalysis.DynamicDependency(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties, typeof(ManaAssetRef))]
        public static T? Load<T>(string file) where T : class
        {
            try
            {
                var written = File.GetLastWriteTimeUtc(file);
                if (Loaded.TryGetValue(file, out var cached) && cached.Written == written && cached.Asset is T known)
                {
                    return known;
                }
                var asset = JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json);
                if (asset != null)
                {
                    Loaded[file] = (written, asset);
                }
                return asset;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Asset '{file}' could not be read: {exception.Message}");
                return null;
            }
        }

        /// <summary>Writes the asset, through a temporary file so a failed save never leaves half an asset.</summary>
        public static void Save<T>(T asset, string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var partial = file + ".part";
            File.WriteAllText(partial, ManaCore.Serialization.ManaJsonFormat.Indent(JsonSerializer.Serialize(asset, Json)));
            File.Move(partial, file, overwrite: true);
            Loaded.TryRemove(file, out _);
        }

        /// <summary>A reference to the asset at <paramref name="file"/>.</summary>
        public static ManaAssetRef Reference(string file)
        {
            return new ManaAssetRef(ReadId(file) ?? string.Empty, Path.GetRelativePath(Root, file).Replace('\\', '/'));
        }

        /// <summary>The asset's id, read without deserialising the rest of it.</summary>
        public static string? ReadId(string file)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                return document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static string NewId() => Guid.NewGuid().ToString("N");

        private static string FindRoot()
        {
            foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
                {
                    var content = Path.Combine(folder.FullName, "Content");
                    if (Directory.Exists(Path.Combine(content, "Levels")) || Directory.Exists(Path.Combine(content, "Prefabs")))
                    {
                        return content;
                    }
                }
            }
            return Path.Combine(AppContext.BaseDirectory, "Content");
        }
    }
}

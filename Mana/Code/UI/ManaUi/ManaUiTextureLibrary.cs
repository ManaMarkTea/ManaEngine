namespace Mana.Code.UI.ManaUi
{
    /// <summary>One image on disk that a material channel could be pointed at.</summary>
    public sealed class ManaUiTextureAsset
    {
        public ManaUiTextureAsset(string path, string relativePath)
        {
            Path = path;
            RelativePath = relativePath;
            Name = System.IO.Path.GetFileName(path);
            Folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path) ?? string.Empty);
        }

        /// <summary>Absolute path, for decoding a thumbnail.</summary>
        public string Path { get; }

        /// <summary>
        /// What gets stored on the material. Relative, because an absolute path baked into a scene would
        /// break the moment the project moved or was opened on another machine.
        /// </summary>
        public string RelativePath { get; }

        public string Name { get; }

        /// <summary>The immediate folder, which is how these are grouped in the picker.</summary>
        public string Folder { get; }
    }

    /// <summary>
    /// The images a material channel can be assigned, found by scanning the project's texture folders.
    /// <para>
    /// Scanned once and remembered. The alternative -- a file dialog -- would work, but a grid of
    /// thumbnails is how anyone actually picks a texture, and it keeps the choice inside the project rather
    /// than letting a material point at something on a desktop somewhere.
    /// </para>
    /// </summary>
    public static class ManaUiTextureLibrary
    {
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };

        private static List<ManaUiTextureAsset>? _assets;

        /// <summary>
        /// Folders searched, relative to each root. Environment maps are deliberately absent:
        /// they are multi-megabyte skies, not surface detail, and offering them in a material picker is an
        /// invitation to a very slow mistake.
        /// </summary>
        public static List<string> SearchFolders { get; } = new()
        {
            System.IO.Path.Combine("Assets", "Textures", "MaterialTextures"),
            System.IO.Path.Combine("Assets", "Textures", "Icons"),
            System.IO.Path.Combine("Assets", "Models")
        };

        public static IReadOnlyList<ManaUiTextureAsset> Assets => _assets ??= Scan();

        /// <summary>Rescans on the next request. For a project whose files changed underneath it.</summary>
        public static void Invalidate()
        {
            _assets = null;
        }

        private static List<ManaUiTextureAsset> Scan()
        {
            var found = new List<ManaUiTextureAsset>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Both roots, because they are not the same thing and either can be the right one: the editor
            // runs with its output folder as the working directory, while a tool or a test run can have
            // the repository root. Searching only one of them finds nothing in the other.
            var roots = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var (folder, baseDirectory) in SearchFolders.SelectMany(f => roots.Select(r => (f, r))))
            {
                var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDirectory, folder));
                if (!Directory.Exists(root))
                {
                    continue;
                }

                IEnumerable<string> files;

                try
                {
                    files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    if (!Extensions.Contains(System.IO.Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!seen.Add(file))
                    {
                        continue;
                    }

                    // Relative to the root it was found under, not to the working directory. Those are the
                    // same for the editor and different for anything launched from elsewhere, and using
                    // the wrong one produces a path full of "..\" that resolves only from where it was
                    // generated.
                    var relative = System.IO.Path.GetRelativePath(baseDirectory, file).Replace('\\', '/');

                    found.Add(new ManaUiTextureAsset(file, relative));
                }
            }

            return found
                .OrderBy(a => a.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}

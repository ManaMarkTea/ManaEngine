namespace ManaVoxel.Code.Assets
{
    /// <summary>Finds catalog, output and checkout assets without changing the launch directory.</summary>
    public static class ManaAssetPaths
    {
        public static string Resolve(string assetReference) => Resolve(assetReference, Environment.CurrentDirectory);

        /// <summary>Resolves assets for a launch directory without changing process-wide state.</summary>
        public static string Resolve(string assetReference, string workingDirectory)
        {
            // The browser has no checkout to search: a catalog asset is wherever the catalog downloaded it.
            if (ManaCore.Assets.ManaAssetCatalog.Current?.LocalFileOf(assetReference) is { } downloaded && File.Exists(downloaded))
            {
                return downloaded;
            }

            if (Path.IsPathRooted(assetReference))
            {
                return Path.GetFullPath(assetReference);
            }

            var normalized = assetReference.Replace('/', Path.DirectorySeparatorChar);
            string[] roots =
            {
                workingDirectory,
                Path.Combine(workingDirectory, "Mana"),
                AppContext.BaseDirectory
            };

            foreach (var root in roots)
            {
                var candidate = Path.GetFullPath(Path.Combine(root, normalized));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // Source assets are not all copied to the output. Locate the checkout without
            // depending on the depth of bin/, Artifacts/Bin/, or runtime-specific outputs.
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (!File.Exists(Path.Combine(directory.FullName, "Mana", "Mana.sln")))
                {
                    continue;
                }

                foreach (var root in new[] { directory.FullName, Path.Combine(directory.FullName, "Mana") })
                {
                    var candidate = Path.GetFullPath(Path.Combine(root, normalized));
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                break;
            }

            return Path.GetFullPath(Path.Combine(workingDirectory, normalized));
        }

    }
}

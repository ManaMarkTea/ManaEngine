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

            var normalized = LegacyFree(assetReference).Replace('/', Path.DirectorySeparatorChar);
            string[] roots =
            {
                workingDirectory,
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

            // An app in another repo (the games) keeps the engine's assets in its checkout.
            if (ManaProjectFolder.RootOverride is { } engine)
            {
                var candidate = Path.GetFullPath(Path.Combine(engine, normalized));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            // Source assets are not all copied to the output. Locate the checkout without
            // depending on the depth of bin/, Artifacts/Bin/, or runtime-specific outputs.
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (!File.Exists(Path.Combine(directory.FullName, "ManaRenderer.sln")))
                {
                    continue;
                }

                var candidate = Path.GetFullPath(Path.Combine(directory.FullName, normalized));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                break;
            }

            return Path.GetFullPath(Path.Combine(workingDirectory, normalized));
        }

        // The engine's assets were once kept in Mana/Assets; levels saved then still name them that way.
        private static string LegacyFree(string reference) =>
            reference.StartsWith("Mana/Assets/", StringComparison.Ordinal) || reference.StartsWith(@"Mana\Assets\", StringComparison.Ordinal)
                ? reference[5..]
                : reference;
    }
}

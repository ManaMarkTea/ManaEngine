using System.IO.Compression;

namespace ManaVoxel.Code.Services
{
    // Reading a glTF's sibling textures back out of the zip it was imported from. Split into its
    // own partial so the browser build can compile just this half: the rest of
    // VoxelFileImportService is editor-facing (VoxelEditorViewModel, ManaVoxel.Code.UI) and has no
    // browser-wasm build, but level loading needs exactly this resolver to bring in zipped meshes.
    // Pure managed -- ZipArchive plus string handling, no native or UI dependencies.
    public partial class VoxelFileImportService
    {
        public static Func<string, byte[]?> CreateZipSidecarResolver(ZipArchive archive, string modelEntryPath)
        {
            var modelDirectory = GetEntryDirectory(modelEntryPath);
            return textureReference => TryReadZipEntryBytes(archive, modelDirectory, textureReference);
        }

        private static byte[]? TryReadZipEntryBytes(ZipArchive archive, string modelDirectory, string textureReference)
        {
            if (string.IsNullOrWhiteSpace(textureReference) ||
                textureReference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var normalized = textureReference
                .Replace('\\', '/')
                .Trim();

            try
            {
                normalized = Uri.UnescapeDataString(normalized);
            }
            catch
            {
                // Keep original if URI decoding fails.
            }

            var candidates = new List<string>();
            AddArchivePathCandidate(candidates, normalized);
            if (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                AddArchivePathCandidate(candidates, normalized[2..]);
            }

            if (!string.IsNullOrWhiteSpace(modelDirectory))
            {
                AddArchivePathCandidate(candidates, $"{modelDirectory}/{normalized}");
                if (normalized.StartsWith("./", StringComparison.Ordinal))
                {
                    AddArchivePathCandidate(candidates, $"{modelDirectory}/{normalized[2..]}");
                }
            }

            foreach (var candidate in candidates)
            {
                var entry = FindArchiveEntryIgnoreCase(archive, candidate);
                if (entry == null)
                {
                    continue;
                }

                using var stream = entry.Open();
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }

            return null;
        }

        private static void AddArchivePathCandidate(List<string> candidates, string value)
        {
            var normalized = value.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (!candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(normalized);
            }
        }

        private static ZipArchiveEntry? FindArchiveEntryIgnoreCase(ZipArchive archive, string fullName)
        {
            var directMatch = archive.GetEntry(fullName);
            if (directMatch != null)
            {
                return directMatch;
            }

            return archive.Entries.FirstOrDefault(entry =>
                string.Equals(entry.FullName, fullName, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetEntryDirectory(string entryPath)
        {
            var normalized = entryPath.Replace('\\', '/');
            var slash = normalized.LastIndexOf('/');
            return slash <= 0 ? string.Empty : normalized[..slash];
        }
    }
}

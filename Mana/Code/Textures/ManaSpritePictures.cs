using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ManaCore.Imaging;

namespace Mana
{
    /// <summary>
    /// Turns what a sprite names into a picture file on disk. A repo path is used as it is; a picture inside an
    /// asset pack ("zip:kenney_new-platformer-pack-1.1.zip/Sprites/Tiles/Default/grass.png", the form models use)
    /// is copied out once into a cache. Several pictures can be joined side by side into one -- a strip of
    /// animation frames, or a platform's end pieces around its repeated middle -- so a sprite still draws from
    /// one texture and picks frames by UV.
    /// </summary>
    public static class ManaSpritePictures
    {
        private const string ZipPrefix = "zip:";
        private static readonly ConcurrentDictionary<string, string> Resolved = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> Zips = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object WriteLock = new();

        public static string CacheFolder { get; set; } = Path.Combine(Path.GetTempPath(), "ManaRenderer", "SpritePictures");

        /// <summary>A file holding the picture, or null when it cannot be found.</summary>
        public static string? Resolve(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                return null;
            }

            if (!reference.StartsWith(ZipPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var path = ManaTexture.ResolveTexturePath(reference);
                return File.Exists(path) ? path : null;
            }

            // Only found pictures are remembered, so a pack dropped in after a miss is picked up next time.
            if (Resolved.TryGetValue(reference, out var known) && File.Exists(known))
            {
                return known;
            }
            var extracted = ExtractFromZip(reference);
            if (extracted != null)
            {
                Resolved[reference] = extracted;
            }
            return extracted;
        }

        /// <summary>The picture's file if finding it costs nothing -- a repo path, or a pack picture already copied out.</summary>
        public static string? TryGetResolved(string reference)
        {
            if (!reference.StartsWith(ZipPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return Resolve(reference);
            }
            return Resolved.TryGetValue(reference, out var known) && File.Exists(known) ? known : null;
        }

        /// <summary>
        /// The pictures joined left to right into one, each in a cell the size of the largest and standing on its
        /// floor, centred across -- so frames of slightly different sizes still keep a character's feet still.
        /// </summary>
        /// <param name="references">The pictures, left to right.</param>
        /// <param name="rows">How many times the joined row repeats downwards.</param>
        public static string? Join(IReadOnlyList<string> references, int rows = 1)
        {
            if (references.Count == 1 && rows <= 1)
            {
                return Resolve(references[0]);
            }
            return JoinGrid(Enumerable.Repeat(references, Math.Max(1, rows)).ToList());
        }

        /// <summary>A grid of pictures, top row first, joined into one: each in a cell the size of the largest, standing on its floor.</summary>
        public static string? JoinGrid(IReadOnlyList<IReadOnlyList<string>> grid)
        {
            if (grid.Count == 0 || grid.Any(row => row.Count == 0))
            {
                return null;
            }

            var distinct = grid.SelectMany(row => row).Distinct(StringComparer.Ordinal).ToList();
            var files = distinct.ToDictionary(reference => reference, Resolve, StringComparer.Ordinal);
            if (files.Values.Any(file => file == null))
            {
                return null;
            }

            var layout = string.Join("/", grid.Select(row => string.Join("|", row.Select(reference => files[reference]))));
            var stamps = string.Join("|", files.Values.Distinct().Select(file => File.GetLastWriteTimeUtc(file!).Ticks));
            var output = Path.Combine(CacheFolder, "joined", Hash(layout + "#" + stamps) + ".png");
            if (File.Exists(output))
            {
                return output;
            }

            try
            {
                var images = files.ToDictionary(pair => pair.Key, pair => ManaImageCodec.Load(pair.Value!), StringComparer.Ordinal);
                var cellWidth = images.Values.Max(image => image.Width);
                var cellHeight = images.Values.Max(image => image.Height);
                var columns = grid.Max(row => row.Count);
                var joined = new ManaImage(cellWidth * columns, cellHeight * grid.Count);
                for (var row = 0; row < grid.Count; row++)
                {
                    for (var column = 0; column < grid[row].Count; column++)
                    {
                        var image = images[grid[row][column]];
                        joined.Draw(image, column * cellWidth + (cellWidth - image.Width) / 2, (row + 1) * cellHeight - image.Height);
                    }
                }
                Save(joined, output);
                return output;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Sprite pictures could not be joined: {exception.Message}");
                return null;
            }
        }

        private static string? ExtractFromZip(string reference)
        {
            var body = reference[ZipPrefix.Length..].Replace('\\', '/');
            var split = body.IndexOf(".zip/", StringComparison.OrdinalIgnoreCase);
            if (split < 0)
            {
                return null;
            }

            var zipName = Path.GetFileName(body[..(split + 4)]);
            var entryPath = body[(split + 5)..];
            if (Path.IsPathRooted(entryPath) || entryPath.Split('/').Any(part => part is ".." or "." || part.Contains(':')))
            {
                return null;
            }
            if (!Zips.TryGetValue(zipName, out var zip) || !File.Exists(zip))
            {
                zip = FindZip(zipName);
                if (zip != null)
                {
                    Zips[zipName] = zip;
                }
            }
            if (zip == null)
            {
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Sprite picture's pack '{zipName}' is not under any Assets folder.");
                return null;
            }

            var output = Path.Combine(CacheFolder, Path.GetFileNameWithoutExtension(zipName) + "-" + Hash(zip + File.GetLastWriteTimeUtc(zip).Ticks)[..8],
                entryPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(output))
            {
                return output;
            }

            try
            {
                using var archive = ZipFile.OpenRead(zip);
                var entry = archive.GetEntry(entryPath)
                    ?? archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, entryPath, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Sprite picture '{entryPath}' is not in {zipName}.");
                    return null;
                }

                lock (WriteLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    // Written beside and moved into place, so a half-written file is never taken for the picture.
                    var partial = output + ".part";
                    entry.ExtractToFile(partial, overwrite: true);
                    File.Move(partial, output, overwrite: true);
                }
                return output;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Sprite picture '{reference}' could not be read: {exception.Message}");
                return null;
            }
        }

        // Under an Assets folder beside the working directory or the program, or any folder above them -- the
        // packs sit in the repo's Assets/, and the program runs from a build folder several levels down.
        private static string? FindZip(string zipName)
        {
            // In the browser the packs are not on disk to be found: the asset catalog downloads them on demand, and a
            // level preloads the ones its sprites use (ManaLevelLoader.EnsureAssetsAsync).
            if (ManaCore.Assets.ManaAssetCatalog.Current?.LocalFileOf($"zip:{zipName}/") is { } catalogFile && File.Exists(catalogFile))
            {
                return catalogFile;
            }
            return PackFiles().FirstOrDefault(zip => string.Equals(Path.GetFileName(zip), zipName, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> PackFiles()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            {
                for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
                {
                    var assets = Path.Combine(folder.FullName, "Assets");
                    if (!seen.Add(assets) || !Directory.Exists(assets))
                    {
                        continue;
                    }
                    string[] zips;
                    try
                    {
                        zips = Directory.GetFiles(assets, "*.zip", SearchOption.AllDirectories);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }
                    foreach (var zip in zips)
                    {
                        yield return zip;
                    }
                }
            }
        }

        private static List<(string Reference, string Name, string Folder)>? _packPictures;

        /// <summary>
        /// Every PNG inside the asset packs, as the "zip:" reference a sprite stores, its file name and its pack's
        /// name -- for a picker. Read from the zips' directories only, once; nothing is extracted until chosen.
        /// </summary>
        public static IReadOnlyList<(string Reference, string Name, string Folder)> PackPictures()
        {
            if (_packPictures != null)
            {
                return _packPictures;
            }

            var found = new List<(string Reference, string Name, string Folder)>();
            var zipsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var zip in PackFiles())
            {
                if (!zipsSeen.Add(Path.GetFileName(zip)))
                {
                    continue;
                }
                try
                {
                    using var archive = ZipFile.OpenRead(zip);
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        {
                            found.Add(($"{ZipPrefix}{Path.GetFileName(zip)}/{entry.FullName}", entry.Name, Path.GetFileNameWithoutExtension(zip)));
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                }
            }
            return _packPictures = found;
        }

        private static void Save(ManaImage image, string output)
        {
            lock (WriteLock)
            {
                var partial = output + ".part";
                ManaImageCodec.SavePng(image, partial);
                File.Move(partial, output, overwrite: true);
            }
        }

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }
}

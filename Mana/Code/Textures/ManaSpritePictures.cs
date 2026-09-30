using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

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

        /// <summary>
        /// The pictures joined left to right into one, each in a cell the size of the largest and standing on its
        /// floor, centred across -- so frames of slightly different sizes still keep a character's feet still.
        /// </summary>
        /// <param name="rows">How many times the joined row repeats downwards -- a block of ground several tiles deep.</param>
        public static string? Join(IReadOnlyList<string> references, int rows = 1)
        {
            rows = Math.Max(1, rows);
            if (references.Count == 0)
            {
                return null;
            }
            if (references.Count == 1 && rows == 1)
            {
                return Resolve(references[0]);
            }

            var files = references.Select(Resolve).ToList();
            if (files.Any(file => file == null))
            {
                return null;
            }

            var key = Hash(string.Join("|", files.Select(file => $"{file}@{File.GetLastWriteTimeUtc(file!).Ticks}")) + $"|rows {rows}");
            var output = Path.Combine(CacheFolder, "joined", key + ".png");
            if (File.Exists(output))
            {
                return output;
            }

            try
            {
                var images = files.Select(file => Image.Load<Rgba32>(file!)).ToList();
                try
                {
                    var cellWidth = images.Max(image => image.Width);
                    var cellHeight = images.Max(image => image.Height);
                    using var strip = new Image<Rgba32>(cellWidth * images.Count, cellHeight * rows);
                    for (var row = 0; row < rows; row++)
                    {
                        for (var i = 0; i < images.Count; i++)
                        {
                            var image = images[i];
                            var at = new Point(i * cellWidth + (cellWidth - image.Width) / 2, (row + 1) * cellHeight - image.Height);
                            strip.Mutate(context => context.DrawImage(image, at, 1.0f));
                        }
                    }
                    Save(strip, output);
                }
                finally
                {
                    images.ForEach(image => image.Dispose());
                }
                return output;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ImageFormatException)
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
                    try
                    {
                        var hit = Directory.EnumerateFiles(assets, zipName, SearchOption.AllDirectories).FirstOrDefault();
                        if (hit != null)
                        {
                            return hit;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
            return null;
        }

        private static void Save(Image<Rgba32> image, string output)
        {
            lock (WriteLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var partial = output + ".part";
                image.SaveAsPng(partial);
                File.Move(partial, output, overwrite: true);
            }
        }

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }
}

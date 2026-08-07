using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ManaGeoTools.Code.GeoTiff.Terrarium
{
    /// <summary>
    /// Fetches Terrarium-encoded elevation tiles from AWS's public "elevation-tiles-prod" dataset
    /// (no auth required), caching each downloaded PNG to a per-user cache folder so repeat runs don't
    /// re-download. This is the first network call anywhere in this codebase -- kept deliberately
    /// simple: one attempt per tile, no retry/backoff, failures are logged and treated as "this tile
    /// just isn't available right now."
    /// </summary>
    public static class TerrariumTileFetcher
    {
        private const string TileUrlTemplate = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{0}/{1}/{2}.png";

        // Terrarium tiles sometimes encode missing coverage as a flat (0,0,0) pixel, which decodes via
        // the formula below to exactly -32768m -- a value that exists nowhere on Earth (lowest point,
        // the Mariana Trench, is about -10935m; highest, Everest, is about 8849m). A generous plausible
        // range with real margin on both ends, used to catch that sentinel (and anything similarly
        // corrupt) without ever rejecting genuine extreme terrain.
        private const float MinPlausibleElevationMeters = -11000f;
        private const float MaxPlausibleElevationMeters = 9000f;

        // A single shared instance -- creating one HttpClient per request exhausts sockets under load,
        // a well-known .NET pitfall this avoids by construction.
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public static string CacheRoot { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ManaRenderer", "TerrainCache", "terrarium");

        public static async Task<TerrariumHeightTile?> FetchTileAsync(int zoom, int tileX, int tileY)
        {
            var cachePath = Path.Combine(CacheRoot, zoom.ToString(), tileX.ToString(), $"{tileY}.png");

            byte[] pngBytes;
            if (File.Exists(cachePath))
            {
                pngBytes = await File.ReadAllBytesAsync(cachePath);
            }
            else
            {
                var url = string.Format(TileUrlTemplate, zoom, tileX, tileY);
                try
                {
                    pngBytes = await Http.GetByteArrayAsync(url);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    Console.WriteLine($"[Terrarium] Failed to fetch tile {zoom}/{tileX}/{tileY}: {ex.Message}");
                    return null;
                }

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    await File.WriteAllBytesAsync(cachePath, pngBytes);
                }
                catch (IOException ex)
                {
                    // Caching is an optimization, not a requirement -- a write failure (e.g. a locked
                    // file from a concurrent fetch of the same tile) shouldn't stop this tile from
                    // being usable this run.
                    Console.WriteLine($"[Terrarium] Failed to cache tile {zoom}/{tileX}/{tileY}: {ex.Message}");
                }
            }

            try
            {
                return DecodeTile(zoom, tileX, tileY, pngBytes);
            }
            catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
            {
                Console.WriteLine($"[Terrarium] Failed to decode tile {zoom}/{tileX}/{tileY}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Total size, in bytes, of every PNG currently cached under CacheRoot. Purely informational (e.g. an Inspector display) -- walks the directory each call, not tracked incrementally.</summary>
        public static long GetCacheSizeBytes()
        {
            if (!Directory.Exists(CacheRoot))
            {
                return 0;
            }

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(CacheRoot, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // A concurrent fetch could be mid-write to this exact file -- skip it, this total
                    // is informational only, not something anything else depends on being exact.
                }
            }

            return total;
        }

        /// <summary>Deletes every cached tile under CacheRoot. Safe to call at any time -- the cache is purely a re-downloadable optimization, nothing depends on it existing.</summary>
        public static void ClearCache()
        {
            if (!Directory.Exists(CacheRoot))
            {
                return;
            }

            try
            {
                Directory.Delete(CacheRoot, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Could hit a file a concurrent fetch is mid-write to -- log and leave whatever
                // couldn't be removed for next time rather than crashing the caller (a UI button click).
                Console.WriteLine($"[Terrarium] Failed to fully clear cache: {ex.Message}");
            }
        }

        private static TerrariumHeightTile DecodeTile(int zoom, int tileX, int tileY, byte[] pngBytes)
        {
            using var image = Image.Load<Rgba32>(pngBytes);
            var heights = new float[TerrariumHeightTile.TileSize * TerrariumHeightTile.TileSize];

            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        var pixel = row[x];
                        var elevation = pixel.R * 256f + pixel.G + pixel.B / 256f - 32768f;
                        // Out-of-range decodes are treated as "no data" (0m), the same convention
                        // GeoTiff.GetHeightAtLatLong already uses for its own no-data case -- rather
                        // than applying the sentinel directly as a genuine, absurdly tall/deep spike.
                        heights[y * TerrariumHeightTile.TileSize + x] =
                            elevation is >= MinPlausibleElevationMeters and <= MaxPlausibleElevationMeters ? elevation : 0f;
                    }
                }
            });

            return new TerrariumHeightTile(zoom, tileX, tileY, heights);
        }
    }
}

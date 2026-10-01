using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mana
{
    /// <summary>
    /// Makes a normal map for a picture that has none, so flat sprite art still catches the light. Height rises
    /// with distance from the picture's transparent edge -- a rounded bevel round every shape -- which suits
    /// outlined, flat-coloured art like Kenney's far better than height from brightness (an outline would read as
    /// a trench). Written once to a cache beside the copied-out pack pictures.
    /// </summary>
    public static class ManaSpriteNormals
    {
        private static readonly ConcurrentDictionary<string, string?> Made = new(StringComparer.Ordinal);
        private static readonly object WriteLock = new();

        /// <summary>The generated map's file for a picture, or null when the picture cannot be read.</summary>
        /// <param name="picture">The picture file.</param>
        /// <param name="bevelPixels">How far in from the edge the bevel rounds over, in the picture's pixels.</param>
        /// <param name="innerEdges">How deep the creases are where colours meet inside the shape, 0 (none) to 1 (as deep as the outline).</param>
        public static string? For(string picture, float bevelPixels, float innerEdges = 0.35f)
        {
            if (!File.Exists(picture))
            {
                return null;
            }
            var bevel = Math.Clamp(bevelPixels, 1.0f, 256.0f);
            var inner = Math.Clamp(innerEdges, 0.0f, 1.0f);
            var key = $"{picture}|{File.GetLastWriteTimeUtc(picture).Ticks}|{bevel:F2}|{inner:F2}";
            return Made.GetOrAdd(key, _ => Make(picture, bevel, inner, key));
        }

        private static string? Make(string picture, float bevel, float inner, string key)
        {
            var output = Path.Combine(ManaSpritePictures.CacheFolder, "normals", Hash(key) + ".png");
            if (File.Exists(output))
            {
                return output;
            }
            try
            {
                using var image = Image.Load<Rgba32>(picture);
                using var normals = Build(image, bevel, inner);
                lock (WriteLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    var partial = output + ".part";
                    normals.SaveAsPng(partial);
                    File.Move(partial, output, overwrite: true);
                }
                return output;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ImageFormatException)
            {
                ManaCore.Diagnostics.ManaLog.WriteDiagnostic($"Sprite normal map for '{picture}' could not be made: {exception.Message}");
                return null;
            }
        }

        /// <summary>The normal map itself: tangent space, green up the picture, flat (0.5, 0.5, 1) where it is transparent.</summary>
        public static Image<Rgba32> Build(Image<Rgba32> picture, float bevelPixels, float innerEdges = 0.35f)
        {
            var width = picture.Width;
            var height = picture.Height;
            var inside = new bool[width * height];
            var colours = new Rgba32[width * height];
            picture.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < width; x++)
                    {
                        inside[y * width + x] = row[x].A >= 128;
                        colours[y * width + x] = row[x];
                    }
                }
            });

            // Inner edges: where the colour jumps between neighbours -- an outline drawn round an arm, an eye, a
            // helmet's glass. Soft shading changes a little a pixel and never counts.
            var plain = new bool[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    plain[i] = inside[i]
                        && !Jumps(i, x + 1 < width ? i + 1 : -1) && !Jumps(i, x > 0 ? i - 1 : -1)
                        && !Jumps(i, y + 1 < height ? i + width : -1) && !Jumps(i, y > 0 ? i - width : -1);
                }
            }
            bool Jumps(int a, int b)
            {
                if (b < 0 || !inside[b])
                {
                    return false;
                }
                var p = colours[a];
                var q = colours[b];
                return Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B) > ColourJump;
            }

            // Only transparency makes an outer edge, not the picture's border: a ground tile is solid to its sides
            // and has to meet the next one flat, or a run of them would show a groove at every join.
            var outer = DistanceToOutside(inside, width, height);
            var parts = innerEdges > 0.0f ? DistanceToOutside(plain, width, height) : outer;
            var bump = new float[width * height];
            for (var i = 0; i < bump.Length; i++)
            {
                // Each part rounds over inside the silhouette's own shoulder, down to a crease where parts meet.
                // Narrower than the outline's, so a part reads as a crease rather than a second pillow.
                var crease = 1.0f - innerEdges * (1.0f - Shoulder(parts[i] / (bevelPixels * 0.5f)));
                bump[i] = Shoulder(outer[i] / bevelPixels) * crease * bevelPixels;
            }

            var result = new Image<Rgba32>(width, height);
            result.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < width; x++)
                    {
                        if (!inside[y * width + x])
                        {
                            row[x] = new Rgba32(128, 128, 255, 255);
                            continue;
                        }
                        // Sobel slopes; rows run down the picture and green points up it, hence dy's sign.
                        float At(int dx, int dy) => bump[Math.Clamp(y + dy, 0, height - 1) * width + Math.Clamp(x + dx, 0, width - 1)];
                        var gx = (At(1, -1) + 2 * At(1, 0) + At(1, 1) - At(-1, -1) - 2 * At(-1, 0) - At(-1, 1)) / 8.0f;
                        var gy = (At(-1, -1) + 2 * At(0, -1) + At(1, -1) - At(-1, 1) - 2 * At(0, 1) - At(1, 1)) / 8.0f;
                        var normal = Vector3.Normalize(new Vector3(-gx, -gy, 1.0f));
                        row[x] = new Rgba32(Encode(normal.X), Encode(normal.Y), Encode(normal.Z), 255);
                    }
                }
            });
            return result;
        }

        // Summed over red, green and blue: an anti-aliased outline steps far more than this a pixel, a soft highlight far less.
        private const int ColourJump = 110;

        // A quarter-circle-like shoulder: steep at the edge, level by the bevel's width.
        private static float Shoulder(float t)
        {
            var u = 1.0f - MathF.Min(t, 1.0f);
            return 1.0f - u * u;
        }

        private static byte Encode(float component) => (byte)Math.Clamp(MathF.Round((component * 0.5f + 0.5f) * 255.0f), 0, 255);

        /// <summary>Each pixel's straight-line distance to the nearest transparent one (Felzenszwalb and Huttenlocher's exact transform).</summary>
        private static float[] DistanceToOutside(bool[] inside, int width, int height)
        {
            // Further than any pixel can be, yet small enough that float keeps the envelope maths exact.
            var far = (float)(width + height) * (width + height);
            var squared = new float[width * height];
            for (var i = 0; i < squared.Length; i++)
            {
                squared[i] = inside[i] ? far : 0.0f;
            }

            var line = new float[Math.Max(width, height)];
            var result = new float[Math.Max(width, height)];
            var hull = new int[Math.Max(width, height)];
            var bounds = new float[Math.Max(width, height) + 1];

            for (var x = 0; x < width; x++)
            {
                for (var y = 0; y < height; y++)
                {
                    line[y] = squared[y * width + x];
                }
                Transform1D(line, height, result, hull, bounds);
                for (var y = 0; y < height; y++)
                {
                    squared[y * width + x] = result[y];
                }
            }
            for (var y = 0; y < height; y++)
            {
                Array.Copy(squared, y * width, line, 0, width);
                Transform1D(line, width, result, hull, bounds);
                Array.Copy(result, 0, squared, y * width, width);
            }

            var distance = new float[squared.Length];
            for (var i = 0; i < squared.Length; i++)
            {
                distance[i] = MathF.Sqrt(squared[i]);
            }
            return distance;
        }

        // The lower envelope of parabolas rooted at each sample: the squared distance along one line.
        private static void Transform1D(float[] f, int n, float[] d, int[] v, float[] z)
        {
            var k = 0;
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            for (var q = 1; q < n; q++)
            {
                var s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2.0f * (q - v[k]));
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2.0f * (q - v[k]));
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (var q = 0; q < n; q++)
            {
                while (z[k + 1] < q)
                {
                    k++;
                }
                d[q] = (q - v[k]) * (q - v[k]) + f[v[k]];
            }
        }

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
    }
}

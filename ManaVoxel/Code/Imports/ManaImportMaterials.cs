using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ManaCore.Imaging;

namespace ManaVoxel.Code.Imports
{
    /// <summary>
    /// Rebuilds a converted scene's materials from its pack's texture naming -- what FBX carries none of. Each material
    /// looks for <c>&lt;name&gt;_&lt;Kind&gt;</c> pictures anywhere in the pack: BaseColor (alpha is opacity), Specular or ORM
    /// (red occlusion, green roughness, blue metal: glTF's own packing), Normal, Emissive. DDS files are referenced
    /// where they lie; only DirectX normal maps are rewritten, flipped for glTF.
    /// </summary>
    public static class ManaImportMaterials
    {
        private enum Kind { BaseColor, Orm, Normal, Emissive }

        private static readonly (Kind Kind, string[] Suffixes)[] Kinds =
        [
            (Kind.BaseColor, ["BaseColor", "Albedo", "Diffuse", "Color"]),
            (Kind.Orm, ["Specular", "ORM", "OcclusionRoughnessMetallic", "MetallicRoughness"]),
            (Kind.Normal, ["Normal", "Normals"]),
            (Kind.Emissive, ["Emissive", "Emission"])
        ];

        // Formats tried in order: the compressed original first.
        private static readonly string[] Extensions = [".dds", ".png", ".jpg", ".jpeg", ".tga"];

        private sealed record Texture(string Path, bool HasData, Vector3 Colour);

        /// <summary>Every picture under <paramref name="root"/> by file name, case aside.</summary>
        public static Dictionary<string, string> IndexPictures(string root)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (Extensions.Contains(System.IO.Path.GetExtension(file).ToLowerInvariant()))
                {
                    index.TryAdd(System.IO.Path.GetFileName(file), file);
                }
            }
            return index;
        }

        /// <summary>
        /// Rewrites the document's materials, images and textures. Returns the emissive materials' glow colours (each
        /// channel at most 1), by material index -- for lights where they glow.
        /// </summary>
        /// <param name="savedFolder">Where the document will be saved: texture paths are written relative to it.</param>
        public static Dictionary<int, Vector3> Rebuild(ManaGltfDocument gltf, Dictionary<string, string> pictures,
            string flippedFolder, bool directXNormals, string savedFolder, IProgress<string>? progress)
        {
            var oldImages = gltf.Array("images").Select(image => image?["uri"]?.GetValue<string>() ?? "").ToList();
            var oldTextures = gltf.Array("textures");
            string? Stem(JsonNode? textureInfo)
            {
                if (textureInfo?["index"] is not { } index || oldTextures[index.GetValue<int>()]?["source"] is not { } source)
                {
                    return null;
                }
                var name = System.IO.Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(oldImages[source.GetValue<int>()]));
                var cut = name.LastIndexOf('_');
                if (cut > 0)
                {
                    return name[..cut];
                }
                return name;
            }

            var plans = new List<(JsonObject Material, string Name, bool DoubleSided, Dictionary<Kind, string> Found)>();
            foreach (var node in gltf.Materials)
            {
                var material = node!.AsObject();
                var name = material["name"]?.GetValue<string>() ?? "";
                var doubleSided = name.EndsWith(".DoubleSided", StringComparison.Ordinal);
                var baseName = name;
                if (doubleSided)
                {
                    baseName = name[..^".DoubleSided".Length];
                }
                // The file names the FBX pointed at carry the truest name for the texture set; the material's own after.
                var prefixes = new List<string>();
                if (Stem(material["pbrMetallicRoughness"]?["baseColorTexture"]) is { } fromBase)
                {
                    prefixes.Add(fromBase);
                }
                if (Stem(material["normalTexture"]) is { } fromNormal)
                {
                    prefixes.Add(fromNormal);
                }
                prefixes.Add(baseName);
                var found = new Dictionary<Kind, string>();
                foreach (var (kind, suffixes) in Kinds)
                {
                    var match = prefixes.SelectMany(prefix => suffixes.SelectMany(suffix => Extensions.Select(extension => $"{prefix}_{suffix}{extension}")))
                        .Select(file => pictures.GetValueOrDefault(file)).FirstOrDefault(path => path != null);
                    if (match != null)
                    {
                        found[kind] = match;
                    }
                }
                plans.Add((material, baseName, doubleSided, found));
            }

            var jobs = plans.SelectMany(plan => plan.Found.Select(pair => (pair.Key, pair.Value))).Distinct().ToList();
            progress?.Report($"Reading {jobs.Count} textures");
            Directory.CreateDirectory(flippedFolder);
            var textures = new ConcurrentDictionary<(Kind, string), Texture>();
            Parallel.ForEach(jobs, job => textures[job] = Prepare(job.Key, job.Value, flippedFolder, directXNormals));

            var images = new JsonArray();
            var textureArray = new JsonArray();
            var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            JsonObject Reference(string path)
            {
                if (!lookup.TryGetValue(path, out var index))
                {
                    var relative = System.IO.Path.GetRelativePath(savedFolder, path).Replace('\\', '/');
                    images.Add(new JsonObject { ["uri"] = relative });
                    textureArray.Add(new JsonObject { ["sampler"] = 0, ["source"] = images.Count - 1 });
                    index = textureArray.Count - 1;
                    lookup[path] = index;
                }
                return new JsonObject { ["index"] = index };
            }

            var glows = new Dictionary<int, Vector3>();
            for (var i = 0; i < plans.Count; i++)
            {
                var (material, name, doubleSided, found) = plans[i];
                var rebuilt = new JsonObject { ["name"] = name, ["doubleSided"] = doubleSided };
                var pbr = new JsonObject { ["baseColorFactor"] = new JsonArray(1, 1, 1, 1), ["metallicFactor"] = 0.0, ["roughnessFactor"] = 0.6 };
                if (found.TryGetValue(Kind.BaseColor, out var baseColour))
                {
                    var texture = textures[(Kind.BaseColor, baseColour)];
                    pbr["baseColorTexture"] = Reference(texture.Path);
                    if (texture.HasData)
                    {
                        // Glass reads through; anything else with holes (leaves, railings) is cut out.
                        var lower = name.ToLowerInvariant();
                        if (lower.Contains("glass") || lower.Contains("window"))
                        {
                            rebuilt["alphaMode"] = "BLEND";
                        }
                        else
                        {
                            rebuilt["alphaMode"] = "MASK";
                            rebuilt["alphaCutoff"] = 0.5;
                        }
                    }
                }
                else if (material["pbrMetallicRoughness"]?["baseColorFactor"] is JsonArray factor)
                {
                    pbr["baseColorFactor"] = factor.DeepClone();
                }
                if (found.TryGetValue(Kind.Orm, out var orm))
                {
                    var texture = textures[(Kind.Orm, orm)];
                    pbr["metallicRoughnessTexture"] = Reference(texture.Path);
                    pbr["metallicFactor"] = 1.0;
                    pbr["roughnessFactor"] = 1.0;
                    // Packs that call red occlusion and leave it black would put out all sky light: only a filled one.
                    if (texture.HasData)
                    {
                        rebuilt["occlusionTexture"] = Reference(texture.Path);
                    }
                }
                if (found.TryGetValue(Kind.Normal, out var normal))
                {
                    rebuilt["normalTexture"] = Reference(textures[(Kind.Normal, normal)].Path);
                }
                if (found.TryGetValue(Kind.Emissive, out var emissive))
                {
                    var texture = textures[(Kind.Emissive, emissive)];
                    rebuilt["emissiveTexture"] = Reference(texture.Path);
                    rebuilt["emissiveFactor"] = new JsonArray(1, 1, 1);
                    if (texture.HasData)
                    {
                        glows[i] = texture.Colour;
                    }
                }
                rebuilt["pbrMetallicRoughness"] = pbr;
                material.Clear();
                foreach (var (key, value) in rebuilt.ToList())
                {
                    rebuilt.Remove(key);
                    material[key] = value;
                }
            }

            gltf.Root["images"] = images;
            gltf.Root["textures"] = textureArray;
            gltf.Root["samplers"] = new JsonArray(new JsonObject { ["magFilter"] = 9729, ["minFilter"] = 9987, ["wrapS"] = 10497, ["wrapT"] = 10497 });
            return glows;
        }

        // What a texture says about its material, and (for a DirectX normal map) the flipped copy to use instead.
        private static Texture Prepare(Kind kind, string path, string flippedFolder, bool directXNormals)
        {
            try
            {
                switch (kind)
                {
                    case Kind.BaseColor:
                        return new Texture(path, Small(path).Pixels.Where((_, i) => i % 4 == 3).Any(alpha => alpha < 250), Vector3.One);
                    case Kind.Orm:
                        return new Texture(path, Small(path).Pixels.Where((_, i) => i % 4 == 0).Any(red => red > 12), Vector3.One);
                    case Kind.Emissive:
                        var colour = GlowColour(Small(path));
                        return new Texture(path, colour != null, colour ?? Vector3.Zero);
                    case Kind.Normal when directXNormals:
                        return new Texture(FlipGreen(path, flippedFolder), true, Vector3.One);
                    default:
                        return new Texture(path, true, Vector3.One);
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or NotSupportedException)
            {
                ManaConsole.WriteDiagnostic($"Import: {System.IO.Path.GetFileName(path)} unreadable ({exception.Message}); used as it is.");
                return new Texture(path, false, Vector3.One);
            }
        }

        // The mean of the bright texels, scaled so its brightest channel is 1; null for a dark picture.
        private static Vector3? GlowColour(ManaImage image)
        {
            var sum = Vector3.Zero;
            var count = 0;
            var p = image.Pixels;
            for (var i = 0; i < p.Length; i += 4)
            {
                var texel = new Vector3(p[i], p[i + 1], p[i + 2]) / 255.0f;
                if (MathF.Max(texel.X, MathF.Max(texel.Y, texel.Z)) > 0.2f)
                {
                    sum += texel;
                    count++;
                }
            }
            if (count == 0)
            {
                return null;
            }
            var mean = sum / count;
            return mean / MathF.Max(mean.X, MathF.Max(mean.Y, mean.Z));
        }

        // A small copy to judge a picture by: a DDS's own mip near 128 texels, anything else decoded whole.
        private static ManaImage Small(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (ManaDds.IsDds(bytes))
            {
                var dds = ManaDds.Read(bytes);
                var level = 0;
                while (level + 1 < dds.Levels.Length && ManaDds.LevelWidth(dds.Width, level) > 128)
                {
                    level++;
                }
                return dds.Decode(level);
            }
            return ManaImageCodec.Load(bytes);
        }

        /// <summary>A DirectX normal map as glTF wants it (green = 1 - green): BC5 flipped inside its blocks, else a PNG.</summary>
        public static string FlipGreen(string path, string folder)
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(path);
            var bytes = File.ReadAllBytes(path);
            if (ManaDds.IsDds(bytes) && ManaDds.Read(bytes).Format == ManaDdsFormat.Bc5)
            {
                var target = System.IO.Path.Combine(folder, stem + ".dds");
                if (!File.Exists(target))
                {
                    var dataStart = 128;
                    if (System.Text.Encoding.ASCII.GetString(bytes, 84, 4) == "DX10")
                    {
                        dataStart = 148;
                    }
                    FlipBc5Green(bytes.AsSpan(dataStart));
                    WriteAtomically(target, bytes);
                }
                return target;
            }

            var png = System.IO.Path.Combine(folder, stem + ".png");
            if (!File.Exists(png))
            {
                var image = ManaImageCodec.Load(bytes);
                for (var i = 1; i < image.Pixels.Length; i += 4)
                {
                    image.Pixels[i] = (byte)(255 - image.Pixels[i]);
                }
                ManaImageCodec.SavePng(image, png + ".part");
                File.Move(png + ".part", png, overwrite: true);
            }
            return png;
        }

        /// <summary>
        /// G becomes 255 - G in every BC5 block without re-encoding: each green (BC4) half gets its endpoints swapped and
        /// inverted, and its 3-bit indices remapped to the mirrored palette entries.
        /// </summary>
        public static void FlipBc5Green(Span<byte> blocks)
        {
            ReadOnlySpan<byte> eight = [1, 0, 7, 6, 5, 4, 3, 2];
            ReadOnlySpan<byte> six = [1, 0, 5, 4, 3, 2, 7, 6];
            for (var block = 0; block + 16 <= blocks.Length; block += 16)
            {
                var green = blocks.Slice(block + 8, 8);
                var e0 = green[0];
                var e1 = green[1];
                ulong bits = 0;
                for (var i = 0; i < 6; i++)
                {
                    bits |= (ulong)green[2 + i] << (8 * i);
                }
                ulong flipped = 0;
                var map = six;
                if (e0 > e1)
                {
                    map = eight;
                }
                for (var i = 0; i < 16; i++)
                {
                    flipped |= (ulong)map[(int)((bits >> (3 * i)) & 7)] << (3 * i);
                }
                green[0] = (byte)(255 - e1);
                green[1] = (byte)(255 - e0);
                for (var i = 0; i < 6; i++)
                {
                    green[2 + i] = (byte)(flipped >> (8 * i));
                }
            }
        }

        private static void WriteAtomically(string path, byte[] bytes)
        {
            File.WriteAllBytes(path + ".part", bytes);
            File.Move(path + ".part", path, overwrite: true);
        }
    }
}

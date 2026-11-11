#if !MANA_BROWSER
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ManaCore.Imaging;
using Mana.Code.Levels;
using ManaEditor.Code.Levels;

namespace ManaEditor.Code.Imports
{
    /// <summary>
    /// Turns a downloaded scene pack (a zip in Downloaded/) into levels the editor opens, all in Library/Imported/:
    /// unzip; convert each FBX in process (ufbx, ManaFbxConverter); rebuild its materials from the pack's texture naming; put every part's
    /// pivot at its geometry; then write a level that places the scene, lights it as the pack's own scene file says
    /// (its sun, its HDR sky), bakes probes over it and batches it, plus a night version lit by what glows. A glTF or
    /// GLB in the pack is placed as it is. Runs off the main thread; progress comes as one line at a time.
    /// </summary>
    public static partial class ManaSceneImporter
    {
        // Where an imported scene stands: clear of the editor room's zero-axis walls, and above its floor.
        private const float PlaceX = 100.0f;
        private const float PlaceZ = 100.0f;
        private const float LowestPlaceY = 8.0f;

        /// <summary>The engine sky's own sun strength (ManaSky.SunIntensity): an imported sun is set as bright.</summary>
        private const float SunStrength = 1.6f;

        /// <summary>Imports <paramref name="zip"/> unless its import is current. Returns its levels, the one to open first.</summary>
        public static async Task<IReadOnlyList<string>> EnsureAsync(string checkout, string zip, IProgress<string>? progress, CancellationToken token = default)
        {
            if (ManaDownloads.CurrentLevels(checkout, zip) is { } current)
            {
                return current;
            }
            return await Task.Run(() => ImportAsync(checkout, zip, progress, token), token).ConfigureAwait(false);
        }

        private static async Task<IReadOnlyList<string>> ImportAsync(string checkout, string zip, IProgress<string>? progress, CancellationToken token)
        {
            var started = System.Diagnostics.Stopwatch.StartNew();
            var pack = Path.GetFileNameWithoutExtension(zip);
            var folder = ManaDownloads.ImportFolder(checkout, zip);
            var source = Path.Combine(folder, "source");
            var stamp = ManaDownloads.Stamp(zip);
            Directory.CreateDirectory(folder);

            var extractedStamp = Path.Combine(source, ".zip-stamp");
            var stampText = $"{stamp.ZipLength}:{stamp.ZipTicks}";
            if (!File.Exists(extractedStamp) || File.ReadAllText(extractedStamp) != stampText)
            {
                progress?.Report($"Unpacking {Path.GetFileName(zip)}");
                if (Directory.Exists(source))
                {
                    Directory.Delete(source, recursive: true);
                }
                // ExtractToDirectory refuses entries that would land outside the folder.
                ZipFile.ExtractToDirectory(zip, source);
                File.WriteAllText(extractedStamp, stampText);
            }
            token.ThrowIfCancellationRequested();

            var scenes = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetExtension(path).ToLowerInvariant() is ".fbx" or ".gltf" or ".glb")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (scenes.Count == 0)
            {
                throw new InvalidDataException($"{Path.GetFileName(zip)} holds no FBX, glTF or GLB scene.");
            }

            var notes = ReadMe(source);
            // Packs that say their normal maps are DirectX's (ORCA's do) get them flipped; glTF's are OpenGL's.
            var directXNormals = notes.Contains("directx", StringComparison.OrdinalIgnoreCase);
            var pictures = ManaImportMaterials.IndexPictures(source);
            var levels = new List<string>();
            foreach (var scene in scenes)
            {
                token.ThrowIfCancellationRequested();
                var stem = Path.GetFileNameWithoutExtension(scene);
                var name = Words(stem);
                string asset;
                (Vector3 Colour, Vector3 Direction)? sun = null;
                (Vector3 Eye, Vector3 Look)? camera = null;
                (Vector3 Min, Vector3 Max) bounds;
                List<ManaImportGlow> glows = new();
                if (Path.GetExtension(scene).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    var sceneFolder = Path.Combine(folder, stem);
                    progress?.Report($"Converting {Path.GetFileName(scene)}");
                    var raw = ManaFbxConverter.Convert(scene, sceneFolder);

                    progress?.Report($"{name}: materials");
                    var gltf = ManaGltfDocument.Load(raw);
                    sun = ManaImportScene.Sun(gltf);
                    camera = ManaImportScene.Camera(gltf);
                    var glowColours = ManaImportMaterials.Rebuild(gltf, pictures, Path.Combine(folder, "Textures"), directXNormals, sceneFolder, progress);
                    var pyscene = Path.ChangeExtension(scene, ".pyscene");
                    if (File.Exists(pyscene))
                    {
                        var adjusted = ManaPySceneMaterials.Apply(gltf, ManaPySceneMaterials.Parse(File.ReadAllText(pyscene)));
                        ManaConsole.WriteDiagnostic($"Import {pack}/{stem}: {adjusted} materials set as its scene file says.");
                    }
                    foreach (var key in new[] { "animations", "cameras" })
                    {
                        gltf.Root.Remove(key);
                    }
                    foreach (var node in gltf.Nodes)
                    {
                        node?.AsObject().Remove("camera");
                    }

                    progress?.Report($"{name}: pivots");
                    var moved = ManaImportScene.RecentrePivots(gltf);
                    bounds = ManaImportScene.Bounds(gltf);
                    glows = ManaImportScene.Glows(gltf, glowColours);
                    var target = Path.Combine(sceneFolder, stem + ".gltf");
                    gltf.Save(target, stem + ".bin");
                    asset = target;
                    ManaConsole.WriteDiagnostic($"Import {pack}/{stem}: {gltf.Materials.Count} materials, {moved} meshes re-pivoted, {glows.Count} glows.");
                }
                else
                {
                    asset = scene;
                    bounds = (new Vector3(-5.0f, 0.0f, -5.0f), new Vector3(5.0f, 5.0f, 5.0f));
                }

                var sky = SkyPhoto(scene, folder, sun, progress);
                var day = Level(checkout, pack, stem, name, asset, bounds, sun, camera, sky);
                if (sun == null && glows.Count > 0)
                {
                    // No sun: an interior lit by its own fittings, which glow.
                    day.Nodes.Add(GlowLights(pack, stem, glows, Offset(bounds), indoors: true));
                }
                levels.Add(Save(day, folder, name));
                if (sun != null && glows.Count > 0)
                {
                    levels.Add(Save(Night(day, pack, stem, glows, Offset(bounds)), folder, name + " Night"));
                }
            }

            var manifest = ManaDownloads.Stamp(zip);
            manifest.Levels = levels.Select(Path.GetFileName).OfType<string>().ToList();
            ManaDownloads.WriteManifest(folder, manifest);
            progress?.Report($"Imported {pack}: {levels.Count} level(s) in {started.Elapsed.TotalMinutes:0.0} min");
            return levels;
        }

        // "BistroExterior" reads "Bistro Exterior"; "Bistro_Interior-Wine" reads "Bistro Interior Wine".
        public static string Words(string stem)
        {
            var spaced = CamelCase().Replace(stem.Replace('_', ' ').Replace('-', ' '), "$1 $2");
            return string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        [GeneratedRegex("([a-z0-9])([A-Z])")]
        private static partial Regex CamelCase();

        private static string ReadMe(string source)
        {
            var text = new StringBuilder();
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".txt" or ".md"))
            {
                text.AppendLine(File.ReadAllText(file));
            }
            return text.ToString();
        }

        // Up from the floor by however far the scene reaches below its own origin.
        private static Vector3 Offset((Vector3 Min, Vector3 Max) bounds) =>
            new(PlaceX, MathF.Max(LowestPlaceY, MathF.Ceiling(0.25f - bounds.Min.Y)), PlaceZ);

        /// <summary>
        /// The sky the pack's scene file names (Falcor's <c>.pyscene</c>: <c>EnvMap("x.hdr")</c>), as the 8-bit picture the
        /// Photo sky reads: turned so its sun stands where the scene's sun light comes from, exposed like the engine's
        /// own sky photo. Null when there is none.
        /// </summary>
        private static string? SkyPhoto(string scene, string folder, (Vector3 Colour, Vector3 Direction)? sun, IProgress<string>? progress)
        {
            var pyscene = Path.ChangeExtension(scene, ".pyscene");
            if (!File.Exists(pyscene) || EnvMap().Match(File.ReadAllText(pyscene)) is not { Success: true } match)
            {
                return null;
            }
            var hdrPath = Path.Combine(Path.GetDirectoryName(scene) ?? "", match.Groups[1].Value);
            if (!File.Exists(hdrPath))
            {
                return null;
            }
            var target = Path.Combine(folder, "Sky", Path.GetFileNameWithoutExtension(hdrPath) + ".png");
            if (File.Exists(target))
            {
                return target;
            }

            progress?.Report($"Sky {Path.GetFileName(hdrPath)}");
            var hdr = ManaHdr.Read(File.ReadAllBytes(hdrPath));
            int width = hdr.Width, height = hdr.Height;
            var luminance = new float[width * height];
            var brightest = 0;
            for (var i = 0; i < luminance.Length; i++)
            {
                luminance[i] = 0.2126f * hdr.Rgb[i * 3] + 0.7152f * hdr.Rgb[i * 3 + 1] + 0.0722f * hdr.Rgb[i * 3 + 2];
                if (luminance[i] > luminance[brightest])
                {
                    brightest = i;
                }
            }
            // Mana reads a panorama at u = atan2(z, x) / 2pi + 0.5 (EquirectImage): put the HDR's sun where the light comes from.
            var shift = 0;
            if (sun is { } light)
            {
                var toward = -light.Direction;
                var wanted = MathF.Atan2(toward.Z, toward.X) / (2.0f * MathF.PI) + 0.5f;
                shift = (int)MathF.Round(wanted * width) - brightest % width;
            }
            // As bright as the engine's own sky photo (upper-half median 0.35 linear), which its lighting is tuned for.
            var upper = luminance.AsSpan(0, width * (height / 2)).ToArray();
            Array.Sort(upper);
            var exposure = 0.35f / MathF.Max(upper[upper.Length / 2], 1e-6f);

            var image = new ManaImage(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var from = (y * width + ((x - shift) % width + width) % width) * 3;
                    var to = (y * width + x) * 4;
                    for (var c = 0; c < 3; c++)
                    {
                        image.Pixels[to + c] = Srgb(MathF.Min(hdr.Rgb[from + c] * exposure, 1.0f));
                    }
                    image.Pixels[to + 3] = 255;
                }
            }
            ManaImageCodec.SavePng(image, target);
            return target;
        }

        [GeneratedRegex("EnvMap\\(\\s*\"([^\"]+)\"")]
        private static partial Regex EnvMap();

        private static byte Srgb(float linear)
        {
            var encoded = linear * 12.92f;
            if (linear > 0.0031308f)
            {
                encoded = 1.055f * MathF.Pow(linear, 1.0f / 2.4f) - 0.055f;
            }
            return (byte)Math.Clamp((int)MathF.Round(encoded * 255.0f), 0, 255);
        }

        private static ManaLevel Level(string checkout, string pack, string stem, string name, string asset, (Vector3 Min, Vector3 Max) bounds,
            (Vector3 Colour, Vector3 Direction)? sun, (Vector3 Eye, Vector3 Look)? camera, string? skyPhoto)
        {
            var offset = Offset(bounds);
            var level = new ManaLevel { Name = name };

            var (eye, look) = camera ?? (bounds.Max + (bounds.Max - bounds.Min) * 0.15f, (bounds.Min + bounds.Max) * 0.5f);
            var direction = look - eye;
            level.Camera = new ManaLevelCamera
            {
                Position = Floats(eye + offset),
                Rotation = [MathF.Atan2(direction.Y, MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z)), MathF.Atan2(-direction.X, -direction.Z), 0.0f]
            };

            level.Nodes.Add(new ManaLevelNode
            {
                Id = Id(pack, stem, "scene"),
                Name = name,
                Kind = ManaLevelSourceKind.Mesh,
                Position = Floats(offset),
                Asset = Path.GetRelativePath(checkout, asset).Replace('\\', '/'),
                // Thousands of parts drawn one by one cost far more than their triangles: the renderer batches them.
                MergeParts = true
            });

            // Baked light over the whole scene, a metre past its edges: what sky and sun really reach, bounced.
            var size = bounds.Max - bounds.Min + new Vector3(2.0f);
            level.Nodes.Add(Group(pack, stem, "probes", "Light Probes", (bounds.Min + bounds.Max) * 0.5f + offset, Script(pack, stem, "probes", "ProbeVolume", new()
            {
                ["size"] = Floats(size), ["probeSpacing"] = 2.0f, ["voxelSize"] = 1.0f, ["bounces"] = 3
            })));

            if (sun is { } light)
            {
                var strongest = MathF.Max(light.Colour.X, MathF.Max(light.Colour.Y, light.Colour.Z));
                level.Nodes.Add(Group(pack, stem, "sun", "Sun", (bounds.Min + bounds.Max) * 0.5f + offset + new Vector3(0, 40, 0), Script(pack, stem, "sun", "Light", new()
                {
                    ["color"] = new[] { light.Colour.X, light.Colour.Y, light.Colour.Z, 1.0f },
                    ["intensity"] = SunStrength / MathF.Max(strongest, 0.01f),
                    ["lightType"] = "Directional",
                    ["castsShadows"] = true,
                    ["direction"] = Floats(light.Direction)
                })));
                // With sky light walled off as it really is (the probes), a street in shade wants a brighter camera.
                level.Sky = new ManaLevelSky { Exposure = 1.6f };
                if (skyPhoto != null)
                {
                    level.Sky.Mode = "Photo";
                    level.Sky.Photo = Path.GetRelativePath(checkout, skyPhoto).Replace('\\', '/');
                }
            }
            return level;
        }

        // The day level at night: no sun or photo, a still night sky, and a light at each glowing thing.
        private static ManaLevel Night(ManaLevel day, string pack, string stem, List<ManaImportGlow> glows, Vector3 offset)
        {
            var night = ManaLevel.FromJson(day.ToJson());
            night.Name = day.Name + " Night";
            night.Nodes.RemoveAll(node => node.Name == "Sun");
            night.Sky = new ManaLevelSky { Mode = "DayNightCycle", DateTime = new DateTimeOffset(2026, 6, 21, 23, 30, 0, TimeSpan.FromHours(-8)), TimeSpeed = 0.0f };
            night.Nodes.Add(GlowLights(pack, stem, glows, offset, indoors: false));
            return night;
        }

        // Outdoors a lamp must reach the street from a post; indoors a room's walls and the probes' bounces hold its
        // light in, and twenty street-lamp lights in one bistro blew it out.
        private static ManaLevelNode GlowLights(string pack, string stem, List<ManaImportGlow> glows, Vector3 offset, bool indoors)
        {
            var group = new ManaLevelNode
            {
                Id = Id(pack, stem, "glows"), Name = "Emissive Lights", Kind = ManaLevelSourceKind.Group, Position = Floats(offset),
                Children = new List<ManaLevelNode>()
            };
            for (var i = 0; i < glows.Count; i++)
            {
                var glow = glows[i];
                // A saturated colour is a coloured bulb: a faint tint nearby. White or warm is a lamp, a sign, a window.
                var intensity = 0.5f;
                var range = 3.0f;
                if (MathF.Min(glow.Colour.X, MathF.Min(glow.Colour.Y, glow.Colour.Z)) >= 0.35f)
                {
                    intensity = 2.0f + glow.Size * 2.0f;
                    range = MathF.Min(12.0f, 7.0f + glow.Size * 6.0f);
                    if (indoors)
                    {
                        intensity = 0.4f + glow.Size * 0.4f;
                        range = MathF.Min(8.0f, 4.0f + glow.Size * 4.0f);
                    }
                }
                group.Children.Add(new ManaLevelNode
                {
                    Id = Id(pack, stem, $"glow {i}"), Name = $"Light {i}", Kind = ManaLevelSourceKind.Group, Position = Floats(glow.Position),
                    Scripts = [Script(pack, stem, $"glow {i}", "Light", new()
                    {
                        ["color"] = new[] { glow.Colour.X, glow.Colour.Y, glow.Colour.Z, 1.0f },
                        ["intensity"] = intensity, ["range"] = range, ["lightType"] = "Point"
                    })]
                });
            }
            return group;
        }

        private static ManaLevelNode Group(string pack, string stem, string key, string name, Vector3 position, ManaLevelScript script) => new()
        {
            Id = Id(pack, stem, key), Name = name, Kind = ManaLevelSourceKind.Group, Position = Floats(position), Scripts = [script]
        };

        private static ManaLevelScript Script(string pack, string stem, string key, string type, Dictionary<string, object> properties) => new()
        {
            Id = Id(pack, stem, key + " script"),
            Type = type,
            Properties = properties.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value))
        };

        // The same ids on every import, so a level's edits saved over an import keep finding their nodes.
        private static string Id(string pack, string stem, string key) =>
            Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"{pack}/{stem}/{key}"))).ToLowerInvariant();

        private static float[] Floats(Vector3 value) => [MathF.Round(value.X, 3), MathF.Round(value.Y, 3), MathF.Round(value.Z, 3)];

        private static string Save(ManaLevel level, string folder, string name)
        {
            var path = Path.Combine(folder, name + ManaLevel.FileExtension);
            level.Save(path);
            return path;
        }
    }
}
#endif

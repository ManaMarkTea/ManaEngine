using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ManaVoxel.Code.Imports
{
    /// <summary>
    /// The material settings a Falcor scene file (<c>.pyscene</c>) adds to its FBX -- what FBX cannot carry: glass and
    /// liquids that transmit light, their absorption, roughness, metalness, double sides. Read from the plain
    /// <c>m = sceneBuilder.getMaterial("Name")</c> / <c>m.property = value</c> lines; anything cleverer is skipped.
    /// </summary>
    public static partial class ManaPySceneMaterials
    {
        /// <summary>One material's settings: numbers, a colour (float3) or a flag, by Falcor's property name.</summary>
        public sealed class Settings
        {
            public Dictionary<string, float> Numbers { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, Vector3> Colours { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, bool> Flags { get; } = new(StringComparer.Ordinal);
        }

        /// <summary>Every material the file sets something on, by material name.</summary>
        public static Dictionary<string, Settings> Parse(string text)
        {
            var colours = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (Match entry in DictionaryColour().Matches(text))
            {
                colours[entry.Groups[1].Value] = Float3(entry.Groups[2].Value);
            }

            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            var materials = new Dictionary<string, Settings>(StringComparer.Ordinal);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Split('#')[0].Trim();
                if (GetMaterial().Match(line) is { Success: true } get)
                {
                    variables[get.Groups[1].Value] = get.Groups[2].Value;
                    continue;
                }
                if (Assignment().Match(line) is not { Success: true } set || !variables.TryGetValue(set.Groups[1].Value, out var name))
                {
                    continue;
                }
                if (!materials.TryGetValue(name, out var settings))
                {
                    materials[name] = settings = new Settings();
                }
                var property = set.Groups[2].Value;
                var value = set.Groups[3].Value.Trim();
                if (value is "True" or "False")
                {
                    settings.Flags[property] = value == "True";
                }
                else if (value.StartsWith("float3", StringComparison.Ordinal))
                {
                    settings.Colours[property] = Float3(value);
                }
                else if (DictionaryLookup().Match(value) is { Success: true } lookup && colours.TryGetValue(lookup.Groups[1].Value, out var colour))
                {
                    settings.Colours[property] = colour;
                }
                else if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    settings.Numbers[property] = number;
                }
            }
            return materials;
        }

        /// <summary>
        /// Applies the settings to the glTF's materials by name. A rasteriser cannot refract, so a transmissive material
        /// (glass, water, wine) is blended instead: tinted by what its absorption lets through a centimetre of it, and as
        /// opaque as that is dark. Returns the materials changed.
        /// </summary>
        public static int Apply(ManaGltfDocument gltf, Dictionary<string, Settings> settings)
        {
            var changed = 0;
            foreach (var node in gltf.Materials)
            {
                if (node is not JsonObject material || material["name"]?.GetValue<string>() is not { } name || !settings.TryGetValue(name, out var set))
                {
                    continue;
                }
                var pbr = material["pbrMetallicRoughness"] as JsonObject ?? new JsonObject();
                material["pbrMetallicRoughness"] = pbr;
                if (set.Numbers.TryGetValue("roughness", out var roughness))
                {
                    pbr["roughnessFactor"] = roughness;
                }
                if (set.Numbers.TryGetValue("metallic", out var metallic))
                {
                    pbr["metallicFactor"] = metallic;
                }
                if (set.Flags.TryGetValue("doubleSided", out var doubleSided))
                {
                    material["doubleSided"] = doubleSided;
                }
                if (set.Numbers.TryGetValue("specularTransmission", out var transmission) && transmission > 0.0f)
                {
                    var tint = Vector3.One;
                    if (set.Colours.TryGetValue("volumeAbsorption", out var absorption))
                    {
                        tint = new Vector3(MathF.Exp(-absorption.X * 0.01f), MathF.Exp(-absorption.Y * 0.01f), MathF.Exp(-absorption.Z * 0.01f));
                    }
                    var darkness = 1.0f - (tint.X + tint.Y + tint.Z) / 3.0f;
                    var alpha = Math.Clamp(0.15f + darkness * 0.6f, 0.1f, 0.9f) * transmission + (1.0f - transmission);
                    // The pack's glass swatch is mid-grey: multiplied in, every bottle went grey. The tint is the colour.
                    pbr.Remove("baseColorTexture");
                    // Glass gives back little light of its own -- what shows is its reflections and what is behind it --
                    // and a rack of stemware stacks many layers: lit at full tint, they summed to white.
                    var diffuse = tint * (1.0f - 0.8f * transmission);
                    pbr["baseColorFactor"] = new JsonArray(diffuse.X, diffuse.Y, diffuse.Z, alpha);
                    material["alphaMode"] = "BLEND";
                    material.Remove("alphaCutoff");
                }
                changed++;
            }
            return changed;
        }

        private static Vector3 Float3(string text)
        {
            // Inside the brackets only: the 3 of "float3" is not a value.
            var open = text.IndexOf('(');
            if (open >= 0)
            {
                text = text[(open + 1)..];
            }
            var numbers = Number().Matches(text).Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
            if (numbers.Length < 3)
            {
                return Vector3.Zero;
            }
            return new Vector3(numbers[0], numbers[1], numbers[2]);
        }

        [GeneratedRegex("^(\\w+)\\s*=\\s*sceneBuilder\\.getMaterial\\(\\s*\"([^\"]+)\"\\s*\\)")]
        private static partial Regex GetMaterial();

        [GeneratedRegex("^(\\w+)\\.(\\w+)\\s*=\\s*(.+)$")]
        private static partial Regex Assignment();

        [GeneratedRegex("'(\\w+)'\\s*:\\s*(float3\\([^)]*\\))")]
        private static partial Regex DictionaryColour();

        [GeneratedRegex("^\\w+\\[\\s*'(\\w+)'\\s*\\]$")]
        private static partial Regex DictionaryLookup();

        [GeneratedRegex("-?\\d+(\\.\\d+)?([eE][-+]?\\d+)?")]
        private static partial Regex Number();
    }
}

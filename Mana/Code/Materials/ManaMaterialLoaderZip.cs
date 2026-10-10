using System.IO.Compression;

namespace Mana.Code.Materials
{
    public static class ManaMaterialLoaderZip
    {
        public static ManaMaterial LoadFromZip(ManaContext context, string filePath)
        {
            var material = new ManaMaterial(context);
            ApplyTo(material, filePath);
            return material;
        }

        public static string? ChannelOf(string name)
        {
            if (!new[] { ".png", ".jpg", ".jpeg", ".tga", ".bmp" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }
            var tokens = Path.GetFileNameWithoutExtension(name).ToLowerInvariant().Split(['_', '-', ' ', '.']);
            foreach (var (channel, aliases) in new (string, string[])[]
            {
                ("Albedo", ["albedo", "basecolor", "diffuse", "color"]),
                ("Occlusion", ["ao", "occlusion", "ambientocclusion", "ambient"]),
                ("Roughness", ["roughness", "rough"]),
                ("Normal", ["normal", "normalgl", "nor", "norgl"]),
                ("Emission", ["emissive", "emission"]),
                ("Metallic", ["metallic", "metalness"]),
                ("Opacity", ["opacity", "alpha"])
            })
            {
                if (tokens.Intersect(aliases).Any() && !tokens.Contains("dx"))
                {
                    return channel;
                }
            }
            // Specular is not metalness; height and previews are not surface channels.
            return null;
        }

        public static void ApplyTo(ManaMaterial material, string filePath)
        {
            var diskPath = ManaCore.Assets.ManaAssetCatalog.Current?.LocalFileOf(filePath)
                ?? ManaTexture.ResolveTexturePath(filePath);
            using var archive = ZipFile.OpenRead(diskPath);
            var maps = new Dictionary<string, ManaTexture>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (ChannelOf(entry.FullName) is not { } channel || maps.ContainsKey(channel))
                {
                    continue;
                }
                maps[channel] = new ManaTexture(material.Context)
                {
                    Name = entry.Name,
                    OriginalFilename = $"zip:{Path.GetFileName(filePath)}/{entry.FullName}"
                };
            }
            if (!maps.ContainsKey("Albedo"))
            {
                throw new InvalidDataException("A material ZIP needs an albedo, basecolor or diffuse map.");
            }
            material.Albedo.TextureValue = maps.GetValueOrDefault("Albedo");
            material.Normal.TextureValue = maps.GetValueOrDefault("Normal");
            material.Roughness.TextureValue = maps.GetValueOrDefault("Roughness");
            material.Metallic.TextureValue = maps.GetValueOrDefault("Metallic");
            material.Occlusion.TextureValue = maps.GetValueOrDefault("Occlusion");
            material.Emission.TextureValue = maps.GetValueOrDefault("Emission");
            material.Opacity.TextureValue = maps.GetValueOrDefault("Opacity");
            material.Albedo.Value = System.Numerics.Vector4.One;
            material.Emission.Value = System.Numerics.Vector4.One;
            material.Opacity.Value = 1.0f;
            material.Roughness.Value = maps.ContainsKey("Roughness") ? 1.0f : 0.8f;
            material.Metallic.Value = maps.ContainsKey("Metallic") ? 1.0f : 0.0f;
            material.Occlusion.Value = 1.0f;
            material.NormalSelector = maps.ContainsKey("Normal") ? 0.0f : 1.0f;
            material.EmissionStrength = maps.ContainsKey("Emission") ? 1.0f : 0.0f;
            material.Name = Path.GetFileNameWithoutExtension(filePath).Replace('-', ' ').Replace('_', ' ');
            material.SourceZip = filePath;
        }
    }
}

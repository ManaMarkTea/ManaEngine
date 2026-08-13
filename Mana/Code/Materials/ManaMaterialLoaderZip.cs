using System.Diagnostics;

namespace Mana.Code.Materials
{
    public static class ManaMaterialLoaderZip
    {
        public static unsafe ManaMaterial LoadFromZip(ManaContext context, string filePath)
        {
            ManaMaterial output = new ManaMaterial(context);
            output.Name = ToMaterialName(filePath);

            var zipArchive = System.IO.Compression.ZipFile.OpenRead(filePath);

            Trace.WriteLine($"Zip Material: {filePath}");
            foreach (var zipEntry in zipArchive.Entries)
            {
                Trace.WriteLine($"  {zipEntry.FullName}");

                ManaTexture textureFromZip = new ManaTexture(context);
                textureFromZip.Name = zipEntry.FullName;
                textureFromZip.PixelStream = zipEntry.Open();
                textureFromZip.Load();
                textureFromZip.PixelStream.Close();
                AssignToMaterial(context, output, textureFromZip);
            }

            return output;
        }

        private static List<string> albedoNames = new List<string> { "_albedo", "_basecolor", "_diffuse" };
        private static List<string> ambientNames = new List<string> { "_ao", "_ambient" };
        private static List<string> roughnessNames = new List<string> { "_roughness" };
        private static List<string> normalNames = new List<string> { "_normal" };
        private static List<string> emissionNames = new List<string> { "_emissive", "_emission" };
        private static List<string> metallicNames = new List<string> { "_metallic", "_specular" };

        private static string ToMaterialName(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "Zip Material";
            }

            fileName = fileName
                .Replace("-bl", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("_bl", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace('-', ' ')
                .Replace('_', ' ');

            var words = fileName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant());

            return $"{string.Join(' ', words)} Material";
        }

        private static void AssignToMaterial(ManaContext context, ManaMaterial material, ManaTexture texture)
        {
            if (texture.Name.Contains(albedoNames))
            {
                material.Albedo.TextureValue = texture;
            }
            if (texture.Name.Contains(ambientNames))
            {
                material.Occlusion.TextureValue = texture;
            }
            if (texture.Name.Contains(metallicNames))
            {
                material.Metallic.TextureValue = texture;
            }
            if (texture.Name.Contains(roughnessNames))
            {
                material.Roughness.TextureValue = texture;
            }
            if (texture.Name.Contains(normalNames))
            {
                material.Normal.TextureValue = texture;
                material.NormalSelector = 0.0f;
            }
            if (texture.Name.Contains(emissionNames))
            {
                material.Emission.TextureValue = texture;
            }
        }
    }
}

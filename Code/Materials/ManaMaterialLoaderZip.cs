using MetaRenderer.Code.ImageLoaders;
using MetaRenderer.Code;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections;

using System.IO;
using Mana.Code;
using Silk.NET.Assimp;
using System.Diagnostics;

namespace Mana.Code.Materials
{
    public static class ManaMaterialLoaderZip
    {
        public static unsafe ManaMaterial LoadFromZip(ManaContext context, string filePath)
        {
            ManaMaterial output = new ManaMaterial(context);

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
        private static List<string> ambientNames = new List<string> { "_ao", "_ambient"};
        private static List<string> roughnessNames = new List<string> { "_roughness" };
        private static List<string> normalNames = new List<string> { "_normal" };
        private static List<string> emissionNames = new List<string> { "_emissive", "_emission" };
        private static List<string> metallicNames = new List<string> { "_metallic", "_specular" };

        private static void AssignToMaterial(ManaContext context, ManaMaterial material, ManaTexture texture)
        {
            if (texture.Name.Contains(albedoNames))
            {
                material.Albedo.TextureValue = texture;
            }
            if (texture.Name.Contains(ambientNames))
            {
                material.Ambient.TextureValue = texture;
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

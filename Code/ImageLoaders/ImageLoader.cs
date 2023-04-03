using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MetaRenderer.Code.ImageLoaders
{
    public class ImageLoader
    {/*
        public static bool LoadImageData(SharpGLTF.Schema2.Texture rawTextureFile, MetaTexture texture, string name)
        {
            // stb_image loads from the top-left pixel, whereas OpenGL loads from the bottom-left, causing the texture to be flipped vertically.
            // This will correct that, making the texture display properly.
            StbImage.stbi_set_flip_vertically_on_load(0);

            var image = rawTextureFile.PrimaryImage;

            // Load the image.
            ImageResult imgData = ImageResult.FromMemory(image.Content.Content.ToArray());
            string sourcePath = image.Content.SourcePath;

            texture.Name = name;
            texture.PixelData = imgData.Data;
            texture.ElementCount = imgData.Comp == ColorComponents.RedGreenBlue ? 3 : 4;
            texture.Width = imgData.Width;
            texture.Height = imgData.Height;

            if (rawTextureFile.Sampler != null)
            {
                texture.IsWrapU = rawTextureFile.Sampler.WrapS == SharpGLTF.Schema2.TextureWrapMode.REPEAT;
                texture.IsWrapV = rawTextureFile.Sampler.WrapT == SharpGLTF.Schema2.TextureWrapMode.REPEAT;
            }
            texture.Load();

            return true;
        }*/
    }
}

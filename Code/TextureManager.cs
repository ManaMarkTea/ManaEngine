using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class TextureManager
    {
        private static TextureManager _Instance;        
        public static TextureManager Instance
        {
            get
            {
                if (_Instance == null)
                {
                    _Instance = new TextureManager();
                }

                return _Instance;
            }
        }

        private ManaTexture _DefaultTexture;

        private TextureManager()
        {
            _DefaultTexture = new ManaTexture();

            /*
            // Load the image.
            ImageResult imgData = ImageResult.FromStream(new FileStream("Assets/Textures/DebugTexture_Grid.png", FileMode.Open, FileAccess.Read));

            _DefaultTexture.PixelData = imgData.Data;
            _DefaultTexture.ElementCount = imgData.Comp == ColorComponents.RedGreenBlue ? 3 : 4;
            _DefaultTexture.Width = imgData.Width;
            _DefaultTexture.Height = imgData.Height;
            _DefaultTexture.IsWrapU = true;
            _DefaultTexture.IsWrapV = true;
            _DefaultTexture.Load();
            */
            
        }
        public ManaTexture CreateTexture(string key)
        {
            if (LoadedTextures.ContainsKey(key))
            {
                return LoadedTextures[key];
            }

            return new ManaTexture();
        }

        public ManaTexture FindTexture(string key)
        {
            if (LoadedTextures.ContainsKey(key))
            {
                return LoadedTextures[key];
            }

            return _DefaultTexture;
        }

        public Dictionary<string, ManaTexture> LoadedTextures = new ();

    }
}

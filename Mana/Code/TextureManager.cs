namespace Mana
{
    public class TextureManager : ManaObject
    {
        private static Dictionary<ManaContext, TextureManager> _Instances = new Dictionary<ManaContext, TextureManager>();
        public static TextureManager GetInstance(ManaContext context)
        {
            lock (_Instances)
            {
                if (!_Instances.ContainsKey(context))
                {
                    _Instances.Add(context, new TextureManager(context));
                }

                return _Instances[context];
            }
        }

        private ManaTexture _DefaultTexture;

        private TextureManager(ManaContext context) : base(context)
        {
            _DefaultTexture = new ManaTexture(Context);
            _DefaultTexture.OriginalFilename = "Assets/Textures/DebugTexture_Grid.png";
            _DefaultTexture.Load();

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

            var texture = new ManaTexture(Context);
            LoadedTextures.Add(key, texture);
            return texture;
        }

        public ManaTexture FindTexture(string key)
        {
            if (LoadedTextures.ContainsKey(key))
            {
                return LoadedTextures[key];
            }

            return _DefaultTexture;
        }

        public Dictionary<string, ManaTexture> LoadedTextures = new();

    }
}

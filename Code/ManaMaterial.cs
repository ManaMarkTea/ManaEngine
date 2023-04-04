using MetaRenderer.Code.ImageLoaders;
using MetaRenderer.Code;
using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections;

using System.IO;

namespace Mana
{
    public enum ManaMaterialChannelTypes
    {
        None = 0,
        IntValue,
        BooleanValue,
        ScalarValue,
        VectorValue,
        TextureValue,
        StringValue
    }

    public class MetaMaterialChannel
    {
        public string Name { get; set; }

        public ManaMaterialChannelTypes ChannelType = ManaMaterialChannelTypes.None;
        public int IntValue { get; set; }
        public bool BooleanValue { get; set; }
        public float ScalarValue { get; set; }
        public Vector4 VectorValue { get; set; }
        public ManaTexture Texture { get; set; }
        public string Value { get; set; }

        public override string ToString()
        {
            string output = Name + " = ";
            switch (ChannelType)
            {
                case ManaMaterialChannelTypes.None:break;
                case ManaMaterialChannelTypes.IntValue: output += IntValue; break;
                case ManaMaterialChannelTypes.BooleanValue: output += BooleanValue; break;
                case ManaMaterialChannelTypes.ScalarValue: output += ScalarValue; break;
                case ManaMaterialChannelTypes.VectorValue: output += VectorValue; break;
                case ManaMaterialChannelTypes.TextureValue: output += Texture?.ToString(); break;
                case ManaMaterialChannelTypes.StringValue: output += Value; break;
            }

            return output;
        }
    }

    public class ManaMaterial
    {
        public ManaShader Shader { get; set; }
        public ManaWorld World { get; set; }

        public ManaMaterial(ManaWorld world)
        {
            World = world;
            Channels = new Dictionary<string, MetaMaterialChannel>();
            Shader = new ManaShader(world);
            Shader.LoadShader("Assets/Shaders/BardPbr.vert", "Assets/Shaders/BardPbr.frag");
        }

        public Dictionary<string, MetaMaterialChannel> Channels { get; set; }
        public string Name { get; set; }

        public unsafe void LoadFromGltf(Material* material)
        {
            Shader = new ManaShader(World);
            //Shader.LoadShader("Assets/Shaders/basic_pos_uv.vert", "Assets/Shaders/PixelShader.frag");
            Shader.LoadShader("Assets/Shaders/PBR_Emeiri/pbr.vert", "Assets/Shaders/PBR_Emeiri/pbr.frag");


            for (int i = 0; i < material->MNumProperties; i++)
            {
                MaterialProperty* channel = material->MProperties[i];
                byte[] byteArray = new byte[channel->MDataLength];
                Marshal.Copy((IntPtr)channel->MData, byteArray, 0, (int)channel->MDataLength);      

                MetaMaterialChannel metaChannel = new MetaMaterialChannel();

                var key = channel->MKey.ToString();
                if (channel->MSemantic != 0)
                {
                    TextureType tType = (TextureType)channel->MSemantic;
                    if (tType != TextureType.Unknown)
                    {
                        key += "_" + tType;
                    }
                    else
                    {
                        key += "_" + channel->MSemantic;
                    }
                }

                metaChannel.Name = key;

                if (channel->MType == PropertyTypeInfo.String)
                {                    
                    string propertyValue = UTF32Encoding.ASCII.GetString(byteArray, 4, byteArray.Length - 4).Trim('\0');
                    metaChannel.Value = propertyValue;
                    metaChannel.ChannelType = ManaMaterialChannelTypes.StringValue;
                }                
                else if ( channel->MType == PropertyTypeInfo.Float)
                {
                    var floatArray = new float[byteArray.Length / 4];
                    Buffer.BlockCopy(byteArray, 0, floatArray, 0, byteArray.Length);

                    if (floatArray.Length == 4) 
                    {
                        metaChannel.VectorValue = new Vector4(floatArray[0], floatArray[1], floatArray[2], floatArray[3]);
                        metaChannel.ChannelType = ManaMaterialChannelTypes.VectorValue;
                    }
                    else if (floatArray.Length == 1)
                    {
                        metaChannel.ScalarValue = floatArray[0];
                        metaChannel.ChannelType = ManaMaterialChannelTypes.ScalarValue;
                    }
                    else
                    {
                        int x = 4;
                    }
                    
                }
                else if (channel->MType == PropertyTypeInfo.Integer)
                {
                    var intArray = new int[byteArray.Length / 4];
                    Buffer.BlockCopy(byteArray, 0, intArray, 0, byteArray.Length);

                    if (intArray.Length == 1)
                    {
                        metaChannel.IntValue = intArray[0];
                        metaChannel.ChannelType = ManaMaterialChannelTypes.IntValue;
                    }
                }
                else if (channel->MType == PropertyTypeInfo.Buffer && byteArray.Length == 4)
                {
                    var intArray = new int[byteArray.Length / 4];
                    Buffer.BlockCopy(byteArray, 0, intArray, 0, byteArray.Length);

                    if (intArray.Length == 1)
                    {
                        metaChannel.BooleanValue = intArray[0] == 0 ? false : true;
                        metaChannel.ChannelType = ManaMaterialChannelTypes.BooleanValue;
                    }
                }
                else if (channel->MType == PropertyTypeInfo.Buffer && byteArray.Length == 1)
                {
                    var singleByte = new byte[1];
                    Buffer.BlockCopy(byteArray, 0, singleByte, 0, byteArray.Length);

                    if (singleByte.Length == 1)
                    {                        
                        metaChannel.IntValue = singleByte[0];
                        metaChannel.ChannelType |= ManaMaterialChannelTypes.IntValue;
                    }
                }
                else
                {
                    int x = 3;
                }
                
                TextureType type = (TextureType)channel->MSemantic;
                if (type != TextureType.None)
                {
                    string filePath = metaChannel.Value;
                    if (filePath != null)
                    {
                        string fullPath = System.IO.Path.Combine(World.ActivePath, filePath);
                        var channelTexture = TextureManager.Instance.CreateTexture(filePath);
                        if (!channelTexture.IsLoaded)
                        {
                            channelTexture.OriginalFilename = fullPath;
                            channelTexture.Name = metaChannel.Value;
                            channelTexture.Load();
                        }
                        metaChannel.Texture = channelTexture;
                    }
                }

                Channels[key] = metaChannel;
            }

            if (Channels.ContainsKey("?mat.name"))
            {
                Name = Channels["?mat.name"].Value;
            }

        }

        private unsafe List<ManaTexture> LoadMaterialTextures(Material* mat, TextureType type, string typeName)
        {
            var textureCount = World._AssetImporter.GetMaterialTextureCount(mat, type);

            List<ManaTexture> textures = new List<ManaTexture>();
            for (uint i = 0; i < textureCount; i++)
            {
                AssimpString path;
                World._AssetImporter.GetMaterialTexture(mat, type, i, &path, null, null, null, null, null, null);
                
                ManaTexture manaTexture = TextureManager.Instance.CreateTexture(path);
                manaTexture.OriginalFilename = path;
                textures.Add(manaTexture);
            }
            return textures;
        }

        public void Bind(Matrix4x4 objectMatrix)
        {
            int i = 0;
            foreach (var texture in Channels)
            {
                if (texture.Value.Texture != null)
                {
                    texture.Value.Texture.Bind(i++);
                }
            }

            Shader.BindShader(this, objectMatrix, World.MainCamera);
        }

        public void UnBind()
        {
            int i = 0;
            foreach (var texture in Channels)
            {
                if (texture.Value.Texture != null)
                {
                    texture.Value.Texture.UnBind(i++);
                }
            }
            
            Shader.UnbindShader();
        }

    }



    /// <summary>
    /// Defines the purpose of a texture.
    /// </summary>
    public enum TextureSemanticType : int
    {
        /// <summary>
        /// No texture, but the value can be used as a 'texture semantic'.
        /// </summary>
        None = 0x0,

        /// <summary>
        /// A diffuse texture that is combined with the result of the diffuse lighting equation.
        /// </summary>
        Diffuse = 0x1,

        /// <summary>
        /// A specular texture that is combined with the result of the specular lighting equation.
        /// </summary>
        Specular = 0x2,

        /// <summary>
        /// An ambient texture that is combined with the ambient lighting equation.
        /// </summary>
        Ambient = 0x3,

        /// <summary>
        /// An emissive texture that is added to the result of the lighting calculation. It is not influenced
        /// by incoming light, instead it represents the light that the object is naturally emitting.
        /// </summary>
        Emissive = 0x4,

        /// <summary>
        /// A height map texture. by convention, higher gray-scale values stand for
        /// higher elevations from some base height.
        /// </summary>
        Height = 0x5,

        /// <summary>
        /// A tangent-space normal map. There are several conventions for normal maps
        /// and Assimp does (intentionally) not distinguish here.
        /// </summary>
        Normals = 0x6,

        /// <summary>
        /// A texture that defines the glossiness of the material. This is the exponent of the specular (phong)
        /// lighting equation. Usually there is a conversion function defined to map the linear color values
        /// in the texture to a suitable exponent.
        /// </summary>
        Shininess = 0x7,

        /// <summary>
        /// The texture defines per-pixel opacity. usually 'white' means opaque and 'black' means 'transparency. Or quite
        /// the opposite.
        /// </summary>
        Opacity = 0x8,

        /// <summary>
        /// A displacement texture. The exact purpose and format is application-dependent. Higher color values stand for higher vertex displacements.
        /// </summary>
        Displacement = 0x9,

        /// <summary>
        /// A lightmap texture (aka Ambient occlusion). Both 'lightmaps' and dedicated 'ambient occlusion maps' are covered by this material property. The
        /// texture contains a scaling value for the final color value of a pixel. Its intensity is not affected by incoming light.
        /// </summary>
        Lightmap = 0xA,

        /// <summary>
        /// A reflection texture. Contains the color of a perfect mirror reflection. This is rarely used, almost never for real-time applications.
        /// </summary>
        Reflection = 0xB,

        /// <summary>
        /// An unknown texture that does not mention any of the defined texture type definitions. It is still imported, but is excluded from any
        /// further postprocessing.
        /// </summary>
        Unknown = 0xC
    }

}

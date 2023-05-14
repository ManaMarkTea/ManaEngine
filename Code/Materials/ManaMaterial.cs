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
using Mana.Code;

namespace Mana.Code.Materials
{
    public class ManaMaterial : ManaObject, ICloneable
    {
        public static ManaMaterial DefaultMaterial { get; internal set; } = new ManaMaterial(Game.Instance.Context);


        public ManaShader Shader { get; set; }
 
        public ManaTextureVector4 Albedo;
        public ManaTextureVector4 Normal;
        public ManaTextureVector4 Emission;
        public ManaTextureSingle Metallic;
        public ManaTextureSingle Roughness;
        public ManaTextureSingle Ambient;

        public double EmissionStrength = 0.0;

        public ManaMaterial(ManaContext context) : base(context)
        {
            Name = "Material";
            Channels = new Dictionary<string, ManaMaterialChannel>();            
            var shader = new ManaPbrShader(context);
            Shader = shader;

            Albedo = new ManaTextureVector4(Context, Colors.Orange);
            Normal = new ManaTextureVector4(Context, Vector4.UnitX);
            Emission = new ManaTextureVector4(Context, Colors.Black);
            Metallic = new ManaTextureSingle(Context, 0.5);
            Roughness = new ManaTextureSingle(Context, 0.5);
            Ambient = new ManaTextureSingle(Context, 0.25);
        }

        public Dictionary<string, ManaMaterialChannel> Channels { get; set; }        


        public unsafe void LoadFromGltf(Material* material)
        {
            for (int i = 0; i < material->MNumProperties; i++)
            {
                MaterialProperty* channel = material->MProperties[i];
                byte[] byteArray = new byte[channel->MDataLength];
                Marshal.Copy((IntPtr)channel->MData, byteArray, 0, (int)channel->MDataLength);

                ManaMaterialChannel metaChannel = new ManaMaterialChannel();

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
                    string propertyValue = Encoding.ASCII.GetString(byteArray, 4, byteArray.Length - 4).Trim('\0');
                    metaChannel.Value = propertyValue;
                    metaChannel.ChannelType = ManaMaterialChannelTypes.StringValue;
                }
                else if (channel->MType == PropertyTypeInfo.Float)
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
                        string fullPath = Path.Combine(Context.World.ActivePath, filePath);
                        var channelTexture = TextureManager.Instance.CreateTexture(filePath);
                        if (!channelTexture.IsLoaded)
                        {
                            channelTexture.OriginalFilename = fullPath;
                            channelTexture.Name = metaChannel.Value;
                            channelTexture.Load();
                        }
                        metaChannel.Texture = channelTexture;

                        switch (type)
                        {
                            case TextureType.Diffuse: { Albedo.TextureValue = channelTexture; } break;
                            case TextureType.BaseColor: { Albedo.TextureValue = channelTexture; } break;
                            case TextureType.Metalness: { Metallic.TextureValue = channelTexture; } break;
                            case TextureType.DiffuseRoughness: { Roughness.TextureValue = channelTexture; } break;
                            case TextureType.Ambient: { Ambient.TextureValue = channelTexture; } break;
                            case TextureType.AmbientOcclusion: { Ambient.TextureValue = channelTexture; } break;
                            case TextureType.Emissive: { Emission.TextureValue = channelTexture; } break;
                            case TextureType.EmissionColor: { Emission.TextureValue = channelTexture; } break;
                            
                        }

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
            var textureCount = Context.World._AssetImporter.GetMaterialTextureCount(mat, type);

            List<ManaTexture> textures = new List<ManaTexture>();
            for (uint i = 0; i < textureCount; i++)
            {
                AssimpString path;
                Context.World._AssetImporter.GetMaterialTexture(mat, type, i, &path, null, null, null, null, null, null);

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

            Shader.BindShader(this, objectMatrix, Context.World.MainCamera);
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

        public object Clone()
        {
            var copy = MemberwiseClone() as ManaMaterial;

            copy.Albedo = (ManaTextureVector4)copy.Albedo.Clone();
            copy.Normal = (ManaTextureVector4)copy.Normal.Clone();
            copy.Emission = (ManaTextureVector4)copy.Emission.Clone();
            copy.Metallic = (ManaTextureSingle)copy.Metallic.Clone();
            copy.Roughness = (ManaTextureSingle)copy.Roughness.Clone();
            copy.Ambient = (ManaTextureSingle)copy.Ambient.Clone();

            copy.Albedo.Load();
            copy.Normal.Load();
            copy.Emission.Load();
            copy.Metallic.Load();
            copy.Roughness.Load();
            copy.Ambient.Load();

            return copy;
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

using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Materials
{
    public static class ManaMaterialLoaderGltf
    {

        public static unsafe ManaMaterial LoadFromGltf(ManaContext context, Material* material)
        {
            ManaMaterial outputMaterial = new ManaMaterial(context);

            for (int i = 0; i < material->MNumProperties; i++)
            {
                MaterialProperty* channel = material->MProperties[i];
                byte[] byteArray = new byte[channel->MDataLength];
                Marshal.Copy((IntPtr)channel->MData, byteArray, 0, (int)channel->MDataLength);

                ManaMaterialChannel metaChannel = new ManaMaterialChannel();
                string key = ExtracKey(channel);

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

                TextureType type = (TextureType)channel->MSemantic;
                if (type != TextureType.None)
                {
                    string filePath = metaChannel.Value;
                    if (filePath != null)
                    {
                        string fullPath = Path.Combine(context.World.ActivePath, filePath);
                        var channelTexture = TextureManager.GetInstance(context).CreateTexture(filePath);
                        if (!channelTexture.IsLoaded)
                        {
                            channelTexture.OriginalFilename = fullPath;
                            channelTexture.Name = metaChannel.Value;
                            channelTexture.Load();
                        }
                        metaChannel.Texture = channelTexture;

                        switch (type)
                        {
                            case TextureType.Diffuse: { outputMaterial.Albedo.TextureValue = channelTexture; } break;
                            case TextureType.BaseColor: { outputMaterial.Albedo.TextureValue = channelTexture; } break;
                            case TextureType.Metalness: { outputMaterial.Metallic.TextureValue = channelTexture; } break;
                            case TextureType.DiffuseRoughness: { outputMaterial.Roughness.TextureValue = channelTexture; } break;
                            case TextureType.Ambient: { outputMaterial.Ambient.TextureValue = channelTexture; } break;
                            case TextureType.AmbientOcclusion: { outputMaterial.Ambient.TextureValue = channelTexture; } break;
                            case TextureType.Emissive: { outputMaterial.Emission.TextureValue = channelTexture; } break;
                            case TextureType.EmissionColor: { outputMaterial.Emission.TextureValue = channelTexture; } break;
                        }
                    }
                }

                outputMaterial.Channels[key] = metaChannel;
            }

            if (outputMaterial.Channels.ContainsKey("?mat.name"))
            {
                outputMaterial.Name = outputMaterial.Channels["?mat.name"].Value;
            }

            return outputMaterial;            
        }

        private static unsafe string ExtracKey(MaterialProperty* channel)
        {
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

            return key;
        }

        private static unsafe List<ManaTexture> LoadMaterialTextures(ManaContext context, Material* mat, TextureType type, string typeName)
        {
            var textureCount = context.World._AssetImporter.GetMaterialTextureCount(mat, type);

            List<ManaTexture> textures = new List<ManaTexture>();
            for (uint i = 0; i < textureCount; i++)
            {
                AssimpString path;
                context.World._AssetImporter.GetMaterialTexture(mat, type, i, &path, null, null, null, null, null, null);

                ManaTexture manaTexture = TextureManager.GetInstance(context).CreateTexture(path);
                manaTexture.OriginalFilename = path;
                textures.Add(manaTexture);
            }
            return textures;
        }
    }
}

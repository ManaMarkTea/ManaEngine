using Mana.Code.Nodes;
using Mana.Code.Graphics;
using Mana.Code.ManaShaders;
using ManaCore.Graphics;
using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Shapes.Loaders
{
    public class ManaGltfLoader : ManaObject
    {
        public Assimp _AssetImporter { get; set; } = null!;
        public string ActivePath { get; set; } = string.Empty;

        public List<ManaMesh> Meshes { get; set; }

        public List<ManaMaterial> Materials { get; set; }

        public List<ManaScene> Scenes { get; set; }

        public ManaGltfLoader(ManaContext context) : base(context)
        {
            Meshes = new();
            Materials = new();
            Scenes = new();

        }

        public unsafe ManaSceneNode LoadFile(string sceneFile)
        {
            _AssetImporter = Assimp.GetApi();

            var scene = _AssetImporter.ImportFile(sceneFile, (uint)PostProcessSteps.Triangulate);
            ActivePath = System.IO.Path.GetDirectoryName(sceneFile) ?? string.Empty;

            if (scene == null || scene->MFlags == Silk.NET.Assimp.Assimp.SceneFlagsIncomplete || scene->MRootNode == null)
            {
                var error = _AssetImporter.GetErrorStringS();
                throw new Exception(error);
            }

            ManaScene manaScene = new ManaScene(Context);
            manaScene.Name = Path.GetFileNameWithoutExtension(sceneFile);

            for (int i = 0; i < scene->MNumMaterials; i++)
            {
                var material = scene->MMaterials[i];

                ManaMaterial manaMaterial = LoadMaterial(Context, material);
                Materials.Add(manaMaterial);
            }

            for (int i = 0; i < scene->MNumMeshes; i++)
            {
                var mesh = scene->MMeshes[i];
                ManaMesh model = LoadMesh(Context, mesh);
                Meshes.Add(model);
            }

            var rootSceneNode = LoadScene(scene, manaScene);
            Scenes.Add(manaScene);

            return rootSceneNode;
        }

        public unsafe ManaMesh LoadFirstMeshCpuOnly(string sceneFile)
        {
            _AssetImporter = Assimp.GetApi();

            var scene = _AssetImporter.ImportFile(sceneFile, (uint)PostProcessSteps.Triangulate);
            ActivePath = Path.GetDirectoryName(sceneFile) ?? string.Empty;

            if (scene == null || scene->MFlags == Silk.NET.Assimp.Assimp.SceneFlagsIncomplete || scene->MRootNode == null || scene->MNumMeshes == 0)
            {
                var error = _AssetImporter.GetErrorStringS();
                throw new Exception(string.IsNullOrWhiteSpace(error)
                    ? $"Could not load a mesh from '{sceneFile}'."
                    : error);
            }

            var mesh = scene->MMeshes[0];
            var output = new ManaMesh(Context)
            {
                Name = string.IsNullOrWhiteSpace(mesh->MName) ? Path.GetFileNameWithoutExtension(sceneFile) : mesh->MName
            };

            uint[] indexes = new uint[0];
            Vector3[] positions = new Vector3[mesh->MNumVertices];
            Vector3[] normals = mesh->MNormals != null ? new Vector3[mesh->MNumVertices] : Array.Empty<Vector3>();
            Vector4[] colors = mesh->MColors.Element0 != null ? new Vector4[mesh->MNumVertices] : Array.Empty<Vector4>();
            Vector2[] textureCoordinates0 = mesh->MTextureCoords.Element0 != null ? new Vector2[mesh->MNumVertices] : Array.Empty<Vector2>();
            Vector2[] textureCoordinates1 = mesh->MTextureCoords.Element1 != null ? new Vector2[mesh->MNumVertices] : Array.Empty<Vector2>();

            Vector3 minSize = Vector3.One * float.MaxValue;
            Vector3 maxSize = Vector3.One * float.MinValue;

            List<uint> indices = new List<uint>();
            for (uint i = 0; i < mesh->MNumFaces; i++)
            {
                Face face = mesh->MFaces[i];
                for (uint j = 0; j < face.MNumIndices; j++)
                {
                    indices.Add(face.MIndices[j]);
                }
            }

            indexes = indices.ToArray();

            Parallel.For(0, mesh->MNumVertices, (i) =>
            {
                Vector3 position = new Vector3(mesh->MVertices[i].X, mesh->MVertices[i].Y, mesh->MVertices[i].Z);
                positions[i] = position;

                lock (output)
                {
                    minSize = Vector3.Min(minSize, position);
                    maxSize = Vector3.Max(maxSize, position);
                }

                if (mesh->MNormals != null)
                {
                    normals[i] = new Vector3(mesh->MNormals[i].X, mesh->MNormals[i].Y, mesh->MNormals[i].Z).Normalized();
                }

                if (mesh->MColors.Element0 != null)
                {
                    colors[i] = new Vector4(mesh->MColors.Element0[i].X, mesh->MColors.Element0[i].Y, mesh->MColors.Element0[i].Z, mesh->MColors.Element0[i].W);
                }

                if (mesh->MTextureCoords.Element0 != null)
                {
                    textureCoordinates0[i] = new Vector2(mesh->MTextureCoords.Element0[i].X, mesh->MTextureCoords.Element0[i].Y);
                }

                if (mesh->MTextureCoords.Element1 != null)
                {
                    textureCoordinates1[i] = new Vector2(mesh->MTextureCoords.Element1[i].X, mesh->MTextureCoords.Element1[i].Y);
                }
            });

            output.SetSize(minSize, maxSize);

            string? albedoTexturePath = null;
            Vector4? albedoFactor = null;
            if (mesh->MMaterialIndex < scene->MNumMaterials)
            {
                ReadCpuOnlyMaterial(scene->MMaterials[mesh->MMaterialIndex], ref albedoTexturePath, ref albedoFactor);
            }

            output.SetCpuData(new ManaMeshCpuData
            {
                Indexes = indexes,
                Positions = positions,
                Normals = normals,
                Colors = colors,
                TextureCoordinates0 = textureCoordinates0,
                TextureCoordinates1 = textureCoordinates1,
                AlbedoTexturePath = albedoTexturePath,
                AlbedoFactor = albedoFactor
            });

            return output;
        }

        private unsafe void ReadCpuOnlyMaterial(Material* material, ref string? albedoTexturePath, ref Vector4? albedoFactor)
        {
            for (int i = 0; i < material->MNumProperties; i++)
            {
                MaterialProperty* channel = material->MProperties[i];
                byte[] byteArray = new byte[channel->MDataLength];
                Marshal.Copy((IntPtr)channel->MData, byteArray, 0, (int)channel->MDataLength);
                string key = ExtracKey(channel);

                if ((TextureType)channel->MSemantic is TextureType.BaseColor or TextureType.Diffuse)
                {
                    if (channel->MType == PropertyTypeInfo.String)
                    {
                        string propertyValue = Encoding.ASCII.GetString(byteArray, 4, byteArray.Length - 4).Trim('\0');
                        if (!string.IsNullOrWhiteSpace(propertyValue))
                        {
                            string fullPath = Path.Combine(ActivePath, propertyValue);
                            if (System.IO.File.Exists(fullPath))
                            {
                                albedoTexturePath = fullPath;
                            }
                        }
                    }

                    continue;
                }

                if ((key == "$clr.base" || key == "$clr.diffuse") && channel->MType == PropertyTypeInfo.Float)
                {
                    var floatArray = new float[byteArray.Length / 4];
                    System.Buffer.BlockCopy(byteArray, 0, floatArray, 0, byteArray.Length);
                    if (floatArray.Length >= 4)
                    {
                        albedoFactor = new Vector4(floatArray[0], floatArray[1], floatArray[2], floatArray[3]);
                    }
                }
            }
        }

        public unsafe ManaSceneNode LoadScene(Scene* scene, ManaScene manaScene)
        {
            var root = scene->MRootNode;
            var rootSceneNode = LoadSceneNode(root);
            manaScene.Children.Add(rootSceneNode);

            return rootSceneNode;
        }

        private unsafe ManaSceneNode LoadSceneNode(Node* node)
        {
            var local = node->MTransformation;
            local = Matrix4x4.Transpose(local);
            var metaSceneNode = new ManaSceneNode(Context, local);
            metaSceneNode.Name = node->MName;

            if (node->MNumMeshes != 0)
            {
                for (int i = 0; i < node->MNumMeshes; i++)
                {
                    int meshIndex = (int)node->MMeshes[i];
                    ManaMesh foundMesh = Meshes[meshIndex];
                    if (foundMesh != null)
                    {
                        metaSceneNode.Components.Add(new MeshRendererComponent(metaSceneNode, foundMesh) { });
                    }
                    else
                    {
                        Trace.TraceWarning($"Mesh not found {node->MName} had could not find {meshIndex}");
                    }
                }
            }

            for (int i = 0; i < node->MNumChildren; i++)
            {
                var child = node->MChildren[i];
                var childSceneNode = LoadSceneNode(child);
                metaSceneNode.AddChild(childSceneNode);
            }

            return metaSceneNode;
        }



        public unsafe ManaMaterial LoadMaterial(ManaContext context, Material* material)
        {
            ManaMaterial outputMaterial = new ManaMaterial(context);
            outputMaterial.Shader = new ManaPbrShader(context);

            List<string> textureNames = new List<string>();

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
                    System.Buffer.BlockCopy(byteArray, 0, floatArray, 0, byteArray.Length);

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
                    System.Buffer.BlockCopy(byteArray, 0, intArray, 0, byteArray.Length);

                    if (intArray.Length == 1)
                    {
                        metaChannel.IntValue = intArray[0];
                        metaChannel.ChannelType = ManaMaterialChannelTypes.IntValue;
                    }
                }
                else if (channel->MType == PropertyTypeInfo.Buffer && byteArray.Length == 4)
                {
                    var intArray = new int[byteArray.Length / 4];
                    System.Buffer.BlockCopy(byteArray, 0, intArray, 0, byteArray.Length);

                    if (intArray.Length == 1)
                    {
                        metaChannel.BooleanValue = intArray[0] == 0 ? false : true;
                        metaChannel.ChannelType = ManaMaterialChannelTypes.BooleanValue;
                    }
                }
                else if (channel->MType == PropertyTypeInfo.Buffer && byteArray.Length == 1)
                {
                    var singleByte = new byte[1];
                    System.Buffer.BlockCopy(byteArray, 0, singleByte, 0, byteArray.Length);

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
                        string fullPath = Path.Combine(ActivePath, filePath);

                        if (System.IO.File.Exists(fullPath))
                        {
                            var channelTexture = TextureManager.GetInstance(context).CreateTexture(filePath);
                            textureNames.Add(Path.GetFileNameWithoutExtension(fullPath));
                            if (!channelTexture.IsLoaded)
                            {
                                channelTexture.OriginalFilename = fullPath;
                                channelTexture.Name = metaChannel.Value;
                                if (context.OpenGl != null)
                                {
                                    channelTexture.Load();
                                }
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
                                case TextureType.Normals: { outputMaterial.Normal.TextureValue = channelTexture; outputMaterial.NormalSelector = 0.0f; } break;
                                case TextureType.NormalCamera: { outputMaterial.Normal.TextureValue = channelTexture; outputMaterial.NormalSelector = 0.0f; } break;
                            }
                        }
                    }
                }
                else
                {
                    switch (metaChannel.Name)
                    {
                        case "$clr.base":
                        case "$clr.diffuse": outputMaterial.Albedo.Value = metaChannel.VectorValue; break;
                        case "$mat.metallicFactor": outputMaterial.Metallic.Value = metaChannel.ScalarValue; break;
                        case "$mat.roughnessFactor": outputMaterial.Roughness.Value = metaChannel.ScalarValue; break;
                        case "$mat.shininess": outputMaterial.Roughness.Value = 1.0f - metaChannel.ScalarValue; break;
                        case "$mat.emissive": outputMaterial.Emission.Value = metaChannel.VectorValue; break;
                        case "$mat.opacity": /*later*/ break;

                    }                    
                }
                
                

                outputMaterial.Channels[key] = metaChannel;
            }

            if (outputMaterial.Channels.ContainsKey("?mat.name") &&
                !string.IsNullOrWhiteSpace(outputMaterial.Channels["?mat.name"].Value))
            {
                outputMaterial.Name = ToMaterialDisplayName(outputMaterial.Channels["?mat.name"].Value);
            }
            else if (textureNames.Count > 0)
            {
                outputMaterial.Name = $"{ToMaterialDisplayName(textureNames[0])} Material";
            }
            else
            {
                outputMaterial.Name = "Imported Material";
            }

            return outputMaterial;
        }

        private static string ToMaterialDisplayName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "Imported";
            }

            var words = name
                .Replace('-', ' ')
                .Replace('_', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1));

            return string.Join(' ', words);
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


        public unsafe ManaMesh LoadMesh(ManaContext context, Mesh* mesh)
        {
            ManaMesh output = new ManaMesh(context);

            uint[] indexes = new uint[0];
            Vector3[] positions = new Vector3[0];
            Vector3[] normals = new Vector3[0];
            Vector4[] colors = new Vector4[0];
            Vector2[] textureCoordinates0 = new Vector2[0];
            Vector2[] textureCoordinates1 = new Vector2[0];


            Vector3 minSize = Vector3.One * float.MaxValue;
            Vector3 maxSize = Vector3.One * float.MinValue;

            List<ManaMaterial> newMaterials = new List<ManaMaterial>();
            var foundMaterial = Materials[(int)mesh->MMaterialIndex];
            if (foundMaterial != null)
            {
                newMaterials.Add(foundMaterial);
            }

            List<uint> indices = new List<uint>();
            for (uint i = 0; i < mesh->MNumFaces; i++)
            {
                Face face = mesh->MFaces[i];
                // retrieve all indices of the face and store them in the indices vector
                for (uint j = 0; j < face.MNumIndices; j++)
                {
                    indices.Add(face.MIndices[j]);
                }
            }

            indexes = indices.ToArray();

            positions = new Vector3[mesh->MNumVertices];

            if (mesh->MNormals != null)
            {
                normals = new Vector3[mesh->MNumVertices];
            }

            if (mesh->MColors.Element0 != null)
            {
                colors = new Vector4[mesh->MNumVertices];
            }

            if (mesh->MTextureCoords.Element0 != null)
            {
                textureCoordinates0 = new Vector2[mesh->MNumVertices];
            }

            if (mesh->MTextureCoords.Element1 != null)
            {
                textureCoordinates1 = new Vector2[mesh->MNumVertices];
            }

            Dictionary<int, int> threadCounter = new Dictionary<int, int>();

            Parallel.For(0, mesh->MNumVertices, (i) =>
            {
                Vector3 vector = new Vector3(mesh->MVertices[i].X, mesh->MVertices[i].Y, mesh->MVertices[i].Z);
                positions[i] = vector;

                lock (output)
                {
                    minSize = Vector3.Min(minSize, positions[i]);
                    maxSize = Vector3.Max(maxSize, positions[i]);

                    if (!threadCounter.ContainsKey(Thread.CurrentThread.ManagedThreadId))
                    {
                        threadCounter[Thread.CurrentThread.ManagedThreadId] = 0;
                    }

                    threadCounter[Thread.CurrentThread.ManagedThreadId]++;
                }

                if (mesh->MNormals != null)
                {
                    Vector3 normal = new Vector3(mesh->MNormals[i].X, mesh->MNormals[i].Y, mesh->MNormals[i].Z);
                    normals[i] = normal.Normalized();                    
                }

                if (mesh->MColors.Element0 != null)
                {
                    Vector4 color = new Vector4(mesh->MColors.Element0[i].X, mesh->MColors.Element0[i].Y, mesh->MColors.Element0[i].Z, mesh->MColors.Element0[i].W);
                    colors[i] = color;
                }

                if (mesh->MTextureCoords.Element0 != null)
                {
                    Vector2 normal = new Vector2(mesh->MTextureCoords.Element0[i].X, mesh->MTextureCoords.Element0[i].Y);
                    textureCoordinates0[i] = normal;
                }

                if (mesh->MTextureCoords.Element1 != null)
                {
                    Vector2 normal = new Vector2(mesh->MTextureCoords.Element1[i].X, mesh->MTextureCoords.Element1[i].Y);
                    textureCoordinates1[i] = normal;
                }
            });

            output.SetSize(minSize, maxSize);

            var meshData = new ManaMeshCpuData
            {
                Indexes = indexes,
                Positions = positions,
                Normals = normals,
                Colors = colors,
                TextureCoordinates0 = textureCoordinates0,
                TextureCoordinates1 = textureCoordinates1,
                AlbedoTexturePath = GetMaterialTexturePath(foundMaterial, TextureType.BaseColor, TextureType.Diffuse),
                AlbedoFactor = foundMaterial?.Albedo.Value
            };

            output.SetCpuData(meshData);
            if (context.OpenGl != null && !context.DeferGpuUploads)
            {
                var gpuMesh = context.RequireOpenGlDevice().CreateMesh(context, meshData);
                if (gpuMesh is ManaOpenGlMesh openGlMesh)
                {
                    output.VertexBufferObject = openGlMesh.VertexBufferObject;
                }
            }
            output.Materials = newMaterials.ToArray();
            return output;
        }

        private static string? GetMaterialTexturePath(ManaMaterial? material, params TextureType[] textureTypes)
        {
            if (material == null)
            {
                return null;
            }

            foreach (var textureType in textureTypes)
            {
                var match = material.Channels.Values.FirstOrDefault(channel =>
                    channel.Texture != null &&
                    channel.Name.EndsWith($"_{textureType}", StringComparison.OrdinalIgnoreCase));
                if (match?.Texture != null && !string.IsNullOrWhiteSpace(match.Texture.OriginalFilename))
                {
                    return match.Texture.OriginalFilename;
                }
            }

            return null;
        }
    }
}

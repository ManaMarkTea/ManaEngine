using Mana.Code.Animation;
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
        private unsafe Scene* _activeScene;
        private Func<string, byte[]?>? _externalAssetResolver;

        public List<ManaMesh> Meshes { get; set; }

        public List<ManaMaterial> Materials { get; set; }

        public List<ManaScene> Scenes { get; set; }

        // Animation clips found in the last imported file, in Assimp order. Also handed to the actor
        // node's ManaSkeletalAnimationComponent, so this is mostly for callers that want to inspect
        // what a file shipped without walking the scene graph.
        public List<ManaAnimationClip> AnimationClips { get; } = new();

        // Lets a caller substitute a ManaSkeletalActorNode subclass for the root of a rigged file --
        // ManaPawnNode uses this to import straight into a controllable character instead of having to
        // retype the node after the fact. Ignored for files with no skin or clips.
        public Func<ManaContext, Matrix4x4, ManaSkeletalActorNode>? SkeletalActorFactory { get; set; }

        // Per-mesh skin data harvested during LoadMesh, keyed by Assimp mesh index. Resolved into a
        // real ManaSkeleton only after the node tree exists, since bones are scene nodes by name.
        private readonly Dictionary<int, ManaMeshSkinSource> _meshSkinSources = new();
        private readonly Dictionary<string, ManaSceneNode> _nodesByName = new(StringComparer.Ordinal);
        private readonly List<(ManaSceneNode Node, ManaMesh Mesh, int MeshIndex)> _skinnedMeshPlacements = new();

        public ManaGltfLoader(ManaContext context) : base(context)
        {
            Meshes = new();
            Materials = new();
            Scenes = new();

        }

        // Raw Assimp bone/weight data for one mesh, before bones are matched up to scene nodes.
        private sealed class ManaMeshSkinSource
        {
            public required string[] BoneNames { get; init; }
            public required Matrix4x4[] OffsetMatrices { get; init; }
            public required int[] BoneIndices { get; init; }
            public required float[] BoneWeights { get; init; }
            public required Vector3[] BindPositions { get; init; }
            public required Vector3[] BindNormals { get; init; }
        }

        public unsafe ManaSceneNode LoadFile(string sceneFile)
        {
            _AssetImporter = Assimp.GetApi();
            _externalAssetResolver = null;

            var scene = _AssetImporter.ImportFile(sceneFile, (uint)PostProcessSteps.Triangulate);
            _activeScene = scene;
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
                CaptureMeshSkinSource(i, mesh);
                Meshes.Add(model);
            }

            LoadAnimationClips(scene);
            var rootSceneNode = LoadScene(scene, manaScene);
            Scenes.Add(manaScene);
            _activeScene = null;

            return rootSceneNode;
        }

        public unsafe ManaSceneNode LoadFileFromMemory(ReadOnlySpan<byte> sceneBytes, string virtualFileName, Func<string, byte[]?>? externalAssetResolver = null)
        {
            _AssetImporter = Assimp.GetApi();
            _externalAssetResolver = externalAssetResolver;

            var extensionHint = Path.GetExtension(virtualFileName);
            if (!string.IsNullOrWhiteSpace(extensionHint) && extensionHint.StartsWith('.'))
            {
                extensionHint = extensionHint[1..];
            }

            Scene* scene;
            fixed (byte* sceneBuffer = sceneBytes)
            {
                scene = _AssetImporter.ImportFileFromMemory(sceneBuffer, (uint)sceneBytes.Length, (uint)PostProcessSteps.Triangulate, extensionHint);
            }
            _activeScene = scene;

            ActivePath = string.Empty;

            if (scene == null || scene->MFlags == Silk.NET.Assimp.Assimp.SceneFlagsIncomplete || scene->MRootNode == null)
            {
                var error = _AssetImporter.GetErrorStringS();
                throw new Exception(error);
            }

            ManaScene manaScene = new ManaScene(Context);
            manaScene.Name = Path.GetFileNameWithoutExtension(virtualFileName);

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
                CaptureMeshSkinSource(i, mesh);
                Meshes.Add(model);
            }

            LoadAnimationClips(scene);
            var rootSceneNode = LoadScene(scene, manaScene);
            Scenes.Add(manaScene);
            _activeScene = null;

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

            return BuildCpuOnlyMesh(scene, Path.GetFileNameWithoutExtension(sceneFile), externalAssetResolver: null);
        }

        /// <summary>
        /// The CPU-only counterpart to <see cref="LoadFileFromMemory"/>: geometry and material colour, no
        /// GPU device touched, for a scene that already lives in memory rather than on disk.
        /// <para>
        /// This is what makes a zip-packed asset -- most of the character and prop library ships this way --
        /// previewable at all. <see cref="LoadFirstMeshCpuOnly"/> resolves a texture reference against a
        /// directory on disk, which a zip entry does not have; <paramref name="externalAssetResolver"/> is
        /// asked for the texture's bytes directly instead, the same contract <see cref="LoadFileFromMemory"/>
        /// uses for the full GPU-hooked import.
        /// </para>
        /// </summary>
        public unsafe ManaMesh LoadFirstMeshCpuOnlyFromMemory(
            ReadOnlySpan<byte> sceneBytes,
            string virtualFileName,
            Func<string, byte[]?>? externalAssetResolver = null)
        {
            _AssetImporter = Assimp.GetApi();

            var extensionHint = Path.GetExtension(virtualFileName);
            if (!string.IsNullOrWhiteSpace(extensionHint) && extensionHint.StartsWith('.'))
            {
                extensionHint = extensionHint[1..];
            }

            Scene* scene;
            fixed (byte* sceneBuffer = sceneBytes)
            {
                scene = _AssetImporter.ImportFileFromMemory(sceneBuffer, (uint)sceneBytes.Length, (uint)PostProcessSteps.Triangulate, extensionHint);
            }

            ActivePath = string.Empty;

            if (scene == null || scene->MFlags == Silk.NET.Assimp.Assimp.SceneFlagsIncomplete || scene->MRootNode == null || scene->MNumMeshes == 0)
            {
                var error = _AssetImporter.GetErrorStringS();
                throw new Exception(string.IsNullOrWhiteSpace(error)
                    ? $"Could not load a mesh from '{virtualFileName}'."
                    : error);
            }

            return BuildCpuOnlyMesh(scene, Path.GetFileNameWithoutExtension(virtualFileName), externalAssetResolver);
        }

        /// <summary>
        /// Builds a preview mesh from every mesh in the scene, not just the first.
        /// <para>
        /// A rigged Kenney character is not one skinned mesh -- it is a handful of separate rigid ones,
        /// <c>leg-left</c>, <c>arm-right</c>, <c>head</c>, <c>torso</c>, each parented to its own joint node
        /// for the animation to drive. Reading only <c>scene-&gt;MMeshes[0]</c> got whichever part happened to
        /// come first in the file, which for these packs is a single small box (a leg, or an arm) -- the mesh
        /// picker showed a box instead of a character. Concatenating every part is what actually shows the
        /// whole silhouette.
        /// </para>
        /// <para>
        /// Node transforms are deliberately not composed in: these kits model every part already sitting at
        /// its resting position (the joints exist for runtime animation, not to place the part at rest), so
        /// the raw per-mesh vertex data already assembles into the right shape. A model that instead relies on
        /// its node transforms to position parts would combine wrong here -- acceptable for a preview
        /// thumbnail, not for anything that has to be geometrically exact.
        /// </para>
        /// </summary>
        private unsafe ManaMesh BuildCpuOnlyMesh(Scene* scene, string fallbackName, Func<string, byte[]?>? externalAssetResolver)
        {
            var firstMesh = scene->MMeshes[0];
            var output = new ManaMesh(Context)
            {
                Name = string.IsNullOrWhiteSpace(firstMesh->MName) ? fallbackName : firstMesh->MName
            };

            var totalVertices = 0u;
            var totalFaces = 0u;
            var hasNormals = true;
            var hasColors = true;
            var hasUv0 = true;
            var hasUv1 = true;

            for (var mi = 0; mi < scene->MNumMeshes; mi++)
            {
                var part = scene->MMeshes[mi];
                totalVertices += part->MNumVertices;
                totalFaces += part->MNumFaces;
                hasNormals &= part->MNormals != null;
                hasColors &= part->MColors.Element0 != null;
                hasUv0 &= part->MTextureCoords.Element0 != null;
                hasUv1 &= part->MTextureCoords.Element1 != null;
            }

            var positions = new Vector3[totalVertices];
            var normals = hasNormals ? new Vector3[totalVertices] : Array.Empty<Vector3>();
            var colors = hasColors ? new Vector4[totalVertices] : Array.Empty<Vector4>();
            var textureCoordinates0 = hasUv0 ? new Vector2[totalVertices] : Array.Empty<Vector2>();
            var textureCoordinates1 = hasUv1 ? new Vector2[totalVertices] : Array.Empty<Vector2>();
            var indexes = new List<uint>((int)(totalFaces * 3));

            var minSize = Vector3.One * float.MaxValue;
            var maxSize = Vector3.One * float.MinValue;
            var vertexOffset = 0u;

            for (var mi = 0; mi < scene->MNumMeshes; mi++)
            {
                var part = scene->MMeshes[mi];

                for (var i = 0u; i < part->MNumVertices; i++)
                {
                    var position = new Vector3(part->MVertices[i].X, part->MVertices[i].Y, part->MVertices[i].Z);
                    positions[vertexOffset + i] = position;
                    minSize = Vector3.Min(minSize, position);
                    maxSize = Vector3.Max(maxSize, position);

                    if (hasNormals)
                    {
                        normals[vertexOffset + i] = new Vector3(part->MNormals[i].X, part->MNormals[i].Y, part->MNormals[i].Z).Normalized();
                    }

                    if (hasColors)
                    {
                        var c = part->MColors.Element0[i];
                        colors[vertexOffset + i] = new Vector4(c.X, c.Y, c.Z, c.W);
                    }

                    if (hasUv0)
                    {
                        var uv = part->MTextureCoords.Element0[i];
                        textureCoordinates0[vertexOffset + i] = new Vector2(uv.X, uv.Y);
                    }

                    if (hasUv1)
                    {
                        var uv = part->MTextureCoords.Element1[i];
                        textureCoordinates1[vertexOffset + i] = new Vector2(uv.X, uv.Y);
                    }
                }

                for (var i = 0u; i < part->MNumFaces; i++)
                {
                    var face = part->MFaces[i];
                    for (var j = 0u; j < face.MNumIndices; j++)
                    {
                        indexes.Add(face.MIndices[j] + vertexOffset);
                    }
                }

                vertexOffset += part->MNumVertices;
            }

            output.SetSize(minSize, maxSize);

            // The first part's material stands in for the whole preview -- ManaMeshCpuData carries one
            // albedo, and a combined multi-part character has no single correct answer for which of its
            // several materials that should be. Good enough for a preview; wrong for anything that needs
            // every part in its own real colour.
            string? albedoTexturePath = null;
            byte[]? albedoTextureBytes = null;
            Vector4? albedoFactor = null;
            if (firstMesh->MMaterialIndex < scene->MNumMaterials)
            {
                ReadCpuOnlyMaterial(
                    scene->MMaterials[firstMesh->MMaterialIndex],
                    externalAssetResolver,
                    ref albedoTexturePath,
                    ref albedoTextureBytes,
                    ref albedoFactor);
            }

            output.SetCpuData(new ManaMeshCpuData
            {
                Indexes = indexes.ToArray(),
                Positions = positions,
                Normals = normals,
                Colors = colors,
                TextureCoordinates0 = textureCoordinates0,
                TextureCoordinates1 = textureCoordinates1,
                AlbedoTexturePath = albedoTexturePath,
                AlbedoTextureBytes = albedoTextureBytes,
                AlbedoFactor = albedoFactor
            });

            return output;
        }

        /// <summary>
        /// Reads a material's base colour without touching the GPU or <see cref="TextureManager"/> -- this
        /// is the CPU-only path, used only for a preview thumbnail.
        /// <para>
        /// <paramref name="externalAssetResolver"/>, when given, means the scene came from memory (a zip
        /// entry) and a texture reference has to be resolved as bytes rather than a file on disk; when null,
        /// a texture on disk beside the scene file is read directly. Either way the caller ends up with the
        /// texture's raw (still-encoded) bytes -- decoding is left to whoever actually needs pixels.
        /// </para>
        /// </summary>
        private unsafe void ReadCpuOnlyMaterial(
            Material* material,
            Func<string, byte[]?>? externalAssetResolver,
            ref string? albedoTexturePath,
            ref byte[]? albedoTextureBytes,
            ref Vector4? albedoFactor)
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
                            if (externalAssetResolver != null)
                            {
                                albedoTextureBytes = externalAssetResolver(propertyValue);
                            }
                            else
                            {
                                string fullPath = Path.Combine(ActivePath, propertyValue);
                                if (System.IO.File.Exists(fullPath))
                                {
                                    albedoTexturePath = fullPath;
                                    try
                                    {
                                        albedoTextureBytes = System.IO.File.ReadAllBytes(fullPath);
                                    }
                                    catch (IOException)
                                    {
                                        // A texture that fails to read only costs the preview its colour,
                                        // not the mesh itself.
                                    }
                                }
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

            // A file with skins or clips gets a ManaSkeletalActorNode root, which owns the per-frame
            // pose/propagate/skin ordering. Everything else keeps the plain node it always had.
            var isRigged = _meshSkinSources.Count > 0 || AnimationClips.Count > 0;
            var rootSceneNode = LoadSceneNode(root, isRigged);
            manaScene.Children.Add(rootSceneNode);

            if (rootSceneNode is ManaSkeletalActorNode actor)
            {
                BindSkeleton(actor);
            }

            return rootSceneNode;
        }

        private unsafe ManaSceneNode LoadSceneNode(Node* node, bool asSkeletalActor = false)
        {
            var local = node->MTransformation;
            local = Matrix4x4.Transpose(local);
            var metaSceneNode = asSkeletalActor
                ? SkeletalActorFactory?.Invoke(Context, local) ?? new ManaSkeletalActorNode(Context, local)
                : new ManaSceneNode(Context, local);
            metaSceneNode.Name = node->MName;

            // Bones are matched to nodes by name (Assimp's aiBone carries only a name), so the lookup
            // has to be built as the tree is. First name wins if a file has duplicates.
            if (!string.IsNullOrEmpty(metaSceneNode.Name))
            {
                _nodesByName.TryAdd(metaSceneNode.Name, metaSceneNode);
            }

            if (node->MNumMeshes != 0)
            {
                for (int i = 0; i < node->MNumMeshes; i++)
                {
                    int meshIndex = (int)node->MMeshes[i];
                    ManaMesh foundMesh = Meshes[meshIndex];
                    if (foundMesh != null)
                    {
                        metaSceneNode.Components.Add(new MeshRendererComponent(metaSceneNode, foundMesh) { });

                        if (_meshSkinSources.ContainsKey(meshIndex))
                        {
                            _skinnedMeshPlacements.Add((metaSceneNode, foundMesh, meshIndex));
                        }
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

        // Assimp gives one aiBone list per mesh with a weight list per bone; skinning wants the
        // transpose (a fixed-width influence table per vertex), so this inverts it here rather than
        // making every frame of skinning pay for the indirection.
        private unsafe void CaptureMeshSkinSource(int meshIndex, Mesh* mesh)
        {
            if (mesh->MNumBones == 0 || mesh->MNumVertices == 0)
            {
                return;
            }

            var vertexCount = (int)mesh->MNumVertices;
            var boneCount = (int)mesh->MNumBones;
            var boneNames = new string[boneCount];
            var offsetMatrices = new Matrix4x4[boneCount];

            var influences = ManaSkinBinding.InfluencesPerVertex;
            var boneIndices = new int[vertexCount * influences];
            var boneWeights = new float[vertexCount * influences];
            Array.Fill(boneIndices, -1);

            var droppedInfluences = 0;
            for (var bone = 0; bone < boneCount; bone++)
            {
                var sourceBone = mesh->MBones[bone];
                boneNames[bone] = sourceBone->MName;
                offsetMatrices[bone] = Matrix4x4.Transpose(sourceBone->MOffsetMatrix);

                for (var weight = 0; weight < sourceBone->MNumWeights; weight++)
                {
                    var vertexWeight = sourceBone->MWeights[weight];
                    var vertex = (int)vertexWeight.MVertexId;
                    if (vertex < 0 || vertex >= vertexCount || vertexWeight.MWeight <= 0.0f)
                    {
                        continue;
                    }

                    var slot = -1;
                    for (var candidate = 0; candidate < influences; candidate++)
                    {
                        if (boneIndices[(vertex * influences) + candidate] < 0)
                        {
                            slot = candidate;
                            break;
                        }
                    }

                    if (slot < 0)
                    {
                        droppedInfluences++;
                        continue;
                    }

                    boneIndices[(vertex * influences) + slot] = bone;
                    boneWeights[(vertex * influences) + slot] = vertexWeight.MWeight;
                }
            }

            if (droppedInfluences > 0)
            {
                Trace.TraceWarning(
                    $"Mesh '{mesh->MName}' has vertices with more than {influences} bone influences; " +
                    $"{droppedInfluences} were dropped.");
            }

            var bindPositions = new Vector3[vertexCount];
            var bindNormals = mesh->MNormals != null ? new Vector3[vertexCount] : Array.Empty<Vector3>();
            for (var i = 0; i < vertexCount; i++)
            {
                bindPositions[i] = new Vector3(mesh->MVertices[i].X, mesh->MVertices[i].Y, mesh->MVertices[i].Z);
                if (mesh->MNormals != null)
                {
                    bindNormals[i] = new Vector3(mesh->MNormals[i].X, mesh->MNormals[i].Y, mesh->MNormals[i].Z).Normalized();
                }
            }

            _meshSkinSources[meshIndex] = new ManaMeshSkinSource
            {
                BoneNames = boneNames,
                OffsetMatrices = offsetMatrices,
                BoneIndices = boneIndices,
                BoneWeights = boneWeights,
                BindPositions = bindPositions,
                BindNormals = bindNormals
            };
        }

        private unsafe void LoadAnimationClips(Scene* scene)
        {
            for (var i = 0; i < scene->MNumAnimations; i++)
            {
                var animation = scene->MAnimations[i];
                var channels = new List<ManaAnimationChannel>((int)animation->MNumChannels);

                for (var channelIndex = 0; channelIndex < animation->MNumChannels; channelIndex++)
                {
                    var nodeAnimation = animation->MChannels[channelIndex];

                    var positionKeys = new ManaVectorKey[nodeAnimation->MNumPositionKeys];
                    for (var key = 0; key < nodeAnimation->MNumPositionKeys; key++)
                    {
                        var source = nodeAnimation->MPositionKeys[key];
                        positionKeys[key] = new ManaVectorKey(
                            (float)source.MTime,
                            new Vector3(source.MValue.X, source.MValue.Y, source.MValue.Z));
                    }

                    var scaleKeys = new ManaVectorKey[nodeAnimation->MNumScalingKeys];
                    for (var key = 0; key < nodeAnimation->MNumScalingKeys; key++)
                    {
                        var source = nodeAnimation->MScalingKeys[key];
                        scaleKeys[key] = new ManaVectorKey(
                            (float)source.MTime,
                            new Vector3(source.MValue.X, source.MValue.Y, source.MValue.Z));
                    }

                    var rotationKeys = new ManaQuaternionKey[nodeAnimation->MNumRotationKeys];
                    for (var key = 0; key < nodeAnimation->MNumRotationKeys; key++)
                    {
                        var source = nodeAnimation->MRotationKeys[key];
                        rotationKeys[key] = new ManaQuaternionKey(
                            (float)source.MTime,
                            new Quaternion(source.MValue.X, source.MValue.Y, source.MValue.Z, source.MValue.W));
                    }

                    channels.Add(new ManaAnimationChannel
                    {
                        NodeName = nodeAnimation->MNodeName,
                        PositionKeys = positionKeys,
                        RotationKeys = rotationKeys,
                        ScaleKeys = scaleKeys
                    });
                }

                var clipName = animation->MName.ToString();
                AnimationClips.Add(new ManaAnimationClip
                {
                    Name = string.IsNullOrWhiteSpace(clipName) ? $"Clip {i}" : clipName,
                    DurationInTicks = animation->MDuration,
                    // Assimp leaves this at 0 when the format doesn't state one; glTF times are already
                    // in seconds, so 1 tick per second is the right fallback for this importer.
                    TicksPerSecond = animation->MTicksPerSecond > 0.0 ? animation->MTicksPerSecond : 1.0,
                    Channels = channels
                });
            }
        }

        // Resolves bone names to the nodes built by LoadSceneNode, then attaches one
        // ManaSkinnedMeshComponent per skinned mesh placement.
        private void BindSkeleton(ManaSkeletalActorNode actor)
        {
            // The actor's animation component was built with an empty subtree (it is created in the
            // node constructor), so the rest pose and node lookup only become real once the tree is.
            actor.Animation.RefreshBindPose();
            actor.Animation.AddClips(AnimationClips);

            if (_skinnedMeshPlacements.Count == 0)
            {
                return;
            }

            var skeleton = new ManaSkeleton(Context) { Name = $"{actor.Name} Skeleton" };
            actor.Skeleton = skeleton;

            foreach (var placement in _skinnedMeshPlacements)
            {
                if (!_meshSkinSources.TryGetValue(placement.MeshIndex, out var skinSource))
                {
                    continue;
                }

                // Per-mesh bone ordering is remapped onto the skeleton's shared ordering, so two meshes
                // over the same joints (a Kenney character's body and head) share one bone list.
                var remappedIndices = new int[skinSource.BoneIndices.Length];
                var localToSkeleton = new int[skinSource.BoneNames.Length];
                var missingBones = 0;

                for (var bone = 0; bone < skinSource.BoneNames.Length; bone++)
                {
                    var boneName = skinSource.BoneNames[bone];
                    if (skeleton.TryGetBoneIndex(boneName, out var existingIndex))
                    {
                        localToSkeleton[bone] = existingIndex;
                        continue;
                    }

                    if (!_nodesByName.TryGetValue(boneName, out var boneNode))
                    {
                        localToSkeleton[bone] = -1;
                        missingBones++;
                        continue;
                    }

                    if (!Matrix4x4.Decompose(boneNode.LocalTransform, out var bindScale, out var bindRotation, out var bindPosition))
                    {
                        bindScale = Vector3.One;
                        bindRotation = Quaternion.Identity;
                        bindPosition = boneNode.LocalTransform.Translation;
                    }

                    localToSkeleton[bone] = skeleton.AddBone(new ManaBone
                    {
                        Name = boneName,
                        Node = boneNode,
                        OffsetMatrix = skinSource.OffsetMatrices[bone],
                        BindPosition = bindPosition,
                        BindRotation = bindRotation,
                        BindScale = bindScale
                    });
                }

                if (missingBones > 0)
                {
                    Trace.TraceWarning($"Skinned mesh '{placement.Mesh.Name}' references {missingBones} bone(s) with no matching scene node.");
                }

                for (var i = 0; i < skinSource.BoneIndices.Length; i++)
                {
                    var localIndex = skinSource.BoneIndices[i];
                    remappedIndices[i] = localIndex < 0 ? -1 : localToSkeleton[localIndex];
                }

                var binding = new ManaSkinBinding
                {
                    Skeleton = skeleton,
                    BindPositions = skinSource.BindPositions,
                    BindNormals = skinSource.BindNormals,
                    BoneIndices = remappedIndices,
                    BoneWeights = skinSource.BoneWeights
                };

                var skinnedMesh = new ManaSkinnedMeshComponent(placement.Node, placement.Mesh, binding);
                placement.Node.Components.Add(skinnedMesh);
                actor.RegisterSkinnedMesh(skinnedMesh);
            }

            if (AnimationClips.Count > 0)
            {
                actor.Animation.Play(AnimationClips.FirstOrDefault(clip =>
                    string.Equals(clip.Name, "idle", StringComparison.OrdinalIgnoreCase)) ?? AnimationClips[0]);
            }
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
                        if (TryLoadEmbeddedTexture(context, filePath, out var embeddedTexture))
                        {
                            metaChannel.Texture = embeddedTexture;
                            textureNames.Add(filePath);

                            switch (type)
                            {
                                case TextureType.Diffuse: { outputMaterial.Albedo.TextureValue = embeddedTexture; } break;
                                case TextureType.BaseColor: { outputMaterial.Albedo.TextureValue = embeddedTexture; } break;
                                case TextureType.Metalness: { outputMaterial.Metallic.TextureValue = embeddedTexture; } break;
                                case TextureType.DiffuseRoughness: { outputMaterial.Roughness.TextureValue = embeddedTexture; } break;
                                case TextureType.Ambient: { outputMaterial.Occlusion.TextureValue = embeddedTexture; } break;
                                case TextureType.AmbientOcclusion: { outputMaterial.Occlusion.TextureValue = embeddedTexture; } break;
                                // glTF's occlusion map arrives as a lightmap, packed with roughness and
                                // metallic in one texture. Nothing read it, so occlusion was always 1.
                                case TextureType.Lightmap: { outputMaterial.Occlusion.TextureValue = embeddedTexture; } break;
                                case TextureType.Emissive: { outputMaterial.Emission.TextureValue = embeddedTexture; } break;
                                case TextureType.EmissionColor: { outputMaterial.Emission.TextureValue = embeddedTexture; } break;
                                case TextureType.Normals: { outputMaterial.Normal.TextureValue = embeddedTexture; outputMaterial.NormalSelector = 0.0f; } break;
                                case TextureType.NormalCamera: { outputMaterial.Normal.TextureValue = embeddedTexture; outputMaterial.NormalSelector = 0.0f; } break;
                            }
                        }
                        else if (TryLoadResolvedExternalTexture(context, filePath, out var resolvedTexture))
                        {
                            metaChannel.Texture = resolvedTexture;
                            textureNames.Add(filePath);

                            switch (type)
                            {
                                case TextureType.Diffuse: { outputMaterial.Albedo.TextureValue = resolvedTexture; } break;
                                case TextureType.BaseColor: { outputMaterial.Albedo.TextureValue = resolvedTexture; } break;
                                case TextureType.Metalness: { outputMaterial.Metallic.TextureValue = resolvedTexture; } break;
                                case TextureType.DiffuseRoughness: { outputMaterial.Roughness.TextureValue = resolvedTexture; } break;
                                case TextureType.Ambient: { outputMaterial.Occlusion.TextureValue = resolvedTexture; } break;
                                case TextureType.AmbientOcclusion: { outputMaterial.Occlusion.TextureValue = resolvedTexture; } break;
                                case TextureType.Lightmap: { outputMaterial.Occlusion.TextureValue = resolvedTexture; } break;
                                case TextureType.Emissive: { outputMaterial.Emission.TextureValue = resolvedTexture; } break;
                                case TextureType.EmissionColor: { outputMaterial.Emission.TextureValue = resolvedTexture; } break;
                                case TextureType.Normals: { outputMaterial.Normal.TextureValue = resolvedTexture; outputMaterial.NormalSelector = 0.0f; } break;
                                case TextureType.NormalCamera: { outputMaterial.Normal.TextureValue = resolvedTexture; outputMaterial.NormalSelector = 0.0f; } break;
                            }
                        }
                        else
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
                                    case TextureType.Ambient: { outputMaterial.Occlusion.TextureValue = channelTexture; } break;
                                    case TextureType.AmbientOcclusion: { outputMaterial.Occlusion.TextureValue = channelTexture; } break;
                                    case TextureType.Lightmap: { outputMaterial.Occlusion.TextureValue = channelTexture; } break;
                                    case TextureType.Emissive: { outputMaterial.Emission.TextureValue = channelTexture; } break;
                                    case TextureType.EmissionColor: { outputMaterial.Emission.TextureValue = channelTexture; } break;
                                    case TextureType.Normals: { outputMaterial.Normal.TextureValue = channelTexture; outputMaterial.NormalSelector = 0.0f; } break;
                                    case TextureType.NormalCamera: { outputMaterial.Normal.TextureValue = channelTexture; outputMaterial.NormalSelector = 0.0f; } break;
                                }
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

                        // glTF carries transparency on the material rather than in a separate map: the
                        // alpha lives in the base colour texture, and alphaMode says what to do with it.
                        // Without this the FlightHelmet's lenses render as solid plastic -- the alpha is
                        // sampled and then thrown away, because an opaque material discards it.
                        case "$mat.opacity": outputMaterial.Opacity.Value = metaChannel.ScalarValue; break;
                        case "$mat.gltf.alphaCutoff": outputMaterial.AlphaCutoff = metaChannel.ScalarValue; break;
                        case "$mat.gltf.alphaMode":
                            outputMaterial.OpacityMode = metaChannel.Value switch
                            {
                                "OPAQUE" => ManaOpacityMode.Opaque,
                                "MASK" => ManaOpacityMode.Cutout,
                                "BLEND" => ManaOpacityMode.Blend,
                                _ => ManaOpacityMode.Auto
                            };
                            break;

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

        private unsafe bool TryLoadEmbeddedTexture(ManaContext context, string textureReference, out ManaTexture texture)
        {
            texture = null!;
            if (_activeScene == null || string.IsNullOrWhiteSpace(textureReference))
            {
                return false;
            }

            if (!textureReference.StartsWith("*", StringComparison.Ordinal))
            {
                return false;
            }

            if (!int.TryParse(textureReference[1..], out var embeddedIndex) ||
                embeddedIndex < 0 ||
                embeddedIndex >= _activeScene->MNumTextures)
            {
                return false;
            }

            var embedded = _activeScene->MTextures[embeddedIndex];
            if (embedded == null || embedded->PcData == null)
            {
                return false;
            }

            // Assimp contract: MHeight == 0 means compressed blob, MWidth is byte length.
            if (embedded->MHeight != 0)
            {
                return false;
            }

            var byteCount = checked((int)embedded->MWidth);
            if (byteCount <= 0)
            {
                return false;
            }

            var bytes = new byte[byteCount];
            Marshal.Copy((IntPtr)embedded->PcData, bytes, 0, byteCount);

            var textureKey = $"embedded:{textureReference}:{byteCount}";
            texture = TextureManager.GetInstance(context).CreateTexture(textureKey);
            texture.Name = textureReference;
            texture.PixelStream = new MemoryStream(bytes, writable: false);
            if (context.OpenGl != null && !texture.IsLoaded)
            {
                texture.Load();
            }

            return true;
        }

        private bool TryLoadResolvedExternalTexture(ManaContext context, string textureReference, out ManaTexture texture)
        {
            texture = null!;
            if (_externalAssetResolver == null || string.IsNullOrWhiteSpace(textureReference))
            {
                return false;
            }

            var bytes = _externalAssetResolver(textureReference);
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            var key = $"resolved:{textureReference}:{bytes.Length}";
            texture = TextureManager.GetInstance(context).CreateTexture(key);
            texture.Name = textureReference;
            texture.PixelStream = new MemoryStream(bytes, writable: false);
            if (context.OpenGl != null && !texture.IsLoaded)
            {
                texture.Load();
            }

            return true;
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
            if (!string.IsNullOrWhiteSpace(mesh->MName))
            {
                output.Name = mesh->MName;
            }

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
                AlbedoTextureBytes = GetMaterialTextureBytes(foundMaterial, TextureType.BaseColor, TextureType.Diffuse),
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

        private static byte[]? GetMaterialTextureBytes(ManaMaterial? material, params TextureType[] textureTypes)
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
                var stream = match?.Texture?.PixelStream;
                if (stream == null)
                {
                    continue;
                }

                var originalPosition = stream.CanSeek ? stream.Position : 0L;
                try
                {
                    if (stream.CanSeek)
                    {
                        stream.Position = 0;
                    }

                    using var copy = new MemoryStream();
                    stream.CopyTo(copy);
                    var bytes = copy.ToArray();
                    if (bytes.Length > 0)
                    {
                        return bytes;
                    }
                }
                finally
                {
                    if (stream.CanSeek)
                    {
                        stream.Position = originalPosition;
                    }
                }
            }

            return null;
        }
    }
}

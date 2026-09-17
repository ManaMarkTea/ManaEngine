using Mana.Code.Animation;
using Mana.Code.Nodes;
#if !MANA_BROWSER
using Mana.Code.Graphics;
#endif
using Mana.Code.ManaShaders;
using Mana.Code.Materials;
using ManaCore.Graphics;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Mana.Code.Shapes.Loaders
{
    // Pure-managed counterpart to ManaGltfLoader for targets with no native Assimp build
    // (browser-wasm; see WebPort-Plan.md M4). Reads glTF/GLB only, via SharpGLTF, matching the
    // same public surface and producing the same Mana data (ManaMesh/ManaMaterial/ManaScene/
    // skeleton/animation) so callers -- ManaWorld.LoadFromGltf(Memory), CharacterSpawnService,
    // VoxelFileImportService -- don't need to know which backend loaded a file.
    //
    // Verified against Mana/Assets/Models/FlightHelmet/glTF/FlightHelmet.gltf for the static
    // mesh + material path. The skinning/animation path below is implemented against SharpGLTF's
    // documented Skin/Animation API but has not been exercised against a live rigged asset --
    // worth a dedicated check before relying on it for a real character.
    public class ManaSharpGltfLoader : ManaObject
    {
        public string ActivePath { get; set; } = string.Empty;

        public List<ManaMesh> Meshes { get; set; }
        public List<ManaMaterial> Materials { get; set; }
        public List<ManaScene> Scenes { get; set; }
        public List<ManaAnimationClip> AnimationClips { get; } = new();
        public Func<ManaContext, Matrix4x4, ManaSkeletalActorNode>? SkeletalActorFactory { get; set; }

        private ModelRoot _model = null!;
        private readonly Dictionary<Node, ManaSceneNode> _nodeMap = new();
        private readonly Dictionary<Node, ManaMesh[]> _meshesByNode = new();
        private readonly List<(ManaSceneNode Node, ManaMesh Mesh, Skin Skin)> _skinnedMeshPlacements = new();
        private Func<string, byte[]?>? _externalAssetResolver;

        public ManaSharpGltfLoader(ManaContext context) : base(context)
        {
            Meshes = new();
            Materials = new();
            Scenes = new();
        }

        public ManaSceneNode LoadFile(string sceneFile)
        {
            // Asked before the loader, so the reason is the real one. SharpGLTF meets a required extension it
            // does not implement and reports the file as malformed, which sends you looking for a corrupt
            // download instead of a re-export.
            if (ManaGltfSupport.DescribeUnsupported(sceneFile) is { } unsupported)
            {
                throw new NotSupportedException(
                    $"'{System.IO.Path.GetFileName(sceneFile)}' cannot be loaded because {unsupported}");
            }

            _model = ModelRoot.Load(sceneFile);
            ActivePath = System.IO.Path.GetDirectoryName(sceneFile) ?? string.Empty;
            _externalAssetResolver = null;
            return LoadModel(System.IO.Path.GetFileNameWithoutExtension(sceneFile));
        }

        public ManaSceneNode LoadFileFromMemory(
            ReadOnlySpan<byte> sceneBytes,
            string virtualFileName,
            Func<string, byte[]?>? externalAssetResolver = null)
        {
            ActivePath = string.Empty;
            _externalAssetResolver = externalAssetResolver;

            var bytes = sceneBytes.ToArray();
            var readContext = ReadContext.Create(assetName =>
            {
                var resolved = externalAssetResolver?.Invoke(assetName);
                if (resolved == null)
                {
                    throw new System.IO.FileNotFoundException($"'{assetName}' was not supplied by the resolver.");
                }
                return new ArraySegment<byte>(resolved);
            });

            using var stream = new System.IO.MemoryStream(bytes);

            if (ManaGltfSupport.DescribeUnsupported(stream) is { } unsupported)
            {
                throw new NotSupportedException(
                    $"'{virtualFileName}' cannot be loaded because {unsupported}");
            }

            stream.Position = 0;
            _model = readContext.ReadSchema2(stream);

            return LoadModel(System.IO.Path.GetFileNameWithoutExtension(virtualFileName));
        }

        private ManaSceneNode LoadModel(string sceneName)
        {
            _nodeMap.Clear();
            _meshesByNode.Clear();
            _skinnedMeshPlacements.Clear();

            var manaScene = new ManaScene(Context) { Name = sceneName };

            foreach (var material in _model.LogicalMaterials)
            {
                Materials.Add(LoadMaterial(material));
            }

            foreach (var mesh in _model.LogicalMeshes)
            {
                Meshes.Add(LoadMesh(mesh));
            }

            LoadAnimationClips();

            var defaultScene = _model.DefaultScene ?? _model.LogicalScenes.FirstOrDefault();
            var rootNodes = defaultScene?.VisualChildren.ToArray() ?? Array.Empty<Node>();

            var isRigged = _model.LogicalSkins.Count > 0 || _model.LogicalAnimations.Count > 0;

            ManaSceneNode rootSceneNode;
            if (rootNodes.Length == 1)
            {
                rootSceneNode = LoadSceneNode(rootNodes[0], isRigged);
            }
            else
            {
                // Multiple root nodes (rare, but valid glTF): wrap them under one synthetic root so
                // the caller still gets a single ManaSceneNode back, matching ManaGltfLoader's
                // single-root contract (Assimp's aiScene always has exactly one root node).
                rootSceneNode = isRigged
                    ? (SkeletalActorFactory?.Invoke(Context, Matrix4x4.Identity) ?? new ManaSkeletalActorNode(Context, Matrix4x4.Identity))
                    : new ManaSceneNode(Context, Matrix4x4.Identity);
                rootSceneNode.Name = sceneName;
                foreach (var root in rootNodes)
                {
                    rootSceneNode.AddChild(LoadSceneNode(root));
                }
            }

            manaScene.Children.Add(rootSceneNode);

            if (rootSceneNode is ManaSkeletalActorNode actor)
            {
                BindSkeleton(actor);
            }

            Scenes.Add(manaScene);
            return rootSceneNode;
        }

        private ManaSceneNode LoadSceneNode(Node node, bool asSkeletalActor = false)
        {
            var local = node.LocalMatrix;
            var metaSceneNode = asSkeletalActor
                ? SkeletalActorFactory?.Invoke(Context, local) ?? new ManaSkeletalActorNode(Context, local)
                : new ManaSceneNode(Context, local);
            metaSceneNode.Name = node.Name ?? string.Empty;
            _nodeMap[node] = metaSceneNode;

            if (node.Mesh != null)
            {
                var meshIndex = node.Mesh.LogicalIndex;
                var foundMesh = Meshes[meshIndex];
                metaSceneNode.Components.Add(new MeshRendererComponent(metaSceneNode, foundMesh));

                if (node.Skin != null)
                {
                    _skinnedMeshPlacements.Add((metaSceneNode, foundMesh, node.Skin));
                }
            }

            foreach (var child in node.VisualChildren)
            {
                metaSceneNode.AddChild(LoadSceneNode(child));
            }

            return metaSceneNode;
        }

        private ManaMesh LoadMesh(SharpGLTF.Schema2.Mesh mesh)
        {
            var output = new ManaMesh(Context) { Name = mesh.Name ?? string.Empty };

            // A glTF mesh may have multiple primitives (one per material); the engine's ManaMesh is
            // one draw with one material list, so primitives are concatenated the same way
            // ManaGltfLoader's CPU-only preview path concatenates Assimp's per-part meshes.
            var indexes = new List<uint>();
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Vector4>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var boneIndices = new List<int>();
            var boneWeights = new List<float>();

            var minSize = Vector3.One * float.MaxValue;
            var maxSize = Vector3.One * float.MinValue;
            uint vertexOffset = 0;
            ManaMaterial? firstMaterial = null;

            foreach (var primitive in mesh.Primitives)
            {
                var posAccessor = primitive.VertexAccessors.GetValueOrDefault("POSITION");
                if (posAccessor == null)
                {
                    continue;
                }

                var posArray = posAccessor.AsVector3Array();
                var normalArray = primitive.VertexAccessors.GetValueOrDefault("NORMAL")?.AsVector3Array();
                var colorArray = primitive.VertexAccessors.GetValueOrDefault("COLOR_0")?.AsColorArray();
                var uv0Array = primitive.VertexAccessors.GetValueOrDefault("TEXCOORD_0")?.AsVector2Array();
                var uv1Array = primitive.VertexAccessors.GetValueOrDefault("TEXCOORD_1")?.AsVector2Array();
                var jointsArray = primitive.VertexAccessors.GetValueOrDefault("JOINTS_0")?.AsVector4Array();
                var weightsArray = primitive.VertexAccessors.GetValueOrDefault("WEIGHTS_0")?.AsVector4Array();

                var material = primitive.Material != null
                    ? Materials[primitive.Material.LogicalIndex]
                    : null;
                firstMaterial ??= material;

                for (var i = 0; i < posArray.Count; i++)
                {
                    var position = posArray[i];
                    positions.Add(position);
                    minSize = Vector3.Min(minSize, position);
                    maxSize = Vector3.Max(maxSize, position);

                    normals.Add(normalArray != null ? Vector3.Normalize(normalArray[i]) : Vector3.UnitY);
                    colors.Add(colorArray != null ? colorArray[i] : Vector4.One);
                    // V is flipped to match the convention the rest of the engine expects.
                    // glTF puts UV (0,0) at the image's top-left; Assimp -- which the desktop
                    // loader uses -- normalizes to its own bottom-left origin, so every consumer
                    // downstream (notably ManaWebGpuMeshTextureCache, which flips every texture it
                    // uploads to match) is written against the flipped convention. Emitting raw
                    // glTF UVs here instead made characters sample the mirrored row of their
                    // palette atlas and render almost black.
                    uv0.Add(uv0Array != null ? new Vector2(uv0Array[i].X, 1.0f - uv0Array[i].Y) : Vector2.Zero);
                    uv1.Add(uv1Array != null ? new Vector2(uv1Array[i].X, 1.0f - uv1Array[i].Y) : Vector2.Zero);

                    if (jointsArray != null && weightsArray != null)
                    {
                        var joints = jointsArray[i];
                        var weights = weightsArray[i];
                        boneIndices.Add((int)joints.X);
                        boneIndices.Add((int)joints.Y);
                        boneIndices.Add((int)joints.Z);
                        boneIndices.Add((int)joints.W);
                        boneWeights.Add(weights.X);
                        boneWeights.Add(weights.Y);
                        boneWeights.Add(weights.Z);
                        boneWeights.Add(weights.W);
                    }
                }

                if (primitive.IndexAccessor != null)
                {
                    foreach (var index in primitive.IndexAccessor.AsIndicesArray())
                    {
                        indexes.Add(index + vertexOffset);
                    }
                }

                vertexOffset = (uint)positions.Count;
            }

            output.SetSize(minSize, maxSize);

            var meshData = new ManaMeshCpuData
            {
                Indexes = indexes.ToArray(),
                Positions = positions.ToArray(),
                Normals = normals.ToArray(),
                Colors = colors.ToArray(),
                TextureCoordinates0 = uv0.ToArray(),
                TextureCoordinates1 = uv1.ToArray(),
                AlbedoTexturePath = null,
                AlbedoTextureBytes = GetChannelTextureBytes(firstMaterial),
                AlbedoFactor = firstMaterial?.Albedo.Value
            };

            output.SetCpuData(meshData);
#if !MANA_BROWSER
            if (Context.OpenGl != null && !Context.DeferGpuUploads)
            {
                var gpuMesh = Context.RequireOpenGlDevice().CreateMesh(Context, meshData);
                if (gpuMesh is ManaOpenGlMesh openGlMesh)
                {
                    output.VertexBufferObject = openGlMesh.VertexBufferObject;
                }
            }
#endif

            output.Materials = firstMaterial != null ? new[] { firstMaterial } : Array.Empty<ManaMaterial>();

            if (boneIndices.Count > 0)
            {
                _pendingSkinSources[mesh] = new ManaMeshSkinSource
                {
                    BoneIndices = boneIndices.ToArray(),
                    BoneWeights = boneWeights.ToArray(),
                    BindPositions = positions.ToArray(),
                    BindNormals = normals.ToArray()
                };
            }

            return output;
        }

        private sealed class ManaMeshSkinSource
        {
            public required int[] BoneIndices { get; init; }
            public required float[] BoneWeights { get; init; }
            public required Vector3[] BindPositions { get; init; }
            public required Vector3[] BindNormals { get; init; }
        }

        private readonly Dictionary<SharpGLTF.Schema2.Mesh, ManaMeshSkinSource> _pendingSkinSources = new();

        private ManaMaterial LoadMaterial(Material material)
        {
            var outputMaterial = new ManaMaterial(Context)
            {
                Shader = new ManaPbrShader(Context),
                Name = string.IsNullOrWhiteSpace(material.Name) ? "Imported Material" : material.Name
            };

            outputMaterial.OpacityMode = material.Alpha switch
            {
                SharpGLTF.Schema2.AlphaMode.OPAQUE => ManaOpacityMode.Opaque,
                SharpGLTF.Schema2.AlphaMode.MASK => ManaOpacityMode.Cutout,
                SharpGLTF.Schema2.AlphaMode.BLEND => ManaOpacityMode.Blend,
                _ => ManaOpacityMode.Auto
            };
            outputMaterial.AlphaCutoff = material.AlphaCutoff;

            BindColorChannel(material, "BaseColor", outputMaterial.Albedo);
            BindColorChannel(material, "Emissive", outputMaterial.Emission);
            BindScalarChannel(material, "MetallicRoughness", "MetallicFactor", outputMaterial.Metallic, defaultValue: 1f);
            BindScalarChannel(material, "MetallicRoughness", "RoughnessFactor", outputMaterial.Roughness, defaultValue: 1f);
            // glTF packs metallic (B) and roughness (G) into one texture -- the same texture is bound
            // to both slots here, same as Occlusion below when an exporter reuses one packed ORM map
            // for all three (as FlightHelmet does); the shader is what actually knows which channel
            // each slot samples.
            BindScalarTextureOnly(material, "MetallicRoughness", outputMaterial.Metallic);
            BindScalarTextureOnly(material, "MetallicRoughness", outputMaterial.Roughness);
            BindScalarTextureOnly(material, "Occlusion", outputMaterial.Occlusion);

            if (BindScalarTextureOnly(material, "Normal", outputMaterial.Normal))
            {
                outputMaterial.NormalSelector = 0.0f;
            }

            return outputMaterial;
        }

        // BaseColor/Emissive: an RGBA factor plus an optional texture that modulates it.
        private void BindColorChannel(Material material, string channelKey, ManaTextureVector4 target)
        {
            var channel = material.FindChannel(channelKey);
            if (channel == null)
            {
                return;
            }

            if (TryReadColorParameters(channel.Value.Parameters, out var color))
            {
                target.Value = color;
            }

            target.TextureValue = LoadChannelTexture(material, channelKey, channel.Value.Texture);
        }

        /// <summary>
        /// Reads a colour channel's RGBA factor, tolerating both shapes SharpGLTF has used.
        /// <para>
        /// 1.0.x exposes a colour channel as a SINGLE parameter whose value is already a
        /// <see cref="Vector4"/>; older builds exposed three or four separate float scalars. Only
        /// handling the scalar form meant the count check failed silently and the factor was left
        /// at <see cref="ManaMaterial"/>'s default of <c>Colors.Orange</c> -- which the PBR shader
        /// multiplies every albedo sample by, zeroing the blue channel and tinting the entire
        /// scene orange.
        /// </para>
        /// </summary>
        private static bool TryReadColorParameters(
            IReadOnlyList<SharpGLTF.Schema2.IMaterialParameter> parameters,
            out Vector4 color)
        {
            color = Vector4.One;
            if (parameters == null || parameters.Count == 0)
            {
                return false;
            }

            if (parameters[0].Value is Vector4 packed)
            {
                color = packed;
                return true;
            }

            if (parameters.Count >= 3)
            {
                color = new Vector4(
                    Convert.ToSingle(parameters[0].Value),
                    Convert.ToSingle(parameters[1].Value),
                    Convert.ToSingle(parameters[2].Value),
                    parameters.Count > 3 ? Convert.ToSingle(parameters[3].Value) : 1f);
                return true;
            }

            return false;
        }

        // Normal/Occlusion carry a texture only (a strength scalar for Occlusion lives in
        // MetallicRoughness's alpha in the glTF spec, which nothing here reads yet -- same gap
        // ManaGltfLoader has for Assimp's equivalent).
        private bool BindScalarTextureOnly(Material material, string channelKey, ManaTextureVector4 target)
        {
            var channel = material.FindChannel(channelKey);
            var texture = LoadChannelTexture(material, channelKey, channel?.Texture);
            target.TextureValue = texture;
            return texture != null;
        }

        private bool BindScalarTextureOnly(Material material, string channelKey, ManaTextureSingle target)
        {
            var channel = material.FindChannel(channelKey);
            var texture = LoadChannelTexture(material, channelKey, channel?.Texture);
            target.TextureValue = texture;
            return texture != null;
        }

        private static void BindScalarChannel(Material material, string channelKey, string parameterName, ManaTextureSingle target, float defaultValue)
        {
            var channel = material.FindChannel(channelKey);
            if (channel == null)
            {
                return;
            }

            var parameter = channel.Value.Parameters.FirstOrDefault(p => string.Equals(p.Name, parameterName, StringComparison.OrdinalIgnoreCase));
            target.Value = parameter != null ? Convert.ToSingle(parameter.Value) : defaultValue;
        }

        private ManaTexture? LoadChannelTexture(Material material, string channelKey, SharpGLTF.Schema2.Texture? sourceTexture)
        {
            var image = sourceTexture?.PrimaryImage;
            if (image == null)
            {
                return null;
            }

            var bytes = image.Content.Content.ToArray();
            var key = $"gltf:{material.LogicalIndex}:{channelKey}:{bytes.Length}";
            var texture = TextureManager.GetInstance(Context).CreateTexture(key);
            texture.Name = $"{material.Name}_{channelKey}";

            // Never used to actually load the texture -- PixelStream below is set unconditionally, and
            // ManaTexture only falls back to resolving OriginalFilename from disk when PixelStream is
            // null -- but callers elsewhere (packed-texture identity checks, error messages, "reveal in
            // Finder") read it as a human-meaningful name, the same thing the desktop Assimp path filled
            // in from the referenced file's own path. SourcePath is empty for a GLB/embedded image (no
            // separate file exists), where the glTF-authored image name is the best available label.
            texture.OriginalFilename = !string.IsNullOrEmpty(image.Content.SourcePath)
                ? image.Content.SourcePath
                : image.Name ?? string.Empty;

            texture.PixelStream = new System.IO.MemoryStream(bytes, writable: false);
            if (Context.OpenGl != null && !texture.IsLoaded)
            {
                texture.Load();
            }

            return texture;
        }

        private static byte[]? GetChannelTextureBytes(ManaMaterial? material)
        {
            var stream = material?.Albedo.TextureValue?.PixelStream;
            if (stream == null)
            {
                return null;
            }

            var originalPosition = stream.CanSeek ? stream.Position : 0L;
            try
            {
                if (stream.CanSeek)
                {
                    stream.Position = 0;
                }
                using var copy = new System.IO.MemoryStream();
                stream.CopyTo(copy);
                return copy.ToArray();
            }
            finally
            {
                if (stream.CanSeek)
                {
                    stream.Position = originalPosition;
                }
            }
        }

        private void LoadAnimationClips()
        {
            foreach (var animation in _model.LogicalAnimations)
            {
                var channelsByNode = animation.Channels.GroupBy(c => c.TargetNode);
                var channels = new List<ManaAnimationChannel>();

                foreach (var group in channelsByNode)
                {
                    var node = group.Key;
                    if (node == null)
                    {
                        continue;
                    }

                    var positionKeys = Array.Empty<ManaVectorKey>();
                    var rotationKeys = Array.Empty<ManaQuaternionKey>();
                    var scaleKeys = Array.Empty<ManaVectorKey>();

                    foreach (var channel in group)
                    {
                        var translation = channel.GetTranslationSampler();
                        if (translation != null)
                        {
                            positionKeys = translation.GetLinearKeys().Select(k => new ManaVectorKey(k.Item1, k.Item2)).ToArray();
                        }

                        var rotation = channel.GetRotationSampler();
                        if (rotation != null)
                        {
                            rotationKeys = rotation.GetLinearKeys().Select(k => new ManaQuaternionKey(k.Item1, k.Item2)).ToArray();
                        }

                        var scale = channel.GetScaleSampler();
                        if (scale != null)
                        {
                            scaleKeys = scale.GetLinearKeys().Select(k => new ManaVectorKey(k.Item1, k.Item2)).ToArray();
                        }
                    }

                    channels.Add(new ManaAnimationChannel
                    {
                        NodeName = node.Name ?? string.Empty,
                        PositionKeys = positionKeys,
                        RotationKeys = rotationKeys,
                        ScaleKeys = scaleKeys
                    });
                }

                AnimationClips.Add(new ManaAnimationClip
                {
                    Name = string.IsNullOrWhiteSpace(animation.Name) ? $"Clip {AnimationClips.Count}" : animation.Name,
                    // glTF animation times are already in seconds -- 1 tick per second, matching
                    // ManaGltfLoader's fallback for formats (like glTF, via Assimp) that don't state one.
                    DurationInTicks = animation.Duration,
                    TicksPerSecond = 1.0,
                    Channels = channels
                });
            }
        }

        private void BindSkeleton(ManaSkeletalActorNode actor)
        {
            actor.Animation.RefreshBindPose();
            actor.Animation.AddClips(AnimationClips);

            if (_skinnedMeshPlacements.Count == 0)
            {
                return;
            }

            var skeleton = new ManaSkeleton(Context) { Name = $"{actor.Name} Skeleton" };
            actor.Skeleton = skeleton;

            foreach (var (placementNode, placementMesh, skin) in _skinnedMeshPlacements)
            {
                var meshLogical = _model.LogicalMeshes.FirstOrDefault(m => Meshes[m.LogicalIndex] == placementMesh);
                if (meshLogical == null || !_pendingSkinSources.TryGetValue(meshLogical, out var skinSource))
                {
                    continue;
                }

                var localToSkeleton = new int[skin.JointsCount];
                var missingBones = 0;

                for (var i = 0; i < skin.JointsCount; i++)
                {
                    var (jointNode, inverseBind) = skin.GetJoint(i);
                    var boneName = jointNode.Name ?? $"joint_{i}";

                    if (skeleton.TryGetBoneIndex(boneName, out var existingIndex))
                    {
                        localToSkeleton[i] = existingIndex;
                        continue;
                    }

                    if (!_nodeMap.TryGetValue(jointNode, out var boneNode))
                    {
                        localToSkeleton[i] = -1;
                        missingBones++;
                        continue;
                    }

                    if (!Matrix4x4.Decompose(boneNode.LocalTransform, out var bindScale, out var bindRotation, out var bindPosition))
                    {
                        bindScale = Vector3.One;
                        bindRotation = Quaternion.Identity;
                        bindPosition = boneNode.LocalTransform.Translation;
                    }

                    localToSkeleton[i] = skeleton.AddBone(new ManaBone
                    {
                        Name = boneName,
                        Node = boneNode,
                        OffsetMatrix = inverseBind,
                        BindPosition = bindPosition,
                        BindRotation = bindRotation,
                        BindScale = bindScale
                    });
                }

                if (missingBones > 0)
                {
                    System.Diagnostics.Trace.TraceWarning($"Skinned mesh '{placementMesh.Name}' references {missingBones} joint(s) with no matching scene node.");
                }

                var remappedIndices = new int[skinSource.BoneIndices.Length];
                for (var i = 0; i < skinSource.BoneIndices.Length; i++)
                {
                    var localIndex = skinSource.BoneIndices[i];
                    remappedIndices[i] = localIndex < 0 || localIndex >= localToSkeleton.Length ? -1 : localToSkeleton[localIndex];
                }

                var binding = new ManaSkinBinding
                {
                    Skeleton = skeleton,
                    BindPositions = skinSource.BindPositions,
                    BindNormals = skinSource.BindNormals,
                    BoneIndices = remappedIndices,
                    BoneWeights = skinSource.BoneWeights
                };

                var skinnedMesh = new ManaSkinnedMeshComponent(placementNode, placementMesh, binding);
                placementNode.Components.Add(skinnedMesh);
                actor.RegisterSkinnedMesh(skinnedMesh);
            }

            if (AnimationClips.Count > 0)
            {
                actor.Animation.Play(AnimationClips.FirstOrDefault(clip =>
                    string.Equals(clip.Name, "idle", StringComparison.OrdinalIgnoreCase)) ?? AnimationClips[0]);
            }
        }
    }
}

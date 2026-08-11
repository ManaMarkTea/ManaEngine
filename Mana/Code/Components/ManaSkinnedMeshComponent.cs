using Mana.Code.Animation;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace Mana
{
    // CPU skinning: rewrites the mesh's positions and normals from the bind pose every frame the
    // skeleton moves, then bumps ManaMesh.CpuDataVersion so the backends pick the new geometry up.
    // GPU skinning would need joint attributes in the shared vertex layout and a per-instance bone
    // buffer in every pipeline; at Kenney-character vertex counts this costs microseconds, so the
    // simpler path wins until there is a reason it doesn't.
    public class ManaSkinnedMeshComponent : ManaComponent
    {
        private readonly Vector3[] skinnedPositions;
        private readonly Vector3[] skinnedNormals;
        private Matrix4x4[] skinMatrices;
        private Matrix4x4 lastOwnerWorldTransform;
        private bool hasSkinned;

        public ManaMesh Mesh { get; }
        public ManaSkinBinding Binding { get; }
        public bool Enabled { get; set; } = true;

        public ManaSkinnedMeshComponent(ManaSceneNode owner, ManaMesh mesh, ManaSkinBinding binding) : base(owner)
        {
            Mesh = mesh;
            Binding = binding;
            Name = $"Skinned Mesh ({mesh.Name})";

            skinnedPositions = new Vector3[binding.VertexCount];
            skinnedNormals = new Vector3[binding.BindNormals.Length];
            skinMatrices = new Matrix4x4[Math.Max(1, binding.Skeleton.BoneCount)];
        }

        // Expects ManaSkeleton.UpdateBindSpaceToWorld() to have already run for this frame;
        // skeletonPoseChanged is what that call returned. The skinned result also depends on this
        // node's own world transform (see GetSkinMatrices), so that is checked separately -- skipping
        // the rewrite is what keeps a still rig from re-uploading identical vertices every frame.
        public void ApplySkinning(bool skeletonPoseChanged)
        {
            if (!Enabled || Binding.VertexCount == 0)
            {
                return;
            }

            var ownerWorldTransform = Owner.WorldTransform;
            if (hasSkinned && !skeletonPoseChanged && lastOwnerWorldTransform.Equals(ownerWorldTransform))
            {
                return;
            }

            lastOwnerWorldTransform = ownerWorldTransform;
            hasSkinned = true;

            if (skinMatrices.Length < Binding.Skeleton.BoneCount)
            {
                skinMatrices = new Matrix4x4[Binding.Skeleton.BoneCount];
            }

            Binding.Skeleton.GetSkinMatrices(ownerWorldTransform, skinMatrices);

            var hasNormals = skinnedNormals.Length == Binding.VertexCount;
            var boneIndices = Binding.BoneIndices;
            var boneWeights = Binding.BoneWeights;

            // Bound against the skeleton, not the buffer: skinMatrices may be longer than the bone
            // count, and GetSkinMatrices only fills the first BoneCount entries.
            var boneCount = Binding.Skeleton.BoneCount;

            for (var vertex = 0; vertex < Binding.VertexCount; vertex++)
            {
                var position = Vector3.Zero;
                var normal = Vector3.Zero;
                var totalWeight = 0.0f;

                var influenceBase = vertex * ManaSkinBinding.InfluencesPerVertex;
                for (var influence = 0; influence < ManaSkinBinding.InfluencesPerVertex; influence++)
                {
                    var boneIndex = boneIndices[influenceBase + influence];
                    var weight = boneWeights[influenceBase + influence];
                    if (boneIndex < 0 || weight <= 0.0f || boneIndex >= boneCount)
                    {
                        continue;
                    }

                    var skinMatrix = skinMatrices[boneIndex];
                    position += Vector3.Transform(Binding.BindPositions[vertex], skinMatrix) * weight;
                    if (hasNormals)
                    {
                        normal += Vector3.TransformNormal(Binding.BindNormals[vertex], skinMatrix) * weight;
                    }

                    totalWeight += weight;
                }

                // An unweighted vertex belongs to the mesh's own node, not to any bone -- leaving it at
                // the origin would stretch a triangle across the whole model.
                if (totalWeight <= 0.0f)
                {
                    skinnedPositions[vertex] = Binding.BindPositions[vertex];
                    if (hasNormals)
                    {
                        skinnedNormals[vertex] = Binding.BindNormals[vertex];
                    }

                    continue;
                }

                skinnedPositions[vertex] = position / totalWeight;
                if (hasNormals)
                {
                    var scaledNormal = normal / totalWeight;
                    skinnedNormals[vertex] = scaledNormal.LengthSquared() > 1e-12f
                        ? Vector3.Normalize(scaledNormal)
                        : Binding.BindNormals[vertex];
                }
            }

            PublishSkinnedGeometry();
        }

        // Reuses the skinned arrays across frames and only allocates the (small) descriptor, since
        // ManaMeshCpuData is init-only. The backends key off CpuDataVersion, not array identity.
        private void PublishSkinnedGeometry()
        {
            var previous = Mesh.CpuData;
            if (previous == null)
            {
                return;
            }

            var normals = skinnedNormals.Length == Binding.VertexCount ? skinnedNormals : previous.Normals;

            Mesh.SetCpuData(new ManaMeshCpuData
            {
                Indexes = previous.Indexes,
                Positions = skinnedPositions,
                Normals = normals,
                Colors = previous.Colors,
                TextureCoordinates0 = previous.TextureCoordinates0,
                TextureCoordinates1 = previous.TextureCoordinates1,
                AlbedoTexturePath = previous.AlbedoTexturePath,
                AlbedoTextureBytes = previous.AlbedoTextureBytes,
                AlbedoFactor = previous.AlbedoFactor
            });

            // The WebGPU path picks the new geometry up from CpuDataVersion during scene reconcile, but
            // the OpenGL path draws straight from the VBO built at load time -- without this it would
            // render every rig frozen in its bind pose.
            Mesh.VertexBufferObject?.UpdateDynamicData(skinnedPositions, normals, Array.Empty<Vector4>());
        }
    }
}

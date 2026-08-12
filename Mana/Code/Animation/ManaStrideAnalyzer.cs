using Mana.Code.Nodes;

namespace Mana.Code.Animation
{
    public sealed record ManaStrideMeasurement(
        string ClipName,
        float StepLength,
        float CycleSeconds,
        float ReferenceSpeed,
        int FootBoneCount)
    {
        // How far the animation says the character travels in one full cycle.
        public float StrideLength => StepLength * 2.0f;
    }

    // Works out how fast a character has to travel for an in-place locomotion clip not to look like
    // it is sliding on the floor.
    //
    // The exact answer is root motion -- if a clip translates its root bone, the displacement per
    // cycle IS the distance and no estimation is needed. The Kenney mini-character clips do not have
    // it (their root position keys are a purely vertical bob: zero X/Z range, zero net displacement),
    // which is the normal state of affairs for in-place clips, so this falls back to stride analysis.
    //
    // Method: track a foot bone through one cycle in the actor's own space and take its front-to-back
    // travel as the step length. A planted foot is stationary in the world, so the ground has to pass
    // under the character by exactly one step length per step, i.e. two per cycle.
    //
    // Caveat worth knowing before trusting the number to three decimal places: this assumes the foot
    // is planted across its whole backward sweep. Stylised rigs often are not -- the Kenney legs are a
    // single bone pivoting at the hip with no ankle, and the foot is anywhere near the ground for only
    // about a quarter of the cycle. On rigs like that, treat the result as a calibrated starting point
    // to nudge by eye rather than ground truth.
    public static class ManaStrideAnalyzer
    {
        private const int DefaultSampleCount = 120;

        private static readonly string[] DefaultFootBoneHints = { "foot", "toe", "ankle", "leg" };

        // Side-effect free: evaluates the clip into local matrices rather than posing the live scene
        // graph, so this can run on an actor that is mid-animation without disturbing it.
        public static ManaStrideMeasurement? Measure(
            ManaSkeletalActorNode actor,
            ManaAnimationClip clip,
            IReadOnlyList<string>? footBoneNames = null,
            int sampleCount = DefaultSampleCount)
        {
            if (clip.DurationSeconds <= 0.0 || sampleCount < 8)
            {
                return null;
            }

            // Works off nodes rather than skeleton bones, so this measures a rigid rig (a mesh per
            // body part, no skinning -- the blocky characters) as readily as a skinned one.
            var footNodes = ResolveFootNodes(actor, footBoneNames);
            if (footNodes.Count == 0)
            {
                return null;
            }

            var channelsByNode = new Dictionary<string, ManaAnimationChannel>(StringComparer.Ordinal);
            foreach (var channel in clip.Channels)
            {
                channelsByNode[channel.NodeName] = channel;
            }

            var totalStepLength = 0.0f;
            var measuredFeet = 0;

            foreach (var footNode in footNodes)
            {
                if (!TryFindSoleInNodeSpace(actor, footNode, out var sole))
                {
                    continue;
                }

                var minForward = float.MaxValue;
                var maxForward = float.MinValue;

                for (var sample = 0; sample < sampleCount; sample++)
                {
                    var timeInTicks = (float)(clip.DurationInTicks * sample / sampleCount);
                    var nodeToActor = EvaluateNodeToActor(actor, footNode, channelsByNode, timeInTicks);
                    var footPosition = Vector3.Transform(sole, nodeToActor);

                    // +Z is the model's forward axis (see ManaPawnNode.FacingOffsetDegrees).
                    minForward = MathF.Min(minForward, footPosition.Z);
                    maxForward = MathF.Max(maxForward, footPosition.Z);
                }

                var stepLength = maxForward - minForward;
                if (stepLength > 0.0f)
                {
                    totalStepLength += stepLength;
                    measuredFeet++;
                }
            }

            if (measuredFeet == 0)
            {
                return null;
            }

            var averageStepLength = totalStepLength / measuredFeet;
            var cycleSeconds = (float)clip.DurationSeconds;
            var referenceSpeed = averageStepLength * 2.0f / cycleSeconds;

            return new ManaStrideMeasurement(clip.Name, averageStepLength, cycleSeconds, referenceSpeed, measuredFeet);
        }

        // Composes a bone's transform up to (but not including) the actor, substituting the clip's
        // pose for any node it animates and the bind pose for the rest.
        private static Matrix4x4 EvaluateNodeToActor(
            ManaSkeletalActorNode actor,
            ManaSceneNode node,
            Dictionary<string, ManaAnimationChannel> channelsByNode,
            float timeInTicks)
        {
            var result = Matrix4x4.Identity;

            for (var current = node; current != null && !ReferenceEquals(current, actor); current = current.Parent)
            {
                result *= EvaluateLocalTransform(actor, current, channelsByNode, timeInTicks);
            }

            return result;
        }

        private static Matrix4x4 EvaluateLocalTransform(
            ManaSkeletalActorNode actor,
            ManaSceneNode node,
            Dictionary<string, ManaAnimationChannel> channelsByNode,
            float timeInTicks)
        {
            if (!actor.Animation.TryGetBindPose(node.Name, out var position, out var rotation, out var scale))
            {
                return node.LocalTransform;
            }

            if (channelsByNode.TryGetValue(node.Name, out var channel))
            {
                position = channel.SamplePosition(timeInTicks, position);
                rotation = channel.SampleRotation(timeInTicks, rotation);
                scale = channel.SampleScale(timeInTicks, scale);
            }

            return Matrix4x4.CreateScale(scale) *
                Matrix4x4.CreateFromQuaternion(rotation) *
                Matrix4x4.CreateTranslation(position);
        }

        private static List<ManaSceneNode> ResolveFootNodes(ManaSkeletalActorNode actor, IReadOnlyList<string>? footNodeNames)
        {
            var candidates = new List<ManaSceneNode>();
            CollectNodes(actor, candidates);

            if (footNodeNames is { Count: > 0 })
            {
                return candidates
                    .Where(node => footNodeNames.Contains(node.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            // Most specific hint that matches anything wins, so a rig with both "leg" and "foot" nodes
            // measures the feet rather than the thighs.
            foreach (var hint in DefaultFootBoneHints)
            {
                var matches = candidates
                    .Where(node => node.Name.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count > 0)
                {
                    return matches;
                }
            }

            return new List<ManaSceneNode>();
        }

        private static void CollectNodes(ManaSceneNode node, List<ManaSceneNode> output)
        {
            output.Add(node);
            foreach (var child in node.Children)
            {
                CollectNodes(child, output);
            }
        }

        // The point on this node that touches the floor, expressed in the node's own space so the
        // caller can transform it by the node's animated pose directly.
        //
        // Two sources, because the two rig styles put the geometry in different places: a skinned rig
        // has vertices weighted to a bone (which need the inverse bind matrix to reach bone space),
        // while a rigid rig has a whole mesh parented to the node already in its space.
        private static bool TryFindSoleInNodeSpace(ManaSkeletalActorNode actor, ManaSceneNode node, out Vector3 sole)
        {
            if (actor.Skeleton != null &&
                actor.SkinnedMeshes.Count > 0 &&
                actor.Skeleton.TryGetBoneIndex(node.Name, out var boneIndex))
            {
                var bone = actor.Skeleton.Bones[boneIndex];
                if (TryFindSkinnedSole(actor, boneIndex, out var bindSpaceSole))
                {
                    sole = Vector3.Transform(bindSpaceSole, bone.OffsetMatrix);
                    return true;
                }
            }

            return TryFindRigidSole(node, out sole);
        }

        // Centroid of the lowest bind-pose vertices this bone dominates. A centroid rather than the
        // single lowest vertex, which can land on an inner edge and skew the arc.
        private static bool TryFindSkinnedSole(ManaSkeletalActorNode actor, int boneIndex, out Vector3 sole)
        {
            sole = Vector3.Zero;

            var lowest = float.MaxValue;
            foreach (var skinnedMesh in actor.SkinnedMeshes)
            {
                var binding = skinnedMesh.Binding;
                for (var vertex = 0; vertex < binding.VertexCount; vertex++)
                {
                    if (GetDominantBone(binding, vertex) == boneIndex)
                    {
                        lowest = MathF.Min(lowest, binding.BindPositions[vertex].Y);
                    }
                }
            }

            if (lowest == float.MaxValue)
            {
                return false;
            }

            var sum = Vector3.Zero;
            var count = 0;
            foreach (var skinnedMesh in actor.SkinnedMeshes)
            {
                var binding = skinnedMesh.Binding;
                for (var vertex = 0; vertex < binding.VertexCount; vertex++)
                {
                    if (GetDominantBone(binding, vertex) == boneIndex &&
                        binding.BindPositions[vertex].Y <= lowest + 0.001f)
                    {
                        sum += binding.BindPositions[vertex];
                        count++;
                    }
                }
            }

            if (count == 0)
            {
                return false;
            }

            sole = sum / count;
            return true;
        }

        // Same idea over the meshes parented to the node itself.
        private static bool TryFindRigidSole(ManaSceneNode node, out Vector3 sole)
        {
            sole = Vector3.Zero;

            var meshes = node.GetComponents<MeshRendererComponent>();
            if (meshes.Count == 0)
            {
                return false;
            }

            var lowest = float.MaxValue;
            foreach (var renderer in meshes)
            {
                foreach (var position in GetPositions(renderer))
                {
                    lowest = MathF.Min(lowest, position.Y);
                }
            }

            if (lowest == float.MaxValue)
            {
                return false;
            }

            var sum = Vector3.Zero;
            var count = 0;
            foreach (var renderer in meshes)
            {
                foreach (var position in GetPositions(renderer))
                {
                    if (position.Y <= lowest + 0.001f)
                    {
                        sum += position;
                        count++;
                    }
                }
            }

            if (count == 0)
            {
                return false;
            }

            sole = sum / count;
            return true;
        }

        private static Vector3[] GetPositions(MeshRendererComponent renderer)
        {
            if (renderer.Mesh == null)
            {
                return Array.Empty<Vector3>();
            }

            try
            {
                return (renderer.Mesh.CpuData ?? renderer.Mesh.GetOrCreateCpuData()).Positions;
            }
            catch (InvalidOperationException)
            {
                return Array.Empty<Vector3>();
            }
        }

        private static int GetDominantBone(ManaSkinBinding binding, int vertex)
        {
            var dominant = -1;
            var dominantWeight = 0.0f;
            for (var influence = 0; influence < ManaSkinBinding.InfluencesPerVertex; influence++)
            {
                var weight = binding.BoneWeights[(vertex * ManaSkinBinding.InfluencesPerVertex) + influence];
                if (weight > dominantWeight)
                {
                    dominantWeight = weight;
                    dominant = binding.BoneIndices[(vertex * ManaSkinBinding.InfluencesPerVertex) + influence];
                }
            }

            return dominant;
        }

        // True if the clip carries real horizontal root displacement, in which case stride analysis is
        // unnecessary -- the authored translation is the exact answer.
        public static bool TryGetRootMotionSpeed(ManaAnimationClip clip, string rootBoneName, out float speed)
        {
            speed = 0.0f;

            var channel = clip.Channels.FirstOrDefault(c => string.Equals(c.NodeName, rootBoneName, StringComparison.Ordinal));
            if (channel == null || channel.PositionKeys.Length < 2 || clip.DurationSeconds <= 0.0)
            {
                return false;
            }

            var first = channel.PositionKeys[0].Value;
            var last = channel.PositionKeys[^1].Value;
            var displacement = new Vector3(last.X - first.X, 0.0f, last.Z - first.Z);

            var distance = displacement.Length();
            if (distance <= 1e-5f)
            {
                return false;
            }

            speed = distance / (float)clip.DurationSeconds;
            return true;
        }
    }
}

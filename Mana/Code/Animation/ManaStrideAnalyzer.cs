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
            var skeleton = actor.Skeleton;
            if (skeleton == null || clip.DurationSeconds <= 0.0 || sampleCount < 8)
            {
                return null;
            }

            var footBones = ResolveFootBones(skeleton, footBoneNames);
            if (footBones.Count == 0)
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

            foreach (var bone in footBones)
            {
                var sole = FindSoleCentroid(actor, skeleton, bone);
                var minForward = float.MaxValue;
                var maxForward = float.MinValue;

                for (var sample = 0; sample < sampleCount; sample++)
                {
                    var timeInTicks = (float)(clip.DurationInTicks * sample / sampleCount);
                    var boneToActor = EvaluateNodeToActor(actor, bone.Node, channelsByNode, timeInTicks);
                    var footPosition = Vector3.Transform(sole, bone.OffsetMatrix * boneToActor);

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

        private static List<ManaBone> ResolveFootBones(ManaSkeleton skeleton, IReadOnlyList<string>? footBoneNames)
        {
            if (footBoneNames is { Count: > 0 })
            {
                return footBoneNames
                    .Select(skeleton.FindBone)
                    .Where(bone => bone != null)
                    .Select(bone => bone!)
                    .ToList();
            }

            // Most specific hint that matches anything wins, so a rig with both "leg" and "foot" bones
            // measures the feet rather than the thighs.
            foreach (var hint in DefaultFootBoneHints)
            {
                var matches = skeleton.Bones
                    .Where(bone => bone.Name.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (matches.Count > 0)
                {
                    return matches;
                }
            }

            return new List<ManaBone>();
        }

        // The point on the bone that touches the floor: the centroid of the bind-pose vertices this
        // bone dominates that sit at its lowest extent. A centroid rather than the single lowest
        // vertex, which can land on an inner edge and skew the arc.
        private static Vector3 FindSoleCentroid(ManaSkeletalActorNode actor, ManaSkeleton skeleton, ManaBone bone)
        {
            if (!skeleton.TryGetBoneIndex(bone.Name, out var boneIndex))
            {
                return Vector3.Zero;
            }

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
                return Vector3.Zero;
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

            return count == 0 ? Vector3.Zero : sum / count;
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

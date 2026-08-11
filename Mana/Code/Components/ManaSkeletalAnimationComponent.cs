using Mana.Code.Animation;
using Mana.Code.Nodes;

namespace Mana
{
    // Drives the bone nodes of a rig from a clip. Writes ManaSceneNode.LocalTransform directly rather
    // than Position/Rotation/Scale: the Euler round-trip through Rotation would lose the quaternion's
    // sign and shortest-arc behaviour, which shows up as bones snapping the long way around.
    public class ManaSkeletalAnimationComponent : ManaComponent
    {
        private readonly record struct BonePose(Vector3 Position, Quaternion Rotation, Vector3 Scale);

        private readonly Dictionary<string, ManaSceneNode> nodesByName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BonePose> bindPoses = new(StringComparer.Ordinal);
        private readonly List<ManaAnimationClip> clips = new();

        private ManaAnimationClip? activeClip;
        private ManaSceneNode?[] resolvedChannelNodes = Array.Empty<ManaSceneNode?>();

        public IReadOnlyList<ManaAnimationClip> Clips => clips;
        public ManaAnimationClip? ActiveClip => activeClip;
        public double TimeSeconds { get; set; }
        public float PlaybackSpeed { get; set; } = 1.0f;
        public bool Looping { get; set; } = true;
        public bool IsPlaying { get; set; } = true;

        public ManaSkeletalAnimationComponent(ManaSceneNode owner) : base(owner)
        {
            Name = "Skeletal Animation";
            CaptureBindPose(owner);
        }

        public void AddClips(IEnumerable<ManaAnimationClip> newClips)
        {
            clips.AddRange(newClips);
        }

        // Re-walks the subtree to rebuild the node lookup and rest pose. Needed because the component
        // is constructed with its owner (before the importer has attached any children), and again if
        // a rig is ever restructured at runtime.
        public void RefreshBindPose()
        {
            nodesByName.Clear();
            bindPoses.Clear();
            CaptureBindPose(Owner);

            if (activeClip != null)
            {
                resolvedChannelNodes = activeClip.Channels
                    .Select(channel => nodesByName.GetValueOrDefault(channel.NodeName))
                    .ToArray();
            }
        }

        public bool Play(string clipName, bool restart = true)
        {
            var clip = clips.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, clipName, StringComparison.OrdinalIgnoreCase));
            if (clip == null)
            {
                return false;
            }

            Play(clip, restart);
            return true;
        }

        public void Play(ManaAnimationClip? clip, bool restart = true)
        {
            if (ReferenceEquals(clip, activeClip) && !restart)
            {
                IsPlaying = true;
                return;
            }

            activeClip = clip;
            resolvedChannelNodes = clip == null
                ? Array.Empty<ManaSceneNode?>()
                : clip.Channels.Select(channel => nodesByName.GetValueOrDefault(channel.NodeName)).ToArray();

            if (restart)
            {
                TimeSeconds = 0.0;
            }

            IsPlaying = clip != null;

            // A stopped rig should sit in its bind pose, not wherever the last clip left it.
            if (clip == null)
            {
                ResetToBindPose();
            }
        }

        public void Stop()
        {
            Play((ManaAnimationClip?)null);
        }

        // Called by ManaSkeletalActorNode before the transform walk, so the poses written here are the
        // ones that get propagated into world space this frame.
        public void Advance(TimeSpan deltaTime)
        {
            if (activeClip == null)
            {
                return;
            }

            if (IsPlaying)
            {
                TimeSeconds += deltaTime.TotalSeconds * PlaybackSpeed;
            }

            var duration = activeClip.DurationSeconds;
            if (duration > 0.0)
            {
                if (Looping)
                {
                    TimeSeconds -= Math.Floor(TimeSeconds / duration) * duration;
                }
                else if (TimeSeconds >= duration)
                {
                    TimeSeconds = duration;
                    IsPlaying = false;
                }
                else if (TimeSeconds < 0.0)
                {
                    TimeSeconds = 0.0;
                }
            }

            ApplyPose();
        }

        private void ApplyPose()
        {
            if (activeClip == null)
            {
                return;
            }

            var timeInTicks = (float)(TimeSeconds * activeClip.TicksPerSecond);

            for (var i = 0; i < activeClip.Channels.Count; i++)
            {
                var node = i < resolvedChannelNodes.Length ? resolvedChannelNodes[i] : null;
                if (node == null)
                {
                    continue;
                }

                var channel = activeClip.Channels[i];
                var bindPose = bindPoses.GetValueOrDefault(
                    channel.NodeName,
                    new BonePose(Vector3.Zero, Quaternion.Identity, Vector3.One));

                var position = channel.SamplePosition(timeInTicks, bindPose.Position);
                var rotation = channel.SampleRotation(timeInTicks, bindPose.Rotation);
                var scale = channel.SampleScale(timeInTicks, bindPose.Scale);

                node.LocalTransform =
                    Matrix4x4.CreateScale(scale) *
                    Matrix4x4.CreateFromQuaternion(rotation) *
                    Matrix4x4.CreateTranslation(position);
            }
        }

        // The rest transform a node had before anything animated it. Exposed because evaluating a clip
        // off to the side (see ManaStrideAnalyzer) needs the un-posed value for every node the clip
        // does not animate -- reading LocalTransform there would pick up the live pose instead.
        public bool TryGetBindPose(string nodeName, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            if (bindPoses.TryGetValue(nodeName, out var pose))
            {
                position = pose.Position;
                rotation = pose.Rotation;
                scale = pose.Scale;
                return true;
            }

            position = Vector3.Zero;
            rotation = Quaternion.Identity;
            scale = Vector3.One;
            return false;
        }

        public void ResetToBindPose()
        {
            foreach (var entry in bindPoses)
            {
                if (!nodesByName.TryGetValue(entry.Key, out var node))
                {
                    continue;
                }

                node.LocalTransform =
                    Matrix4x4.CreateScale(entry.Value.Scale) *
                    Matrix4x4.CreateFromQuaternion(entry.Value.Rotation) *
                    Matrix4x4.CreateTranslation(entry.Value.Position);
            }
        }

        // The rig's authored rest pose, captured before anything animates it. Doubles as the fallback
        // for channels that omit a track -- glTF clips routinely ship rotation only.
        private void CaptureBindPose(ManaSceneNode node)
        {
            if (!string.IsNullOrEmpty(node.Name))
            {
                nodesByName.TryAdd(node.Name, node);

                if (!bindPoses.ContainsKey(node.Name))
                {
                    if (!Matrix4x4.Decompose(node.LocalTransform, out var scale, out var rotation, out var position))
                    {
                        scale = Vector3.One;
                        rotation = Quaternion.Identity;
                        position = node.LocalTransform.Translation;
                    }

                    bindPoses[node.Name] = new BonePose(position, rotation, scale);
                }
            }

            foreach (var child in node.Children)
            {
                CaptureBindPose(child);
            }
        }
    }
}

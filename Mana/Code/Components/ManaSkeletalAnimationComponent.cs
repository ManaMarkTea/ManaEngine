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
        private readonly Dictionary<string, ManaAnimationChannel> activeChannels = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BonePose> blendFromPoses = new(StringComparer.Ordinal);
        private readonly List<ManaAnimationClip> clips = new();

        private ManaAnimationClip? activeClip;
        private float blendRemainingSeconds;

        public IReadOnlyList<ManaAnimationClip> Clips => clips;
        public ManaAnimationClip? ActiveClip => activeClip;
        public double TimeSeconds { get; set; }
        public float PlaybackSpeed { get; set; } = 1.0f;
        public bool Looping { get; set; } = true;
        public bool IsPlaying { get; set; } = true;

        // Cross-fade applied when the clip changes. Without it a switch snaps, which is most obvious
        // going from walk to idle: the Kenney idle clip animates only the torso, arms and head, so the
        // legs jump from mid-stride straight back to their rest pose.
        public float BlendDurationSeconds { get; set; } = 0.15f;

        public bool IsBlending => blendRemainingSeconds > 0.0f;

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
            blendFromPoses.Clear();
            CaptureBindPose(Owner);
        }

        public bool HasClip(string clipName)
        {
            return clips.Any(clip => string.Equals(clip.Name, clipName, StringComparison.OrdinalIgnoreCase));
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

            // Snapshot where every bone is right now, before the new clip overwrites anything, so the
            // cross-fade has something to come from.
            CaptureBlendSource();

            activeClip = clip;
            activeChannels.Clear();
            if (clip != null)
            {
                foreach (var channel in clip.Channels)
                {
                    activeChannels[channel.NodeName] = channel;
                }
            }

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

        private void CaptureBlendSource()
        {
            blendFromPoses.Clear();
            blendRemainingSeconds = 0.0f;

            if (BlendDurationSeconds <= 0.0f)
            {
                return;
            }

            foreach (var entry in nodesByName)
            {
                blendFromPoses[entry.Key] = DecomposePose(entry.Value.LocalTransform, entry.Key);
            }

            blendRemainingSeconds = BlendDurationSeconds;
        }

        private BonePose DecomposePose(Matrix4x4 transform, string nodeName)
        {
            if (Matrix4x4.Decompose(transform, out var scale, out var rotation, out var position))
            {
                return new BonePose(position, rotation, scale);
            }

            return bindPoses.GetValueOrDefault(nodeName, new BonePose(Vector3.Zero, Quaternion.Identity, Vector3.One));
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

            if (blendRemainingSeconds > 0.0f)
            {
                blendRemainingSeconds = MathF.Max(blendRemainingSeconds - (float)deltaTime.TotalSeconds, 0.0f);
            }

            ApplyPose();
        }

        // Writes a pose for EVERY bone, not only the ones the active clip animates. A clip defines the
        // whole pose: a bone it says nothing about belongs at its bind pose, not wherever the previous
        // clip happened to leave it. Iterating only the channels is what left the legs frozen mid-stride
        // when walk handed over to an idle clip that has no leg channels.
        private void ApplyPose()
        {
            if (activeClip == null)
            {
                return;
            }

            var timeInTicks = (float)(TimeSeconds * activeClip.TicksPerSecond);
            var blendAlpha = BlendDurationSeconds > 0.0f && blendRemainingSeconds > 0.0f
                ? 1.0f - (blendRemainingSeconds / BlendDurationSeconds)
                : 1.0f;

            foreach (var entry in bindPoses)
            {
                if (!nodesByName.TryGetValue(entry.Key, out var node))
                {
                    continue;
                }

                var bindPose = entry.Value;
                var target = bindPose;

                if (activeChannels.TryGetValue(entry.Key, out var channel))
                {
                    target = new BonePose(
                        channel.SamplePosition(timeInTicks, bindPose.Position),
                        channel.SampleRotation(timeInTicks, bindPose.Rotation),
                        channel.SampleScale(timeInTicks, bindPose.Scale));
                }

                var pose = blendAlpha >= 1.0f || !blendFromPoses.TryGetValue(entry.Key, out var from)
                    ? target
                    : BlendPoses(from, target, blendAlpha);

                node.LocalTransform =
                    Matrix4x4.CreateScale(pose.Scale) *
                    Matrix4x4.CreateFromQuaternion(pose.Rotation) *
                    Matrix4x4.CreateTranslation(pose.Position);
            }
        }

        private static BonePose BlendPoses(BonePose from, BonePose to, float alpha)
        {
            return new BonePose(
                Vector3.Lerp(from.Position, to.Position, alpha),
                Quaternion.Normalize(Quaternion.Slerp(from.Rotation, to.Rotation, alpha)),
                Vector3.Lerp(from.Scale, to.Scale, alpha));
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
        //
        // The owner node itself is deliberately excluded. ApplyPose writes a transform for every entry
        // in here, and the owner's transform belongs to whatever placed the actor in the world -- for a
        // ManaPawnNode that is the locomotion code, so including it would teleport the character back
        // to its import pose every frame.
        private void CaptureBindPose(ManaSceneNode node)
        {
            if (!string.IsNullOrEmpty(node.Name) && !ReferenceEquals(node, Owner))
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

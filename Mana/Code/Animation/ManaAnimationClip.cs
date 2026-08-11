namespace Mana.Code.Animation
{
    public readonly record struct ManaVectorKey(float TimeInTicks, Vector3 Value);

    public readonly record struct ManaQuaternionKey(float TimeInTicks, Quaternion Value);

    // One animated node within a clip. Tracks are independent and any of them may be empty: glTF
    // exporters only write the channels that actually move, so a missing track has to fall back to
    // the node's bind pose rather than to identity.
    public sealed class ManaAnimationChannel
    {
        public required string NodeName { get; init; }
        public required ManaVectorKey[] PositionKeys { get; init; }
        public required ManaQuaternionKey[] RotationKeys { get; init; }
        public required ManaVectorKey[] ScaleKeys { get; init; }

        public Vector3 SamplePosition(float timeInTicks, Vector3 fallback)
        {
            return SampleVector(PositionKeys, timeInTicks, fallback);
        }

        public Vector3 SampleScale(float timeInTicks, Vector3 fallback)
        {
            return SampleVector(ScaleKeys, timeInTicks, fallback);
        }

        public Quaternion SampleRotation(float timeInTicks, Quaternion fallback)
        {
            if (RotationKeys.Length == 0)
            {
                return fallback;
            }

            if (RotationKeys.Length == 1)
            {
                return RotationKeys[0].Value;
            }

            var segment = FindSegment(RotationKeys.Length, timeInTicks, index => RotationKeys[index].TimeInTicks);
            var from = RotationKeys[segment.Index];
            var to = RotationKeys[segment.Index + 1];
            return Quaternion.Normalize(Quaternion.Slerp(from.Value, to.Value, segment.Blend));
        }

        private static Vector3 SampleVector(ManaVectorKey[] keys, float timeInTicks, Vector3 fallback)
        {
            if (keys.Length == 0)
            {
                return fallback;
            }

            if (keys.Length == 1)
            {
                return keys[0].Value;
            }

            var segment = FindSegment(keys.Length, timeInTicks, index => keys[index].TimeInTicks);
            return Vector3.Lerp(keys[segment.Index].Value, keys[segment.Index + 1].Value, segment.Blend);
        }

        // Linear scan rather than a binary search: these clips have single-digit key counts per track,
        // where the scan wins on both simplicity and cache behaviour.
        private static (int Index, float Blend) FindSegment(int keyCount, float timeInTicks, Func<int, float> timeAt)
        {
            if (timeInTicks <= timeAt(0))
            {
                return (0, 0.0f);
            }

            for (var i = 0; i < keyCount - 1; i++)
            {
                var start = timeAt(i);
                var end = timeAt(i + 1);
                if (timeInTicks >= end)
                {
                    continue;
                }

                var span = end - start;
                var blend = span <= float.Epsilon ? 0.0f : (timeInTicks - start) / span;
                return (i, Math.Clamp(blend, 0.0f, 1.0f));
            }

            return (keyCount - 2, 1.0f);
        }
    }

    public sealed class ManaAnimationClip
    {
        public required string Name { get; init; }
        public required double DurationInTicks { get; init; }
        public required double TicksPerSecond { get; init; }
        public required IReadOnlyList<ManaAnimationChannel> Channels { get; init; }

        public double DurationSeconds => TicksPerSecond > 0.0
            ? DurationInTicks / TicksPerSecond
            : DurationInTicks;

        public override string ToString()
        {
            return $"{Name} ({DurationSeconds:0.00}s, {Channels.Count} channels)";
        }
    }
}

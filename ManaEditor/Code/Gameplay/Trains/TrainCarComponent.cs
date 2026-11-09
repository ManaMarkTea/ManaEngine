using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using System.Text.Json.Serialization;

namespace ManaEditor.Code.Gameplay.Trains
{
    /// <summary>
    /// Moves one train car around a rectangular loop. This is intentionally a normal component rather than
    /// a game-mode-owned list item, so a train is behavior on a scene node instead of a controller scanning
    /// the whole world every frame.
    /// </summary>
    public sealed class TrainCarComponent : ManaComponent
    {
        public const string ScriptType = "TrainCar";
        public const float DefaultRailTopHeight = 0.18f;
        public const float DefaultLoopSpeed = 5.25f;

        private Vector3 _basePosition;
        private Vector3 _baseRotation;
        private bool _hasBasePose;
        private double _elapsedSeconds;
        private float _visualRideHeightOffset;

        public TrainCarComponent(ManaSceneNode owner) : base(owner)
        {
            CaptureBasePose(force: true);
        }

        public override bool UpdatesBeforeNodeTransform => true;

        public string RouteKey { get; set; } = "A";

        public int CarIndex { get; set; }

        public float LoopCenterX { get; set; } = 32.0f;

        public float LoopCenterZ { get; set; } = 32.0f;

        public float LoopHalfSizeX { get; set; } = 22.0f;

        public float LoopHalfSizeZ { get; set; } = 16.0f;

        public float CornerRadius { get; set; } = 4.0f;

        public float RailHeight { get; set; } = DefaultRailTopHeight;

        public float Speed { get; set; } = DefaultLoopSpeed;

        public float CarSpacing { get; set; } = 3.45f;

        public float InitialDistance { get; set; }

        public bool Reverse { get; set; }

        public float ModelYawOffset { get; set; }

        public float RideHeightOffset { get; set; }

        /// <summary>Transient runtime pause state. The world lifecycle enables it during play.</summary>
        [JsonIgnore]
        public bool Paused { get; set; } = true;

        public override void BeginPlay()
        {
            CaptureBasePose(force: true);
            _elapsedSeconds = 0.0;
            _visualRideHeightOffset = EstimateVisualRideHeightOffset();
            Paused = false;
        }

        public override void EndPlay()
        {
            Restore();
        }

        public void ConfigureDefaultRoute(string routeKey, int carIndex, float railTopHeight)
        {
            RouteKey = string.IsNullOrWhiteSpace(routeKey) ? "A" : routeKey;
            CarIndex = Math.Max(0, carIndex);
            LoopCenterX = 32.0f;
            LoopCenterZ = 32.0f;
            LoopHalfSizeX = 22.0f;
            LoopHalfSizeZ = 16.0f;
            CornerRadius = 4.0f;
            RailHeight = railTopHeight;
            Speed = DefaultLoopSpeed;
            CarSpacing = RouteKey.Equals("B", StringComparison.OrdinalIgnoreCase) ? 3.1f : 3.45f;
            InitialDistance = RouteKey.Equals("B", StringComparison.OrdinalIgnoreCase) ? 72.0f : 0.0f;
            Reverse = false;
            ModelYawOffset = 0.0f;
        }

        internal void ResetAfterLevelPropertiesApplied()
        {
            RouteKey = string.IsNullOrWhiteSpace(RouteKey) ? "A" : RouteKey;
            CarIndex = Math.Max(0, CarIndex);
            Paused = true;
            _elapsedSeconds = 0.0;
            CaptureBasePose(force: true);
        }

        public void CaptureBasePose(bool force = false)
        {
            if (_hasBasePose && !force)
            {
                return;
            }

            _basePosition = Owner.Position;
            _baseRotation = Owner.Rotation;
            _hasBasePose = true;
        }

        public void Restore()
        {
            CaptureBasePose();
            Owner.Position = _basePosition;
            Owner.Rotation = _baseRotation;
            _elapsedSeconds = 0.0;
            Paused = true;
        }

        public override void UpdateComponent()
        {
            base.UpdateComponent();

            if (Paused)
            {
                CaptureBasePose(force: true);
                return;
            }

            CaptureBasePose();
            _elapsedSeconds += ManaTime.GlobalTime.DeltaTime.TotalSeconds;

            var route = new TrainLoopRoute(
                new Vector2(LoopCenterX, LoopCenterZ),
                new Vector2(LoopHalfSizeX, LoopHalfSizeZ),
                CornerRadius,
                RailHeight,
                Reverse);

            var distance = InitialDistance + (float)_elapsedSeconds * Speed - CarIndex * CarSpacing;
            var sample = route.Sample(distance);
            var yaw = MathF.Atan2(sample.Direction.X, sample.Direction.Z) + ModelYawOffset;

            Owner.Position = new Vector3(
                sample.Position.X,
                sample.Position.Y + _visualRideHeightOffset + RideHeightOffset,
                sample.Position.Z);
            Owner.Rotation = new Vector3(_baseRotation.X, yaw, _baseRotation.Z);
        }

        private float EstimateVisualRideHeightOffset()
        {
            if (!ManaCollisionBounds.TryGetVisualBox(Owner, out var size, out var offset)
                || !Matrix4x4.Decompose(Owner.WorldTransform, out var scale, out _, out _))
            {
                return 0.0f;
            }

            var visualBottom = offset.Y * MathF.Abs(scale.Y) - size.Y * 0.5f;
            return -visualBottom;
        }

        private readonly record struct TrainSample(Vector3 Position, Vector3 Direction);

        private sealed class TrainLoopRoute
        {
            private readonly Vector2 _center;
            private readonly Vector2 _halfSize;
            private readonly float _cornerRadius;
            private readonly float _straightX;
            private readonly float _straightZ;
            private readonly float _arcLength;
            private readonly float _length;
            private readonly float _height;
            private readonly bool _reverse;

            public TrainLoopRoute(Vector2 center, Vector2 halfSize, float cornerRadius, float height, bool reverse)
            {
                _center = center;
                _halfSize = new Vector2(MathF.Max(0.01f, halfSize.X), MathF.Max(0.01f, halfSize.Y));
                _cornerRadius = MathF.Min(MathF.Max(0.0f, cornerRadius), MathF.Min(_halfSize.X, _halfSize.Y));
                _height = height;
                _reverse = reverse;

                _straightX = MathF.Max(0.0f, (_halfSize.X - _cornerRadius) * 2.0f);
                _straightZ = MathF.Max(0.0f, (_halfSize.Y - _cornerRadius) * 2.0f);
                _arcLength = MathF.PI * _cornerRadius * 0.5f;
                _length = MathF.Max(0.01f, (_straightX + _straightZ) * 2.0f + _arcLength * 4.0f);
            }

            public TrainSample Sample(float distance)
            {
                var d = Wrap(distance, _length);
                if (_reverse)
                {
                    d = Wrap(_length - d, _length);
                }

                var sample = SampleClockwise(d);
                return _reverse
                    ? new TrainSample(sample.Position, -sample.Direction)
                    : sample;
            }

            private TrainSample SampleClockwise(float distance)
            {
                var x0 = _center.X - _halfSize.X;
                var x1 = _center.X + _halfSize.X;
                var z0 = _center.Y - _halfSize.Y;
                var z1 = _center.Y + _halfSize.Y;
                var r = _cornerRadius;

                var d = distance;
                if (Take(ref d, _straightX, out var s))
                {
                    return SampleStraight(new Vector2(x0 + r + s, z0), new Vector2(1.0f, 0.0f));
                }

                if (Take(ref d, _arcLength, out s))
                {
                    return SampleArc(new Vector2(x1 - r, z0 + r), -MathF.PI * 0.5f, s / _arcLength);
                }

                if (Take(ref d, _straightZ, out s))
                {
                    return SampleStraight(new Vector2(x1, z0 + r + s), new Vector2(0.0f, 1.0f));
                }

                if (Take(ref d, _arcLength, out s))
                {
                    return SampleArc(new Vector2(x1 - r, z1 - r), 0.0f, s / _arcLength);
                }

                if (Take(ref d, _straightX, out s))
                {
                    return SampleStraight(new Vector2(x1 - r - s, z1), new Vector2(-1.0f, 0.0f));
                }

                if (Take(ref d, _arcLength, out s))
                {
                    return SampleArc(new Vector2(x0 + r, z1 - r), MathF.PI * 0.5f, s / _arcLength);
                }

                if (Take(ref d, _straightZ, out s))
                {
                    return SampleStraight(new Vector2(x0, z1 - r - s), new Vector2(0.0f, -1.0f));
                }

                return SampleArc(new Vector2(x0 + r, z0 + r), MathF.PI, d / _arcLength);
            }

            private TrainSample SampleArc(Vector2 center, float startAngle, float t)
            {
                if (_cornerRadius <= 0.0f || _arcLength <= 0.0f)
                {
                    return SampleStraight(center, new Vector2(1.0f, 0.0f));
                }

                var angle = startAngle + Math.Clamp(t, 0.0f, 1.0f) * MathF.PI * 0.5f;
                var radial = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var tangent = Vector2.Normalize(new Vector2(-MathF.Sin(angle), MathF.Cos(angle)));
                return new TrainSample(
                    new Vector3(center.X + radial.X * _cornerRadius, _height, center.Y + radial.Y * _cornerRadius),
                    new Vector3(tangent.X, 0.0f, tangent.Y));
            }

            private TrainSample SampleStraight(Vector2 position, Vector2 direction) =>
                new(new Vector3(position.X, _height, position.Y), new Vector3(direction.X, 0.0f, direction.Y));

            private static bool Take(ref float distance, float segmentLength, out float segmentDistance)
            {
                if (segmentLength <= 0.0f)
                {
                    segmentDistance = 0.0f;
                    return false;
                }

                if (distance <= segmentLength)
                {
                    segmentDistance = distance;
                    return true;
                }

                distance -= segmentLength;
                segmentDistance = 0.0f;
                return false;
            }

            private static float Wrap(float value, float length)
            {
                var wrapped = value % length;
                return wrapped < 0.0f ? wrapped + length : wrapped;
            }
        }
    }
}

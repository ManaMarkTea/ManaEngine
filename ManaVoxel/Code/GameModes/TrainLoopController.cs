using Mana.Code.Nodes;
using ManaCore.Graphics;
using Silk.NET.Input;

namespace Mana.Code.GameModes
{
    internal sealed class TrainLoopController
    {
        private const string TrainPrefix = "Loop Train ";
        private const string TrackPrefix = "Track ";
        private const string TrackDebugPrefix = "Track Debug ";
        private const float DefaultRailTopHeight = 0.18f;
        private const float SharedLoopSpeed = 5.25f;
        private const float BoundsCenterMarkerLift = 0.48f;
        private const float SnapMarkerLift = 0.72f;
        private const float PivotMarkerLift = 1.48f;

        private readonly ManaWorld _world;
        private readonly List<TrainGroup> _groups;
        private readonly List<ManaSceneNode> _trackRoots;
        private readonly List<ManaSceneNode> _transientDebugNodes = new();
        private readonly bool _initialBoundsVisible;
        private double _elapsedSeconds;
        private bool _boundsVisible;
        private bool _boundsKeyWasDown;
        private bool _boundsVisibilityInitialized;

        private TrainLoopController(ManaWorld world, List<TrainGroup> groups, List<ManaSceneNode> trackRoots)
        {
            _world = world;
            _groups = groups;
            _trackRoots = trackRoots;
            _initialBoundsVisible = ReadInitialBoundsVisible();
        }

        public bool HasTrains => _groups.Count > 0;
        public bool HasTrackBounds => _trackRoots.Count > 0;
        public bool BoundsVisible => _boundsVisible;

        public static TrainLoopController Create(ManaWorld world)
        {
            var trainCars = new List<TrainCar>();
            var trackRoots = new List<ManaSceneNode>();

            lock (world.SceneLock)
            {
                foreach (var scene in world.Scenes)
                {
                    foreach (var child in scene.Children)
                    {
                        CollectTrainNodes(child, trainCars, trackRoots);
                    }
                }
            }

            var railTopHeight = EstimateRailTopHeight(trackRoots);
            var routes = TrainRoute.CreateDefaults(railTopHeight);
            var groups = trainCars
                .GroupBy(car => car.RouteKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var route = routes.TryGetValue(group.Key, out var configured)
                        ? configured
                        : TrainRoute.FallbackFor(group.Key, railTopHeight);

                    return new TrainGroup(
                        route,
                        group.OrderBy(car => car.Index).ToList());
                })
                .Where(group => group.Cars.Count > 0)
                .ToList();

            return new TrainLoopController(world, groups, trackRoots);
        }

        public void Update(double deltaTime)
        {
            EnsureBoundsVisibilityInitialized();

            if (_groups.Count == 0)
            {
                return;
            }

            _elapsedSeconds += deltaTime;

            foreach (var group in _groups)
            {
                var route = group.Route;
                var leadDistance = route.InitialDistance + (float)_elapsedSeconds * route.Speed;

                for (var i = 0; i < group.Cars.Count; i++)
                {
                    var car = group.Cars[i];
                    var distance = leadDistance - i * route.CarSpacing;
                    var sample = route.Sample(distance);
                    var yaw = MathF.Atan2(sample.Direction.X, sample.Direction.Z) + route.ModelYawOffset;

                    car.Node.Position = new Vector3(
                        sample.Position.X,
                        sample.Position.Y + car.RideHeightOffset,
                        sample.Position.Z);
                    car.Node.Rotation = new Vector3(car.BaseRotation.X, yaw, car.BaseRotation.Z);
                }
            }
        }

        public void UpdateDebugInput(ManaContext context, bool keyboardCaptured)
        {
            EnsureBoundsVisibilityInitialized();

            if (_trackRoots.Count == 0)
            {
                return;
            }

            if (keyboardCaptured)
            {
                _boundsKeyWasDown = false;
                return;
            }

            var keyboard = context.Input?.Keyboards.FirstOrDefault();
            var boundsKeyDown = keyboard?.IsKeyPressed(Key.B) == true;
            if (boundsKeyDown && !_boundsKeyWasDown)
            {
                SetBoundsVisible(!_boundsVisible);
            }

            _boundsKeyWasDown = boundsKeyDown;
        }

        public void SetBoundsVisible(bool visible)
        {
            if (_boundsVisible == visible)
            {
                return;
            }

            _boundsVisible = visible;

            lock (_world.SceneLock)
            {
                RemoveBoundsNoLock();

                if (!visible)
                {
                    return;
                }

                foreach (var trackRoot in _trackRoots)
                {
                    if (!TryGetLocalBounds(trackRoot, out var min, out var max))
                    {
                        continue;
                    }

                    var center = (min + max) * 0.5f;
                    var halfSize = Vector3.Max((max - min) * 0.5f + new Vector3(0.035f), new Vector3(0.06f));
                    var cage = new ManaWireframeCage(
                        trackRoot.Context,
                        new Vector4(0.05f, 0.95f, 1.0f, 1.0f),
                        halfSize,
                        thickness: 0.035f);

                    AddTransientDebugMesh(trackRoot, "Bounds", center, cage);
                    AddTransientDebugMesh(
                        trackRoot,
                        "Bounds Center",
                        new Vector3(center.X, max.Y + BoundsCenterMarkerLift, center.Z),
                        new ManaCube(
                            trackRoot.Context,
                            new Vector4(0.2f, 0.35f, 1.0f, 1.0f),
                            new Vector3(0.22f)));

                    AddTransientDebugMesh(
                        trackRoot,
                        "Pivot",
                        new Vector3(0.0f, max.Y + PivotMarkerLift, 0.0f),
                        new ManaCube(
                            trackRoot.Context,
                            new Vector4(1.0f, 0.05f, 0.85f, 1.0f),
                            new Vector3(0.12f, 1.35f, 0.12f)));

                    var snapPoints = EstimateTrackSnapPoints(trackRoot, min, max);
                    for (var i = 0; i < snapPoints.Count; i++)
                    {
                        AddTransientDebugMesh(
                            trackRoot,
                            $"Snap {i}",
                            snapPoints[i],
                            new ManaCube(
                                trackRoot.Context,
                                new Vector4(1.0f, 0.9f, 0.05f, 1.0f),
                                new Vector3(0.22f, 0.68f, 0.22f)));
                    }
                }
            }
        }

        public void Restore()
        {
            SetBoundsVisible(false);

            foreach (var group in _groups)
            {
                foreach (var car in group.Cars)
                {
                    car.Node.Position = car.BasePosition;
                    car.Node.Rotation = car.BaseRotation;
                }
            }
        }

        private void EnsureBoundsVisibilityInitialized()
        {
            if (_boundsVisibilityInitialized)
            {
                return;
            }

            _boundsVisibilityInitialized = true;

            if (_initialBoundsVisible)
            {
                SetBoundsVisible(true);
            }
        }

        private static bool ReadInitialBoundsVisible()
        {
            var value = Environment.GetEnvironmentVariable("MANA_TRAIN_BOUNDS");
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            return !value.Equals("0", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("false", StringComparison.OrdinalIgnoreCase)
                && !value.Equals("off", StringComparison.OrdinalIgnoreCase);
        }

        private void RemoveBoundsNoLock()
        {
            foreach (var debugNode in _transientDebugNodes)
            {
                debugNode.Parent?.RemoveChild(debugNode);
            }

            _transientDebugNodes.Clear();
        }

        private static void CollectTrainNodes(
            ManaSceneNode node,
            List<TrainCar> trainCars,
            List<ManaSceneNode> trackRoots)
        {
            if (TryReadTrainCar(node, out var car))
            {
                trainCars.Add(car);
            }

            if (node.Name.StartsWith(TrackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                trackRoots.Add(node);
            }

            foreach (var child in node.Children)
            {
                CollectTrainNodes(child, trainCars, trackRoots);
            }
        }

        private static bool TryReadTrainCar(ManaSceneNode node, out TrainCar car)
        {
            car = default;

            if (!node.Name.StartsWith(TrainPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parts = node.Name[TrainPrefix.Length..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2 || !int.TryParse(parts[1], out var index))
            {
                return false;
            }

            var rideHeightOffset = 0.0f;
            if (TryGetLocalBounds(node, out var min, out _))
            {
                rideHeightOffset = -min.Y * node.Scale.Y;
            }

            car = new TrainCar(node, parts[0], index, node.Position, node.Rotation, rideHeightOffset);
            return true;
        }

        private static float EstimateRailTopHeight(IReadOnlyList<ManaSceneNode> trackRoots)
        {
            foreach (var trackRoot in trackRoots)
            {
                if (!TryGetLocalBounds(trackRoot, out _, out var max))
                {
                    continue;
                }

                return trackRoot.Position.Y + max.Y * trackRoot.Scale.Y;
            }

            return DefaultRailTopHeight;
        }

        private static bool TryGetLocalBounds(ManaSceneNode root, out Vector3 min, out Vector3 max)
        {
            min = new Vector3(float.MaxValue);
            max = new Vector3(float.MinValue);

            AccumulateLocalBounds(root, root, ref min, ref max);
            return min.X <= max.X;
        }

        private static void AccumulateLocalBounds(ManaSceneNode root, ManaSceneNode node, ref Vector3 min, ref Vector3 max)
        {
            if (node.Name.StartsWith(TrackDebugPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var renderer in node.GetComponents<MeshRendererComponent>())
            {
                if (!TryGetCpuData(renderer, out var cpuData))
                {
                    continue;
                }

                var toRoot = GetNodeToRootTransform(root, node);
                foreach (var position in cpuData.Positions)
                {
                    var local = Vector3.Transform(position, toRoot);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                }
            }

            foreach (var child in node.Children)
            {
                AccumulateLocalBounds(root, child, ref min, ref max);
            }
        }

        private void AddTransientDebugMesh(ManaSceneNode trackRoot, string kind, Vector3 localPosition, ManaMesh mesh)
        {
            // Transient debug geometry: visible in the scene, never captured back into the level.
            var node = new ManaSceneNode(trackRoot.Context, Matrix4x4.Identity)
            {
                Name = $"{TrackDebugPrefix}{kind} {trackRoot.Name}",
                Position = localPosition,
                Rotation = Vector3.Zero,
                Scale = Vector3.One,
                IncludeInEnvironmentCapture = false,
                LevelSource = null
            };

            node.Components.Add(new MeshRendererComponent(node, mesh)
            {
                DrawAfterScene = true
            });

            trackRoot.AddChild(node);
            _transientDebugNodes.Add(node);
        }

        private static IReadOnlyList<Vector3> EstimateTrackSnapPoints(ManaSceneNode trackRoot, Vector3 min, Vector3 max)
        {
            var y = max.Y + SnapMarkerLift;

            if (trackRoot.Name.Contains("Corner", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    new Vector3(0.0f, y, 0.0f),
                    new Vector3(MathF.Round(min.X), y, MathF.Round(max.Z))
                };
            }

            return new[]
            {
                new Vector3((min.X + max.X) * 0.5f, y, min.Z),
                new Vector3((min.X + max.X) * 0.5f, y, max.Z)
            };
        }

        private static Matrix4x4 GetNodeToRootTransform(ManaSceneNode root, ManaSceneNode node)
        {
            var result = Matrix4x4.Identity;
            for (var current = node; current != null && !ReferenceEquals(current, root); current = current.Parent)
            {
                result *= current.LocalTransform;
            }

            return result;
        }

        private static bool TryGetCpuData(MeshRendererComponent renderer, out ManaMeshCpuData cpuData)
        {
            cpuData = null!;

            if (renderer.Mesh == null)
            {
                return false;
            }

            try
            {
                cpuData = renderer.Mesh.CpuData ?? renderer.Mesh.GetOrCreateCpuData();
                return cpuData.Positions.Length > 0;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private readonly record struct TrainCar(
            ManaSceneNode Node,
            string RouteKey,
            int Index,
            Vector3 BasePosition,
            Vector3 BaseRotation,
            float RideHeightOffset);

        private sealed record TrainGroup(TrainRoute Route, List<TrainCar> Cars);

        private readonly record struct TrainSample(Vector3 Position, Vector3 Direction);

        private sealed class TrainRoute
        {
            public static IReadOnlyDictionary<string, TrainRoute> CreateDefaults(float railTopHeight) =>
                new Dictionary<string, TrainRoute>(StringComparer.OrdinalIgnoreCase)
                {
                    ["A"] = new(
                        center: new Vector2(32.0f, 32.0f),
                        halfSize: new Vector2(22.0f, 16.0f),
                        cornerRadius: 4.0f,
                        height: railTopHeight,
                        speed: SharedLoopSpeed,
                        carSpacing: 3.45f,
                        initialDistance: 0.0f,
                        reverse: false),
                    ["B"] = new(
                        center: new Vector2(32.0f, 32.0f),
                        halfSize: new Vector2(22.0f, 16.0f),
                        cornerRadius: 4.0f,
                        height: railTopHeight,
                        speed: SharedLoopSpeed,
                        carSpacing: 3.1f,
                        initialDistance: 72.0f,
                        reverse: false)
                };

            private readonly Vector2 _center;
            private readonly Vector2 _halfSize;
            private readonly float _cornerRadius;
            private readonly float _straightX;
            private readonly float _straightZ;
            private readonly float _arcLength;
            private readonly float _length;
            private readonly bool _reverse;

            public TrainRoute(
                Vector2 center,
                Vector2 halfSize,
                float cornerRadius,
                float height,
                float speed,
                float carSpacing,
                float initialDistance,
                bool reverse)
            {
                _center = center;
                _halfSize = halfSize;
                _cornerRadius = MathF.Min(cornerRadius, MathF.Min(halfSize.X, halfSize.Y));
                Height = height;
                Speed = speed;
                CarSpacing = carSpacing;
                InitialDistance = initialDistance;
                _reverse = reverse;

                _straightX = MathF.Max(0.0f, (_halfSize.X - _cornerRadius) * 2.0f);
                _straightZ = MathF.Max(0.0f, (_halfSize.Y - _cornerRadius) * 2.0f);
                _arcLength = MathF.PI * _cornerRadius * 0.5f;
                _length = (_straightX + _straightZ) * 2.0f + _arcLength * 4.0f;
            }

            public float Height { get; }

            public float Speed { get; }

            public float CarSpacing { get; }

            public float InitialDistance { get; }

            public float ModelYawOffset { get; init; }

            public static TrainRoute FallbackFor(string routeKey, float railTopHeight)
            {
                var offset = MathF.Abs(routeKey.GetHashCode() % 100) / 100.0f;
                return new TrainRoute(
                    new Vector2(32.0f, 32.0f),
                    new Vector2(22.0f, 16.0f),
                    4.0f,
                    railTopHeight,
                    4.8f + offset,
                    3.25f,
                    offset * 40.0f,
                    reverse: false);
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
                    return SampleStraight(
                        new Vector2(x0 + r + s, z0),
                        new Vector2(1.0f, 0.0f));
                }

                if (Take(ref d, _arcLength, out s))
                {
                    return SampleArc(new Vector2(x1 - r, z0 + r), -MathF.PI * 0.5f, s / _arcLength);
                }

                if (Take(ref d, _straightZ, out s))
                {
                    return SampleStraight(
                        new Vector2(x1, z0 + r + s),
                        new Vector2(0.0f, 1.0f));
                }

                if (Take(ref d, _arcLength, out s))
                {
                    return SampleArc(new Vector2(x1 - r, z1 - r), 0.0f, s / _arcLength);
                }

                if (Take(ref d, _straightX, out s))
                {
                    return SampleStraight(
                        new Vector2(x1 - r - s, z1),
                        new Vector2(-1.0f, 0.0f));
                }

                if (Take(ref d, _arcLength, out s))
                {
                    return SampleArc(new Vector2(x0 + r, z1 - r), MathF.PI * 0.5f, s / _arcLength);
                }

                if (Take(ref d, _straightZ, out s))
                {
                    return SampleStraight(
                        new Vector2(x0, z1 - r - s),
                        new Vector2(0.0f, -1.0f));
                }

                return SampleArc(new Vector2(x0 + r, z0 + r), MathF.PI, d / _arcLength);
            }

            private TrainSample SampleArc(Vector2 center, float startAngle, float t)
            {
                var angle = startAngle + Math.Clamp(t, 0.0f, 1.0f) * MathF.PI * 0.5f;
                var radial = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var tangent = Vector2.Normalize(new Vector2(-MathF.Sin(angle), MathF.Cos(angle)));
                return new TrainSample(
                    new Vector3(center.X + radial.X * _cornerRadius, Height, center.Y + radial.Y * _cornerRadius),
                    new Vector3(tangent.X, 0.0f, tangent.Y));
            }

            private TrainSample SampleStraight(Vector2 position, Vector2 direction) =>
                new(
                    new Vector3(position.X, Height, position.Y),
                    new Vector3(direction.X, 0.0f, direction.Y));

            private static bool Take(ref float distance, float segmentLength, out float segmentDistance)
            {
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

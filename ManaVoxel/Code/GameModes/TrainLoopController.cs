using Mana.Code.Nodes;
using ManaCore.Graphics;
using ManaVoxel.Code.Gameplay.Trains;
using Silk.NET.Input;

namespace Mana.Code.GameModes
{
    internal sealed class TrainLoopController
    {
        private const string TrainPrefix = "Loop Train ";
        private const string TrackPrefix = "Track ";
        private const string TrackDebugPrefix = "Track Debug ";
        private const float DefaultRailTopHeight = 0.18f;
        private const float BoundsCenterMarkerLift = 0.48f;
        private const float SnapMarkerLift = 0.72f;
        private const float PivotMarkerLift = 1.48f;

        private readonly ManaWorld _world;
        private readonly List<TrainCarComponent> _trainCars;
        private readonly List<ManaSceneNode> _trackRoots;
        private readonly List<ManaSceneNode> _transientDebugNodes = new();
        private readonly bool _initialBoundsVisible;
        private bool _boundsVisible;
        private bool _boundsKeyWasDown;
        private bool _boundsVisibilityInitialized;

        private TrainLoopController(ManaWorld world, List<TrainCarComponent> trainCars, List<ManaSceneNode> trackRoots)
        {
            _world = world;
            _trainCars = trainCars;
            _trackRoots = trackRoots;
            _initialBoundsVisible = ReadInitialBoundsVisible();
        }

        public bool HasTrains => _trainCars.Count > 0;
        public bool HasTrackBounds => _trackRoots.Count > 0;
        public bool BoundsVisible => _boundsVisible;

        public static TrainLoopController Create(ManaWorld world)
        {
            var trainCars = new List<TrainCarComponent>();
            var legacyCars = new List<LegacyTrainCar>();
            var trackRoots = new List<ManaSceneNode>();

            lock (world.SceneLock)
            {
                foreach (var scene in world.Scenes)
                {
                    foreach (var child in scene.Children)
                    {
                        CollectTrainNodes(child, trainCars, legacyCars, trackRoots);
                    }
                }
            }

            var railTopHeight = EstimateRailTopHeight(trackRoots);

            foreach (var legacy in legacyCars)
            {
                if (legacy.Node.GetComponents<TrainCarComponent>().Any())
                {
                    continue;
                }

                var component = new TrainCarComponent(legacy.Node)
                {
                    RideHeightOffset = legacy.RideHeightOffset
                };
                component.ConfigureDefaultRoute(legacy.RouteKey, legacy.Index, railTopHeight);
                legacy.Node.Components.Add(component);
                trainCars.Add(component);
            }

            foreach (var car in trainCars)
            {
                car.CaptureBasePose();
                car.Paused = false;
            }

            return new TrainLoopController(world, trainCars, trackRoots);
        }

        public void Update(double deltaTime)
        {
            EnsureBoundsVisibilityInitialized();
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

            foreach (var car in _trainCars)
            {
                car.Restore();
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
            List<TrainCarComponent> trainCars,
            List<LegacyTrainCar> legacyCars,
            List<ManaSceneNode> trackRoots)
        {
            foreach (var component in node.GetComponents<TrainCarComponent>())
            {
                trainCars.Add(component);
            }

            if (TryReadLegacyTrainCar(node, out var legacyCar))
            {
                legacyCars.Add(legacyCar);
            }

            if (node.Name.StartsWith(TrackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                trackRoots.Add(node);
            }

            foreach (var child in node.Children)
            {
                CollectTrainNodes(child, trainCars, legacyCars, trackRoots);
            }
        }

        private static bool TryReadLegacyTrainCar(ManaSceneNode node, out LegacyTrainCar car)
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

            car = new LegacyTrainCar(node, parts[0], index, rideHeightOffset);
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

        private readonly record struct LegacyTrainCar(
            ManaSceneNode Node,
            string RouteKey,
            int Index,
            float RideHeightOffset);
    }
}

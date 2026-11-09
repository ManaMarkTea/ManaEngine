using Mana.Code.Nodes;
using ManaCore.Graphics;

namespace Mana.Code.GameModes
{
    /// <summary>Editor-only bounds, pivot, and snap-point markers for authored track pieces.</summary>
    internal sealed class TrackDebugVisualization
    {
        private const string TrackPrefix = "Track ";
        private const string TrackDebugPrefix = "Track Debug ";
        private const float BoundsCenterMarkerLift = 0.48f;
        private const float SnapMarkerLift = 0.72f;
        private const float PivotMarkerLift = 1.48f;

        private readonly ManaWorld _world;
        private readonly List<ManaSceneNode> _trackRoots = new();
        private readonly List<ManaSceneNode> _transientDebugNodes = new();
        private bool _boundsVisible;
        private bool _pivotsVisible;
        private bool _snapPointsVisible;

        public TrackDebugVisualization(ManaWorld world)
        {
            _world = world;
        }

        public void SetVisibility(bool bounds, bool pivots, bool snapPoints)
        {
            lock (_world.SceneLock)
            {
                if (!bounds && !pivots && !snapPoints)
                {
                    if (!_boundsVisible && !_pivotsVisible && !_snapPointsVisible)
                    {
                        return;
                    }

                    _boundsVisible = false;
                    _pivotsVisible = false;
                    _snapPointsVisible = false;
                    RemoveDebugNodesNoLock();
                    _trackRoots.Clear();
                    return;
                }

                var discoveredRoots = DiscoverTrackRootsNoLock();
                var rootsChanged = discoveredRoots.Count != _trackRoots.Count
                    || discoveredRoots.Where((root, index) => !ReferenceEquals(root, _trackRoots[index])).Any();
                if (!rootsChanged
                    && _boundsVisible == bounds
                    && _pivotsVisible == pivots
                    && _snapPointsVisible == snapPoints)
                {
                    return;
                }

                _boundsVisible = bounds;
                _pivotsVisible = pivots;
                _snapPointsVisible = snapPoints;
                RemoveDebugNodesNoLock();
                _trackRoots.Clear();
                _trackRoots.AddRange(discoveredRoots);

                foreach (var trackRoot in _trackRoots)
                {
                    AddMarkersNoLock(trackRoot, bounds, pivots, snapPoints);
                }
            }
        }

        private void AddMarkersNoLock(ManaSceneNode trackRoot, bool bounds, bool pivots, bool snapPoints)
        {
            if (!TryGetLocalBounds(trackRoot, out var min, out var max))
            {
                return;
            }

            if (bounds)
            {
                var center = (min + max) * 0.5f;
                var halfSize = Vector3.Max((max - min) * 0.5f + new Vector3(0.035f), new Vector3(0.06f));
                AddTransientDebugMesh(
                    trackRoot,
                    "Bounds",
                    center,
                    new ManaWireframeCage(
                        trackRoot.Context,
                        new Vector4(0.05f, 0.95f, 1.0f, 1.0f),
                        halfSize,
                        thickness: 0.035f));
                AddTransientDebugMesh(
                    trackRoot,
                    "Bounds Center",
                    new Vector3(center.X, max.Y + BoundsCenterMarkerLift, center.Z),
                    new ManaCube(
                        trackRoot.Context,
                        new Vector4(0.2f, 0.35f, 1.0f, 1.0f),
                        new Vector3(0.22f)));
            }

            if (pivots)
            {
                AddTransientDebugMesh(
                    trackRoot,
                    "Pivot",
                    new Vector3(0.0f, max.Y + PivotMarkerLift, 0.0f),
                    new ManaCube(
                        trackRoot.Context,
                        new Vector4(1.0f, 0.05f, 0.85f, 1.0f),
                        new Vector3(0.12f, 1.35f, 0.12f)));
            }

            if (snapPoints)
            {
                var points = EstimateTrackSnapPoints(trackRoot, min, max);
                for (var i = 0; i < points.Count; i++)
                {
                    AddTransientDebugMesh(
                        trackRoot,
                        $"Snap {i}",
                        points[i],
                        new ManaCube(
                            trackRoot.Context,
                            new Vector4(1.0f, 0.9f, 0.05f, 1.0f),
                            new Vector3(0.22f, 0.68f, 0.22f)));
                }
            }
        }

        private List<ManaSceneNode> DiscoverTrackRootsNoLock()
        {
            var roots = new List<ManaSceneNode>();
            foreach (var scene in _world.Scenes)
            {
                foreach (var child in scene.Children)
                {
                    CollectTrackRoots(child, roots);
                }
            }

            return roots;
        }

        private static void CollectTrackRoots(ManaSceneNode node, List<ManaSceneNode> output)
        {
            if (node.IsEditorTransient
                || node.Name.StartsWith(TrackDebugPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (node.Name.StartsWith(TrackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                output.Add(node);
            }

            foreach (var child in node.Children)
            {
                CollectTrackRoots(child, output);
            }
        }

        private void RemoveDebugNodesNoLock()
        {
            foreach (var debugNode in _transientDebugNodes)
            {
                debugNode.Parent?.RemoveChild(debugNode);
            }

            _transientDebugNodes.Clear();
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
            if (node.IsEditorTransient)
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
            var node = new ManaSceneNode(trackRoot.Context, Matrix4x4.Identity)
            {
                Name = $"{TrackDebugPrefix}{kind} {trackRoot.Name}",
                Position = localPosition,
                Rotation = Vector3.Zero,
                Scale = Vector3.One,
                IncludeInEnvironmentCapture = false,
                IsEditorTransient = true,
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
    }
}

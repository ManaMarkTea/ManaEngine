using Mana.Code.Nodes;
using ManaCore.Graphics;

namespace Mana.Code.Components
{
    public static class ManaCollisionBounds
    {
        public static bool TryGetVisualBox(ManaSceneNode node, out Vector3 size, out Vector3 offset)
        {
            size = Vector3.Zero;
            offset = Vector3.Zero;

            if (!Matrix4x4.Invert(node.WorldTransform, out var worldToRoot))
            {
                return false;
            }

            var hasBounds = false;
            var localMin = Vector3.Zero;
            var localMax = Vector3.Zero;
            CollectVisualBounds(node, worldToRoot, ref hasBounds, ref localMin, ref localMax);
            if (!hasBounds)
            {
                return false;
            }

            offset = (localMin + localMax) * 0.5f;
            var localSize = localMax - localMin;
            Matrix4x4.Decompose(node.WorldTransform, out var worldScale, out _, out _);
            size = new Vector3(
                MathF.Abs(localSize.X * worldScale.X),
                MathF.Abs(localSize.Y * worldScale.Y),
                MathF.Abs(localSize.Z * worldScale.Z));

            return size.X > 0.0f && size.Y > 0.0f && size.Z > 0.0f;
        }

        public static bool MatchesVisualBox(
            ManaSceneNode node,
            Vector3 size,
            Vector3 offset,
            float epsilon = 0.001f)
        {
            return TryGetVisualBox(node, out var visualSize, out var visualOffset)
                && NearlyEqual(size, visualSize, epsilon)
                && NearlyEqual(offset, visualOffset, epsilon);
        }

        public static bool TryGetVisualHullPoints(ManaSceneNode node, out Vector3[] points)
        {
            points = Array.Empty<Vector3>();
            if (!Matrix4x4.Invert(node.WorldTransform, out var worldToRoot))
            {
                return false;
            }

            var localPoints = new List<Vector3>();
            CollectVisualPoints(node, worldToRoot, localPoints);
            if (localPoints.Count < 4)
            {
                return false;
            }

            Matrix4x4.Decompose(node.WorldTransform, out var worldScale, out _, out _);
            points = localPoints
                .Select(point => new Vector3(
                    point.X * worldScale.X,
                    point.Y * worldScale.Y,
                    point.Z * worldScale.Z))
                .Distinct()
                .ToArray();
            return points.Length >= 4;
        }

        private static void CollectVisualBounds(
            ManaSceneNode node,
            Matrix4x4 rootWorldToLocal,
            ref bool hasBounds,
            ref Vector3 localMin,
            ref Vector3 localMax)
        {
            var nodeToRoot = node.WorldTransform * rootWorldToLocal;

            foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
            {
                AddRendererBounds(renderer, nodeToRoot, ref hasBounds, ref localMin, ref localMax);
            }

            foreach (var child in node.Children)
            {
                CollectVisualBounds(child, rootWorldToLocal, ref hasBounds, ref localMin, ref localMax);
            }
        }

        private static void AddRendererBounds(
            MeshRendererComponent renderer,
            Matrix4x4 nodeToRoot,
            ref bool hasBounds,
            ref Vector3 localMin,
            ref Vector3 localMax)
        {
            if (renderer.Mesh == null)
            {
                return;
            }

            ManaMeshCpuData cpuData;
            try
            {
                cpuData = renderer.Mesh.CpuData ?? renderer.Mesh.GetOrCreateCpuData();
            }
            catch (InvalidOperationException)
            {
                return;
            }

            foreach (var position in cpuData.Positions)
            {
                var rootPosition = Vector3.Transform(position, nodeToRoot);
                if (!hasBounds)
                {
                    localMin = rootPosition;
                    localMax = rootPosition;
                    hasBounds = true;
                    continue;
                }

                localMin = Vector3.Min(localMin, rootPosition);
                localMax = Vector3.Max(localMax, rootPosition);
            }
        }

        private static void CollectVisualPoints(
            ManaSceneNode node,
            Matrix4x4 rootWorldToLocal,
            List<Vector3> points)
        {
            var nodeToRoot = node.WorldTransform * rootWorldToLocal;
            foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
            {
                if (renderer.Mesh == null)
                {
                    continue;
                }

                ManaMeshCpuData cpuData;
                try
                {
                    cpuData = renderer.Mesh.CpuData ?? renderer.Mesh.GetOrCreateCpuData();
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                foreach (var position in cpuData.Positions)
                {
                    points.Add(Vector3.Transform(position, nodeToRoot));
                }
            }

            foreach (var child in node.Children)
            {
                CollectVisualPoints(child, rootWorldToLocal, points);
            }
        }

        private static bool NearlyEqual(Vector3 left, Vector3 right, float epsilon)
        {
            return MathF.Abs(left.X - right.X) <= epsilon
                && MathF.Abs(left.Y - right.Y) <= epsilon
                && MathF.Abs(left.Z - right.Z) <= epsilon;
        }
    }
}

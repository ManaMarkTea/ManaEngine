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

        /// <summary>
        /// Refits a collider that follows its model's visual bounds when the model's box has changed, keeping its
        /// settings. Returns the collider in use afterwards -- a new one when it was refitted.
        /// </summary>
        public static ManaCollisionComponent Refit(ManaCollisionComponent collision)
        {
            if (collision.FollowsVisualBounds != true || collision.Shape is not BepuPhysics.Collidables.Box box)
            {
                return collision;
            }

            var owner = collision.Owner;
            if (!TryGetVisualBox(owner, out var size, out var offset)
                || (NearlyEqual(new Vector3(box.Width, box.Height, box.Length), size, 0.001f) && NearlyEqual(collision.Position, offset, 0.001f)))
            {
                return collision;
            }

            var showDebug = collision.ShowDebug;
            var syncNodeToPhysics = collision.SyncNodeToPhysics;
            owner.Context.Physics.RemoveBody(collision.Body);
            var replacement = owner.Context.Physics.AddBoxBody(owner, size, offset);
            replacement.ShowDebug = showDebug;
            replacement.SyncNodeToPhysics = syncNodeToPhysics;
            replacement.FollowsVisualBounds = true;
            return replacement;
        }

        /// <summary>Refits every collider in the world that follows its model's bounds and has fallen behind it.</summary>
        public static void RefitAll(ManaWorld world)
        {
            var following = new List<ManaCollisionComponent>();
            void Visit(ManaSceneNode node)
            {
                foreach (var component in node.Components)
                {
                    if (component is ManaCollisionComponent { FollowsVisualBounds: true } collision)
                    {
                        following.Add(collision);
                    }
                }
                foreach (var child in node.Children)
                {
                    Visit(child);
                }
            }

            lock (world.SceneLock)
            {
                foreach (var scene in world.Scenes)
                {
                    foreach (var node in scene.Children)
                    {
                        Visit(node);
                    }
                }

                // Collected first: a refit swaps the component out of the node it was found on.
                foreach (var collision in following)
                {
                    Refit(collision);
                }
            }
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

            // Editor-only children -- a moving platform's destination ghost, selection helpers -- are not the model.
            foreach (var child in node.Children.Where(ManaSceneNode.IsPartOfLook))
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

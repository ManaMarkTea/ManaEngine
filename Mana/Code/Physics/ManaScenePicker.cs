using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaCore.Graphics;

namespace Mana.Code
{
    // Picks scene nodes by ray-testing each MeshRendererComponent's triangles in local space
    // (AABB used only as a broad-phase filter), rather than the physics simulation, so it works
    // on nodes with no collider (gltf imports, shapes) and resolves overlapping parts correctly.
    public static class ManaScenePicker
    {
        public static ManaSceneNode? Pick(
            IEnumerable<ManaScene> scenes,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            HashSet<ManaSceneNode>? excludeNodes = null)
        {
            ManaSceneNode? closestNode = null;
            var closestDistance = float.PositiveInfinity;

            foreach (var scene in scenes)
            {
                foreach (var node in scene.Children)
                {
                    PickNode(node, rayOrigin, rayDirection, excludeNodes, ref closestNode, ref closestDistance);
                }
            }

            return closestNode;
        }

        private static void PickNode(
            ManaSceneNode node,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            HashSet<ManaSceneNode>? excludeNodes,
            ref ManaSceneNode? closestNode,
            ref float closestDistance)
        {
            if (excludeNodes == null || !excludeNodes.Contains(node))
            {
                if (TryIntersect(node, rayOrigin, rayDirection, out var distance) && distance < closestDistance)
                {
                    closestDistance = distance;
                    closestNode = node;
                }
            }

            foreach (var child in node.Children)
            {
                PickNode(child, rayOrigin, rayDirection, excludeNodes, ref closestNode, ref closestDistance);
            }
        }

        private static bool TryIntersect(ManaSceneNode node, Vector3 rayOrigin, Vector3 rayDirection, out float worldDistance)
        {
            worldDistance = float.PositiveInfinity;

            if (!Matrix4x4.Invert(node.WorldTransform, out var worldToLocal))
            {
                return false;
            }

            var localOrigin = Vector3.Transform(rayOrigin, worldToLocal);
            var localDirection = Vector3.TransformNormal(rayDirection, worldToLocal);
            if (localDirection.LengthSquared() < 1e-12f)
            {
                return false;
            }

            var bestLocalT = float.PositiveInfinity;
            var hitAny = false;

            foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
            {
                if (renderer.Mesh == null || !TryGetCpuData(renderer.Mesh, out var cpuData))
                {
                    continue;
                }

                GetLocalBounds(cpuData, out var localMin, out var localMax);
                if (!RayIntersectsAabb(localOrigin, localDirection, localMin, localMax))
                {
                    continue;
                }

                if (TryIntersectTriangles(localOrigin, localDirection, cpuData, out var meshLocalT) && meshLocalT < bestLocalT)
                {
                    bestLocalT = meshLocalT;
                    hitAny = true;
                }
            }

            if (!hitAny)
            {
                return false;
            }

            var localHitPosition = localOrigin + localDirection * bestLocalT;
            var worldHitPosition = Vector3.Transform(localHitPosition, node.WorldTransform);
            worldDistance = Vector3.Distance(rayOrigin, worldHitPosition);
            return true;
        }

        private static bool TryIntersectTriangles(Vector3 origin, Vector3 direction, ManaMeshCpuData cpuData, out float t)
        {
            t = float.PositiveInfinity;
            var hitAny = false;
            var positions = cpuData.Positions;
            var indexes = cpuData.Indexes;

            for (var i = 0; i + 2 < indexes.Length; i += 3)
            {
                var v0 = positions[indexes[i]];
                var v1 = positions[indexes[i + 1]];
                var v2 = positions[indexes[i + 2]];

                if (RayIntersectsTriangle(origin, direction, v0, v1, v2, out var triangleT) && triangleT < t)
                {
                    t = triangleT;
                    hitAny = true;
                }
            }

            return hitAny;
        }

        private static bool RayIntersectsTriangle(Vector3 origin, Vector3 direction, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
        {
            const float epsilon = 1e-7f;
            t = 0f;

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var h = Vector3.Cross(direction, edge2);
            var a = Vector3.Dot(edge1, h);
            if (System.MathF.Abs(a) < epsilon)
            {
                return false;
            }

            var f = 1.0f / a;
            var s = origin - v0;
            var u = f * Vector3.Dot(s, h);
            if (u < 0.0f || u > 1.0f)
            {
                return false;
            }

            var q = Vector3.Cross(s, edge1);
            var v = f * Vector3.Dot(direction, q);
            if (v < 0.0f || u + v > 1.0f)
            {
                return false;
            }

            t = f * Vector3.Dot(edge2, q);
            return t > epsilon;
        }

        private static bool TryGetCpuData(ManaMesh mesh, out ManaMeshCpuData cpuData)
        {
            var data = mesh.CpuData;
            if (data == null)
            {
                try
                {
                    data = mesh.GetOrCreateCpuData();
                }
                catch (System.InvalidOperationException)
                {
                    cpuData = null!;
                    return false;
                }
            }

            cpuData = data;
            return data.Positions.Length > 0 && data.Indexes.Length >= 3;
        }

        private static void GetLocalBounds(ManaMeshCpuData cpuData, out Vector3 min, out Vector3 max)
        {
            min = cpuData.Positions[0];
            max = cpuData.Positions[0];
            foreach (var position in cpuData.Positions)
            {
                min = Vector3.Min(min, position);
                max = Vector3.Max(max, position);
            }
        }

        private static bool RayIntersectsAabb(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max)
        {
            var tMin = 0f;
            var tMax = float.PositiveInfinity;

            return IntersectSlab(origin.X, direction.X, min.X, max.X, ref tMin, ref tMax) &&
                   IntersectSlab(origin.Y, direction.Y, min.Y, max.Y, ref tMin, ref tMax) &&
                   IntersectSlab(origin.Z, direction.Z, min.Z, max.Z, ref tMin, ref tMax);
        }

        private static bool IntersectSlab(float origin, float direction, float min, float max, ref float tMin, ref float tMax)
        {
            const float epsilon = 1e-9f;
            if (System.MathF.Abs(direction) < epsilon)
            {
                return origin >= min && origin <= max;
            }

            var t1 = (min - origin) / direction;
            var t2 = (max - origin) / direction;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tMin = System.MathF.Max(tMin, t1);
            tMax = System.MathF.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}

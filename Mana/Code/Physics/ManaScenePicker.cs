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
        // Node is the picked scene node; Renderer is specifically which MeshRendererComponent on that
        // node won the closest-hit test (a node can host several, e.g. PlanetNode's shared-root patch
        // nodes) -- null only if somehow no renderer contributed to the hit.
        public readonly record struct ScenePickResult(ManaSceneNode Node, MeshRendererComponent? Renderer);
        public readonly record struct ScenePickHitResult(
            ManaSceneNode Node,
            MeshRendererComponent? Renderer,
            Vector3 Position,
            Vector3 Normal,
            float Distance);

        public static ScenePickResult? Pick(
            IEnumerable<ManaScene> scenes,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            HashSet<ManaSceneNode>? excludeNodes = null,
            bool useBoundsFallback = false)
        {
            ManaSceneNode? closestNode = null;
            MeshRendererComponent? closestRenderer = null;
            var closestDistance = float.PositiveInfinity;

            foreach (var scene in scenes)
            {
                foreach (var node in scene.Children)
                {
                    PickNode(
                        node,
                        rayOrigin,
                        rayDirection,
                        excludeNodes,
                        useBoundsFallback,
                        ref closestNode,
                        ref closestRenderer,
                        ref closestDistance);
                }
            }

            return closestNode != null ? new ScenePickResult(closestNode, closestRenderer) : null;
        }

        public static ScenePickHitResult? PickWithHit(
            IEnumerable<ManaScene> scenes,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            HashSet<ManaSceneNode>? excludeNodes = null)
        {
            ManaSceneNode? closestNode = null;
            MeshRendererComponent? closestRenderer = null;
            var closestDistance = float.PositiveInfinity;
            var closestPosition = Vector3.Zero;
            var closestNormal = Vector3.UnitY;

            foreach (var scene in scenes)
            {
                foreach (var node in scene.Children)
                {
                    PickNodeWithHit(
                        node,
                        rayOrigin,
                        rayDirection,
                        excludeNodes,
                        ref closestNode,
                        ref closestRenderer,
                        ref closestDistance,
                        ref closestPosition,
                        ref closestNormal);
                }
            }

            return closestNode != null
                ? new ScenePickHitResult(closestNode, closestRenderer, closestPosition, closestNormal, closestDistance)
                : null;
        }

        private static void PickNode(
            ManaSceneNode node,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            HashSet<ManaSceneNode>? excludeNodes,
            bool useBoundsFallback,
            ref ManaSceneNode? closestNode,
            ref MeshRendererComponent? closestRenderer,
            ref float closestDistance)
        {
            if (excludeNodes == null || !excludeNodes.Contains(node))
            {
                if (TryIntersect(node, rayOrigin, rayDirection, useBoundsFallback, out var distance, out var renderer)
                    && distance < closestDistance)
                {
                    closestDistance = distance;
                    closestNode = node;
                    closestRenderer = renderer;
                }
            }

            foreach (var child in node.Children)
            {
                PickNode(
                    child,
                    rayOrigin,
                    rayDirection,
                    excludeNodes,
                    useBoundsFallback,
                    ref closestNode,
                    ref closestRenderer,
                    ref closestDistance);
            }
        }

        private static void PickNodeWithHit(
            ManaSceneNode node,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            HashSet<ManaSceneNode>? excludeNodes,
            ref ManaSceneNode? closestNode,
            ref MeshRendererComponent? closestRenderer,
            ref float closestDistance,
            ref Vector3 closestPosition,
            ref Vector3 closestNormal)
        {
            if (excludeNodes == null || !excludeNodes.Contains(node))
            {
                if (TryIntersectWithHit(node, rayOrigin, rayDirection, out var distance, out var renderer, out var position, out var normal) &&
                    distance < closestDistance)
                {
                    closestDistance = distance;
                    closestNode = node;
                    closestRenderer = renderer;
                    closestPosition = position;
                    closestNormal = normal;
                }
            }

            foreach (var child in node.Children)
            {
                PickNodeWithHit(
                    child,
                    rayOrigin,
                    rayDirection,
                    excludeNodes,
                    ref closestNode,
                    ref closestRenderer,
                    ref closestDistance,
                    ref closestPosition,
                    ref closestNormal);
            }
        }

        private static bool TryIntersect(
            ManaSceneNode node,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            bool useBoundsFallback,
            out float worldDistance,
            out MeshRendererComponent? hitRenderer)
        {
            worldDistance = float.PositiveInfinity;
            hitRenderer = null;

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
                if (!renderer.IsVisible || renderer.Mesh == null || !TryGetCpuData(renderer.Mesh, out var cpuData))
                {
                    continue;
                }

                GetLocalBounds(cpuData, out var localMin, out var localMax);

                // Recenter the ray AND every triangle vertex around this mesh's own local bounding-box
                // center before doing any of the actual intersection math, instead of testing directly
                // against coordinates whose absolute magnitude can be much larger than the mesh itself
                // (e.g. a planet-patch mesh sits at radial distance ~40 world units regardless of how
                // small the patch's own triangles are). Everything below uses single-precision float;
                // subtracting two nearly-equal ~40-magnitude numbers to get a tiny edge vector loses
                // most of its significant digits (catastrophic cancellation), which is exactly what a
                // deeply split (and therefore tiny) PlanetPatchMesh triggers -- its ray/triangle tests
                // start spuriously failing, so the ray falls through to whatever's behind it. `t` is a
                // scalar distance along the ray, unaffected by translating the whole local coordinate
                // system by a constant offset, so this doesn't change the result -- only its numerical
                // stability.
                var center = (localMin + localMax) * 0.5f;
                var recenteredOrigin = localOrigin - center;
                var recenteredMin = localMin - center;
                var recenteredMax = localMax - center;

                if (!RayIntersectsAabb(recenteredOrigin, localDirection, recenteredMin, recenteredMax, out var boxLocalT))
                {
                    continue;
                }

                if (TryIntersectTriangles(recenteredOrigin, localDirection, cpuData, center, out var meshLocalT) && meshLocalT < bestLocalT)
                {
                    bestLocalT = meshLocalT;
                    hitAny = true;
                    hitRenderer = renderer;
                }
                else if ((useBoundsFallback || node.LevelSource != null) && boxLocalT < bestLocalT)
                {
                    // Authored editor objects should remain easy to select even when very flat geometry
                    // grazes the ray closely enough that triangle precision rejects it.
                    bestLocalT = boxLocalT;
                    hitAny = true;
                    hitRenderer = renderer;
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

        private static bool TryIntersectWithHit(
            ManaSceneNode node,
            Vector3 rayOrigin,
            Vector3 rayDirection,
            out float worldDistance,
            out MeshRendererComponent? hitRenderer,
            out Vector3 worldHitPosition,
            out Vector3 worldHitNormal)
        {
            worldDistance = float.PositiveInfinity;
            hitRenderer = null;
            worldHitPosition = Vector3.Zero;
            worldHitNormal = Vector3.UnitY;

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
            var bestLocalNormal = Vector3.UnitY;
            var hitAny = false;

            foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
            {
                if (!renderer.IsVisible || renderer.Mesh == null || !TryGetCpuData(renderer.Mesh, out var cpuData))
                {
                    continue;
                }

                GetLocalBounds(cpuData, out var localMin, out var localMax);

                var center = (localMin + localMax) * 0.5f;
                var recenteredOrigin = localOrigin - center;
                var recenteredMin = localMin - center;
                var recenteredMax = localMax - center;

                if (!RayIntersectsAabb(recenteredOrigin, localDirection, recenteredMin, recenteredMax))
                {
                    continue;
                }

                if (TryIntersectTrianglesWithNormal(recenteredOrigin, localDirection, cpuData, center, out var meshLocalT, out var meshLocalNormal) &&
                    meshLocalT < bestLocalT)
                {
                    bestLocalT = meshLocalT;
                    bestLocalNormal = meshLocalNormal;
                    hitAny = true;
                    hitRenderer = renderer;
                }
            }

            if (!hitAny)
            {
                return false;
            }

            var localHitPosition = localOrigin + localDirection * bestLocalT;
            worldHitPosition = Vector3.Transform(localHitPosition, node.WorldTransform);
            worldHitNormal = Vector3.Normalize(Vector3.TransformNormal(bestLocalNormal, node.WorldTransform));
            if (worldHitNormal.LengthSquared() < 1e-12f)
            {
                worldHitNormal = Vector3.UnitY;
            }

            worldDistance = Vector3.Distance(rayOrigin, worldHitPosition);
            return true;
        }

        private static bool TryIntersectTriangles(Vector3 origin, Vector3 direction, ManaMeshCpuData cpuData, Vector3 center, out float t)
        {
            t = float.PositiveInfinity;
            var hitAny = false;
            var positions = cpuData.Positions;
            var indexes = cpuData.Indexes;

            for (var i = 0; i + 2 < indexes.Length; i += 3)
            {
                var v0 = positions[indexes[i]] - center;
                var v1 = positions[indexes[i + 1]] - center;
                var v2 = positions[indexes[i + 2]] - center;

                if (RayIntersectsTriangle(origin, direction, v0, v1, v2, out var triangleT) && triangleT < t)
                {
                    t = triangleT;
                    hitAny = true;
                }
            }

            return hitAny;
        }

        private static bool TryIntersectTrianglesWithNormal(
            Vector3 origin,
            Vector3 direction,
            ManaMeshCpuData cpuData,
            Vector3 center,
            out float t,
            out Vector3 normal)
        {
            t = float.PositiveInfinity;
            normal = Vector3.UnitY;
            var hitAny = false;
            var positions = cpuData.Positions;
            var indexes = cpuData.Indexes;

            for (var i = 0; i + 2 < indexes.Length; i += 3)
            {
                var v0 = positions[indexes[i]] - center;
                var v1 = positions[indexes[i + 1]] - center;
                var v2 = positions[indexes[i + 2]] - center;

                if (RayIntersectsTriangle(origin, direction, v0, v1, v2, out var triangleT) && triangleT < t)
                {
                    t = triangleT;
                    var faceNormal = Vector3.Cross(v1 - v0, v2 - v0);
                    normal = faceNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(faceNormal) : Vector3.UnitY;
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

            // The determinant `a` is the "is the ray parallel to this triangle's plane" check, and its
            // magnitude scales with the triangle's OWN size (roughly |edge1|*|edge2|) -- a fixed
            // epsilon tuned for "normal" scene-scale triangles spuriously rejects small-but-perfectly-
            // valid ones, since their determinant shrinks well below any fixed absolute threshold even
            // for a dead-on hit. Confirmed empirically: for a ~5e-5-unit triangle (roughly what an
            // 8-times-split PlanetPatchMesh triangle looks like), `a` comes out around 2.5e-9, already
            // below the original fixed 1e-7 epsilon -- the ray silently "missed" perfectly good
            // geometry and fell through to whatever was behind it. Scaling the threshold by the
            // triangle's own edge lengths keeps the SAME effective epsilon for normal-sized triangles
            // (edges ~1) while correctly staying permissive for tiny ones.
            var threshold = epsilon * edge1.Length() * edge2.Length();
            if (System.MathF.Abs(a) < threshold)
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
            return RayIntersectsAabb(origin, direction, min, max, out _);
        }

        private static bool RayIntersectsAabb(
            Vector3 origin,
            Vector3 direction,
            Vector3 min,
            Vector3 max,
            out float t)
        {
            var tMin = 0f;
            var tMax = float.PositiveInfinity;

            var hit = IntersectSlab(origin.X, direction.X, min.X, max.X, ref tMin, ref tMax) &&
                      IntersectSlab(origin.Y, direction.Y, min.Y, max.Y, ref tMin, ref tMax) &&
                      IntersectSlab(origin.Z, direction.Z, min.Z, max.Z, ref tMin, ref tMax);
            t = hit ? tMin : float.PositiveInfinity;
            return hit;
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

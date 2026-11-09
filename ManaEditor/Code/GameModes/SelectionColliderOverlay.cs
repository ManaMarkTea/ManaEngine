using BepuPhysics.Collidables;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;

namespace Mana.Code.GameModes
{
    // The yellow wireframes showing the selection's colliders.
    internal sealed class SelectionColliderOverlay(ManaContext context, HashSet<ManaSceneNode> pickingExclusions)
    {
        private static readonly Vector4 ColliderDebugColor = new(1.0f, 0.86f, 0.05f, 1.0f);
        private ManaSceneNode? _selectionColliderDebugRoot;
        private ManaSceneNode? _selectionColliderDebugTarget;
        private readonly List<ColliderDebugEntry> _selectionColliderDebugEntries = new();

        private sealed record ColliderDebugEntry(
            ManaCollisionComponent Collision,
            ManaSceneNode Node,
            Vector3 Size);

        public void Sync(ManaSceneNode? selectedNode)
        {
            var collisions = new List<ManaCollisionComponent>();
            CollectSelectedBoxCollisions(selectedNode, collisions);
            CollectPinnedBoxCollisions(collisions);

            if (collisions.Count == 0 || !ManaEditorGizmos.Visible)
            {
                Clear();
                return;
            }

            if (NeedsColliderDebugRebuild(selectedNode, collisions))
            {
                RebuildSelectionColliderDebug(selectedNode, collisions);
            }

            if (!UpdateColliderDebugTransforms())
            {
                Clear();
            }
        }

        private void CollectSelectedBoxCollisions(ManaSceneNode? selectedNode, List<ManaCollisionComponent> output)
        {
            if (selectedNode == null)
            {
                return;
            }

            var before = output.Count;
            CollectNodeBoxCollisions(selectedNode, output, pinnedOnly: false);
            if (output.Count > before)
            {
                return;
            }

            for (var current = selectedNode.Parent; current != null; current = current.Parent)
            {
                before = output.Count;
                CollectOwnBoxCollisions(current, output, pinnedOnly: false);
                if (output.Count > before)
                {
                    return;
                }
            }
        }

        private void CollectPinnedBoxCollisions(List<ManaCollisionComponent> output)
        {
            lock (context.World.SceneLock)
            {
                foreach (var scene in context.World.Scenes)
                {
                    foreach (var child in scene.Children)
                    {
                        CollectNodeBoxCollisions(child, output, pinnedOnly: true);
                    }
                }
            }
        }

        private void CollectNodeBoxCollisions(
            ManaSceneNode node,
            List<ManaCollisionComponent> output,
            bool pinnedOnly)
        {
            if (ReferenceEquals(node, _selectionColliderDebugRoot))
            {
                return;
            }

            CollectOwnBoxCollisions(node, output, pinnedOnly);

            foreach (var child in node.Children)
            {
                CollectNodeBoxCollisions(child, output, pinnedOnly);
            }
        }

        private void CollectOwnBoxCollisions(
            ManaSceneNode node,
            List<ManaCollisionComponent> output,
            bool pinnedOnly)
        {
            foreach (var collision in node.GetComponents<ManaCollisionComponent>())
            {
                if (pinnedOnly && !collision.ShowDebug)
                {
                    continue;
                }

                if (collision.Shape is not (Box or ConvexHull)
                    || !context.Physics.Simulation.Bodies.BodyExists(collision.Body))
                {
                    continue;
                }

                if (!output.Any(existing => ReferenceEquals(existing, collision)))
                {
                    output.Add(collision);
                }
            }
        }

        private bool NeedsColliderDebugRebuild(
            ManaSceneNode? selectedNode,
            IReadOnlyList<ManaCollisionComponent> collisions)
        {
            if (!ReferenceEquals(_selectionColliderDebugTarget, selectedNode)
                || _selectionColliderDebugRoot == null
                || _selectionColliderDebugEntries.Count != collisions.Count)
            {
                return true;
            }

            for (var i = 0; i < collisions.Count; i++)
            {
                if (!ReferenceEquals(_selectionColliderDebugEntries[i].Collision, collisions[i]))
                {
                    return true;
                }

                if (!_selectionColliderDebugEntries[i].Size.Equals(CollisionSize(collisions[i])))
                {
                    return true;
                }
            }

            return false;
        }

        private void RebuildSelectionColliderDebug(
            ManaSceneNode? selectedNode,
            IReadOnlyList<ManaCollisionComponent> collisions)
        {
            lock (context.World.SceneLock)
            {
                ClearSelectionColliderDebugNoLock();

                var root = new ManaSceneNode(context, Matrix4x4.Identity)
                {
                    Name = "Selection Collider Debug (Transient)",
                    IncludeInEnvironmentCapture = false,
                    IsEditorTransient = true,
                    LevelSource = null
                };

                context.World.DefaultScene.Children.Add(root);
                pickingExclusions.Add(root);

                _selectionColliderDebugRoot = root;
                _selectionColliderDebugTarget = selectedNode;

                foreach (var collision in collisions)
                {
                    var size = CollisionSize(collision);
                    var debugMesh = CreateColliderDebugMesh(collision, size);

                    var node = new ManaSceneNode(context, Matrix4x4.Identity)
                    {
                        Name = $"Collider {CollisionOwnerName(collision.Owner)}",
                        IncludeInEnvironmentCapture = false,
                        IsEditorTransient = true,
                        LevelSource = null
                    };

                    node.Components.Add(new MeshRendererComponent(node, debugMesh)
                    {
                        DrawAfterScene = true
                    });

                    root.AddChild(node);
                    pickingExclusions.Add(node);
                    _selectionColliderDebugEntries.Add(new ColliderDebugEntry(collision, node, size));
                }
            }
        }

        private bool UpdateColliderDebugTransforms()
        {
            foreach (var entry in _selectionColliderDebugEntries)
            {
                if (!context.Physics.Simulation.Bodies.BodyExists(entry.Collision.Body))
                {
                    return false;
                }

                var pose = context.Physics.Simulation.Bodies[entry.Collision.Body].Pose;
                var transform = Matrix4x4.CreateFromQuaternion(pose.Orientation)
                    * Matrix4x4.CreateTranslation(pose.Position);
                entry.Node.SetLocalTransform(transform);
                entry.Node.WorldTransform = transform;
            }

            return true;
        }

        public void Clear()
        {
            lock (context.World.SceneLock)
            {
                ClearSelectionColliderDebugNoLock();
            }
        }

        private void ClearSelectionColliderDebugNoLock()
        {
            foreach (var entry in _selectionColliderDebugEntries)
            {
                pickingExclusions.Remove(entry.Node);
            }

            if (_selectionColliderDebugRoot != null)
            {
                pickingExclusions.Remove(_selectionColliderDebugRoot);
                _selectionColliderDebugRoot.Parent?.RemoveChild(_selectionColliderDebugRoot);

                foreach (var scene in context.World.Scenes)
                {
                    scene.Children.Remove(_selectionColliderDebugRoot);
                }
            }

            _selectionColliderDebugRoot = null;
            _selectionColliderDebugTarget = null;
            _selectionColliderDebugEntries.Clear();
        }

        private static Vector3 CollisionSize(ManaCollisionComponent collision)
        {
            if (collision.Shape is Box box)
            {
                return new Vector3(box.Width, box.Height, box.Length);
            }

            if (collision.Shape is ConvexHull hull)
            {
                hull.ComputeBounds(Quaternion.Identity, out var min, out var max);
                return max - min;
            }

            return Vector3.Zero;
        }

        private ManaMesh CreateColliderDebugMesh(ManaCollisionComponent collision, Vector3 size)
        {
            if (collision.Shape is not ConvexHull hull)
            {
                return new ManaWireframeCage(
                    context,
                    ColliderDebugColor,
                    Vector3.Max(size * 0.5f, new Vector3(0.025f)),
                    thickness: 0.035f);
            }

            var faces = new List<ManaLinePolyline>(hull.FaceToVertexIndicesStart.Length);
            for (var faceIndex = 0; faceIndex < hull.FaceToVertexIndicesStart.Length; faceIndex++)
            {
                hull.GetVertexIndicesForFace(faceIndex, out var vertexIndices);
                var points = new List<Vector3>(vertexIndices.Length);
                for (var vertexIndex = 0; vertexIndex < vertexIndices.Length; vertexIndex++)
                {
                    hull.GetPoint(vertexIndices[vertexIndex], out var point);
                    points.Add(point);
                }

                if (points.Count >= 3)
                {
                    faces.Add(new ManaLinePolyline(points, closed: true));
                }
            }

            return new ManaLineMesh(
                context,
                faces,
                thickness: 0.035f,
                ColliderDebugColor,
                emissionStrength: 1.5f);
        }

        private static string CollisionOwnerName(ManaSceneNode owner)
        {
            return string.IsNullOrWhiteSpace(owner.Name) ? owner.GetType().Name : owner.Name;
        }
    }
}

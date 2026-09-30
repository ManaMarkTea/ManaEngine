using BepuPhysics.Collidables;
using ManaUI;
using System.Linq;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using Mana.Code.UI;
using ManaVoxel.Code.Services;
using ManaVoxel.Code.Tools;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Levels;
using ManaVoxel.Code.Search;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;
using ManaCore.Diagnostics;
using ManaCore.Graphics;
using ManaCore.Loading;
using ManaGraphics.Code.Diagnostics;
using ManaGraphics.Code.Graphics;
using ManaGeoTools.Code;
using ManaGeoTools.Code.Planet;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Mana.Code.GameModes
{
    // The yellow wireframes showing the selection's colliders.
    public partial class VoxelEditorGameMode
    {
        private static readonly Vector4 ColliderDebugColor = new(1.0f, 0.86f, 0.05f, 1.0f);
        private ManaSceneNode? _selectionColliderDebugRoot;
        private ManaSceneNode? _selectionColliderDebugTarget;
        private readonly List<ColliderDebugEntry> _selectionColliderDebugEntries = new();

        private sealed record ColliderDebugEntry(
            ManaCollisionComponent Collision,
            ManaSceneNode Node,
            Vector3 Size);

        private void SyncSelectionColliderDebug(ManaSceneNode? selectedNode)
        {
            var collisions = new List<ManaCollisionComponent>();
            CollectSelectedBoxCollisions(selectedNode, collisions);
            CollectPinnedBoxCollisions(collisions);

            if (collisions.Count == 0 || !ManaEditorGizmos.Visible)
            {
                ClearSelectionColliderDebug();
                return;
            }

            if (NeedsColliderDebugRebuild(selectedNode, collisions))
            {
                RebuildSelectionColliderDebug(selectedNode, collisions);
            }

            if (!UpdateColliderDebugTransforms())
            {
                ClearSelectionColliderDebug();
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
            lock (Context.World.SceneLock)
            {
                foreach (var scene in Context.World.Scenes)
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
                    || !Context.Physics.Simulation.Bodies.BodyExists(collision.Body))
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
            lock (Context.World.SceneLock)
            {
                ClearSelectionColliderDebugNoLock();

                var root = new ManaSceneNode(Context, Matrix4x4.Identity)
                {
                    Name = "Selection Collider Debug (Transient)",
                    IncludeInEnvironmentCapture = false,
                    IsEditorTransient = true,
                    LevelSource = null
                };

                Context.World.DefaultScene.Children.Add(root);
                _pickingExclusions.Add(root);

                _selectionColliderDebugRoot = root;
                _selectionColliderDebugTarget = selectedNode;

                foreach (var collision in collisions)
                {
                    var size = CollisionSize(collision);
                    var debugMesh = CreateColliderDebugMesh(collision, size);

                    var node = new ManaSceneNode(Context, Matrix4x4.Identity)
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
                    _pickingExclusions.Add(node);
                    _selectionColliderDebugEntries.Add(new ColliderDebugEntry(collision, node, size));
                }
            }
        }

        private bool UpdateColliderDebugTransforms()
        {
            foreach (var entry in _selectionColliderDebugEntries)
            {
                if (!Context.Physics.Simulation.Bodies.BodyExists(entry.Collision.Body))
                {
                    return false;
                }

                var pose = Context.Physics.Simulation.Bodies[entry.Collision.Body].Pose;
                var transform = Matrix4x4.CreateFromQuaternion(pose.Orientation)
                    * Matrix4x4.CreateTranslation(pose.Position);
                entry.Node.SetLocalTransform(transform);
                entry.Node.WorldTransform = transform;
            }

            return true;
        }

        private void ClearSelectionColliderDebug()
        {
            lock (Context.World.SceneLock)
            {
                ClearSelectionColliderDebugNoLock();
            }
        }

        private void ClearSelectionColliderDebugNoLock()
        {
            foreach (var entry in _selectionColliderDebugEntries)
            {
                _pickingExclusions.Remove(entry.Node);
            }

            if (_selectionColliderDebugRoot != null)
            {
                _pickingExclusions.Remove(_selectionColliderDebugRoot);
                _selectionColliderDebugRoot.Parent?.RemoveChild(_selectionColliderDebugRoot);

                foreach (var scene in Context.World.Scenes)
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
                    Context,
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
                Context,
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

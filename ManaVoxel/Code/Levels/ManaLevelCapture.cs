using BepuPhysics.Collidables;
using Mana;
using Mana.Code;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// Walks a world and writes down what somebody placed in it.
    /// <para>
    /// The rule is one line long: a node is captured when it carries a <see cref="ManaLevelSource"/>, and
    /// its subtree is not walked any further once it is, except for authored groups. That second half
    /// matters -- an imported GLB arrives as a whole hierarchy of mesh nodes under the one node that knows
    /// which asset it came from, and capturing the children as well would save the same model twice, once
    /// as a reference and once as a pile of nodes nothing knows how to rebuild.
    /// </para>
    /// </summary>
    public static class ManaLevelCapture
    {
        /// <summary>
        /// Captures the world. <paramref name="cameraPose"/> overrides where the camera is recorded as
        /// being -- passed while playing, when the live camera is welded to the character's boom and saving
        /// it would mean every subsequent open started behind whoever was being played.
        /// </summary>
        public static ManaLevel Capture(
            ManaWorld world,
            string name = "Untitled",
            (Vector3 Position, Vector3 Rotation)? cameraPose = null)
        {
            var level = new ManaLevel { Name = name };

            lock (world.SceneLock)
            {
                foreach (var scene in world.Scenes)
                {
                    foreach (var child in scene.Children)
                    {
                        CaptureNode(child, level.Nodes);
                    }
                }

                var pose = cameraPose ?? (world.CameraNode != null
                    ? (world.CameraNode.Position, world.CameraNode.Rotation)
                    : null);

                if (pose.HasValue)
                {
                    level.Camera = new ManaLevelCamera
                    {
                        Position = ToArray(pose.Value.Position),
                        Rotation = ToArray(pose.Value.Rotation)
                    };
                }
            }

            return level;
        }

        private static void CaptureNode(ManaSceneNode node, List<ManaLevelNode> output)
        {
            if (node.LevelSource is { } source)
            {
                var captured = CaptureSource(node, source);
                if (captured != null)
                {
                    output.Add(captured);
                }

                // Whether or not it produced an entry, an authored non-group node's subtree belongs to it
                // -- see the class comment.
                return;
            }

            foreach (var child in node.Children)
            {
                CaptureNode(child, output);
            }
        }

        private static ManaLevelNode? CaptureSource(ManaSceneNode node, ManaLevelSource source)
        {
            var entry = new ManaLevelNode
            {
                Name = node.Name,
                Kind = source.Kind,
                Position = ToArray(node.Position),
                Rotation = ToArray(node.Rotation),
                Scale = ToArray(node.Scale)
            };

            switch (source.Kind)
            {
                case ManaLevelSourceKind.Group:
                    entry.Children = new List<ManaLevelNode>();
                    foreach (var child in node.Children)
                    {
                        CaptureNode(child, entry.Children);
                    }

                    break;

                case ManaLevelSourceKind.Shape:
                    entry.Shape = source.Shape;
                    entry.Color = ToArray(source.Color);
                    entry.Size = ToArray(source.Size);
                    entry.Collision = CaptureCollision(node);
                    break;

                case ManaLevelSourceKind.Mesh:
                    if (string.IsNullOrWhiteSpace(source.Asset))
                    {
                        // A mesh with no asset reference cannot be reloaded, so saving it would only
                        // produce a node that silently vanishes on the next open.
                        return null;
                    }

                    entry.Asset = source.Asset;
                    entry.Collision = CaptureCollision(node);
                    break;

                case ManaLevelSourceKind.Voxels:
                    if (node is not ManaVoxelsNode voxelsNode)
                    {
                        return null;
                    }

                    var voxels = voxelsNode.VoxelShape.CommittedVoxels;
                    if (voxels.Count == 0)
                    {
                        return null;
                    }

                    entry.Voxels = ManaLevelVoxelCodec.Encode(voxels);
                    break;

                case ManaLevelSourceKind.PlayerStart:
                    break;

                default:
                    return null;
            }

            return entry;
        }

        /// <summary>
        /// The kinematic box a platform stands on, read back off the Bepu shape rather than remembered
        /// separately -- there is only one copy of the dimensions this way, and it is the one the
        /// simulation is actually using.
        /// </summary>
        private static ManaLevelCollision? CaptureCollision(ManaSceneNode node)
        {
            foreach (var collision in node.GetComponents<ManaCollisionComponent>())
            {
                if (collision.Shape is not Box box)
                {
                    continue;
                }

                return new ManaLevelCollision
                {
                    Size = new[] { box.Width, box.Height, box.Length },
                    Offset = ToArray(collision.Position)
                };
            }

            return null;
        }

        private static float[] ToArray(Vector3 value) => new[] { value.X, value.Y, value.Z };

        private static float[] ToArray(Vector4 value) => new[] { value.X, value.Y, value.Z, value.W };
    }
}

using Mana;
using Mana.Code;
using Mana.Code.Nodes;
using Mana.Code.Shapes;

namespace ManaVoxel.Code.Gameplay.Flight
{
    /// <summary>
    /// Builds the thing you ride, out of primitives, because no rigged flying model ships with the asset
    /// packs. Blocky on purpose -- it sits alongside voxel scenery rather than looking like a placeholder
    /// that wandered in from another game.
    /// <para>
    /// Authored facing +Z, matching the convention <see cref="ManaPawnNode.FacingDirection"/> uses, so the
    /// mount and its rider can share one yaw with no offset between them.
    /// </para>
    /// <para>
    /// The parts that move are returned rather than looked up by name afterwards, so
    /// <see cref="FlyingMountComponent"/> can flap and bank them without walking the tree every frame. Both
    /// pivots rotate about axes that do not depend on which way the rig faces: a wing sweeps in Z (it
    /// extends along X), and the tail swings in Y.
    /// </para>
    /// <para>
    /// Nothing built here keeps a <see cref="Mana.Code.Levels.ManaLevelSource"/>, which is what keeps a
    /// summoned mount out of a saved level -- a level holds what somebody placed, and this is conjured at
    /// runtime and gone again on dismount.
    /// </para>
    /// </summary>
    public sealed class FlyingMountModel
    {
        private FlyingMountModel(
            ManaSceneNode root,
            ManaSceneNode leftWing,
            ManaSceneNode rightWing,
            ManaSceneNode tail,
            float saddleOffset)
        {
            Root = root;
            LeftWing = leftWing;
            RightWing = rightWing;
            Tail = tail;
            SaddleOffset = saddleOffset;
        }

        public ManaSceneNode Root { get; }
        public ManaSceneNode LeftWing { get; }
        public ManaSceneNode RightWing { get; }
        public ManaSceneNode Tail { get; }

        /// <summary>
        /// Where the rider's feet go, as an offset up the mount's own up-axis from <see cref="Root"/>'s
        /// origin. Negative: the root sits at shoulder height and the hull hangs below it, so the rider
        /// drops slightly to stand on the hull's back.
        /// </summary>
        public float SaddleOffset { get; }

        private static readonly Vector4 Body = new(0.24f, 0.40f, 0.62f, 1.0f);
        private static readonly Vector4 Belly = new(0.62f, 0.72f, 0.82f, 1.0f);
        private static readonly Vector4 Membrane = new(0.78f, 0.42f, 0.30f, 1.0f);
        private static readonly Vector4 Horn = new(0.92f, 0.88f, 0.76f, 1.0f);

        /// <summary>
        /// Assembles the mount under a fresh root node, sized so a <paramref name="riderHeight"/>-tall
        /// character is in proportion to it.
        /// </summary>
        public static FlyingMountModel Build(ManaContext context, ManaScene scene, float riderHeight)
        {
            // Authored against a 2-unit rider and scaled from there, so a taller character gets a
            // proportionally bigger mount rather than a differently-shaped one.
            var unit = MathF.Max(riderHeight, 0.5f) / 2.0f;

            var root = new ManaSceneNode(context, Matrix4x4.Identity)
            {
                Name = "Flying Mount",

                // Kept out of the outliner and out of picking: it is a vehicle being sat on, not scenery
                // anybody wants to click mid-flight.
                IsEditorTransient = true
            };

            var shapes = new ManaShapeFactory(context, scene);

            Adopt(root, shapes.AddCube(new Vector3(0.0f, -0.55f, 0.0f) * unit, Body, new Vector3(1.1f, 0.7f, 2.6f) * unit), "Hull");
            Adopt(root, shapes.AddCube(new Vector3(0.0f, -0.85f, -0.1f) * unit, Belly, new Vector3(0.8f, 0.3f, 2.2f) * unit), "Belly");

            // Neck stepped up toward the head rather than rotated: stepping suits the blocky read, and it
            // keeps the rig free of pitch-sign bookkeeping that would have to agree with the flight code.
            Adopt(root, shapes.AddCube(new Vector3(0.0f, -0.25f, 1.35f) * unit, Body, new Vector3(0.55f, 0.55f, 0.9f) * unit), "Neck Lower");
            Adopt(root, shapes.AddCube(new Vector3(0.0f, 0.15f, 1.95f) * unit, Body, new Vector3(0.45f, 0.45f, 0.8f) * unit), "Neck Upper");
            Adopt(root, shapes.AddCube(new Vector3(0.0f, 0.45f, 2.45f) * unit, Body, new Vector3(0.5f, 0.45f, 0.8f) * unit), "Head");
            Adopt(root, shapes.AddWedge(new Vector3(0.0f, 0.34f, 2.95f) * unit, Belly, new Vector3(0.42f, 0.28f, 0.45f) * unit), "Snout");

            foreach (var side in new[] { -1.0f, 1.0f })
            {
                Adopt(root, shapes.AddPyramid(new Vector3(0.17f * side, 0.72f, 2.3f) * unit, Horn, new Vector3(0.12f, 0.45f, 0.12f) * unit), "Horn");
            }

            // Wings pivot at the shoulder. The membrane hangs out along +/-X from that pivot rather than
            // being centred on it, which is what makes rotating the pivot a flap instead of a spin.
            var leftWing = BuildWing(context, scene, root, unit, -1.0f);
            var rightWing = BuildWing(context, scene, root, unit, 1.0f);

            var tail = new ManaSceneNode(context, Matrix4x4.CreateTranslation(new Vector3(0.0f, -0.45f, -1.3f) * unit))
            {
                Name = "Tail Pivot",
                IsEditorTransient = true
            };

            root.AddChild(tail);

            Adopt(tail, shapes.AddCube(new Vector3(0.0f, 0.0f, -0.7f) * unit, Body, new Vector3(0.4f, 0.4f, 1.4f) * unit), "Tail Base");
            Adopt(tail, shapes.AddCube(new Vector3(0.0f, 0.0f, -1.7f) * unit, Body, new Vector3(0.26f, 0.26f, 0.9f) * unit), "Tail Tip");
            Adopt(tail, shapes.AddPyramid(new Vector3(0.0f, 0.18f, -2.25f) * unit, Membrane, new Vector3(0.5f, 0.4f, 0.5f) * unit), "Tail Fin");

            foreach (var side in new[] { -1.0f, 1.0f })
            {
                Adopt(root, shapes.AddCube(new Vector3(0.45f * side, -1.0f, 0.55f) * unit, Body, new Vector3(0.22f, 0.5f, 0.24f) * unit), "Foreleg");
                Adopt(root, shapes.AddCube(new Vector3(0.5f * side, -1.0f, -0.65f) * unit, Body, new Vector3(0.26f, 0.55f, 0.28f) * unit), "Hindleg");
            }

            // Hull spans 0.7 units tall centred at -0.55, so its back is 0.2 below the root.
            return new FlyingMountModel(root, leftWing, rightWing, tail, -0.2f * unit);
        }

        private static ManaSceneNode BuildWing(
            ManaContext context,
            ManaScene scene,
            ManaSceneNode root,
            float unit,
            float side)
        {
            var pivot = new ManaSceneNode(context, Matrix4x4.CreateTranslation(new Vector3(0.5f * side, -0.3f, 0.2f) * unit))
            {
                Name = side < 0.0f ? "Left Wing Pivot" : "Right Wing Pivot",
                IsEditorTransient = true
            };

            root.AddChild(pivot);

            var shapes = new ManaShapeFactory(context, scene);

            // A long leading spar plus a wider membrane behind it, stepping down in size so the silhouette
            // tapers toward the tip the way a wing does.
            Adopt(pivot, shapes.AddCube(new Vector3(1.1f * side, 0.06f, 0.15f) * unit, Body, new Vector3(2.2f, 0.16f, 0.35f) * unit), "Wing Spar");
            Adopt(pivot, shapes.AddCube(new Vector3(0.95f * side, 0.0f, -0.5f) * unit, Membrane, new Vector3(1.9f, 0.09f, 1.1f) * unit), "Wing Membrane");
            Adopt(pivot, shapes.AddCube(new Vector3(2.35f * side, 0.02f, -0.25f) * unit, Membrane, new Vector3(0.9f, 0.08f, 0.7f) * unit), "Wing Tip");

            return pivot;
        }

        /// <summary>
        /// Re-homes a shape from the scene root onto <paramref name="parent"/>. The factory always adds to
        /// the scene -- it exists to place scenery -- so a rig assembled out of it has to be moved part by
        /// part, and a scene holds its children directly rather than through a node, so the node's own
        /// Parent is not what needs clearing.
        /// </summary>
        private static ManaSceneNode Adopt(ManaSceneNode parent, ManaSceneNode node, string name)
        {
            node.Parent?.RemoveChild(node);
            foreach (var scene in node.Context.World.Scenes)
            {
                scene.Children.Remove(node);
            }

            node.Name = name;
            node.IsEditorTransient = true;

            // Cleared because ManaShapeFactory stamps a LevelSource on everything it makes, and that stamp
            // is precisely what marks a node as placed and therefore worth saving.
            node.LevelSource = null;

            parent.AddChild(node);
            return node;
        }
    }
}

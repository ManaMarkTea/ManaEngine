using BepuPhysics.Collidables;
using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    public sealed class CollisionWidget : InspectorWidget<ManaSceneNode>
    {
        protected override bool Accepts(ManaSceneNode node)
        {
            return TryGetBoxCollision(node, out _, out _, out _);
        }

        protected override UiElement Build(ManaSceneNode node, InspectorContext context)
        {
            var theme = context.Theme;
            var showCollider = new CheckBox().Label("Show collider")
                .Checked(ReadShowDebug(node))
                .OnChanged(value =>
                {
                    if (TryGetBoxCollision(node, out _, out var collision, out _))
                    {
                        collision.ShowDebug = value;
                    }
                });
            showCollider.BindPolled(box => box.IsChecked, () => ReadShowDebug(node));

            var syncToNode = new CheckBox().Label("Sync to node")
                .Checked(ReadSyncNodeToPhysics(node))
                .OnChanged(value =>
                {
                    if (TryGetBoxCollision(node, out _, out var collision, out _))
                    {
                        collision.SyncNodeToPhysics = value;
                    }
                });
            syncToNode.BindPolled(box => box.IsChecked, () => ReadSyncNodeToPhysics(node));

            return new Expander()
                .Header("Collision")
                .Section(theme.Spacing,
                    showCollider,
                    syncToNode,

                    InspectorControls.Row(context, "Owner",
                        ReadOnly(theme, () => TryGetBoxCollision(node, out var owner, out _, out _)
                            ? DisplayName(owner)
                            : string.Empty)),

                    InspectorControls.Row(context, "Size",
                        ReadOnly(theme, () => Format(SizeOf(node)))),

                    InspectorControls.Row(context, "Offset", InspectorControls.Vector(
                        () => TryGetBoxCollision(node, out _, out var collision, out _) ? collision.Position : Vector3.Zero,
                        value =>
                        {
                            if (TryGetBoxCollision(node, out _, out var collision, out _))
                            {
                                collision.Position = value;
                            }
                        },
                        step: 0.05f)));
        }

        private static bool ReadShowDebug(ManaSceneNode node)
        {
            return TryGetBoxCollision(node, out _, out var collision, out _) && collision.ShowDebug;
        }

        private static bool ReadSyncNodeToPhysics(ManaSceneNode node)
        {
            return TryGetBoxCollision(node, out _, out var collision, out _) && collision.SyncNodeToPhysics;
        }

        private static Vector3 SizeOf(ManaSceneNode node)
        {
            return TryGetBoxCollision(node, out _, out _, out var box)
                ? new Vector3(box.Width, box.Height, box.Length)
                : Vector3.Zero;
        }

        private static bool TryGetBoxCollision(
            ManaSceneNode node,
            out ManaSceneNode owner,
            out ManaCollisionComponent collision,
            out Box box)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                foreach (var component in current.GetComponents<ManaCollisionComponent>())
                {
                    if (component.Shape is Box found)
                    {
                        owner = current;
                        collision = component;
                        box = found;
                        return true;
                    }
                }
            }

            owner = null!;
            collision = null!;
            box = default;
            return false;
        }

        private static TextBlock ReadOnly(UiTheme theme, Func<string> read)
        {
            return new TextBlock()
                .BindTextPolled(read)
                .Foreground(theme.Text)
                .Ellipsis()
                .CenterV();
        }

        private static string Format(Vector3 value)
        {
            return $"{value.X:0.###}, {value.Y:0.###}, {value.Z:0.###}";
        }

        private static string DisplayName(ManaSceneNode node)
        {
            return string.IsNullOrWhiteSpace(node.Name) ? node.GetType().Name : node.Name;
        }
    }
}

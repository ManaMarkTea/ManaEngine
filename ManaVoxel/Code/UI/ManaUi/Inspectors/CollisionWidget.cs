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
            return TryGetCollision(node, out _, out _);
        }

        protected override UiElement Build(ManaSceneNode node, InspectorContext context)
        {
            var theme = context.Theme;
            var showCollider = new CheckBox().Label("Show collider")
                .Checked(ReadShowDebug(node))
                .OnChanged(value =>
                {
                    if (TryGetCollision(node, out _, out var collision))
                    {
                        collision.ShowDebug = value;
                    }
                });
            showCollider.BindPolled(box => box.IsChecked, () => ReadShowDebug(node));

            var syncToNode = new CheckBox().Label("Sync to node")
                .Checked(ReadSyncNodeToPhysics(node))
                .OnChanged(value =>
                {
                    if (TryGetCollision(node, out _, out var collision))
                    {
                        collision.SyncNodeToPhysics = value;
                    }
                });
            syncToNode.BindPolled(box => box.IsChecked, () => ReadSyncNodeToPhysics(node));

            var fitVisualBounds = new ManaUI.Button()
                .Label("Fit visual bounds")
                .Variant(ButtonVariant.Subtle)
                .OnClick(() => FitToVisualBounds(node));
            fitVisualBounds.BindPolled(button => button.IsEnabled, () =>
                TryGetBoxCollision(node, out var owner, out _, out _)
                && ManaCollisionBounds.TryGetVisualBox(owner, out _, out _));

            return new Expander()
                .Header("Collision")
                .Section(theme.Spacing,
                    showCollider,
                    syncToNode,
                    fitVisualBounds,

                    InspectorControls.Row(context, "Owner",
                        ReadOnly(theme, () => TryGetCollision(node, out var owner, out _)
                            ? DisplayName(owner)
                            : string.Empty)),

                    InspectorControls.Row(context, "Source",
                        ReadOnly(theme, () => TryGetCollision(node, out var owner, out var collision)
                            ? SourceOf(owner, collision)
                            : string.Empty)),

                    InspectorControls.Row(context, "Size",
                        ReadOnly(theme, () => Format(SizeOf(node)))),

                    InspectorControls.Row(context, "Offset", InspectorControls.Vector(
                        () => TryGetCollision(node, out _, out var collision) ? collision.Position : Vector3.Zero,
                        value =>
                        {
                            if (TryGetCollision(node, out _, out var collision))
                            {
                                collision.Position = value;
                            }
                        },
                        step: 0.05f)));
        }

        private static bool ReadShowDebug(ManaSceneNode node)
        {
            return TryGetCollision(node, out _, out var collision) && collision.ShowDebug;
        }

        private static bool ReadSyncNodeToPhysics(ManaSceneNode node)
        {
            return TryGetCollision(node, out _, out var collision) && collision.SyncNodeToPhysics;
        }

        private static Vector3 SizeOf(ManaSceneNode node)
        {
            if (!TryGetCollision(node, out _, out var collision))
            {
                return Vector3.Zero;
            }

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

        private static string SourceOf(ManaSceneNode owner, ManaCollisionComponent collision)
        {
            if (collision.Shape is Box box)
            {
                var size = new Vector3(box.Width, box.Height, box.Length);
                return ManaCollisionBounds.MatchesVisualBox(owner, size, collision.Position)
                    ? "Visual bounds"
                    : "Explicit box";
            }

            return collision.Shape is ConvexHull ? "Visual convex hull" : "Convex shape";
        }

        private static void FitToVisualBounds(ManaSceneNode node)
        {
            if (!TryGetBoxCollision(node, out var owner, out var collision, out _)
                || !ManaCollisionBounds.TryGetVisualBox(owner, out var size, out var offset))
            {
                return;
            }

            var showDebug = collision.ShowDebug;
            var syncNodeToPhysics = collision.SyncNodeToPhysics;
            owner.Context.Physics.RemoveBody(collision.Body);
            var replacement = owner.Context.Physics.AddBoxBody(owner, size, offset);
            replacement.ShowDebug = showDebug;
            replacement.SyncNodeToPhysics = syncNodeToPhysics;
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

        private static bool TryGetCollision(
            ManaSceneNode node,
            out ManaSceneNode owner,
            out ManaCollisionComponent collision)
        {
            for (var current = node; current != null; current = current.Parent)
            {
                collision = current.GetComponents<ManaCollisionComponent>().FirstOrDefault()!;
                if (collision != null)
                {
                    owner = current;
                    return true;
                }
            }

            owner = null!;
            collision = null!;
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

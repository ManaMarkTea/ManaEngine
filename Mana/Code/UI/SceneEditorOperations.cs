using Mana.Code.Components;
using Mana.Code.Nodes;

namespace Mana.Code.UI
{
    public static class SceneEditorOperations
    {
        public static bool CanDeleteNode(ManaWorld world, ManaSceneNode? node)
        {
            return node != null && !ReferenceEquals(node, world.CameraNode);
        }

        public static bool DeleteNode(ManaWorld world, SceneSelectionState? selection, ManaSceneNode? node)
        {
            if (!CanDeleteNode(world, node))
            {
                return false;
            }

            var fallbackParent = node!.Parent;
            var fallbackScene = FindOwningScene(world, node);
            var removed = false;

            lock (world.SceneLock)
            {
                RemovePhysicsBodies(node.Context, node);

                if (node.Parent != null)
                {
                    node.Parent.RemoveChild(node);
                    removed = true;
                }
                else
                {
                    foreach (var scene in world.Scenes)
                    {
                        if (scene.Children.Remove(node))
                        {
                            removed = true;
                            break;
                        }
                    }
                }
            }

            if (!removed)
            {
                return false;
            }

            if (selection != null)
            {
                if (selection.SelectedNode != null && IsNodeOrDescendant(node, selection.SelectedNode))
                {
                    selection.SelectedNode = fallbackParent;
                    selection.SelectedScene = fallbackParent == null ? fallbackScene : null;
                    selection.SelectedComponent = null;
                }
                else if (selection.SelectedComponent != null
                    && IsNodeOrDescendant(node, selection.SelectedComponent.Owner))
                {
                    selection.SelectedComponent = null;
                }

                selection.NotifySceneGraphChanged();
            }

            return true;
        }

        public static bool CanRemoveComponent(ManaComponent? component)
        {
            if (component == null)
            {
                return false;
            }

            var world = component.Owner.Context.World;
            return component is not CameraComponent || !ReferenceEquals(component.Owner, world.CameraNode);
        }

        public static bool RemoveComponent(SceneSelectionState? selection, ManaComponent? component)
        {
            if (!CanRemoveComponent(component))
            {
                return false;
            }

            var owner = component!.Owner;
            var world = owner.Context.World;
            var removed = false;

            lock (world.SceneLock)
            {
                if (component is ManaCollisionComponent collision)
                {
                    var physics = component.Context.Physics;
                    if (physics != null && physics.PhysicsMap.ContainsKey(collision.Body))
                    {
                        physics.RemoveBody(collision.Body);
                        removed = !owner.Components.Contains(component);
                    }
                    else
                    {
                        removed = owner.Components.Remove(component);
                    }
                }
                else
                {
                    removed = owner.Components.Remove(component);
                }
            }

            if (!removed)
            {
                return false;
            }

            if (selection != null)
            {
                if (ReferenceEquals(selection.SelectedComponent, component))
                {
                    selection.SelectedScene = null;
                    selection.SelectedNode = owner;
                    selection.SelectedComponent = null;
                }

                selection.NotifySceneGraphChanged();
            }

            return true;
        }

        private static void RemovePhysicsBodies(ManaContext context, ManaSceneNode node)
        {
            var physics = context.Physics;
            if (physics == null)
            {
                return;
            }

            foreach (var child in node.Children.ToList())
            {
                RemovePhysicsBodies(context, child);
            }

            foreach (var collision in node.GetComponents<ManaCollisionComponent>())
            {
                if (physics.PhysicsMap.ContainsKey(collision.Body))
                {
                    physics.RemoveBody(collision.Body);
                }
                else
                {
                    node.Components.Remove(collision);
                }
            }
        }

        private static bool IsNodeOrDescendant(ManaSceneNode root, ManaSceneNode candidate)
        {
            for (var current = candidate; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, root))
                {
                    return true;
                }
            }

            return false;
        }

        private static ManaScene? FindOwningScene(ManaWorld world, ManaSceneNode node)
        {
            foreach (var scene in world.Scenes)
            {
                if (Contains(scene.Children, node))
                {
                    return scene;
                }
            }

            return null;
        }

        private static bool Contains(IEnumerable<ManaSceneNode> nodes, ManaSceneNode target)
        {
            foreach (var node in nodes)
            {
                if (ReferenceEquals(node, target) || Contains(node.Children, target))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

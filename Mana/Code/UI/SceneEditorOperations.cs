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

        /// <summary>
        /// Adds an empty scene to the world and selects it. Named "New Scene", then "New Scene 2" and so on, so
        /// two of them are never indistinguishable in the outliner -- which is where a scene is picked as the
        /// target of a move.
        /// </summary>
        public static ManaScene CreateScene(ManaWorld world, SceneSelectionState? selection, string? name = null)
        {
            var baseName = string.IsNullOrWhiteSpace(name) ? "New Scene" : name.Trim();
            var unique = baseName;
            var number = 1;

            var scene = new ManaScene(world.Context);

            lock (world.SceneLock)
            {
                while (world.Scenes.Any(existing => string.Equals(existing.Name, unique, StringComparison.Ordinal)))
                {
                    number++;
                    unique = $"{baseName} {number}";
                }

                scene.Name = unique;
                world.Scenes.Add(scene);
            }

            if (selection != null)
            {
                selection.SelectedScene = scene;
                selection.SelectedNode = null;
                selection.SelectedComponent = null;
                selection.NotifySceneGraphChanged();
            }

            return scene;
        }

        /// <summary>
        /// Whether <paramref name="node"/> can go into <paramref name="target"/>: anything but the editor
        /// camera, and only if it is not already one of that scene's own top-level nodes.
        /// </summary>
        public static bool CanMoveNodeToScene(ManaWorld world, ManaSceneNode? node, ManaScene? target)
        {
            return node != null
                && target != null
                && !ReferenceEquals(node, world.CameraNode)
                && world.Scenes.Contains(target)
                && !target.Children.Contains(node);
        }

        /// <summary>
        /// Moves a node, with everything under it, to the top level of another scene.
        /// <para>
        /// Scenes have no transform of their own, so a top-level node keeps exactly the place it had. One
        /// that was nested under a parent loses that parent's transform when it is detached, so its own is
        /// rewritten to the world transform it had a moment ago -- it stays where it is on screen.
        /// </para>
        /// </summary>
        public static bool MoveNodeToScene(
            ManaWorld world,
            SceneSelectionState? selection,
            ManaSceneNode? node,
            ManaScene? target)
        {
            if (!CanMoveNodeToScene(world, node, target))
            {
                return false;
            }

            var wasNested = node!.Parent != null;
            var worldTransform = node.WorldTransform;

            lock (world.SceneLock)
            {
                if (wasNested)
                {
                    node.Parent!.RemoveChild(node);
                }
                else
                {
                    foreach (var scene in world.Scenes)
                    {
                        scene.Children.Remove(node);
                    }
                }

                target!.Children.Add(node);
            }

            if (wasNested && Matrix4x4.Decompose(worldTransform, out var scale, out var rotation, out var translation))
            {
                node.Position = translation;
                node.Rotation = rotation.ToEuler();
                node.Scale = scale;
            }

            node.UpdateTransform();

            // Still selected, but the tree has to redraw under the new scene.
            selection?.NotifySceneGraphChanged();
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

        public static bool RemoveComponent(SceneSelectionState? selection, ManaComponent? component,
            bool preserveForUndo = false)
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
                if (!owner.Components.Contains(component)) return false;

                // Retained components must remain reusable; EndPlay releases live audio voices
                // and registrations without permanently disposing the emitter.
                if (preserveForUndo && (world.IsPlaying || component is ManaAudioSourceComponent))
                {
                    component.EndPlay();
                }

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
            component.OnRemoved();

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

            if (!preserveForUndo && component is ManaAudioSourceComponent audio) { audio.Dispose(); }
            return true;
        }

        private static void RemovePhysicsBodies(ManaContext context, ManaSceneNode node)
        {
            foreach (var audio in node.Components.OfType<Mana.Code.Components.ManaAudioSourceComponent>()) { audio.Dispose(); }
            foreach (var child in node.Children.ToList()) { RemovePhysicsBodies(context, child); }
            var physics = context.Physics;
            if (physics == null)
            {
                return;
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

        public static ManaScene? FindOwningScene(ManaWorld world, ManaSceneNode node)
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

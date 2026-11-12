using Mana.Code.Components;
using Mana.Code.Nodes;

namespace Mana.Code.UI
{
    /// <summary>Where a node sits: its scene, its parent (null at the scene's top level) and its index among its siblings.</summary>
    public readonly record struct SceneNodePlace(ManaScene Scene, ManaSceneNode? Parent, int Index);

    public static class SceneEditorOperations
    {
        /// <summary>Where <paramref name="node"/> is now, or null when it is in no scene.</summary>
        public static SceneNodePlace? PlaceOf(ManaWorld world, ManaSceneNode node)
        {
            var scene = FindOwningScene(world, node);
            if (scene == null)
            {
                return null;
            }
            var siblings = node.Parent?.Children ?? scene.Children;
            return new SceneNodePlace(scene, node.Parent, siblings.IndexOf(node));
        }

        /// <summary>
        /// Whether <paramref name="node"/> may go under <paramref name="parent"/> (or the top of <paramref name="scene"/>):
        /// not the editor camera or an editor helper, not into itself or below, and not into or out of a placed prefab,
        /// whose insides belong to the prefab.
        /// </summary>
        public static bool CanMoveNode(ManaWorld world, ManaSceneNode? node, ManaScene? scene, ManaSceneNode? parent)
        {
            if (node == null || scene == null || ReferenceEquals(node, world.CameraNode) || node.IsEditorTransient
                || !world.Scenes.Contains(scene) || IsInsidePrefabInstance(node))
            {
                return false;
            }
            if (parent == null)
            {
                return true;
            }
            return !IsNodeOrDescendant(node, parent)
                && !parent.IsEditorTransient
                && parent.PrefabLink == null
                && !IsInsidePrefabInstance(parent)
                && ReferenceEquals(FindOwningScene(world, parent), scene);
        }

        /// <summary>
        /// Moves a node, with everything under it, under <paramref name="parent"/> (or to the top of <paramref name="scene"/>)
        /// at <paramref name="index"/> among its new siblings. It stays where it is in the world: its own transform is
        /// rewritten against the new parent's.
        /// </summary>
        public static bool MoveNode(
            ManaWorld world, SceneSelectionState? selection, ManaSceneNode node, ManaScene scene, ManaSceneNode? parent, int index)
        {
            if (!CanMoveNode(world, node, scene, parent))
            {
                return false;
            }

            var worldTransform = node.WorldTransform;
            lock (world.SceneLock)
            {
                Detach(world, node);
                if (parent != null)
                {
                    parent.InsertChild(index, node);
                }
                else
                {
                    scene.Children.Insert(Math.Clamp(index, 0, scene.Children.Count), node);
                }
            }
            SetWorldTransform(node, worldTransform);
            selection?.NotifySceneGraphChanged();
            return true;
        }

        /// <summary>Puts a node at a place exactly, keeping its own transform (an undo going back to how it was).</summary>
        public static void PutNode(ManaWorld world, SceneSelectionState? selection, ManaSceneNode node, SceneNodePlace place)
        {
            lock (world.SceneLock)
            {
                Detach(world, node);
                if (place.Parent != null)
                {
                    place.Parent.InsertChild(place.Index, node);
                }
                else
                {
                    place.Scene.Children.Insert(Math.Clamp(place.Index, 0, place.Scene.Children.Count), node);
                }
            }
            node.UpdateTransform();
            selection?.NotifySceneGraphChanged();
        }

        /// <summary>
        /// Takes a node out of the world without destroying it -- its colliders' bodies suspended, its sounds stopped --
        /// so <see cref="RestoreNode"/> can put it back: a delete or a paste that can be undone.
        /// </summary>
        public static bool DetachNode(ManaWorld world, SceneSelectionState? selection, ManaSceneNode node)
        {
            if (!CanDeleteNode(world, node))
            {
                return false;
            }
            var fallbackParent = node.Parent;
            var fallbackScene = FindOwningScene(world, node);
            lock (world.SceneLock)
            {
                if (!Detach(world, node))
                {
                    return false;
                }
                foreach (var component in Walk(node).SelectMany(each => each.Components).ToList())
                {
                    if (component is ManaCollisionComponent collision)
                    {
                        collision.SuspendBody();
                    }
                    else if (component is ManaAudioSourceComponent || world.IsPlaying)
                    {
                        component.EndPlay();
                    }
                }
            }
            if (selection != null)
            {
                if (selection.SelectedNode != null && IsNodeOrDescendant(node, selection.SelectedNode))
                {
                    selection.SelectedNode = fallbackParent;
                    selection.SelectedScene = fallbackParent == null ? fallbackScene : null;
                    selection.SelectedComponent = null;
                }
                selection.NotifySceneGraphChanged();
            }
            return true;
        }

        /// <summary>Puts a node <see cref="DetachNode"/> took out back at a place, its bodies and sounds with it.</summary>
        public static void RestoreNode(ManaWorld world, SceneSelectionState? selection, ManaSceneNode node, SceneNodePlace place)
        {
            // Bodies first: back in the scene, a collider that finds none registers a fresh one of its own.
            lock (world.SceneLock)
            {
                foreach (var component in Walk(node).SelectMany(each => each.Components).ToList())
                {
                    if (component is ManaCollisionComponent collision)
                    {
                        collision.ResumeBody();
                    }
                    else if (component is ManaAudioSourceComponent || world.IsPlaying)
                    {
                        component.BeginPlay();
                    }
                }
            }
            PutNode(world, selection, node, place);
        }

        private static bool Detach(ManaWorld world, ManaSceneNode node)
        {
            if (node.Parent != null)
            {
                node.Parent.RemoveChild(node);
                return true;
            }
            foreach (var scene in world.Scenes)
            {
                if (scene.Children.Remove(node))
                {
                    return true;
                }
            }
            return false;
        }

        private static void SetWorldTransform(ManaSceneNode node, Matrix4x4 worldTransform)
        {
            var local = worldTransform;
            if (node.Parent != null && Matrix4x4.Invert(node.Parent.WorldTransform, out var inverse))
            {
                local = worldTransform * inverse;
            }
            if (Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation))
            {
                node.Position = translation;
                node.Rotation = rotation.ToEuler();
                node.Scale = scale;
            }
            node.UpdateTransform();
        }

        private static bool IsInsidePrefabInstance(ManaSceneNode node)
        {
            for (var current = node.Parent; current != null; current = current.Parent)
            {
                if (current.PrefabLink != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static IEnumerable<ManaSceneNode> Walk(ManaSceneNode node)
        {
            yield return node;
            foreach (var child in node.Children)
            {
                foreach (var below in Walk(child))
                {
                    yield return below;
                }
            }
        }
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

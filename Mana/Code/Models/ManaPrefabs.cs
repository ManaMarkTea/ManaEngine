using Mana.Code.Components;
using Mana.Code.Nodes;
using ManaCore.Diagnostics;

namespace Mana.Code.Models
{
    /// <summary>
    /// Places a prefab from game code with everything in it -- scripts, colliders, sounds, named parts -- as the editor
    /// would place it in a level. <see cref="ManaModelLibrary"/> copies only what draws; this is for a prefab whose
    /// scripts matter (a gun with its sound, a pickup with its trigger). The app owns prefab loading, so it sets
    /// <see cref="Spawner"/>.
    /// </summary>
    public static class ManaPrefabs
    {
        /// <summary>Builds an instance of the prefab at a content path (<c>Prefabs/Lamp.manaprefab</c>) into a scene.</summary>
        public static Func<ManaContext, ManaScene, string, ManaSceneNode?>? Spawner { get; set; }

        /// <summary>
        /// An instance of the prefab at <paramref name="assetPath"/>, added to <paramref name="scene"/> and placed by
        /// <paramref name="placement"/>; null (logged) when it cannot be. Marked editor-transient: never saved into a level.
        /// </summary>
        public static ManaSceneNode? Spawn(ManaContext context, ManaScene scene, string assetPath, Matrix4x4 placement, string? name = null)
        {
            if (Spawner == null)
            {
                ManaLog.WriteDiagnostic($"Prefab '{assetPath}' not placed: no prefab spawner (the app registers one).");
                return null;
            }

            ManaSceneNode? root;
            try
            {
                lock (context.World.SceneLock)
                {
                    root = Spawner(context, scene, assetPath);
                }
            }
            catch (Exception exception)
            {
                ManaLog.WriteDiagnostic($"Prefab '{assetPath}' could not be placed: {exception.Message}");
                return null;
            }
            if (root == null)
            {
                return null;
            }

            foreach (var node in Walk(root))
            {
                node.IsEditorTransient = true;
            }
            if (name != null)
            {
                root.Name = name;
            }
            Matrix4x4.Decompose(placement, out var scale, out var rotation, out var position);
            root.Position = position;
            root.Rotation = rotation.ToEuler();
            root.Scale = scale;
            root.UpdateTransform();

            // The world began play before this existed: its scripts start now, as the level's did then (a sound's cues, say).
            if (context.World.IsPlaying)
            {
                foreach (var component in Walk(root).SelectMany(node => node.Components).ToList())
                {
                    component.BeginPlay();
                }
            }
            return root;
        }

        /// <summary>Takes a placed prefab out of the world: its scripts end, its sounds and colliders go, and it is detached.</summary>
        public static void Despawn(ManaSceneNode root)
        {
            var context = root.Context;
            lock (context.World.SceneLock)
            {
                foreach (var node in Walk(root).ToList())
                {
                    foreach (var component in node.Components.ToList())
                    {
                        if (component is ManaAudioSourceComponent audio)
                        {
                            audio.Dispose();
                        }
                        else if (context.World.IsPlaying)
                        {
                            component.EndPlay();
                        }
                        if (component is ManaCollisionComponent collision && context.Physics != null
                            && context.Physics.Simulation.Bodies.BodyExists(collision.Body))
                        {
                            context.Physics.RemoveBody(collision.Body);
                        }
                    }
                }
                if (root.Parent != null)
                {
                    root.Parent.RemoveChild(root);
                }
                else
                {
                    foreach (var scene in context.World.Scenes)
                    {
                        scene.Children.Remove(root);
                    }
                }
            }
        }

        /// <summary>The first node called <paramref name="name"/> in a placed prefab, itself included: a part code moves or reads.</summary>
        public static ManaSceneNode? Find(ManaSceneNode root, string name) =>
            Walk(root).FirstOrDefault(node => string.Equals(node.Name, name, StringComparison.Ordinal));

        private static IEnumerable<ManaSceneNode> Walk(ManaSceneNode root)
        {
            yield return root;
            foreach (var child in root.Children)
            {
                foreach (var node in Walk(child))
                {
                    yield return node;
                }
            }
        }
    }
}

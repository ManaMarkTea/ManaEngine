using System.Text.Json;
using System.Text.Json.Nodes;
using Mana;
using Mana.Code.Components;
using Mana.Code.Levels;
using Mana.Code.Nodes;
using Mana.Code.UI;
using ManaEditor.Code.Levels;

namespace ManaEditor.Code.Assets
{
    public static class ManaPrefabRuntime
    {
        public static ManaPrefabReference CaptureReference(ManaSceneNode instance)
        {
            var link = instance.PrefabLink ?? throw new InvalidDataException("Prefab has no source link.");
            var overrides = JsonSerializer.Deserialize<List<ManaPrefabOverride>>(link.OverridesJson, ManaLevel.SerializerOptions) ?? [];
            if (link.BaselineJson != null && instance.Children.Count != 1)
            {
                throw new InvalidDataException("Keep the prefab's source root beneath its instance. Add children beneath that root, or unpack the instance first.");
            }
            if (link.BaselineJson != null && instance.Children.Count == 1)
            {
                var baseline = JsonSerializer.Deserialize<ManaLevelNode>(link.BaselineJson, ManaLevel.SerializerOptions)!;
                var before = ManaPrefabOverrides.Flatten(baseline);
                var current = ManaLevelCapture.CaptureSubtree(instance.Children[0]);
                var applied = link.AppliedJson == null ? before : ManaPrefabOverrides.Flatten(
                    JsonSerializer.Deserialize<ManaLevelNode>(link.AppliedJson, ManaLevel.SerializerOptions)!);
                var changes = ManaPrefabOverrides.Diff(applied, ManaPrefabOverrides.Flatten(current));
                foreach (var change in changes)
                {
                    overrides.RemoveAll(old => old.Path.Take(change.Path.Length).SequenceEqual(change.Path));
                    var baselineValue = ValueAt(before, change.Path, out var exists);
                    // Ordinary inspector edits back to the source default revert a prior override.
                    // Unchanged explicit overrides stay pinned even if the source now has that value.
                    if ((change.Remove && exists) || (!change.Remove && (!exists || !JsonNode.DeepEquals(baselineValue, change.Value))))
                    {
                        overrides.Add(change);
                    }
                }
            }
            return new() { AssetId = link.AssetId, AssetPath = link.AssetPath, Overrides = overrides };
        }

        private static JsonNode? ValueAt(JsonObject root, string[] path, out bool exists)
        {
            JsonNode? target = root;
            exists = true;
            foreach (var segment in path)
            {
                if (target is not JsonObject obj || !obj.TryGetPropertyValue(segment, out target))
                {
                    exists = false;
                    return null;
                }
            }
            return target;
        }

        public static ManaSceneNode? InstanceRoot(ManaSceneNode? node)
        {
            while (node != null)
            {
                if (node.PrefabLink != null)
                {
                    return node;
                }
                node = node.Parent;
            }
            return null;
        }

        public static ManaSceneNode Instantiate(ManaContext context, ManaScene scene, ManaLevelNode entry)
        {
            var staging = new ManaScene(context);
            try
            {
                var result = ManaLevelLoader.Load(new ManaLevel { Nodes = [entry] }, context, staging, replaceAuthoredNodes: false);
                var root = result.Nodes.LastOrDefault(node => node.PrefabLink != null && node.Parent == null);
                if (root == null || result.Warnings.Any(w => !w.StartsWith("Override target", StringComparison.Ordinal)
                    && !w.StartsWith("Some prefab overrides", StringComparison.Ordinal)))
                {
                    throw new InvalidDataException(string.Join("\n", result.Warnings));
                }
                staging.Children.Remove(root);
                scene.Children.Add(root);
                return root;
            }
            finally
            {
                foreach (var child in staging.Children) { Destroy(child); }
            }
        }

        /// <summary>Build first, then replace, so a failed source refresh leaves the old instance usable.</summary>
        public static ManaSceneNode Replace(ManaSceneNode instance, ManaPrefabReference reference)
        {
            var context = instance.Context;
            var world = context.World;
            lock (world.SceneLock)
            {
                var scene = SceneEditorOperations.FindOwningScene(world, instance)
                    ?? throw new InvalidOperationException("The instance is no longer in the world.");
                var entry = new ManaLevelNode
                {
                    Id = instance.AuthoredId, Name = instance.Name, Kind = ManaLevelSourceKind.Prefab,
                    Position = [instance.Position.X, instance.Position.Y, instance.Position.Z],
                    Rotation = [instance.Rotation.X, instance.Rotation.Y, instance.Rotation.Z],
                    Scale = [instance.Scale.X, instance.Scale.Y, instance.Scale.Z], Prefab = reference,
                    Scripts = ManaLevelComponentScripts.Capture(instance)
                };
                var staged = new ManaScene(context);
                var replacement = Instantiate(context, staged, entry);
                if (replacement.PrefabLink?.BaselineJson == null || replacement.Children.Count != 1)
                {
                    Destroy(replacement);
                    throw new InvalidDataException("Could not rebuild prefab; the existing instance was retained.");
                }
                staged.Children.Remove(replacement);
                var parent = instance.Parent;
                var siblings = parent?.Children ?? scene.Children;
                var index = siblings.IndexOf(instance);
                var selected = InstanceRoot(world.Selection.SelectedNode) == instance;
                var selectedId = world.Selection.SelectedNode?.AuthoredId;
                Destroy(instance);
                siblings.Remove(instance);
                replacement.Parent = parent;
                siblings.Insert(Math.Clamp(index, 0, siblings.Count), replacement);
                replacement.UpdateTransform();
                if (selected)
                {
                    world.Selection.SelectedNode = Walk(replacement).FirstOrDefault(n => n.AuthoredId == selectedId) ?? replacement;
                    world.Selection.SelectedComponent = null;
                }
                world.Selection.NotifySceneGraphChanged();
                return replacement;
            }
        }

        public static void Destroy(ManaSceneNode root)
        {
            foreach (var node in Walk(root))
            {
                foreach (var audio in node.Components.OfType<Mana.Code.Components.ManaAudioSourceComponent>())
                {
                    audio.Dispose();
                }
                foreach (var collision in node.Components.OfType<ManaCollisionComponent>().ToArray())
                {
                    node.Context.Physics.RemoveBody(collision.Body);
                }
            }
        }

        public static IEnumerable<ManaSceneNode> Walk(ManaSceneNode root)
        {
            yield return root;
            foreach (var child in root.Children)
            {
                foreach (var descendant in Walk(child))
                {
                    yield return descendant;
                }
            }
        }
    }
}

using Mana.Code.Components;
using Mana.Code.Nodes;

namespace ManaVoxel.Code.Levels;

/// <summary>Per-instance collision on imported children; does not modify the source asset.</summary>
internal static class ManaLevelMeshColliders
{
    public static List<ManaLevelChildColliders>? Capture(ManaSceneNode root)
    {
        var overrides = new List<ManaLevelChildColliders>();
        void Walk(ManaSceneNode parent, int[] path, string[] names)
        {
            var children = parent.Children.Where(child => !child.IsEditorTransient).ToArray();
            for (var index = 0; index < children.Length; index++)
            {
                var child = children[index];
                int[] childPath = [.. path, index];
                string[] childNames = [.. names, child.Name ?? string.Empty];
                var boxes = child.Components.OfType<ManaBoxColliderComponent>()
                    .Select(box => new ManaLevelBoxCollider { Size = box.Size, Center = box.Center }).ToList();
                if (boxes.Count > 0)
                    overrides.Add(new ManaLevelChildColliders { Path = ManaLevelMeshParts.Identity(child)?.Path ?? childPath,
                        Names = ManaLevelMeshParts.Identity(child)?.Names ?? childNames, Boxes = boxes });
                Walk(child, childPath, childNames);
            }
        }
        Walk(root, [], []);
        return overrides.Count == 0 ? null : overrides;
    }

    public static void Apply(ManaSceneNode root, List<ManaLevelChildColliders>? overrides, List<string> warnings)
    {
        if (overrides == null) return;
        foreach (var entry in overrides)
        {
            ManaSceneNode? target = root;
            if (entry.Path.Length == 0 || entry.Names.Length != entry.Path.Length) target = null;
            for (var depth = 0; target != null && depth < entry.Path.Length; depth++)
            {
                var children = target.Children.Where(child => !child.IsEditorTransient).ToArray();
                var index = entry.Path[depth];
                target = index >= 0 && index < children.Length ? children[index] : null;
                if (target != null && (target.Name ?? string.Empty) != entry.Names[depth]) target = null;
            }
            if (target == null)
            {
                warnings.Add($"Could not restore child colliders on '{root.Name}': the mesh hierarchy has changed.");
                continue;
            }
            foreach (var saved in entry.Boxes)
            {
                var box = new ManaBoxColliderComponent(target) { Size = saved.Size, Center = saved.Center };
                target.Components.Add(box);
            }
        }
    }
}

using System.Runtime.CompilerServices;
using System.Text.Json;
using Mana;
using Mana.Code.Materials;
using Mana.Code.Nodes;

namespace ManaVoxel.Code.Levels;

/// <summary>Scene-local edits to the hierarchy recreated from a model asset.</summary>
internal static class ManaLevelMeshParts
{
    private sealed record Original(string Name, Vector3 Position, Vector3 Rotation, Vector3 Scale,
        int[] Path, string[] Names, List<ManaLevelMaterial> Materials);
    private static readonly JsonSerializerOptions SnapshotOptions = new() { IncludeFields = true };
    private sealed record Import(List<ManaSceneNode> Nodes);
    private static readonly ConditionalWeakTable<ManaSceneNode, Original> Originals = new();
    private static readonly ConditionalWeakTable<ManaSceneNode, Import> Imports = new();

    public static (int[] Path, string[] Names)? Identity(ManaSceneNode node) =>
        Originals.TryGetValue(node, out var original) ? (original.Path, original.Names) : null;

    public static void RememberNames(ManaSceneNode root)
    {
        var nodes = new List<ManaSceneNode>();
        void Walk(ManaSceneNode parent, int[] path, string[] names)
        {
            var children = parent.Children.Where(n => !n.IsEditorTransient && n.LevelSource == null).ToArray();
            for (var i = 0; i < children.Length; i++)
            {
                var child = children[i];
                int[] next = [.. path, i];
                string[] nextNames = [.. names, child.Name ?? ""];
                Originals.Add(child, new(child.Name ?? "", child.Position, child.Rotation, child.Scale,
                    next, nextNames, Materials(child)));
                nodes.Add(child);
                Walk(child, next, nextNames);
            }
        }
        Walk(root, [], []);
        Imports.Add(root, new(nodes));
    }

    private static List<ManaLevelMaterial> Materials(ManaSceneNode node) =>
        node.Components.OfType<MeshRendererComponent>().Select(r => Snapshot(r.Material, node)).ToList();

    private static ManaLevelMaterial Snapshot(ManaMaterial m, ManaSceneNode node)
    {
        var value = ManaLevelMaterial.Capture(m, node.Context, Vector4.One) ?? new();
        value.Albedo = m.Albedo.Value;
        value.Emission = m.Emission.Value;
        value.EmissionStrength = (float)m.EmissionStrength;
        value.Metallic = m.Metallic.Value;
        value.Roughness = m.Roughness.Value;
        value.Occlusion = m.Occlusion.Value;
        value.NormalStrength = 1 - (float)m.NormalSelector;
        value.Opacity = (float)m.Opacity.Value;
        value.OpacityMode = m.OpacityMode;
        value.AlphaCutoff = (float)m.AlphaCutoff;
        value.UvScale = m.UvScale;
        value.UvOffset = m.UvOffset;
        value.InsetDepth = m.InsetDepth;
        value.FlatShaded = m.FlatShaded;
        value.Unlit = m.Unlit;
        return value;
    }

    public static List<ManaLevelMeshPart>? Capture(ManaSceneNode root)
    {
        if (!Imports.TryGetValue(root, out var import)) return null;
        var reachable = new HashSet<ManaSceneNode>();
        void Walk(ManaSceneNode parent)
        {
            foreach (var child in parent.Children) { reachable.Add(child); Walk(child); }
        }
        Walk(root);
        var result = new List<ManaLevelMeshPart>();
        foreach (var child in import.Nodes)
        {
            var original = Originals.GetValue(child, _ => throw new InvalidOperationException());
            if (!reachable.Contains(child))
            {
                // One removed ancestor is enough to remove its entire subtree on reload.
                if (child.Parent == null || reachable.Contains(child.Parent))
                    result.Add(new() { Path = original.Path, Names = original.Names, Deleted = true });
                continue;
            }
            var scripts = ManaLevelComponentScripts.Capture(child)?.Where(s => s.Type != "BoxCollider").ToList();
            var materials = Materials(child);
            var changedMaterial = JsonSerializer.Serialize(materials, SnapshotOptions) != JsonSerializer.Serialize(original.Materials, SnapshotOptions);
            if (changedMaterial)
                for (var i = 0; i < Math.Min(materials.Count, original.Materials.Count); i++)
                {
                    var current = materials[i];
                    var before = original.Materials[i];
                    if (current.AlbedoMap == null && before.AlbedoMap != null) current.AlbedoMap = "";
                    if (current.NormalMap == null && before.NormalMap != null) current.NormalMap = "";
                    if (current.RoughnessMap == null && before.RoughnessMap != null) current.RoughnessMap = "";
                    if (current.MetallicMap == null && before.MetallicMap != null) current.MetallicMap = "";
                    if (current.OcclusionMap == null && before.OcclusionMap != null) current.OcclusionMap = "";
                    if (current.EmissionMap == null && before.EmissionMap != null) current.EmissionMap = "";
                    if (current.OpacityMap == null && before.OpacityMap != null) current.OpacityMap = "";
                }
            if (child.Name != original.Name || child.Position != original.Position || child.Rotation != original.Rotation
                || child.Scale != original.Scale || scripts?.Count > 0 || changedMaterial)
                result.Add(new() { Path = original.Path, Names = original.Names, Name = child.Name, Position = child.Position,
                    Rotation = child.Rotation, Scale = child.Scale, Scripts = scripts?.Count > 0 ? scripts : null,
                    Materials = changedMaterial ? materials : null });
        }
        return result.Count > 0 ? result : null;
    }

    public static void Apply(ManaSceneNode root, List<ManaLevelMeshPart>? parts, List<string> warnings)
    {
        if (parts == null) return;
        var removals = new List<ManaSceneNode>();
        foreach (var part in parts)
        {
            ManaSceneNode? target = root;
            if (part.Path.Length == 0 || part.Path.Length != part.Names.Length) target = null;
            for (var depth = 0; target != null && depth < part.Path.Length; depth++)
            {
                var children = target.Children.Where(n => !n.IsEditorTransient && n.LevelSource == null).ToArray();
                var index = part.Path[depth];
                target = index >= 0 && index < children.Length ? children[index] : null;
                if (target != null && (!Originals.TryGetValue(target, out var original) || original.Name != part.Names[depth])) target = null;
            }
            if (target == null)
            {
                warnings.Add($"Could not restore mesh part on '{root.Name}': the mesh hierarchy has changed.");
                continue;
            }
            if (part.Deleted) { removals.Add(target); continue; }
            target.Name = part.Name ?? target.Name;
            var baseline = Originals.GetValue(target, _ => throw new InvalidOperationException());
            if (part.Position != baseline.Position) target.Position = part.Position;
            if (part.Rotation != baseline.Rotation) target.Rotation = part.Rotation;
            if (part.Scale != baseline.Scale) target.Scale = part.Scale;
            var renderers = target.Components.OfType<MeshRendererComponent>().ToArray();
            if (part.Materials != null)
                for (var i = 0; i < Math.Min(renderers.Length, part.Materials.Count); i++)
                    part.Materials[i].ApplyTo(renderers[i].DynamicMaterial);
            var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var script in part.Scripts ?? [])
            {
                occurrences.TryGetValue(script.Type, out var occurrence);
                occurrences[script.Type] = occurrence + 1;
                if (!ManaLevelComponentScripts.AddToNode(target, script, occurrence))
                    warnings.Add($"Could not restore component '{script.Type}' on mesh part '{target.Name}'.");
            }
        }
        foreach (var node in removals) node.Parent?.RemoveChild(node);
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Mana.Code.Levels;
using ManaVoxel.Code.Levels;

namespace ManaVoxel.Code.Assets
{
    public sealed class ManaPrefab
    {
        public const string Extension = ".manaprefab";
        public string Format { get; set; } = "mana-prefab";
        public int Version { get; set; } = 1;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Prefab";
        public ManaLevelNode Root { get; set; } = new() { Kind = ManaLevelSourceKind.Group };
        public List<ManaPrefabProperty> Properties { get; set; } = [];

        public static ManaPrefab Load(string path)
        {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("format", out _)
                || !document.RootElement.TryGetProperty("id", out _)
                || !document.RootElement.TryGetProperty("root", out _))
            {
                throw new InvalidDataException("Prefab is missing its format, asset ID, or root.");
            }
            var asset = JsonSerializer.Deserialize<ManaPrefab>(json, ManaLevel.SerializerOptions)
                ?? throw new InvalidDataException("Empty prefab.");
            if (asset.Root == null || Walk(asset.Root).Any(n => n == null || string.IsNullOrWhiteSpace(n.Id)
                || (n.Scripts?.Any(s => s == null || string.IsNullOrWhiteSpace(s.Id)) ?? false)))
            {
                throw new InvalidDataException("Saved prefab nodes and components must have persistent IDs.");
            }
            asset.Validate();
            return asset;
        }

        public void Validate()
        {
            if (Format != "mana-prefab" || Version != 1 || string.IsNullOrWhiteSpace(Id) || Root == null || Properties == null)
            {
                throw new InvalidDataException("Unsupported prefab format, version, or missing identity.");
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in Walk(Root))
            {
                if (node == null)
                {
                    throw new InvalidDataException("Prefab contains an empty node.");
                }
                if (node.Kind == ManaLevelSourceKind.Prefab)
                {
                    throw new InvalidDataException("Unpack nested prefab instances before saving this prefab.");
                }
                if (string.IsNullOrWhiteSpace(node.Id)) { node.Id = Guid.NewGuid().ToString("N"); }
                if (!ids.Add(node.Id))
                {
                    throw new InvalidDataException("Duplicate prefab node ID.");
                }
                foreach (var script in node.Scripts ?? [])
                {
                    if (script == null)
                    {
                        throw new InvalidDataException("Prefab contains an empty component.");
                    }
                    if (string.IsNullOrWhiteSpace(script.Id)) { script.Id = Guid.NewGuid().ToString("N"); }
                    if (!ids.Add(script.Id))
                    {
                        throw new InvalidDataException("Duplicate prefab component ID.");
                    }
                }
            }
            if (Properties.Select(p => p.Id).Distinct().Count() != Properties.Count)
            {
                throw new InvalidDataException("Duplicate exposed property ID.");
            }
        }

        public void Save(string path)
        {
            Validate();
            ManaContentLibrary.Write(path, ManaCore.Serialization.ManaJsonFormat.Indent(JsonSerializer.Serialize(this, ManaLevel.SerializerOptions)));
        }

        public static IEnumerable<ManaLevelNode> Walk(ManaLevelNode root)
        {
            yield return root;
            foreach (var child in root.Children ?? [])
            {
                foreach (var descendant in Walk(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    public sealed class ManaPrefabProperty
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Property";
        public string NodeId { get; set; } = string.Empty;
        public string? ComponentId { get; set; }
        public string Property { get; set; } = string.Empty;
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
    }

    public sealed class ManaPrefabReference
    {
        public string AssetId { get; set; } = string.Empty;
        public string AssetPath { get; set; } = string.Empty;
        public List<ManaPrefabOverride> Overrides { get; set; } = [];
    }

    public sealed class ManaPrefabOverride
    {
        public string[] Path { get; set; } = [];
        public JsonNode? Value { get; set; }
        public bool Remove { get; set; }
        public bool Add { get; set; }
    }

    /// <summary>Diffs a tree by persistent IDs, never names or sibling indices.</summary>
    public static class ManaPrefabOverrides
    {
        public static JsonObject Flatten(ManaLevelNode root)
        {
            var nodes = new JsonObject();
            void Visit(ManaLevelNode node, string? parent, int order)
            {
                if (string.IsNullOrEmpty(node.Id) || nodes.ContainsKey(node.Id))
                {
                    throw new InvalidDataException("Prefab nodes require unique persistent IDs.");
                }
                var data = JsonSerializer.SerializeToNode(node, ManaLevel.SerializerOptions)!.AsObject();
                data.Remove("children");
                data.Remove("prefab");
                data["parent"] = parent;
                data["order"] = order;
                var scripts = new JsonObject();
                foreach (var script in node.Scripts ?? [])
                {
                    if (string.IsNullOrEmpty(script.Id) || scripts.ContainsKey(script.Id))
                    {
                        throw new InvalidDataException("Prefab components require unique persistent IDs.");
                    }
                    scripts[script.Id] = JsonSerializer.SerializeToNode(script, ManaLevel.SerializerOptions);
                }
                data["scripts"] = scripts;
                nodes[node.Id] = data;
                var index = 0;
                foreach (var child in node.Children ?? [])
                {
                    Visit(child, node.Id, index++);
                }
            }
            Visit(root, null, 0);
            return nodes;
        }

        public static List<ManaPrefabOverride> Diff(JsonObject baseline, JsonObject current)
        {
            var result = new List<ManaPrefabOverride>();
            void Compare(JsonObject before, JsonObject after, string[] path)
            {
                foreach (var key in before.Select(p => p.Key).Union(after.Select(p => p.Key)))
                {
                    var next = path.Append(key).ToArray();
                    if (!after.ContainsKey(key))
                    {
                        result.Add(new() { Path = next, Remove = true });
                    }
                    else if (!before.ContainsKey(key))
                    {
                        result.Add(new() { Path = next, Value = after[key]?.DeepClone(), Add = true });
                    }
                    else if (before[key] is JsonObject oldObject && after[key] is JsonObject newObject)
                    {
                        Compare(oldObject, newObject, next);
                    }
                    else if (!JsonNode.DeepEquals(before[key], after[key]))
                    {
                        result.Add(new() { Path = next, Value = after[key]?.DeepClone() });
                    }
                }
            }
            Compare(baseline, current, []);
            return result;
        }

        public static ManaLevelNode Resolve(ManaLevelNode source, IEnumerable<ManaPrefabOverride> overrides, IList<string>? warnings = null)
        {
            var nodes = Flatten(source);
            foreach (var change in overrides)
            {
                if (change.Path.Length == 0)
                {
                    throw new InvalidDataException("Empty prefab override path.");
                }
                JsonObject? target = nodes;
                foreach (var segment in change.Path.SkipLast(1))
                {
                    target = target?[segment] as JsonObject;
                }
                if (target == null)
                {
                    warnings?.Add($"Override target no longer exists: {string.Join('/', change.Path)}");
                    continue;
                }
                var key = change.Path[^1];
                if (change.Remove)
                {
                    target.Remove(key);
                }
                else
                {
                    target[key] = change.Value?.DeepClone();
                }
            }
            var visiting = new HashSet<string>();
            ManaLevelNode Build(string id)
            {
                if (!visiting.Add(id))
                {
                    throw new InvalidDataException("Cyclic prefab hierarchy.");
                }
                var data = nodes[id]?.DeepClone().AsObject() ?? throw new InvalidDataException("Missing prefab root.");
                var scripts = data["scripts"]!.AsObject();
                data["scripts"] = new JsonArray(scripts.Select(p => p.Value?.DeepClone()).ToArray());
                var result = data.Deserialize<ManaLevelNode>(ManaLevel.SerializerOptions)!;
                result.Children = nodes.Where(p => p.Value?["parent"]?.GetValue<string>() == id)
                    .OrderBy(p => p.Value?["order"]?.GetValue<int>() ?? 0)
                    .Select(p => Build(p.Key)).ToList();
                return result;
            }
            var root = Build(source.Id!);
            if (visiting.Count != nodes.Count)
            {
                warnings?.Add("Some prefab overrides refer to children of removed nodes and were not instantiated.");
            }
            return root;
        }
    }
}

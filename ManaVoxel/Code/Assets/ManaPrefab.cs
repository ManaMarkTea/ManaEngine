using System.Diagnostics.CodeAnalysis;
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

        /// <summary>
        /// Set on a variant: the prefab it is made from, and only what it changes -- a blue lamp is the lamp with
        /// its light's colour overridden, and nothing else. A variant's file holds this instead of a root;
        /// <see cref="Load"/> builds <see cref="Root"/> from the base, so everything reading a prefab reads a
        /// variant the same way, and <see cref="Save"/> writes the changes back as overrides. A change to the base
        /// reaches every variant made from it.
        /// </summary>
        public ManaPrefabReference? Base { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsVariant => Base != null;

        /// <summary>A variant's base tree as loaded -- what its changes are told apart from.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public ManaLevelNode? BaseRoot => _baseRoot;

        // The base's tree as loaded, so a save can tell what this variant changed; and the base's exposed
        // properties, which a variant showing only those leaves to the base.
        private ManaLevelNode? _baseRoot;
        private List<ManaPrefabProperty>? _baseProperties;

        // Variants being loaded on this thread, by asset ID: a variant of itself is a loop, not a prefab.
        [ThreadStatic]
        private static HashSet<string>? _loading;

        private const int MaxVariantDepth = 8;

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ManaLevel.ReflectionJson)]
        public static ManaPrefab Load(string path)
        {
            var json = File.ReadAllText(path);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("format", out _)
                || !document.RootElement.TryGetProperty("id", out _)
                || (!document.RootElement.TryGetProperty("root", out _) && !document.RootElement.TryGetProperty("base", out _)))
            {
                throw new InvalidDataException("Prefab is missing its format, asset ID, or root.");
            }
            var asset = JsonSerializer.Deserialize<ManaPrefab>(json, ManaLevel.SerializerOptions)
                ?? throw new InvalidDataException("Empty prefab.");
            if (asset.Base != null)
            {
                asset.ResolveBase();
            }
            if (asset.Root == null || Walk(asset.Root).Any(n => n == null || string.IsNullOrWhiteSpace(n.Id)
                || (n.Scripts?.Any(s => s == null || string.IsNullOrWhiteSpace(s.Id)) ?? false)))
            {
                throw new InvalidDataException("Saved prefab nodes and components must have persistent IDs.");
            }
            asset.Validate();
            return asset;
        }

        /// <summary>A prefab's asset ID, read without building it -- a variant's base is not loaded for this.</summary>
        public static string ReadId(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString() ?? string.Empty
                : string.Empty;
        }

        /// <summary>
        /// A new variant of a prefab: its tree to start from, its exposed properties, and no changes yet.
        /// <paramref name="basePath"/> is the base's file, so the variant can find it again.
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ManaLevel.ReflectionJson)]
        public static ManaPrefab CreateVariant(ManaPrefab source, string basePath, string name)
        {
            var root = JsonSerializer.Deserialize<ManaLevelNode>(JsonSerializer.Serialize(source.Root, ManaLevel.SerializerOptions), ManaLevel.SerializerOptions)!;
            return new ManaPrefab
            {
                Name = name,
                Base = new ManaPrefabReference { AssetId = source.Id, AssetPath = ManaContentLibrary.Reference(basePath) },
                Root = root,
                Properties = source.Properties.ToList(),
                _baseRoot = source.Root,
                _baseProperties = source.Properties.ToList()
            };
        }

        private void ResolveBase()
        {
            _loading ??= new HashSet<string>(StringComparer.Ordinal);
            if (!_loading.Add(Id))
            {
                throw new InvalidDataException($"Prefab variant '{Name}' is made from itself.");
            }

            if (_loading.Count > MaxVariantDepth)
            {
                _loading.Remove(Id);
                throw new InvalidDataException($"Prefab variant '{Name}' is more than {MaxVariantDepth} variants deep.");
            }

            try
            {
                var path = ManaContentLibrary.ResolvePrefab(Base!.AssetId, Base.AssetPath);
                var source = Load(path);
                Base.AssetPath = ManaContentLibrary.Reference(path);
                _baseRoot = source.Root;
                _baseProperties = source.Properties;
                Root = ManaPrefabOverrides.Resolve(source.Root, Base.Overrides);

                // A variant that exposes nothing of its own shows what its base does.
                if (Properties.Count == 0)
                {
                    Properties = source.Properties.ToList();
                }
            }
            finally
            {
                _loading.Remove(Id);
            }
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

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ManaLevel.ReflectionJson)]
        public void Save(string path)
        {
            Validate();
            var json = JsonSerializer.SerializeToNode(this, ManaLevel.SerializerOptions)!.AsObject();
            if (Base != null)
            {
                // A variant keeps only what it changes: its tree, told apart from the base's, as overrides.
                if (_baseRoot == null)
                {
                    var source = Load(ManaContentLibrary.ResolvePrefab(Base.AssetId, Base.AssetPath));
                    _baseRoot = source.Root;
                    _baseProperties ??= source.Properties;
                }

                Base.Overrides = ManaPrefabOverrides.Diff(ManaPrefabOverrides.Flatten(_baseRoot), ManaPrefabOverrides.Flatten(Root));
                json["base"] = JsonSerializer.SerializeToNode(Base, ManaLevel.SerializerOptions);
                json.Remove("root");
                if (_baseProperties != null && Properties.Select(p => p.Id).SequenceEqual(_baseProperties.Select(p => p.Id)))
                {
                    json["properties"] = new JsonArray();
                }
            }

            ManaContentLibrary.Write(path, ManaCore.Serialization.ManaJsonFormat.Indent(json.ToJsonString(ManaLevel.SerializerOptions)));
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
        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ManaLevel.ReflectionJson)]
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

        private static bool IsZeroLike(JsonNode? value) =>
            value is JsonValue json && (json.TryGetValue<bool>(out var flag) ? !flag : json.TryGetValue<double>(out var number) && number == 0.0);

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
                        // A script property added to the engine since the base was saved reads as zero or off in
                        // it: that is the old behaviour, not a change somebody made.
                        if (next.Length >= 2 && next[^2] == "properties" && IsZeroLike(after[key]))
                        {
                            continue;
                        }

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

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = ManaLevel.ReflectionJson)]
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

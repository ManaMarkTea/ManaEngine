using System.Text.Json;
using System.Text.Json.Nodes;
using ManaUI;
using ManaVoxel.Code.Assets;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The .manaui document the UI Designer edits: the JSON itself, which element is selected, and the file.
    /// The designer never keeps elements of its own -- every edit goes into the JSON and the preview is built
    /// from it again, so what is saved is exactly what was seen.
    /// </summary>
    public static class UiDesignerDocument
    {
        public const string FolderName = "UI";

        private static JsonObject? _root;

        public static JsonObject Root
        {
            get
            {
                if (_root == null)
                {
                    var first = List().FirstOrDefault();
                    if (first == null || !TryOpen(first, out _))
                    {
                        Replace(NewDocument(), null);
                    }
                }

                return _root!;
            }
        }

        public static string? Path { get; private set; }

        public static bool Dirty { get; private set; }

        public static JsonObject? Selected { get; private set; }

        /// <summary>Replaced when the tree changes shape or a label in it does: the outline rebuilds.</summary>
        public static object TreeToken { get; private set; } = new();

        /// <summary>Replaced on every edit: the preview rebuilds.</summary>
        public static object PreviewToken { get; private set; } = new();

        /// <summary>Replaced when the selection changes or its properties are rebuilt.</summary>
        public static object SelectionToken { get; private set; } = new();

        public static JsonObject NewDocument() => new()
        {
            ["type"] = "StackPanel",
            ["name"] = "root",
            ["spacing"] = 8,
            ["padding"] = new JsonArray(16, 16),
            ["children"] = new JsonArray(new JsonObject { ["type"] = "TextBlock", ["text"] = "New layout", ["fontSize"] = 20 })
        };

        public static string ResolveFolder() => ManaProjectFolder.Resolve(FolderName);

        public static IReadOnlyList<string> List()
        {
            try
            {
                var folder = ResolveFolder();
                return Mana.Code.Content.ManaContentMounts.For(Mana.Code.Content.ManaContentAssets.Root)
                    .Select(mount => System.IO.Path.Combine(mount.Root, FolderName))
                    .Prepend(folder).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(Directory.Exists)
                    .SelectMany(directory => Directory.EnumerateFiles(directory, "*" + UiMarkup.Extension, SearchOption.AllDirectories))
                    .Order(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        public static void Replace(JsonObject root, string? path)
        {
            _root = root;
            Path = path;
            Selected = root;
            Touch(tree: true);
            Dirty = false;
        }

        public static bool TryOpen(string path, out string? error)
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject root)
                {
                    Replace(root, path);
                    error = null;
                    return true;
                }

                error = "not a JSON object";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                error = exception.Message;
            }

            return false;
        }

        /// <summary>Saves to the open file, or as a new one named after the root element's name.</summary>
        public static string Save(string? name = null)
        {
            if (Path == null || name != null)
            {
                var stem = name ?? Root["name"]?.GetValue<string>() ?? "Layout";
                stem = string.Concat(stem.Split(System.IO.Path.GetInvalidFileNameChars()));
                Path = System.IO.Path.Combine(ResolveFolder(), (stem.Length == 0 ? "Layout" : stem) + UiMarkup.Extension);
            }

            File.WriteAllText(Path, ManaCore.Serialization.ManaJsonFormat.Indent(Root.ToJsonString()) + Environment.NewLine);
            Dirty = false;
            Touch(tree: true);
            Dirty = false;
            return Path;
        }

        public static void Select(JsonObject? node)
        {
            if (!ReferenceEquals(node, Selected))
            {
                Selected = node;
                SelectionToken = new object();
            }
        }

        /// <summary>After an edit: the preview always rebuilds, the outline only when asked.</summary>
        public static void Touch(bool tree = false)
        {
            Dirty = true;
            PreviewToken = new object();
            if (tree)
            {
                TreeToken = new object();
                SelectionToken = new object();
            }
        }

        /// <summary>A property of the selected element: set it, or with null remove it back to its default.</summary>
        public static void Set(JsonObject node, string key, JsonNode? value, bool tree = false)
        {
            if (value == null)
            {
                node.Remove(key);
            }
            else
            {
                node[key] = value;
            }

            Touch(tree);
        }

        public static Type? TypeOf(JsonObject node) =>
            node["type"]?.GetValue<string>() is { } name && UiMarkup.Types.TryGetValue(name, out var type) ? type : null;

        public static bool HoldsChildren(JsonObject node) => TypeOf(node) is { } type && typeof(Panel).IsAssignableFrom(type);

        // A border, a scroller or an expander. Controls are decorators too, but their child is their own
        // insides -- a text box's editor -- and not somewhere to put more layout.
        public static bool HoldsChild(JsonObject node) =>
            TypeOf(node) is { } type && typeof(Decorator).IsAssignableFrom(type) && !typeof(Control).IsAssignableFrom(type);

        /// <summary>The element a node sits in, or null for the root.</summary>
        public static JsonObject? ParentOf(JsonObject node) => node.Parent switch
        {
            JsonArray array => array.Parent as JsonObject,
            JsonObject parent => parent,
            _ => null
        };

        /// <summary>
        /// Adds an element: into the selected one when it holds children (or an empty single child), and
        /// otherwise after it, in its parent.
        /// </summary>
        public static JsonObject Add(string type)
        {
            var node = new JsonObject { ["type"] = type };
            if (type is "TextBlock" or "Button")
            {
                node["text"] = type == "Button" ? "Button" : "Text";
            }
            else if (type == "Grid")
            {
                node["columns"] = "*, *";
                node["rows"] = "Auto";
            }

            var at = Selected ?? Root;
            if (HoldsChildren(at))
            {
                ChildrenOf(at).Add(node);
            }
            else if (HoldsChild(at) && at["child"] == null)
            {
                at["child"] = node;
            }
            else if (ParentOf(at) is { } parent && at.Parent is JsonArray siblings)
            {
                siblings.Insert(siblings.IndexOf(at) + 1, node);
            }
            else
            {
                return at;
            }

            Selected = node;
            Touch(tree: true);
            return node;
        }

        public static void Delete(JsonObject node)
        {
            var parent = ParentOf(node);
            if (parent == null)
            {
                return;
            }

            switch (node.Parent)
            {
                case JsonArray array:
                    array.Remove(node);
                    break;
                case JsonObject owner:
                    owner.Remove("child");
                    break;
            }

            Selected = parent;
            Touch(tree: true);
        }

        /// <summary>Moves an element earlier (-1) or later (+1) among its siblings.</summary>
        public static void Move(JsonObject node, int by)
        {
            if (node.Parent is not JsonArray array)
            {
                return;
            }

            var index = array.IndexOf(node);
            var target = Math.Clamp(index + by, 0, array.Count - 1);
            if (target == index)
            {
                return;
            }

            array.RemoveAt(index);
            array.Insert(target, node);
            Touch(tree: true);
        }

        public static JsonArray ChildrenOf(JsonObject node)
        {
            if (node["children"] is not JsonArray children)
            {
                children = new JsonArray();
                node["children"] = children;
            }

            return children;
        }

        /// <summary>The elements under a node, in document order.</summary>
        public static IEnumerable<JsonObject> Nested(JsonObject node)
        {
            if (node["children"] is JsonArray children)
            {
                foreach (var child in children.OfType<JsonObject>())
                {
                    yield return child;
                }
            }

            if (node["child"] is JsonObject single)
            {
                yield return single;
            }
        }
    }
}

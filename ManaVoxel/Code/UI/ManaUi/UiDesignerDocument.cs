using System.Text.Json;
using System.Text.Json.Nodes;
using ManaUI;
using ManaVoxel.Code.Assets;
using ManaVoxel.Code.Levels;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The .manaui document the UI Designer edits: the JSON itself, which element is selected, the file, and the
    /// undo history. The designer never keeps elements of its own -- every edit goes into the JSON and the preview is
    /// built from it again, so what is saved is exactly what was seen.
    /// <para>
    /// A file is {"format", "version", "id", "name", "viewModel", "root": {...}}: the id is what a UiCanvas's reference
    /// trusts, viewModel names the game mode whose view model the layout binds to. A hand-written file may be just its
    /// root element; saving gives it the rest.
    /// </para>
    /// </summary>
    public static class UiDesignerDocument
    {
        public const string FolderName = "UI";
        public const string FormatName = "mana-ui";

        private const int UndoLimit = 200;

        private static JsonObject? _document;
        private static string _committed = string.Empty;
        private static readonly List<UndoStep> Undos = new();
        private static readonly List<UndoStep> Redos = new();

        private readonly record struct UndoStep(string Json, int[] Selection);

        /// <summary>The whole file.</summary>
        public static JsonObject Document
        {
            get
            {
                if (_document == null)
                {
                    var first = List().FirstOrDefault();
                    if (first == null || !TryOpen(first, out _))
                    {
                        Replace(NewDocument("Layout"), null);
                    }
                }

                return _document!;
            }
        }

        /// <summary>The root element: the outermost thing with a "type".</summary>
        public static JsonObject Root => Document["root"] as JsonObject ?? Document;

        public static string? Path { get; private set; }

        public static bool Dirty { get; private set; }

        public static JsonObject? Selected { get; private set; }

        /// <summary>Replaced when the tree changes shape or a label in it does: the outline rebuilds.</summary>
        public static object TreeToken { get; private set; } = new();

        /// <summary>Replaced on every edit: the preview rebuilds.</summary>
        public static object PreviewToken { get; private set; } = new();

        /// <summary>Replaced when the selection changes or its properties are rebuilt.</summary>
        public static object SelectionToken { get; private set; } = new();

        public static bool CanUndo => Undos.Count > 0;

        public static bool CanRedo => Redos.Count > 0;

        /// <summary>The game mode the layout binds to, as the file says.</summary>
        public static string ViewModelSource
        {
            get
            {
                if (Document["viewModel"] is JsonValue value && value.TryGetValue<string>(out var source))
                {
                    return source;
                }

                return string.Empty;
            }
            set
            {
                if (string.Equals(ViewModelSource, value, StringComparison.Ordinal))
                {
                    return;
                }

                if (value.Length == 0)
                {
                    Document.Remove("viewModel");
                }
                else
                {
                    Document["viewModel"] = value;
                }

                Touch(tree: true);
            }
        }

        /// <summary>The view model's type, which the preview checks paths against and the properties panel offers from.</summary>
        public static Type? ViewModelType
        {
            get
            {
                if (ViewModelSource.Length == 0)
                {
                    return null;
                }

                return ManaLevelGameModes.Create(ViewModelSource)?.HudViewModelType;
            }
        }

        /// <summary>Asks the outline to redraw its rows (a branch folded).</summary>
        public static void RefreshOutline() => TreeToken = new object();

        public static JsonObject NewDocument(string name) => new()
        {
            ["format"] = FormatName,
            ["version"] = 1,
            ["id"] = Guid.NewGuid().ToString("N"),
            ["name"] = name,
            ["root"] = new JsonObject
            {
                ["type"] = "Grid",
                ["name"] = "root",
                ["children"] = new JsonArray(new JsonObject
                {
                    ["type"] = "TextBlock",
                    ["text"] = "New layout",
                    ["fontSize"] = 32,
                    ["horizontalAlignment"] = "Center",
                    ["verticalAlignment"] = "Center"
                })
            }
        };

        /// <summary>Where new layouts go: the project's own content, which a UiCanvas's picker lists.</summary>
        public static string ResolveFolder() => System.IO.Path.Combine(Mana.Code.Content.ManaContentAssets.Root, FolderName);

        public static IReadOnlyList<string> List()
        {
            try
            {
                return Mana.Code.Content.ManaContentMounts.For(Mana.Code.Content.ManaContentAssets.Root)
                    .Select(mount => System.IO.Path.Combine(mount.Root, FolderName))
                    .Where(Directory.Exists)
                    .SelectMany(directory => Directory.EnumerateFiles(directory, "*" + UiMarkup.Extension, SearchOption.AllDirectories))
                    .Order(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        public static void Replace(JsonObject document, string? path)
        {
            _document = document;
            Path = path;
            Selected = Root;
            Undos.Clear();
            Redos.Clear();
            _committed = document.ToJsonString();
            Bump(tree: true);
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

        /// <summary>Saves to the open file, or with <paramref name="name"/> as a new one in the layouts folder.</summary>
        public static string Save(string? name = null)
        {
            if (Document["root"] == null)
            {
                // A hand-written file becomes a proper asset, or nothing could find it by id.
                var root = Document;
                var wrapped = NewDocument(root["name"]?.GetValue<string>() ?? "Layout");
                wrapped["root"] = root;
                _document = wrapped;
                Selected ??= root;
            }

            Document["format"] ??= FormatName;
            Document["version"] ??= 1;
            Document["id"] ??= Guid.NewGuid().ToString("N");
            if (Path == null || name != null)
            {
                var stem = name ?? Document["name"]?.GetValue<string>() ?? "Layout";
                stem = string.Concat(stem.Split(System.IO.Path.GetInvalidFileNameChars()));
                if (stem.Length == 0)
                {
                    stem = "Layout";
                }

                var folder = ResolveFolder();
                if (name != null && Path != null)
                {
                    // Saved as another file, in the same content mount: a second asset, not the first one renamed.
                    folder = System.IO.Path.GetDirectoryName(Path) ?? folder;
                    Document["id"] = Guid.NewGuid().ToString("N");
                }

                Directory.CreateDirectory(folder);
                Document["name"] = stem;
                Path = System.IO.Path.Combine(folder, stem + UiMarkup.Extension);
            }

            File.WriteAllText(Path, ManaCore.Serialization.ManaJsonFormat.Indent(Document.ToJsonString()) + Environment.NewLine);
            _committed = Document.ToJsonString();
            Bump(tree: true);
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

        /// <summary>After an edit: one undo step, the preview always rebuilt, the outline only when asked.</summary>
        public static void Touch(bool tree = false)
        {
            Commit();
            Bump(tree);
        }

        /// <summary>A step of a gesture: shown at once, with the undo step left for <see cref="Commit"/> when it ends.</summary>
        public static void TouchLive()
        {
            Bump(false);
        }

        /// <summary>Closes an edit into one undo step, if anything changed since the last.</summary>
        public static void Commit()
        {
            var now = Document.ToJsonString();
            if (string.Equals(now, _committed, StringComparison.Ordinal))
            {
                return;
            }

            Undos.Add(new UndoStep(_committed, _selectionBefore));
            if (Undos.Count > UndoLimit)
            {
                Undos.RemoveAt(0);
            }

            Redos.Clear();
            _committed = now;
            _selectionBefore = PathOf(Selected);
        }

        private static int[] _selectionBefore = [];

        public static void Undo() => Step(Undos, Redos);

        public static void Redo() => Step(Redos, Undos);

        private static void Step(List<UndoStep> from, List<UndoStep> to)
        {
            Commit();
            if (from.Count == 0)
            {
                return;
            }

            var step = from[^1];
            from.RemoveAt(from.Count - 1);
            to.Add(new UndoStep(_committed, PathOf(Selected)));
            _document = JsonNode.Parse(step.Json) as JsonObject ?? _document;
            _committed = step.Json;
            Selected = AtPath(step.Selection) ?? Root;
            _selectionBefore = step.Selection;
            Bump(tree: true);
        }

        private static void Bump(bool tree)
        {
            Dirty = true;
            PreviewToken = new object();
            if (tree)
            {
                TreeToken = new object();
                SelectionToken = new object();
            }
        }

        // An element's place as indexes down the tree, since undo replaces every node object.
        private static int[] PathOf(JsonObject? node)
        {
            var path = new List<int>();
            while (node != null && !ReferenceEquals(node, Root))
            {
                var parent = ParentOf(node);
                if (parent == null)
                {
                    return [];
                }

                path.Add(Nested(parent).ToList().FindIndex(item => ReferenceEquals(item, node)));
                node = parent;
            }

            path.Reverse();
            return path.ToArray();
        }

        private static JsonObject? AtPath(int[] path)
        {
            var node = Root;
            foreach (var index in path)
            {
                if (Nested(node).ElementAtOrDefault(index) is not { } next)
                {
                    return null;
                }

                node = next;
            }

            return node;
        }

        /// <summary>A property of the selected element: set it, or with null remove it back to its default.</summary>
        public static void Set(JsonObject node, string key, JsonNode? value, bool tree = false)
        {
            _selectionBefore = PathOf(Selected);
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

        public static Type? TypeOf(JsonObject node)
        {
            if (node["type"]?.GetValue<string>() is { } name && UiMarkup.Types.TryGetValue(name, out var type))
            {
                return type;
            }

            return null;
        }

        public static bool HoldsChildren(JsonObject node) => TypeOf(node) is { } type && typeof(Panel).IsAssignableFrom(type) && !typeof(ItemsControl).IsAssignableFrom(type);

        // A border, a scroller or an expander. Controls are decorators too, but their child is their own
        // insides -- a text box's editor -- and not somewhere to put more layout.
        public static bool HoldsChild(JsonObject node) =>
            TypeOf(node) is { } type && (typeof(ItemsControl).IsAssignableFrom(type) || typeof(Decorator).IsAssignableFrom(type) && !typeof(Control).IsAssignableFrom(type));

        /// <summary>Where a single child lives: a list's row template, or a decorator's child.</summary>
        public static string SlotKey(JsonObject node)
        {
            if (TypeOf(node) is { } type && typeof(ItemsControl).IsAssignableFrom(type))
            {
                return "itemTemplate";
            }

            return "child";
        }

        /// <summary>The element a node sits in, or null for the root.</summary>
        public static JsonObject? ParentOf(JsonObject node)
        {
            if (ReferenceEquals(node, Root))
            {
                return null;
            }

            return node.Parent switch
            {
                JsonArray array => array.Parent as JsonObject,
                JsonObject parent => parent,
                _ => null
            };
        }

        /// <summary>Whether <paramref name="node"/> is <paramref name="ancestor"/> or sits somewhere beneath it.</summary>
        public static bool IsWithin(JsonObject? node, JsonObject ancestor)
        {
            for (var at = node; at != null; at = ParentOf(at))
            {
                if (ReferenceEquals(at, ancestor))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Adds an element: into the selected one when it holds children (or an empty single child), and
        /// otherwise after it, in its parent.
        /// </summary>
        public static JsonObject Add(string type)
        {
            _selectionBefore = PathOf(Selected);
            var node = Starter(type);
            var at = Selected ?? Root;
            if (HoldsChildren(at))
            {
                ChildrenOf(at).Add(node);
            }
            else if (HoldsChild(at) && at[SlotKey(at)] == null)
            {
                at[SlotKey(at)] = node;
            }
            else if (ParentOf(at) != null && at.Parent is JsonArray siblings)
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

        // What a new element starts as, so it can be seen and has something to grab.
        private static JsonObject Starter(string type) => type switch
        {
            "TextBlock" => new JsonObject { ["type"] = type, ["text"] = "Text" },
            "Button" => new JsonObject { ["type"] = type, ["text"] = "Button" },
            "Grid" => new JsonObject { ["type"] = type, ["columns"] = "*, *", ["rows"] = "Auto" },
            "Border" => new JsonObject { ["type"] = type, ["width"] = 200, ["height"] = 120, ["background"] = "@Surface", ["cornerRadius"] = 6 },
            "ProgressBar" => new JsonObject { ["type"] = type, ["width"] = 200, ["fraction"] = 0.5 },
            "Slider" => new JsonObject { ["type"] = type, ["width"] = 200, ["minimum"] = 0, ["maximum"] = 100 },
            _ => new JsonObject { ["type"] = type }
        };

        public static void Delete(JsonObject node)
        {
            var parent = ParentOf(node);
            if (parent == null)
            {
                return;
            }

            _selectionBefore = PathOf(Selected);
            Detach(node);
            Selected = parent;
            Touch(tree: true);
        }

        private static void Detach(JsonObject node)
        {
            switch (node.Parent)
            {
                case JsonArray array:
                    array.Remove(node);
                    break;
                case JsonObject owner:
                    owner.Remove(SlotKey(owner));
                    break;
            }
        }

        /// <summary>A copy beside the original, selected. Names go: code finds elements by them, so they stay unique.</summary>
        public static JsonObject? Duplicate(JsonObject node)
        {
            if (node.Parent is not JsonArray siblings)
            {
                return null;
            }

            _selectionBefore = PathOf(Selected);
            var copy = (JsonObject)node.DeepClone();
            void Strip(JsonObject item)
            {
                item.Remove("name");
                foreach (var nested in Nested(item))
                {
                    Strip(nested);
                }
            }

            Strip(copy);
            siblings.Insert(siblings.IndexOf(node) + 1, copy);
            Selected = copy;
            Touch(tree: true);
            return copy;
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

            _selectionBefore = PathOf(Selected);
            array.RemoveAt(index);
            array.Insert(target, node);
            Touch(tree: true);
        }

        /// <summary>
        /// Moves an element into <paramref name="parent"/> at <paramref name="index"/> (the outline's drag). False,
        /// changing nothing, when it cannot go there: into itself, or into something with no room. Its slot settings
        /// go with the parent they belonged to.
        /// </summary>
        public static bool Reparent(JsonObject node, JsonObject parent, int index)
        {
            if (ParentOf(node) == null || IsWithin(parent, node))
            {
                return false;
            }

            var panel = HoldsChildren(parent);
            if (!panel && !(HoldsChild(parent) && (parent[SlotKey(parent)] == null || ReferenceEquals(parent[SlotKey(parent)], node))))
            {
                return false;
            }

            _selectionBefore = PathOf(Selected);
            var oldParent = ParentOf(node);
            if (ReferenceEquals(oldParent, parent) && node.Parent is JsonArray same && same.IndexOf(node) < index)
            {
                index--;
            }

            Detach(node);
            if (!ReferenceEquals(oldParent, parent))
            {
                node.Remove("grid");
                node.Remove("dock");
                node.Remove("canvas");
            }

            if (panel)
            {
                var children = ChildrenOf(parent);
                children.Insert(Math.Clamp(index, 0, children.Count), node);
            }
            else
            {
                parent[SlotKey(parent)] = node;
            }

            Selected = node;
            Touch(tree: true);
            return true;
        }

        public static int ChildrenCount(JsonObject node) => Nested(node).Count();

        /// <summary>The element of that name in the open file, if there is one.</summary>
        public static JsonObject? FindNamed(string name)
        {
            JsonObject? Walk(JsonObject node)
            {
                if (node["name"] is JsonValue value && value.TryGetValue<string>(out var called) && called == name)
                {
                    return node;
                }

                foreach (var child in Nested(node))
                {
                    if (Walk(child) is { } found)
                    {
                        return found;
                    }
                }

                return null;
            }

            return Walk(Root);
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

            if (node["itemTemplate"] is JsonObject template)
            {
                yield return template;
            }
        }
    }

    /// <summary>
    /// Dragging an element or one of its edges in the preview, turned into what the file can say. How a move is
    /// written depends on where the element sits: a canvas child's left/top; otherwise its margin on the sides its
    /// alignment holds it by, and its size only if the drag really changed that. A stack places its children
    /// itself, so those can be resized but not moved -- reorder them in the outline.
    /// </summary>
    public sealed class UiDesignerDrag
    {
        private readonly JsonObject _node;
        private readonly bool _inCanvas;
        private readonly HorizontalAlignment _horizontal;
        private readonly VerticalAlignment _vertical;
        private readonly Thickness _margin;
        private readonly Vector2 _size;
        private readonly bool _hasWidth;
        private readonly bool _hasHeight;
        private readonly Vector2 _canvasStart;
        private readonly bool _fromRight;
        private readonly bool _fromBottom;
        private readonly float _startRight;
        private readonly float _startBottom;
        private bool _resizedX;
        private bool _resizedY;

        public UiDesignerDrag(JsonObject node, UiElement element, JsonObject? parent)
        {
            _node = node;
            Type? parentType = null;
            if (parent != null)
            {
                parentType = UiDesignerDocument.TypeOf(parent);
            }

            _inCanvas = parentType == typeof(Canvas);
            CanMove = parentType != null && parentType != typeof(StackPanel) && parentType != typeof(WrapPanel);
            _horizontal = element.HorizontalAlignment;
            _vertical = element.VerticalAlignment;
            _margin = element.Margin;
            _size = new Vector2(element.Bounds.Width, element.Bounds.Height);
            _hasWidth = !float.IsNaN(element.Width);
            _hasHeight = !float.IsNaN(element.Height);
            if (_inCanvas)
            {
                var slot = element.LayoutSlotOrNull;
                var parentBounds = element.Parent?.Bounds ?? element.Bounds;
                _canvasStart = new Vector2(element.Bounds.X - parentBounds.X, element.Bounds.Y - parentBounds.Y);
                _fromRight = slot != null && float.IsNaN(slot.CanvasLeft) && !float.IsNaN(slot.CanvasRight);
                _fromBottom = slot != null && float.IsNaN(slot.CanvasTop) && !float.IsNaN(slot.CanvasBottom);
                if (slot != null)
                {
                    _startRight = slot.CanvasRight;
                    _startBottom = slot.CanvasBottom;
                }
            }
        }

        public bool CanMove { get; }

        /// <summary>Applies how far each edge has moved since the drag began, in layout units.</summary>
        public void Apply(float left, float top, float right, float bottom)
        {
            // A stretched element with no size of its own takes it from the margins, so only those change.
            _resizedX |= MathF.Abs(right - left) > 0.01f;
            _resizedY |= MathF.Abs(bottom - top) > 0.01f;
            if (_resizedX && (_hasWidth || !IsStretch(_horizontal)))
            {
                _node["width"] = MathF.Round(MathF.Max(_size.X + right - left, 1));
            }

            if (_resizedY && (_hasHeight || !IsStretch(_vertical)))
            {
                _node["height"] = MathF.Round(MathF.Max(_size.Y + bottom - top, 1));
            }

            if (_inCanvas)
            {
                WriteCanvas(left, top, right, bottom);
            }
            else
            {
                var (marginLeft, marginRight) = Edges(_horizontal == HorizontalAlignment.Left, _horizontal == HorizontalAlignment.Right, _margin.Left, _margin.Right, left, right);
                var (marginTop, marginBottom) = Edges(_vertical == VerticalAlignment.Top, _vertical == VerticalAlignment.Bottom, _margin.Top, _margin.Bottom, top, bottom);
                WriteMargin(marginLeft, marginTop, marginRight, marginBottom);
            }

            UiDesignerDocument.TouchLive();
        }

        private static bool IsStretch(HorizontalAlignment alignment) => alignment == HorizontalAlignment.Stretch;

        private static bool IsStretch(VerticalAlignment alignment) => alignment == VerticalAlignment.Stretch;

        // Held by the near side: only it moves. By the far side: only that. Otherwise (centred, stretched) both,
        // which for a centred element shifts its centre by half of each.
        private static (float Near, float Far) Edges(bool near, bool far, float startNear, float startFar, float deltaNear, float deltaFar)
        {
            if (near)
            {
                return (startNear + deltaNear, startFar);
            }

            if (far)
            {
                return (startNear, startFar - deltaFar);
            }

            return (startNear + deltaNear, startFar - deltaFar);
        }

        private void WriteMargin(float left, float top, float right, float bottom)
        {
            left = MathF.Round(left);
            top = MathF.Round(top);
            right = MathF.Round(right);
            bottom = MathF.Round(bottom);
            if (left == 0 && top == 0 && right == 0 && bottom == 0)
            {
                _node.Remove("margin");
            }
            else if (left == right && top == bottom && left == top)
            {
                _node["margin"] = left;
            }
            else
            {
                _node["margin"] = new JsonArray(left, top, right, bottom);
            }
        }

        private void WriteCanvas(float left, float top, float right, float bottom)
        {
            if (_node["canvas"] is not JsonObject canvas)
            {
                canvas = new JsonObject();
                _node["canvas"] = canvas;
            }

            if (_fromRight)
            {
                canvas["right"] = MathF.Round(_startRight - right);
            }
            else
            {
                canvas["left"] = MathF.Round(_canvasStart.X + left);
            }

            if (_fromBottom)
            {
                canvas["bottom"] = MathF.Round(_startBottom - bottom);
            }
            else
            {
                canvas["top"] = MathF.Round(_canvasStart.Y + top);
            }
        }
    }
}

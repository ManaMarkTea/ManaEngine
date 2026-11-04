using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ManaUI
{
    /// <summary>A loaded markup tree, with its named elements and whatever could not be applied.</summary>
    public sealed class UiMarkupResult
    {
        public UiElement? Root { get; init; }

        /// <summary>Elements by their "name", for code to find the pieces it drives.</summary>
        public Dictionary<string, UiElement> Named { get; } = new(StringComparer.Ordinal);

        /// <summary>Each JSON node and the element built from it -- how the designer finds what was clicked.</summary>
        public Dictionary<JsonObject, UiElement> Elements { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Unknown types, properties and values: reported, not thrown, so one typo does not blank the screen.</summary>
        public List<string> Problems { get; } = new();

        public T? Find<T>(string name) where T : UiElement => Named.TryGetValue(name, out var element) ? element as T : null;
    }

    /// <summary>
    /// UI from JSON (a .manaui file): each object names a ManaUI element "type" and sets its properties by
    /// their names -- "text", "fontSize", "margin", "horizontalAlignment". Around that:
    /// <list type="bullet">
    /// <item>"children" (a panel's) or "child" (a border's, a scroller's) nest elements;</item>
    /// <item>"grid": {"row", "column", "rowSpan", "columnSpan"}, "dock" and "canvas": {"left", "top", ...} place a child in its panel;</item>
    /// <item>"columns"/"rows" lay out a Grid ("Auto, *, 2*"), "options" fill a ComboBox;</item>
    /// <item>"bind": {"text": "Score"} follows a property of the view model (MVVM: through INotifyPropertyChanged),
    /// or the long form {"value": {"path": "Volume", "mode": "TwoWay", "format": "{0:F0}", "invert": false, "trigger": "commit"}};
    /// "command" binds a Button to an ICommand;</item>
    /// <item>"on": {"click": "PlayCommand"} runs a command (or method) when the element's event fires;</item>
    /// <item>"context": "Weapons[2]" makes the paths on this element and below relative to that.</item>
    /// <item>An ItemsControl's "itemTemplate" is an element built per item, with the item as its view model:
    /// {"type": "ItemsControl", "bind": {"itemsSource": "Missions"}, "itemTemplate": {"type": "TextBlock", "bind": {"text": "Name"}}}.</item>
    /// <item>"bind" reaches a place in the parent too: "grid.row", "grid.columnSpan", "canvas.left", "dock", "zIndex",
    /// and a Grid's "columns" and "rows".</item>
    /// </list>
    /// Colours are "#RRGGBB[AA]", [r, g, b, a], or "@Accent" for a theme colour; thicknesses a number,
    /// [horizontal, vertical] or [left, top, right, bottom].
    /// </summary>
    public static class UiMarkup
    {
        public const string Extension = ".manaui";

        /// <summary>Keys that are not element properties.</summary>
        public static readonly HashSet<string> ReservedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            "type", "name", "children", "child", "grid", "dock", "canvas", "zIndex", "bind", "on", "columns", "rows", "options", "context", "itemTemplate"
        };

        /// <summary>The element types a document may use, by name: ManaUI's own, and any registered.</summary>
        public static IReadOnlyDictionary<string, Type> Types => TypeTable.Value;

        // Concurrent: a game registers its own elements as its assembly loads, whenever that is.
        private static readonly Lazy<ConcurrentDictionary<string, Type>> TypeTable = new(() => new ConcurrentDictionary<string, Type>(
            typeof(UiElement).Assembly.GetTypes()
                .Where(type => typeof(UiElement).IsAssignableFrom(type) && !type.IsAbstract && type.IsPublic
                    && type.GetConstructor(Type.EmptyTypes) != null)
                .GroupBy(type => type.Name)
                .ToDictionary(group => group.Key, group => group.First()), StringComparer.OrdinalIgnoreCase));

        /// <summary>The types offered for new elements in an editor: the everyday ones, containers first.</summary>
        public static readonly string[] CommonTypes =
        [
            "StackPanel", "Grid", "WrapPanel", "DockPanel", "Canvas", "Border", "PictureFrame", "ScrollViewer",
            "TextBlock", "UiImage", "Button", "CheckBox", "TextBox", "NumericBox", "Slider", "ComboBox", "ProgressBar", "Expander", "ItemsControl"
        ];

        public static void Register(Type type)
        {
            if (!typeof(UiElement).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new ArgumentException($"{type.Name} is not a UiElement with a parameterless constructor.", nameof(type));
            }

            TypeTable.Value[type.Name] = type;
        }

        /// <param name="viewModel">What "bind" and "on" entries work against; without one the layout shows as authored.</param>
        /// <param name="viewModelType">Checks the paths against this type when there is no instance yet (the designer).</param>
        public static UiMarkupResult Load(string json, UiTheme? theme = null, object? viewModel = null, Type? viewModelType = null)
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            }
            catch (JsonException exception)
            {
                var failed = new UiMarkupResult();
                failed.Problems.Add($"Not JSON: {exception.Message}");
                return failed;
            }

            return Load(node as JsonObject, theme, viewModel, viewModelType);
        }

        public static UiMarkupResult Load(JsonObject? document, UiTheme? theme = null, object? viewModel = null, Type? viewModelType = null)
        {
            var problems = new List<string>();
            var named = new Dictionary<string, UiElement>(StringComparer.Ordinal);
            var elements = new Dictionary<JsonObject, UiElement>(ReferenceEqualityComparer.Instance);

            // A document is either the root element itself or {"root": {...}} with room for more beside it.
            var rootNode = document?["root"] as JsonObject ?? document;
            var binding = new BindScope(viewModel, viewModelType ?? viewModel?.GetType());
            var root = rootNode == null ? null : Build(rootNode, theme ?? new UiTheme(), binding, string.Empty, problems, named, elements);
            var result = new UiMarkupResult { Root = root };
            result.Problems.AddRange(problems);
            foreach (var (name, element) in named)
            {
                result.Named[name] = element;
            }

            foreach (var (node, element) in elements)
            {
                result.Elements[node] = element;
            }

            return result;
        }

        private readonly record struct BindScope(object? Instance, Type? Type);

        private static string Scoped(string scope, string path)
        {
            if (scope.Length == 0)
            {
                return path;
            }

            return path is "" or "." ? scope : scope + "." + path;
        }

        private static UiElement? Build(
            JsonObject node,
            UiTheme theme,
            BindScope binding,
            string scope,
            List<string> problems,
            Dictionary<string, UiElement> named,
            Dictionary<JsonObject, UiElement> elements)
        {
            var typeName = node["type"]?.GetValue<string>() ?? string.Empty;
            if (!Types.TryGetValue(typeName, out var type))
            {
                problems.Add($"Unknown element type '{typeName}'.");
                return null;
            }

            var element = (UiElement)Activator.CreateInstance(type)!;
            elements[node] = element;
            foreach (var (key, value) in node)
            {
                if (IsReserved(type, key) || value == null)
                {
                    continue;
                }

                SetProperty(element, key, value, theme, problems);
            }

            if (node["name"]?.GetValue<string>() is { Length: > 0 } name)
            {
                element.Name = name;
                named[name] = element;
            }

            ApplySpecials(element, node, problems);
            ApplySlot(element, node, problems);
            if (node["context"] is JsonValue context && context.TryGetValue<string>(out var contextPath))
            {
                scope = Scoped(scope, contextPath);
            }

            // Before the bindings, so a bound list builds its rows once.
            ApplyItemTemplate(element, node, theme, problems);
            ApplyBindings(element, node, binding, scope, theme, problems);
            ApplyEvents(element, node, binding, scope, problems);

            if (node["children"] is JsonArray children)
            {
                if (element is Panel panel)
                {
                    foreach (var child in children.OfType<JsonObject>())
                    {
                        if (Build(child, theme, binding, scope, problems, named, elements) is { } built)
                        {
                            panel.Children.Add(built);
                        }
                    }
                }
                else
                {
                    problems.Add($"{type.Name} holds no children; use \"child\".");
                }
            }

            if (node["child"] is JsonObject single)
            {
                var built = Build(single, theme, binding, scope, problems, named, elements);
                switch (element)
                {
                    // Before Decorator: an Expander is one, but its Child is its header and body.
                    case Expander expander:
                        expander.Content = built;
                        break;
                    case Decorator decorator:
                        decorator.Child = built;
                        break;
                    default:
                        if (type.GetProperty("Content") is { CanWrite: true } content && content.PropertyType.IsAssignableFrom(typeof(UiElement)))
                        {
                            content.SetValue(element, built);
                        }
                        else if (type.GetProperty("Child") is { CanWrite: true } child && child.PropertyType.IsAssignableFrom(typeof(UiElement)))
                        {
                            child.SetValue(element, built);
                        }
                        else
                        {
                            problems.Add($"{type.Name} holds no child.");
                        }

                        break;
                }
            }

            return element;
        }

        // An ItemsControl's "itemTemplate" is an element tree built once per item, with the item as its view model.
        private static void ApplyItemTemplate(UiElement element, JsonObject node, UiTheme theme, List<string> problems)
        {
            if (node["itemTemplate"] is not JsonObject template)
            {
                return;
            }

            if (element is not ItemsControl items)
            {
                problems.Add($"Only an ItemsControl has an itemTemplate, not {element.GetType().Name}.");
                return;
            }

            var reported = false;
            items.ItemTemplate = item =>
            {
                var rowProblems = new List<string>();
                var row = Build(template, theme, new BindScope(item, item?.GetType()), string.Empty, rowProblems,
                    new Dictionary<string, UiElement>(StringComparer.Ordinal), new Dictionary<JsonObject, UiElement>(ReferenceEqualityComparer.Instance));
                if (!reported && rowProblems.Count > 0)
                {
                    // Every row repeats the same mistakes; the first one's are enough.
                    reported = true;
                    problems.AddRange(rowProblems);
                }

                return row ?? new Border();
            };
        }

        /// <summary>The writable properties a document can set on a type, by the name it uses for them.</summary>
        public static IReadOnlyList<PropertyInfo> SettableProperties(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanWrite && property.SetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0
                    && IsSupported(property.PropertyType))
                .GroupBy(property => property.Name)
                .Select(group => group.First())
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToList();

        public static bool IsSupported(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(string) || type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(bool)
                || type.IsEnum || type == typeof(Vector4) || type == typeof(Vector2) || type == typeof(Thickness) || type == typeof(CornerRadius);
        }

        /// <summary>Whether a key means something other than a property of this type; a SlotGrid's "columns" is its own.</summary>
        public static bool IsReserved(Type type, string key) =>
            ReservedKeys.Contains(key) && !(typeof(SlotGrid).IsAssignableFrom(type) && key.Equals("columns", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The properties a binding can drive, wider than what a document can write: a view model can hand over a
        /// command or a list. Not what only code can supply (templates, child elements), nor a list that has no
        /// way to show its items until a document can give it a template.
        /// </summary>
        public static IReadOnlyList<PropertyInfo> BindableProperties(Type type)
        {
            var needsTemplate = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(property =>
                property.PropertyType.IsGenericType && typeof(Delegate).IsAssignableFrom(property.PropertyType)
                && property.PropertyType.GetGenericArguments()[^1] == typeof(UiElement));
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => UiMarkupTarget.CanDrive(property) && property.Name is not ("Name" or "ProfileName")
                    && !(needsTemplate && typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(string)))
                .GroupBy(property => property.Name)
                .Select(group => group.First())
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The type a "bind" key drives on an element of this type; null when it drives nothing.</summary>
        public static Type? BindTargetType(Type type, string key)
        {
            var property = type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && UiMarkupTarget.CanDrive(property))
            {
                return property.PropertyType;
            }

            return UiMarkupTarget.PlacementType(type, key);
        }

        /// <summary>Whether edits to this target can flow back to the view model (TwoWay, OneWayToSource).</summary>
        public static bool CanWriteBack(Type type, string key) => UiMarkupEdits.CanHook(type, key);

        /// <summary>Whether a view model's value of one type can drive a target of another: the conversions <see cref="Coerce"/> makes.</summary>
        public static bool CanBind(Type source, Type target)
        {
            source = Nullable.GetUnderlyingType(source) ?? source;
            target = Nullable.GetUnderlyingType(target) ?? target;
            if (source == typeof(object))
            {
                // Only the running view model knows.
                return true;
            }

            if (target == typeof(string))
            {
                return source == typeof(string) || source.IsValueType;
            }

            if (target.IsAssignableFrom(source))
            {
                return source != typeof(string) || target != typeof(System.Collections.IEnumerable);
            }

            if (IsNumber(target))
            {
                return IsNumber(source);
            }

            if (target == typeof(Visibility))
            {
                return source == typeof(bool);
            }

            if (target.IsEnum)
            {
                return source == typeof(string) || source == typeof(int);
            }

            if (target == typeof(Vector4) || target == typeof(Vector2))
            {
                return source == typeof(string);
            }

            if (target == typeof(Thickness) || target == typeof(CornerRadius))
            {
                return source == typeof(string) || IsNumber(source);
            }

            return false;
        }

        private static bool IsNumber(Type type) =>
            type == typeof(float) || type == typeof(double) || type == typeof(int) || type == typeof(long) || type == typeof(uint)
            || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(decimal);

        /// <summary>The camelCase name a document uses for a property.</summary>
        public static string KeyFor(string propertyName) =>
            propertyName.Length == 0 ? propertyName : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];

        private static void SetProperty(UiElement element, string key, JsonNode value, UiTheme theme, List<string> problems)
        {
            var property = element.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property == null || !property.CanWrite || property.SetMethod is not { IsPublic: true })
            {
                problems.Add($"{element.GetType().Name} has no property '{key}'.");
                return;
            }

            if (!TryConvert(value, property.PropertyType, theme, out var converted, out var why))
            {
                problems.Add($"{element.GetType().Name}.{property.Name}: {why}");
                return;
            }

            try
            {
                property.SetValue(element, converted);
            }
            catch (Exception exception) when (exception is TargetInvocationException or ArgumentException)
            {
                problems.Add($"{element.GetType().Name}.{property.Name}: {(exception.InnerException ?? exception).Message}");
            }
        }

        private static void ApplySpecials(UiElement element, JsonObject node, List<string> problems)
        {
            if (element is Grid grid)
            {
                if (node["columns"] is JsonValue columns && columns.TryGetValue<string>(out var columnText))
                {
                    grid.SetColumns(columnText);
                }

                if (node["rows"] is JsonValue rows && rows.TryGetValue<string>(out var rowText))
                {
                    grid.SetRows(rowText);
                }
            }
            else if (element is not SlotGrid && (node["columns"] != null || node["rows"] != null))
            {
                problems.Add($"Only a Grid has columns and rows, not {element.GetType().Name}.");
            }

            if (node["options"] is JsonArray options)
            {
                if (element is ComboBox combo)
                {
                    combo.SetItems(options.Select(option => option?.ToString() ?? string.Empty).ToList(), null);
                }
                else
                {
                    problems.Add($"Only a ComboBox has options, not {element.GetType().Name}.");
                }
            }
        }

        private static void ApplySlot(UiElement element, JsonObject node, List<string> problems)
        {
            if (node["grid"] is JsonObject grid)
            {
                var slot = element.EnsureLayoutSlot();
                slot.GridRow = Int(grid, "row", 0);
                slot.GridColumn = Int(grid, "column", 0);
                slot.GridRowSpan = Math.Max(1, Int(grid, "rowSpan", 1));
                slot.GridColumnSpan = Math.Max(1, Int(grid, "columnSpan", 1));
            }

            if (node["dock"] is JsonValue dock && dock.TryGetValue<string>(out var dockText))
            {
                if (Enum.TryParse<Dock>(dockText, ignoreCase: true, out var side))
                {
                    element.EnsureLayoutSlot().Dock = side;
                }
                else
                {
                    problems.Add($"'{dockText}' is not a dock side.");
                }
            }

            if (node["canvas"] is JsonObject canvas)
            {
                var slot = element.EnsureLayoutSlot();
                slot.CanvasLeft = Float(canvas, "left");
                slot.CanvasTop = Float(canvas, "top");
                slot.CanvasRight = Float(canvas, "right");
                slot.CanvasBottom = Float(canvas, "bottom");
            }

            if (TryNumber(node["zIndex"], out var zIndex))
            {
                element.EnsureLayoutSlot().ZIndex = (int)zIndex;
            }

            static int Int(JsonObject from, string key, int fallback)
            {
                if (TryNumber(from[key], out var number))
                {
                    return (int)Math.Round(number);
                }

                return fallback;
            }

            static float Float(JsonObject from, string key)
            {
                if (TryNumber(from[key], out var number))
                {
                    return (float)number;
                }

                return float.NaN;
            }
        }

        private static void ApplyBindings(UiElement element, JsonObject node, BindScope binding, string scope, UiTheme theme, List<string> problems)
        {
            if (node["bind"] is not JsonObject bindings)
            {
                return;
            }

            // A value last, or the range and items bound beside it would not be there yet to hold it.
            foreach (var (key, source) in bindings.OrderBy(pair => pair.Key.ToLowerInvariant() is "value" or "selectedindex"))
            {
                var target = UiMarkupTarget.Find(element, key);
                if (target == null || !ReadBinding(source, out var spec))
                {
                    problems.Add($"{element.GetType().Name} cannot bind '{key}'.");
                    continue;
                }

                // Nothing to bind to yet, or no path chosen yet: a designed layout still shows, with the property as authored.
                if (binding.Type == null || spec.Path.Length == 0)
                {
                    continue;
                }

                var path = UiMarkupPathPart.Parse(Scoped(scope, spec.Path));
                var problem = UiMarkupPathPart.Check(binding.Type, path, out var end, out var observable);
                if (problem == null && spec.Format != null)
                {
                    end = typeof(string);
                }

                if (problem == null && end != null && spec.Mode != BindingMode.OneWayToSource && !CanBind(end, target.Type))
                {
                    problem = $"a {end.Name} cannot drive a {target.Type.Name}.";
                }

                if (problem != null)
                {
                    problems.Add($"{element.GetType().Name}.{target.Name}: {problem}");
                    continue;
                }

                var writesBack = spec.Mode is BindingMode.TwoWay or BindingMode.OneWayToSource;
                var noWriteBack = string.Empty;
                if (writesBack && !UiMarkupEdits.CanHook(element.GetType(), key))
                {
                    noWriteBack = "it raises no edit event";
                }
                else if (writesBack && UiMarkupPathPart.IsReadOnly(binding.Type, path))
                {
                    noWriteBack = $"'{spec.Path}' has no setter";
                }

                if (noWriteBack.Length > 0)
                {
                    problems.Add($"{element.GetType().Name}.{target.Name} cannot write back ({spec.Mode}): {noWriteBack}.");
                    if (spec.Mode == BindingMode.OneWayToSource)
                    {
                        continue;
                    }

                    writesBack = false;
                }

                if (binding.Instance == null)
                {
                    continue;
                }

                var live = new UiMarkupBinding(target, element, binding.Instance, path, spec.Mode, spec.Format, spec.Invert, theme);
                if (writesBack)
                {
                    UiMarkupEdits.Hook(element, key, spec.OnCommit, live.PushToSource);
                }

                // A source that can announce needs no frame-by-frame look.
                if (observable || spec.Mode is BindingMode.OneTime or BindingMode.OneWayToSource)
                {
                    element.AddBinding(live);
                }
                else
                {
                    element.AddPolledBinding(live);
                }
            }
        }

        private readonly record struct BindingSpec(string Path, BindingMode Mode, string? Format, bool Invert, bool OnCommit);

        // "Score", or {"path": "Volume", "mode": "TwoWay", "format": "{0:F0}", "invert": true, "trigger": "commit"}.
        private static bool ReadBinding(JsonNode? source, out BindingSpec spec)
        {
            spec = default;
            if (source is JsonValue plain && plain.TryGetValue<string>(out var path))
            {
                spec = new BindingSpec(path, BindingMode.OneWay, null, false, false);
                return true;
            }

            if (source is not JsonObject full || full["path"] is not JsonValue pathValue || !pathValue.TryGetValue<string>(out var longPath))
            {
                return false;
            }

            var mode = BindingMode.OneWay;
            if (full["mode"] is JsonValue modeValue && modeValue.TryGetValue<string>(out var modeText) && !Enum.TryParse(modeText, ignoreCase: true, out mode))
            {
                return false;
            }

            string? format = null;
            if (full["format"] is JsonValue formatValue)
            {
                formatValue.TryGetValue(out format);
            }

            var invert = full["invert"] is JsonValue invertValue && invertValue.TryGetValue<bool>(out var inverted) && inverted;
            var onCommit = full["trigger"] is JsonValue trigger && trigger.TryGetValue<string>(out var triggerText)
                && string.Equals(triggerText, "commit", StringComparison.OrdinalIgnoreCase);
            spec = new BindingSpec(longPath, mode, format, invert, onCommit);
            return true;
        }

        private static void ApplyEvents(UiElement element, JsonObject node, BindScope binding, string scope, List<string> problems)
        {
            if (node["on"] is not JsonObject events)
            {
                return;
            }

            foreach (var (key, action) in events)
            {
                var info = element.GetType().GetEvent(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                string? actionPath = null;
                JsonNode? parameter = null;
                if (action is JsonValue plain)
                {
                    plain.TryGetValue(out actionPath);
                }
                else if (action is JsonObject full && full["command"] is JsonValue command)
                {
                    command.TryGetValue(out actionPath);
                    parameter = full["parameter"];
                }

                if (info?.EventHandlerType is not { } handlerType || actionPath == null)
                {
                    problems.Add($"{element.GetType().Name} has no event '{key}'.");
                    continue;
                }

                if (binding.Type == null || actionPath.Length == 0)
                {
                    continue;
                }

                var path = UiMarkupPathPart.Parse(Scoped(scope, actionPath));
                if (UiMarkupCommands.Check(binding.Type, path) is { } problem)
                {
                    problems.Add($"{element.GetType().Name}.{info.Name}: {problem}");
                    continue;
                }

                if (binding.Instance is not { } instance)
                {
                    continue;
                }

                object? argument = null;
                if (parameter is not null)
                {
                    TryConvert(parameter, typeof(object), new UiTheme(), out argument, out _);
                }

                Action run = () => UiMarkupCommands.Run(instance, path, argument);
                if (handlerType == typeof(EventHandler))
                {
                    // The everyday case needs no generated code, which the ahead-of-time web build has to interpret.
                    info.AddEventHandler(element, new EventHandler((_, _) => run()));
                    continue;
                }

                var parameters = handlerType.GetMethod("Invoke")!.GetParameters()
                    .Select(item => System.Linq.Expressions.Expression.Parameter(item.ParameterType))
                    .ToArray();
                var body = System.Linq.Expressions.Expression.Invoke(System.Linq.Expressions.Expression.Constant(run));
                info.AddEventHandler(element, System.Linq.Expressions.Expression.Lambda(handlerType, body, parameters).Compile());
            }
        }

        /// <summary>A bound value, into the property's type: numbers across numeric types, anything into text.</summary>
        internal static object? Coerce(object? value, Type type, UiTheme theme)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (value == null)
            {
                return null;
            }

            if (type.IsInstanceOfType(value))
            {
                return value;
            }

            // A view model says whether, not how: no element-type knowledge on its side.
            if (type == typeof(Visibility) && value is bool visible)
            {
                if (visible)
                {
                    return Visibility.Visible;
                }

                return Visibility.Collapsed;
            }

            // A colour named in text ("#FF0000", "@Accent") works from a bound string as from a document.
            if (type == typeof(Vector4) && value is string colour)
            {
                if (TryColor(JsonValue.Create(colour), theme, out var parsed))
                {
                    return parsed;
                }

                return null;
            }

            if (type == typeof(string))
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            // Spacing from one number, or from text as a document writes it ("8, 4").
            if (type == typeof(Thickness) || type == typeof(CornerRadius) || type == typeof(Vector2))
            {
                JsonNode? written = null;
                if (value is string words)
                {
                    written = JsonValue.Create(words);
                }
                else if (IsNumber(value.GetType()))
                {
                    written = JsonValue.Create(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                }

                if (written != null && TryConvert(written, type, theme, out var spacing, out _))
                {
                    return spacing;
                }

                return null;
            }

            try
            {
                if (type.IsEnum)
                {
                    return value is string text ? Enum.Parse(type, text, ignoreCase: true) : Enum.ToObject(type, value);
                }

                return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException or ArgumentException)
            {
                return null;
            }
        }

        /// <summary>A JSON value as a property's type. False, with why, when it cannot be one.</summary>
        public static bool TryConvert(JsonNode value, Type type, UiTheme theme, out object? converted, out string why)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            converted = null;
            why = string.Empty;
            try
            {
                if (type == typeof(string))
                {
                    converted = value is JsonValue text && text.TryGetValue<string>(out var s) ? s : value.ToJsonString();
                    return true;
                }

                if (type == typeof(object))
                {
                    if (TryNumber(value, out var loose))
                    {
                        converted = loose;
                    }
                    else if (value is JsonValue item && item.TryGetValue<bool>(out var flag))
                    {
                        converted = flag;
                    }
                    else
                    {
                        converted = value is JsonValue words && words.TryGetValue<string>(out var said) ? said : value.ToJsonString();
                    }

                    return true;
                }

                if (type == typeof(float) || type == typeof(double) || type == typeof(int))
                {
                    if (!TryNumber(value, out var number))
                    {
                        why = $"'{value.ToJsonString()}' is not a number";
                        return false;
                    }

                    converted = type == typeof(float) ? (object)(float)number : type == typeof(int) ? (int)Math.Round(number) : number;
                    return true;
                }

                if (type == typeof(bool))
                {
                    converted = value.GetValue<bool>();
                    return true;
                }

                if (type.IsEnum)
                {
                    converted = Enum.Parse(type, value.GetValue<string>(), ignoreCase: true);
                    return true;
                }

                if (type == typeof(Vector4))
                {
                    if (TryColor(value, theme, out var color))
                    {
                        converted = color;
                        return true;
                    }

                    why = $"'{value.ToJsonString()}' is not a colour";
                    return false;
                }

                var numbers = Numbers(value);
                if (type == typeof(Vector2) && numbers.Length >= 2)
                {
                    converted = new Vector2(numbers[0], numbers[1]);
                    return true;
                }

                if (type == typeof(Thickness) && numbers.Length is 1 or 2 or 4)
                {
                    converted = numbers.Length switch
                    {
                        1 => new Thickness(numbers[0]),
                        2 => new Thickness(numbers[0], numbers[1]),
                        _ => new Thickness(numbers[0], numbers[1], numbers[2], numbers[3])
                    };
                    return true;
                }

                if (type == typeof(CornerRadius) && numbers.Length is 1 or 4)
                {
                    converted = numbers.Length == 1 ? new CornerRadius(numbers[0]) : new CornerRadius(numbers[0], numbers[1], numbers[2], numbers[3]);
                    return true;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException or ArgumentException)
            {
                why = $"'{value.ToJsonString()}' is not a {type.Name}";
                return false;
            }

            why = $"'{value.ToJsonString()}' is not a {type.Name}";
            return false;
        }

        /// <summary>
        /// A number however its node came to be: one parsed from a file converts freely, but one an editor made
        /// holds its CLR type and answers only to that, so a value typed in the designer would read as missing.
        /// </summary>
        internal static bool TryNumber(JsonNode? value, out double number)
        {
            number = 0;
            if (value is not JsonValue item)
            {
                return false;
            }

            if (item.TryGetValue(out number))
            {
                return true;
            }

            if (item.TryGetValue<int>(out var whole))
            {
                number = whole;
                return true;
            }

            if (item.TryGetValue<float>(out var single))
            {
                number = single;
                return true;
            }

            if (item.TryGetValue<long>(out var big))
            {
                number = big;
                return true;
            }

            return false;
        }

        private static float[] Numbers(JsonNode value)
        {
            if (value is JsonArray array)
            {
                return array.Select(item => item != null && TryNumber(item, out var number) ? (float)number : 0.0f).ToArray();
            }

            if (TryNumber(value, out var single))
            {
                return [(float)single];
            }

            // "4, 2" as text reads the same as [4, 2], which is what a one-line editor field writes.
            if (value is JsonValue text && text.TryGetValue<string>(out var s))
            {
                var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var parsed = new float[parts.Length];
                for (var i = 0; i < parts.Length; i++)
                {
                    if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out parsed[i]))
                    {
                        return [];
                    }
                }

                return parsed;
            }

            return [];
        }

        private static bool TryColor(JsonNode value, UiTheme theme, out Vector4 color)
        {
            color = default;
            if (value is JsonArray)
            {
                var numbers = Numbers(value);
                if (numbers.Length is 3 or 4)
                {
                    color = new Vector4(numbers[0], numbers[1], numbers[2], numbers.Length == 4 ? numbers[3] : 1.0f);
                    return true;
                }

                return false;
            }

            if (value is not JsonValue text || !text.TryGetValue<string>(out var s))
            {
                return false;
            }

            s = s.Trim();
            if (s.StartsWith('@'))
            {
                var token = typeof(UiTheme).GetProperty(s[1..], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (token?.PropertyType == typeof(Vector4))
                {
                    color = (Vector4)token.GetValue(theme)!;
                    return true;
                }

                return false;
            }

            if (s.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            {
                color = Vector4.Zero;
                return true;
            }

            try
            {
                color = UiColor.FromHex(s);
                return true;
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                return false;
            }
        }
    }
}

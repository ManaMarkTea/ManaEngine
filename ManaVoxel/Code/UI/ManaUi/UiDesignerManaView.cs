using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The UI Designer -- UMG for ManaUI. A .manaui file's elements as an outline, the layout built live from
    /// it, and the selected element's properties. Clicking in the preview selects what was clicked; every edit
    /// goes into the JSON, and the preview is built from the JSON again, so what is saved is what was seen.
    /// Layouts load in a game with <see cref="UiMarkup.Load(string, UiTheme?, UiMarkupData?)"/>.
    /// </summary>
    public static class UiDesignerManaView
    {
        public const string PanelId = "uidesigner";

        public static UiElement Build(UiTheme theme)
        {
            var preview = new UiDesignerPreview(theme);
            var body = new Grid().Columns("210, *, 290", theme.Spacing).Rows("*").Children(
                Framed(theme, new DynamicContent().Showing<object>(() => UiDesignerDocument.TreeToken, _ => Outline(theme))
                    .Scrollable().Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Auto)).GridColumn(0),
                Framed(theme, preview).GridColumn(1),
                Framed(theme, new DynamicContent().Showing<object>(() => UiDesignerDocument.SelectionToken, _ => Properties(theme))
                    .Scrollable().Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Disabled)).GridColumn(2));

            return new Grid().Rows("Auto, *", theme.Spacing).Columns("*").Margin(theme.Spacing).Children(
                Toolbar(theme, preview).GridRow(0),
                body.GridRow(1));
        }

        private static UiElement Framed(UiTheme theme, UiElement content) =>
            new Border().Outline(theme.Border, 1.0f).Rounded(3.0f).PaddingOf(2.0f, 2.0f).Content(content);

        // ----- file and structure -----------------------------------------------------------------------

        private static UiElement Toolbar(UiTheme theme, UiDesignerPreview preview)
        {
            var files = new DynamicContent().Showing<object>(() => UiDesignerDocument.TreeToken, _ =>
            {
                var paths = UiDesignerDocument.List().ToList();
                var names = paths.Select(path => System.IO.Path.GetFileNameWithoutExtension(path) ?? "").ToList();
                var current = UiDesignerDocument.Path == null ? -1 : paths.FindIndex(path => string.Equals(path, UiDesignerDocument.Path, StringComparison.OrdinalIgnoreCase));
                if (current < 0)
                {
                    names.Insert(0, "(not saved)");
                    paths.Insert(0, string.Empty);
                    current = 0;
                }

                var combo = new ComboBox().Options(names.ToArray());
                combo.SelectedIndex = current;
                combo.Width = 180.0f;
                combo.SelectionChanged += (_, index) =>
                {
                    if (index >= 0 && index < paths.Count && paths[index].Length > 0 && paths[index] != UiDesignerDocument.Path)
                    {
                        UiDesignerDocument.TryOpen(paths[index], out string? _);
                    }
                };
                combo.WithTooltip($"Layouts in {UiDesignerDocument.FolderName}/");
                return combo;
            });

            var newLayout = new ManaUI.Button().Label("New").Variant(ButtonVariant.Subtle)
                .OnClick(() => UiDesignerDocument.Replace(UiDesignerDocument.NewDocument(), null));
            var save = new ManaUI.Button().Label("Save").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                try
                {
                    UiDesignerDocument.Save();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    preview.Status = $"Could not save: {exception.Message}";
                }
            });
            save.BindPolled(button => button.Text, () => UiDesignerDocument.Dirty ? "Save *" : "Save");
            save.WithTooltip("Save to the open file, or a new one named after the root element");

            var type = new ComboBox().Options(UiMarkup.CommonTypes);
            type.SelectedIndex = Array.IndexOf(UiMarkup.CommonTypes, "TextBlock");
            type.Width = 120.0f;
            var add = new ManaUI.Button().Label("+ Add").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                if (type.SelectedIndex >= 0)
                {
                    UiDesignerDocument.Add(UiMarkup.CommonTypes[type.SelectedIndex]);
                }
            });
            add.WithTooltip("Add an element into the selected one, or after it when it holds nothing");

            var delete = new ManaUI.Button().Label("Delete").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                if (UiDesignerDocument.Selected is { } selected)
                {
                    UiDesignerDocument.Delete(selected);
                }
            });
            delete.BindPolled(button => button.IsEnabled, () => UiDesignerDocument.Selected is { } selected && UiDesignerDocument.ParentOf(selected) != null);

            var up = new ManaUI.Button().Label("Up").Variant(ButtonVariant.Subtle).OnClick(() => MoveSelected(-1));
            var down = new ManaUI.Button().Label("Down").Variant(ButtonVariant.Subtle).OnClick(() => MoveSelected(1));

            var status = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).CenterV().Ellipsis();
            status.BindPolled(text => text.Text, () => preview.Status);
            status.BindPolled(text => text.Tooltip, () => preview.StatusDetail);

            return new Grid().Columns("Auto, Auto, Auto, Auto, Auto, Auto, Auto, Auto, *", theme.Spacing).Rows("Auto").Children(
                files.GridColumn(0), newLayout.GridColumn(1), save.GridColumn(2),
                type.GridColumn(3), add.GridColumn(4), delete.GridColumn(5), up.GridColumn(6), down.GridColumn(7),
                status.GridColumn(8));

            static void MoveSelected(int by)
            {
                if (UiDesignerDocument.Selected is { } selected)
                {
                    UiDesignerDocument.Move(selected, by);
                }
            }
        }

        private static UiElement Outline(UiTheme theme)
        {
            var rows = new StackPanel().Vertical(0.0f);
            void Add(JsonObject node, int depth)
            {
                var name = node["name"]?.GetValue<string>();
                var label = node["type"]?.GetValue<string>() ?? "?";
                var text = node["text"] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
                var caption = name != null ? $"{label}  {name}" : text != null ? $"{label}  \"{text}\"" : label;

                var row = new ManaUI.Button().Label(caption).OnClick(() => UiDesignerDocument.Select(node));
                row.Margin = new Thickness(depth * 12.0f, 0.0f, 0.0f, 0.0f);
                row.BindPolled(button => button.Variant,
                    () => ReferenceEquals(UiDesignerDocument.Selected, node) ? ButtonVariant.Primary : ButtonVariant.Subtle);
                rows.Children.Add(row);
                foreach (var child in UiDesignerDocument.Nested(node))
                {
                    Add(child, depth + 1);
                }
            }

            Add(UiDesignerDocument.Root, 0);
            return rows;
        }

        // ----- properties -------------------------------------------------------------------------------

        private static UiElement Properties(UiTheme theme)
        {
            var node = UiDesignerDocument.Selected;
            var type = node == null ? null : UiDesignerDocument.TypeOf(node);
            if (node == null || type == null)
            {
                return new TextBlock().Text("Select an element.").Foreground(theme.TextMuted).Margin(theme.Spacing);
            }

            var list = new StackPanel().Vertical(2.0f).Margin(theme.Spacing);
            list.Children.Add(new TextBlock().Text(type.Name).FontFamily(theme.FontFamilyMedium));
            list.Children.Add(Row(theme, "name", TextField(node, "name", tree: true)));

            // Where it sits in its parent, when its parent places children by slot.
            var parentType = UiDesignerDocument.ParentOf(node) is { } parent ? UiDesignerDocument.TypeOf(parent) : null;
            if (parentType == typeof(Grid))
            {
                foreach (var key in new[] { "row", "column", "rowSpan", "columnSpan" })
                {
                    list.Children.Add(Row(theme, key, SlotNumber(node, "grid", key)));
                }
            }
            else if (parentType == typeof(DockPanel))
            {
                list.Children.Add(Row(theme, "dock", EnumField(node, "dock", typeof(Dock))));
            }
            else if (parentType == typeof(Canvas))
            {
                foreach (var key in new[] { "left", "top", "right", "bottom" })
                {
                    list.Children.Add(Row(theme, key, SlotNumber(node, "canvas", key)));
                }
            }

            if (type == typeof(Grid))
            {
                list.Children.Add(Row(theme, "columns", TextField(node, "columns")));
                list.Children.Add(Row(theme, "rows", TextField(node, "rows")));
            }

            if (type == typeof(ComboBox))
            {
                list.Children.Add(Row(theme, "options", ListField(node, "options")));
            }

            list.Children.Add(Row(theme, "bind", PairsField(node, "bind", "text=score: read a value each frame")));
            list.Children.Add(Row(theme, "on", PairsField(node, "on", "click=play: run an action on an event")));

            // What the document sets first, then everything else the type has, folded away.
            var properties = UiMarkup.SettableProperties(type);
            var set = properties.Where(property => node.ContainsKey(UiMarkup.KeyFor(property.Name)) && !UiMarkup.ReservedKeys.Contains(property.Name)).ToList();
            foreach (var property in set)
            {
                list.Children.Add(Row(theme, UiMarkup.KeyFor(property.Name), Editor(node, property)));
            }

            var rest = new StackPanel().Vertical(2.0f);
            foreach (var property in properties.Except(set).Where(property => !UiMarkup.ReservedKeys.Contains(property.Name)))
            {
                rest.Children.Add(Row(theme, UiMarkup.KeyFor(property.Name), Editor(node, property)));
            }

            list.Children.Add(new Expander().Header($"All properties ({properties.Count})").Content(rest));
            return list;
        }

        /// <summary>An editor for one property, by its type. Clearing a field puts the property back to its default.</summary>
        private static UiElement Editor(JsonObject node, PropertyInfo property)
        {
            var key = UiMarkup.KeyFor(property.Name);
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var fallback = DefaultOf(property);
            if (type == typeof(bool))
            {
                var box = new CheckBox().Checked(node[key] is JsonValue value && value.TryGetValue<bool>(out var on) ? on : fallback is true);
                box.OnChanged(value => UiDesignerDocument.Set(node, key, JsonValue.Create(value)));
                return box;
            }

            if (type.IsEnum)
            {
                return EnumField(node, key, type);
            }

            if (type == typeof(float) || type == typeof(double) || type == typeof(int))
            {
                var field = new TextBox { Text = node[key]?.ToJsonString() ?? string.Empty };
                field.TextCommitted += (_, text) =>
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        UiDesignerDocument.Set(node, key, null);
                    }
                    else if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        UiDesignerDocument.Set(node, key, JsonValue.Create(number));
                    }
                };
                return field.Hint(Describe(fallback));
            }

            // Text, colours ("#RRGGBB", "@Accent"), thicknesses and vectors ("8, 4") are all typed as text.
            var hint = type == typeof(Vector4) ? "#RRGGBB or @Accent" : Describe(fallback);
            return TextField(node, key, tree: key == "text").Hint(hint);
        }

        private static readonly Dictionary<Type, UiElement?> Fresh = new();

        /// <summary>What a property is on an element nothing has set, read off a new one of its type.</summary>
        private static object? DefaultOf(PropertyInfo property)
        {
            var owner = property.ReflectedType ?? property.DeclaringType!;
            if (!Fresh.TryGetValue(owner, out var instance))
            {
                try
                {
                    instance = Activator.CreateInstance(owner) as UiElement;
                }
                catch (Exception exception) when (exception is TargetInvocationException or MissingMethodException)
                {
                    instance = null;
                }

                Fresh[owner] = instance;
            }

            try
            {
                return instance == null || property.GetMethod == null ? null : property.GetValue(instance);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        private static string Describe(object? value) => value switch
        {
            null => "default",
            float number when float.IsNaN(number) => "auto",
            float number => number.ToString("0.##", CultureInfo.InvariantCulture),
            double number => number.ToString("0.##", CultureInfo.InvariantCulture),
            Thickness t => t.Left == t.Right && t.Top == t.Bottom && t.Left == t.Top
                ? t.Left.ToString("0.##", CultureInfo.InvariantCulture)
                : string.Join(", ", new[] { t.Left, t.Top, t.Right, t.Bottom }.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture))),
            string text when text.Length == 0 => "",
            _ => value.ToString() ?? "default"
        };

        private static TextBox TextField(JsonObject node, string key, bool tree = false)
        {
            var field = new TextBox { Text = Display(node[key]) };
            field.TextCommitted += (_, text) =>
            {
                if (string.IsNullOrEmpty(text))
                {
                    UiDesignerDocument.Set(node, key, null, tree);
                    return;
                }

                // A list of numbers is kept as a JSON list, which is what a hand-written file would say.
                var parts = text.Split(',', StringSplitOptions.TrimEntries);
                var numbers = parts.Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : (double?)null).ToList();
                JsonNode value = key is not ("text" or "name" or "columns" or "rows") && numbers.All(n => n != null) && parts.Length is 2 or 4
                    ? new JsonArray(numbers.Select(n => (JsonNode?)JsonValue.Create(n!.Value)).ToArray())
                    : key is not ("text" or "name" or "columns" or "rows") && numbers.Count == 1 && numbers[0] is { } single
                        ? JsonValue.Create(single)
                        : JsonValue.Create(text);
                UiDesignerDocument.Set(node, key, value, tree);
            };
            return field;
        }

        private static string Display(JsonNode? value) => value switch
        {
            null => string.Empty,
            JsonArray array => string.Join(", ", array.Select(item => item?.ToJsonString() ?? "")),
            JsonValue text when text.TryGetValue<string>(out var s) => s,
            _ => value.ToJsonString()
        };

        private static UiElement EnumField(JsonObject node, string key, Type type)
        {
            var names = new[] { "(default)" }.Concat(Enum.GetNames(type)).ToArray();
            var combo = new ComboBox().Options(names);
            var current = node[key] is JsonValue value && value.TryGetValue<string>(out var s) ? Array.FindIndex(names, n => n.Equals(s, StringComparison.OrdinalIgnoreCase)) : 0;
            combo.SelectedIndex = Math.Max(0, current);
            combo.SelectionChanged += (_, index) =>
                UiDesignerDocument.Set(node, key, index <= 0 ? null : JsonValue.Create(names[index]));
            return combo;
        }

        private static UiElement ListField(JsonObject node, string key)
        {
            var field = new TextBox { Text = node[key] is JsonArray array ? string.Join(", ", array.Select(item => item?.ToString())) : string.Empty };
            field.TextCommitted += (_, text) => UiDesignerDocument.Set(node, key,
                string.IsNullOrWhiteSpace(text) ? null : new JsonArray(text.Split(',', StringSplitOptions.TrimEntries).Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()));
            return field.Hint("a, b, c");
        }

        /// <summary>"text=score, value=volume" for bind and on: property or event, then the key it reads or runs.</summary>
        private static UiElement PairsField(JsonObject node, string key, string tooltip)
        {
            var field = new TextBox
            {
                Text = node[key] is JsonObject pairs ? string.Join(", ", pairs.Select(pair => $"{pair.Key}={Display(pair.Value)}")) : string.Empty
            };
            field.TextCommitted += (_, text) =>
            {
                var pairs = new JsonObject();
                foreach (var part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var split = part.IndexOf('=');
                    if (split > 0 && split < part.Length - 1)
                    {
                        pairs[part[..split].Trim()] = part[(split + 1)..].Trim();
                    }
                }

                UiDesignerDocument.Set(node, key, pairs.Count == 0 ? null : pairs);
            };
            field.WithTooltip(tooltip);
            return field;
        }

        private static UiElement SlotNumber(JsonObject node, string slot, string key)
        {
            var field = new TextBox { Text = node[slot] is JsonObject values && values[key] is { } value ? value.ToJsonString() : string.Empty };
            field.TextCommitted += (_, text) =>
            {
                if (node[slot] is not JsonObject values)
                {
                    values = new JsonObject();
                    node[slot] = values;
                }

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    values[key] = number;
                }
                else
                {
                    values.Remove(key);
                }

                if (values.Count == 0)
                {
                    node.Remove(slot);
                }

                UiDesignerDocument.Touch();
            };
            return field.Hint("0");
        }

        private static UiElement Row(UiTheme theme, string label, UiElement editor)
        {
            var name = new TextBlock().Text(label).FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Ellipsis().CenterV();
            name.Width = 104.0f;
            name.Tooltip = label;
            return new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Children(name.GridColumn(0), editor.GridColumn(1));
        }
    }

    /// <summary>
    /// The live preview: the document built into elements, the selected one outlined, and a click selecting
    /// what it lands on rather than working it -- the controls are being laid out, not used.
    /// </summary>
    public sealed class UiDesignerPreview : Decorator
    {
        private readonly UiTheme _theme;
        private object? _built;
        private UiMarkupResult? _result;

        public UiDesignerPreview(UiTheme theme)
        {
            _theme = theme;
            ClipToBounds = true;

            // Polled, since an edit only replaces the document's token: a binding is read every frame,
            // before layout, where measure is only run when something marked it stale.
            this.BindPolled(preview => preview.Status, () =>
            {
                Rebuild();
                return Status;
            });
        }

        public string Status { get; set; } = string.Empty;

        public string StatusDetail { get; private set; } = string.Empty;

        private void Rebuild()
        {
            if (ReferenceEquals(_built, UiDesignerDocument.PreviewToken))
            {
                return;
            }

            _built = UiDesignerDocument.PreviewToken;
            _result = UiMarkup.Load(UiDesignerDocument.Root, _theme);
            Child = _result.Root;
            Status = _result.Problems.Count == 0 ? string.Empty : $"{_result.Problems.Count} problem(s): {_result.Problems[0]}";
            StatusDetail = string.Join(Environment.NewLine, _result.Problems);
        }

        protected override void OnPreviewPointerPressed(UiPointerEventArgs e)
        {
            if (_result == null)
            {
                return;
            }

            // The deepest element under the pointer: the one with the most ancestors among those hit.
            JsonObject? best = null;
            var bestDepth = -1;
            foreach (var (node, element) in _result.Elements)
            {
                if (!element.Bounds.Contains(e.Position))
                {
                    continue;
                }

                var depth = 0;
                for (var parent = element.Parent; parent != null; parent = parent.Parent)
                {
                    depth++;
                }

                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    best = node;
                }
            }

            if (best != null)
            {
                UiDesignerDocument.Select(best);
                e.Handled = true;
            }
        }

        protected override void OnRenderOverlay(ManaUiDrawList drawList)
        {
            if (_result != null && UiDesignerDocument.Selected is { } selected && _result.Elements.TryGetValue(selected, out var element))
            {
                drawList.DrawBorder(element.Bounds, _theme.Accent, 2.0f, new CornerRadius(2.0f));
            }
        }
    }
}

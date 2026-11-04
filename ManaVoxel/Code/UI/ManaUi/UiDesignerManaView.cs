using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows.Input;
using ManaUI;
using ManaVoxel.Code.Levels;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The UI Designer -- UMG for ManaUI. A .manaui file's elements as an outline (drag to rearrange), the layout built
    /// live from it at a chosen screen size (click to select, drag to move, handles to resize), and the selected
    /// element's properties, bindings and commands. Every edit goes into the JSON and the preview is built from the
    /// JSON again, so what is saved is what was seen. A UiCanvas component shows a layout in a level.
    /// </summary>
    public static class UiDesignerManaView
    {
        public const string PanelId = "uidesigner";

        private static readonly (string Label, Vector2 Size)[] Screens =
        [
            ("1920 x 1080", new Vector2(1920, 1080)),
            ("1280 x 720", new Vector2(1280, 720)),
            ("2560 x 1440", new Vector2(2560, 1440)),
            ("1024 x 768", new Vector2(1024, 768)),
            ("390 x 844 phone", new Vector2(390, 844)),
            ("Fit the panel", Vector2.Zero)
        ];

        public static UiElement Build(UiTheme theme)
        {
            var preview = new UiDesignerPreview(theme);
            var outline = new UiDesignerOutline(theme, preview);
            var body = new Grid().Columns("240, *, 320", theme.Spacing).Rows("*").Children(
                Framed(theme, new DynamicContent().Showing<object>(() => UiDesignerDocument.TreeToken, _ => outline.Rebuild())
                    .Scrollable().Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Auto)).GridColumn(0),
                Framed(theme, preview).GridColumn(1),
                Framed(theme, new DynamicContent().Showing<object>(() => UiDesignerDocument.SelectionToken, _ => Properties(theme))
                    .Scrollable().Scrolls(vertical: ScrollBarVisibility.Auto, horizontal: ScrollBarVisibility.Disabled)).GridColumn(2));

            var guard = new UnsavedGuard(theme);
            return new Grid().Rows("Auto, Auto, Auto, *", theme.Spacing).Columns("*").Margin(theme.Spacing).Children(
                FileBar(theme, preview, guard).GridRow(0),
                EditBar(theme, preview).GridRow(1),
                guard.Bar.GridRow(2),
                body.GridRow(3));
        }

        private static UiElement Framed(UiTheme theme, UiElement content) =>
            new Border().Outline(theme.Border, 1.0f).Rounded(3.0f).PaddingOf(2.0f, 2.0f).Content(content);

        private static ManaUI.Button ToolButton(string label, Action click, string tooltip)
        {
            var button = new ManaUI.Button().Label(label).Variant(ButtonVariant.Subtle).OnClick(click);
            button.WithTooltip(tooltip);
            return button;
        }

        // ----- file ------------------------------------------------------------------------------------

        /// <summary>Asks before something would throw away unsaved edits: save them, drop them, or stay.</summary>
        private sealed class UnsavedGuard
        {
            private Action? _pending;

            public UnsavedGuard(UiTheme theme)
            {
                var message = new TextBlock().Foreground(theme.Warning).CenterV();
                message.BindPolled(text => text.Text, () => $"Unsaved changes in {System.IO.Path.GetFileNameWithoutExtension(UiDesignerDocument.Path) ?? "this new layout"}.");
                var row = new StackPanel().Horizontal(theme.Spacing);
                row.Children.Add(message);
                row.Children.Add(new ManaUI.Button().Label("Save").Variant(ButtonVariant.Primary).OnClick(() =>
                {
                    if (TrySave(out _))
                    {
                        Finish();
                    }
                }));
                row.Children.Add(new ManaUI.Button().Label("Discard").OnClick(Finish));
                row.Children.Add(new ManaUI.Button().Label("Cancel").OnClick(() => _pending = null));
                row.BindPolled(r => r.Visibility, () => Shown(_pending != null));
                Bar = row;
            }

            public UiElement Bar { get; }

            public void Run(Action action)
            {
                if (UiDesignerDocument.Dirty)
                {
                    _pending = action;
                }
                else
                {
                    action();
                }
            }

            private void Finish()
            {
                var action = _pending;
                _pending = null;
                action?.Invoke();
            }
        }

        private static Visibility Shown(bool visible)
        {
            if (visible)
            {
                return Visibility.Visible;
            }

            return Visibility.Collapsed;
        }

        private static bool TrySave(out string? problem, string? name = null)
        {
            problem = null;
            try
            {
                UiDesignerDocument.Save(name);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                problem = $"Could not save: {exception.Message}";
                return false;
            }
        }

        private static UiElement FileBar(UiTheme theme, UiDesignerPreview preview, UnsavedGuard guard)
        {
            var files = new DynamicContent().Showing<object>(() => UiDesignerDocument.TreeToken, _ =>
            {
                var paths = UiDesignerDocument.List().ToList();
                var names = paths.Select(path => System.IO.Path.GetFileNameWithoutExtension(path) ?? "").ToList();
                var current = paths.FindIndex(path => string.Equals(path, UiDesignerDocument.Path, StringComparison.OrdinalIgnoreCase));
                if (current < 0)
                {
                    names.Insert(0, "(not saved)");
                    paths.Insert(0, string.Empty);
                    current = 0;
                }

                var combo = new ComboBox().Options(names.ToArray());
                combo.SelectedIndex = current;
                combo.Width = 190.0f;
                combo.SelectionChanged += (_, index) =>
                {
                    if (index >= 0 && index < paths.Count && paths[index].Length > 0 && paths[index] != UiDesignerDocument.Path)
                    {
                        var path = paths[index];
                        guard.Run(() => UiDesignerDocument.TryOpen(path, out string? _));
                    }
                };
                combo.WithTooltip($"Layouts: each content folder's {UiDesignerDocument.FolderName}/");
                return combo;
            });

            var newLayout = ToolButton("New", () => guard.Run(() => UiDesignerDocument.Replace(UiDesignerDocument.NewDocument("Layout"), null)), "A new empty layout");

            // A name is only asked for when there is no file yet, or for Save As.
            var askingName = false;
            var nameBox = new TextBox { Width = 170.0f };
            nameBox.Hint("Layout name");
            void SaveNamed()
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    return;
                }

                if (TrySave(out var problem, nameBox.Text.Trim()))
                {
                    askingName = false;
                }
                else
                {
                    preview.Status = problem ?? string.Empty;
                }
            }

            var save = ToolButton("Save", () =>
            {
                if (UiDesignerDocument.Path == null)
                {
                    nameBox.Text = UiDesignerDocument.Document["name"]?.GetValue<string>() ?? "Layout";
                    askingName = true;
                }
                else if (!TrySave(out var problem))
                {
                    preview.Status = problem ?? string.Empty;
                }
            }, "Save (Ctrl+S)");
            save.BindPolled(button => button.Text, () =>
            {
                if (UiDesignerDocument.Dirty)
                {
                    return "Save *";
                }

                return "Save";
            });

            var saveAs = ToolButton("Save As", () =>
            {
                nameBox.Text = (System.IO.Path.GetFileNameWithoutExtension(UiDesignerDocument.Path) ?? "Layout") + " Copy";
                askingName = true;
            }, "Save a copy under a new name");
            nameBox.TextCommitted += (_, _) => SaveNamed();
            nameBox.BindPolled(box => box.Visibility, () => Shown(askingName));
            var confirm = ToolButton("OK", SaveNamed, "Save under this name");
            confirm.BindPolled(button => button.Visibility, () => Shown(askingName));

            var undo = ToolButton("Undo", UiDesignerDocument.Undo, "Undo (Ctrl+Z)");
            undo.BindPolled(button => button.IsEnabled, () => UiDesignerDocument.CanUndo);
            var redo = ToolButton("Redo", UiDesignerDocument.Redo, "Redo (Ctrl+Y)");
            redo.BindPolled(button => button.IsEnabled, () => UiDesignerDocument.CanRedo);

            var screen = new ComboBox().Options(Screens.Select(item => item.Label).ToArray());
            screen.SelectedIndex = 0;
            screen.Width = 140.0f;
            screen.SelectionChanged += (_, index) =>
            {
                if (index >= 0 && index < Screens.Length)
                {
                    preview.DesignSize = Screens[index].Size;
                }
            };
            screen.WithTooltip("The screen the layout is shown on. Try a few: anchors should hold at all of them.");

            var viewModel = new DynamicContent().Showing<object>(() => UiDesignerDocument.TreeToken, _ => ViewModelPicker());

            var status = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).CenterV().Ellipsis();
            status.BindPolled(text => text.Text, () => preview.Status);
            status.BindPolled(text => text.Tooltip, () => preview.StatusDetail);

            return new Grid().Columns("Auto, Auto, Auto, Auto, Auto, Auto, Auto, Auto, Auto, Auto, *", theme.Spacing).Rows("Auto").Children(
                files.GridColumn(0), newLayout.GridColumn(1), save.GridColumn(2), saveAs.GridColumn(3), nameBox.GridColumn(4), confirm.GridColumn(5),
                undo.GridColumn(6), redo.GridColumn(7), screen.GridColumn(8), viewModel.GridColumn(9), status.GridColumn(10));
        }

        // Which game mode's view model this layout binds to: kept in the file, so it is known wherever it is opened from.
        private static UiElement ViewModelPicker()
        {
            var sources = ManaLevelGameModes.Available.Select(definition => definition.Type)
                .Where(type => ManaLevelGameModes.Create(type)?.HudViewModelType != null).ToList();
            sources.Insert(0, string.Empty);
            var current = UiDesignerDocument.ViewModelSource;
            if (!sources.Contains(current))
            {
                sources.Add(current);
            }

            var labels = sources.Select(type => type + " view model").ToArray();
            labels[0] = "No view model";
            var combo = new ComboBox().Options(labels);
            combo.SelectedIndex = sources.IndexOf(current);
            combo.Width = 170.0f;
            combo.SelectionChanged += (_, index) =>
            {
                if (index >= 0 && index < sources.Count)
                {
                    UiDesignerDocument.ViewModelSource = sources[index];
                }
            };
            combo.WithTooltip("The view model this layout binds to: its properties and commands are offered below, and paths are checked against it");
            return combo;
        }

        private static UiElement EditBar(UiTheme theme, UiDesignerPreview preview)
        {
            var row = new StackPanel().Horizontal(theme.Spacing);
            var type = new ComboBox().Options(UiMarkup.CommonTypes);
            type.SelectedIndex = Array.IndexOf(UiMarkup.CommonTypes, "TextBlock");
            type.Width = 130.0f;
            row.Children.Add(type);
            row.Children.Add(ToolButton("+ Add", () =>
            {
                if (type.SelectedIndex >= 0)
                {
                    UiDesignerDocument.Add(UiMarkup.CommonTypes[type.SelectedIndex]);
                }
            }, "Add an element into the selected one, or after it when it holds nothing"));

            var duplicate = ToolButton("Duplicate", () => OnSelected(node => UiDesignerDocument.Duplicate(node)), "A copy beside it (Ctrl+D)");
            duplicate.BindPolled(button => button.IsEnabled, () => UiDesignerDocument.Selected?.Parent is JsonArray);
            row.Children.Add(duplicate);

            var delete = ToolButton("Delete", () => OnSelected(UiDesignerDocument.Delete), "Delete it and what is in it (Del)");
            delete.BindPolled(button => button.IsEnabled, () => UiDesignerDocument.Selected is { } selected && UiDesignerDocument.ParentOf(selected) != null);
            row.Children.Add(delete);
            row.Children.Add(ToolButton("Up", () => OnSelected(node => UiDesignerDocument.Move(node, -1)), "Earlier among its siblings: drawn under them"));
            row.Children.Add(ToolButton("Down", () => OnSelected(node => UiDesignerDocument.Move(node, 1)), "Later among its siblings: drawn over them"));

            var hint = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).CenterV()
                .Text("Click to select, drag to move, handles to resize. Drag in the outline to rearrange. Selecting a hidden element shows it here.");
            row.Children.Add(hint);
            return row;

            static void OnSelected(Action<JsonObject> act)
            {
                if (UiDesignerDocument.Selected is { } selected)
                {
                    act(selected);
                }
            }
        }

        // ----- properties -------------------------------------------------------------------------------

        private static readonly string[] LayoutKeys =
            ["HorizontalAlignment", "VerticalAlignment", "Width", "Height", "Margin", "Padding", "Visibility", "Opacity"];

        private static UiElement Properties(UiTheme theme)
        {
            var node = UiDesignerDocument.Selected;
            if (node == null || UiDesignerDocument.TypeOf(node) is not { } type)
            {
                return new TextBlock().Text("Select an element.").Foreground(theme.TextMuted).Margin(theme.Spacing);
            }

            var list = new StackPanel().Vertical(2.0f).Margin(theme.Spacing);
            var shown = new HashSet<string>(StringComparer.Ordinal);

            // Every row goes through here, so whatever a row sets can be bound instead.
            UiElement Prop(string label, string key, Func<UiElement> editor)
            {
                shown.Add(key);
                return Bindable(theme, node, label, key, editor);
            }

            list.Children.Add(new TextBlock().Text(type.Name).FontSize(theme.FontSizeHeading).Foreground(theme.Accent));
            list.Children.Add(Row(theme, "name", TextField(node, "name", tree: true)));

            // Where it sits in its parent, when its parent places children by slot.
            Type? parentType = null;
            if (UiDesignerDocument.ParentOf(node) is { } parent)
            {
                parentType = UiDesignerDocument.TypeOf(parent);
            }

            if (parentType == typeof(Grid))
            {
                Section(theme, list, "In its grid");
                foreach (var key in new[] { "row", "column", "rowSpan", "columnSpan" })
                {
                    list.Children.Add(Prop(key, "grid." + key, () => SlotNumber(node, "grid", key)));
                }
            }
            else if (parentType == typeof(DockPanel))
            {
                Section(theme, list, "In its dock panel");
                list.Children.Add(Prop("dock", "dock", () => EnumField(node, "dock", typeof(Dock))));
            }
            else if (parentType == typeof(Canvas))
            {
                Section(theme, list, "On its canvas");
                foreach (var key in new[] { "left", "top", "right", "bottom" })
                {
                    list.Children.Add(Prop(key, "canvas." + key, () => SlotNumber(node, "canvas", key)));
                }
            }

            if (type == typeof(Grid))
            {
                list.Children.Add(Prop("columns", "columns", () => TextField(node, "columns")));
                list.Children.Add(Prop("rows", "rows", () => TextField(node, "rows")));
            }

            if (type == typeof(ComboBox))
            {
                // The items a document lists, or the list a view model holds.
                list.Children.Add(Prop("options", "itemsSource", () => ListField(node, "options")));
            }

            var properties = UiMarkup.BindableProperties(type).Where(property => !UiMarkup.IsReserved(type, property.Name) && !shown.Contains(UiMarkup.KeyFor(property.Name))).ToList();
            UiElement PropertyRow(PropertyInfo property)
            {
                var key = UiMarkup.KeyFor(property.Name);
                return Prop(key, key, () => Editor(theme, node, property));
            }

            Section(theme, list, "Layout");
            var layout = properties.Where(property => LayoutKeys.Contains(property.Name)).OrderBy(property => Array.IndexOf(LayoutKeys, property.Name)).ToList();
            foreach (var property in layout)
            {
                list.Children.Add(PropertyRow(property));
            }

            list.Children.Add(Prop("zIndex", "zIndex", () => NumberField(node, "zIndex", "0")));

            Section(theme, list, "Bindings");
            Bindings(theme, list, node, type);
            var commands = properties.Where(property => property.Name is "Command" or "CommandParameter").ToList();
            foreach (var property in commands)
            {
                list.Children.Add(PropertyRow(property));
            }

            var strays = list.Children.Count;

            // What the document sets or binds first, then everything else the type has, folded away.
            var other = properties.Except(layout).Except(commands).ToList();
            var set = other.Where(property => node.ContainsKey(UiMarkup.KeyFor(property.Name)) || IsBound(node, UiMarkup.KeyFor(property.Name))).ToList();
            if (set.Count > 0)
            {
                Section(theme, list, "Set here");
                foreach (var property in set)
                {
                    list.Children.Add(PropertyRow(property));
                }
            }

            var rest = new StackPanel().Vertical(2.0f);
            foreach (var property in other.Except(set))
            {
                rest.Children.Add(PropertyRow(property));
            }

            list.Children.Add(new Expander().Header($"All properties ({other.Count})").Content(rest));

            // Bindings with no row of their own (a hand-written typo, a slot of a parent it has left) stay visible, to fix or remove.
            if (node["bind"] is JsonObject binds)
            {
                foreach (var (target, _) in binds.ToList().Where(pair => !shown.Contains(pair.Key)))
                {
                    list.Children.Insert(strays, BoundRow(theme, node, binds, target, target, null));
                }
            }

            return list;
        }

        private static bool IsBound(JsonObject node, string key) => node["bind"] is JsonObject binds && binds.ContainsKey(key);

        /// <summary>A property's row: its editor with a bind button, or once bound, the path that drives it.</summary>
        private static UiElement Bindable(UiTheme theme, JsonObject node, string label, string key, Func<UiElement> editor)
        {
            if (node["bind"] is JsonObject binds && binds.ContainsKey(key))
            {
                return BoundRow(theme, node, binds, label, key, editor);
            }

            var bind = new ManaUI.Button().Label("\U0001F517").Variant(ButtonVariant.Subtle);
            bind.Name = "bind:" + key;
            bind.OnClick(() => StartBinding(bind, node, key));
            bind.WithTooltip($"Bind {label} to the view model");
            return Row(theme, label, new Grid().Columns("*, Auto", 2.0f).Rows("Auto").Children(editor().GridColumn(0), bind.GridColumn(1)));
        }

        // With a view model the list comes straight up, and nothing is written until a path is chosen.
        private static void StartBinding(UiElement anchor, JsonObject node, string key)
        {
            if (UiDesignerDocument.ViewModelType == null)
            {
                Bind(node, key, string.Empty);
                return;
            }

            var target = TargetOf(node, key);
            OpenPicker(anchor, node, KindFor(target), string.Empty, path => Bind(node, key, path), target, key);
        }

        private static Type? TargetOf(JsonObject node, string key)
        {
            if (UiDesignerDocument.TypeOf(node) is not { } type)
            {
                return null;
            }

            return UiMarkup.BindTargetType(type, key);
        }

        private static void Bind(JsonObject node, string key, string path)
        {
            if (node["bind"] is not JsonObject binds)
            {
                binds = new JsonObject();
                node["bind"] = binds;
            }

            binds[key] = path;
            UiDesignerDocument.Touch(tree: true);
        }

        /// <summary>For scripts: binds a property of the selected element, or with no path opens its picker from its button.</summary>
        public static void BindSelected(UiElement anchor, string key, string? path)
        {
            if (UiDesignerDocument.Selected is not { } node)
            {
                return;
            }

            if (path != null)
            {
                Bind(node, key, path);
            }
            else
            {
                StartBinding(anchor, node, key);
            }
        }

        private static void Section(UiTheme theme, StackPanel list, string title) =>
            list.Children.Add(new TextBlock().Text(title).FontSize(theme.FontSizeSmall).Foreground(theme.Accent).Margin(0.0f, theme.Spacing, 0.0f, 2.0f));

        /// <summary>An editor for one property, by its type. Clearing a field puts the property back to its default.</summary>
        private static UiElement Editor(UiTheme theme, JsonObject node, PropertyInfo property)
        {
            var key = UiMarkup.KeyFor(property.Name);
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var fallback = DefaultOf(property);
            if (type == typeof(bool))
            {
                var isChecked = fallback is true;
                if (node[key] is JsonValue value && value.TryGetValue<bool>(out var on))
                {
                    isChecked = on;
                }

                var box = new CheckBox().Checked(isChecked);
                box.OnChanged(value => UiDesignerDocument.Set(node, key, JsonValue.Create(value)));
                return box;
            }

            if (type.IsEnum)
            {
                return EnumField(node, key, type);
            }

            if (type == typeof(float) || type == typeof(double) || type == typeof(int))
            {
                return NumberField(node, key, Describe(fallback));
            }

            if (type == typeof(Vector4))
            {
                return ColorEditor(theme, node, key, fallback);
            }

            // What a document cannot say (a command, a list) can only come from the view model.
            if (!UiMarkup.IsSupported(type))
            {
                return new TextBlock().Text("from the view model").FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).CenterV();
            }

            // Text, thicknesses and vectors ("8, 4") are all typed as text.
            return TextField(node, key, tree: key == "text").Hint(Describe(fallback));
        }

        private static TextBox NumberField(JsonObject node, string key, string hint)
        {
            var field = new TextBox { Text = Display(node[key]) };
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
            return field.Hint(hint);
        }

        // A swatch that picks, beside the text it writes: "@Accent" follows the theme, which a picker cannot say.
        private static UiElement ColorEditor(UiTheme theme, JsonObject node, string key, object? fallback)
        {
            var current = Vector4.Zero;
            if (fallback is Vector4 start)
            {
                current = start;
            }

            if (node[key] is { } value && UiMarkup.TryConvert(value, typeof(Vector4), theme, out var converted, out _) && converted is Vector4 parsed)
            {
                current = parsed;
            }

            var swatch = new ColorField { Color = current, ShowHexLabel = false, Width = 44.0f };
            swatch.ColorCommitted += (_, color) => UiDesignerDocument.Set(node, key, JsonValue.Create(UiColor.ToHex(color, includeAlpha: true)), tree: true);
            var text = TextField(node, key, tree: true).Hint("#RRGGBBAA or @Accent");
            return new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Children(swatch.GridColumn(0), text.GridColumn(1));
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
                if (instance == null || property.GetMethod == null)
                {
                    return null;
                }

                return property.GetValue(instance);
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
            Thickness t when t.Left == t.Right && t.Top == t.Bottom && t.Left == t.Top => t.Left.ToString("0.##", CultureInfo.InvariantCulture),
            Thickness t => string.Join(", ", new[] { t.Left, t.Top, t.Right, t.Bottom }.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture))),
            CornerRadius c when c.TopLeft == c.TopRight && c.TopLeft == c.BottomRight && c.TopLeft == c.BottomLeft => c.TopLeft.ToString("0.##", CultureInfo.InvariantCulture),
            CornerRadius c => string.Join(", ", new[] { c.TopLeft, c.TopRight, c.BottomRight, c.BottomLeft }.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture))),
            Vector4 color => UiColor.ToHex(color, includeAlpha: true),
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

                UiDesignerDocument.Set(node, key, TypedText(key, text), tree);
            };
            return field;
        }

        // A list of numbers is kept as a JSON list, and one number as a number, as a hand-written file would say them.
        private static JsonNode TypedText(string key, string text)
        {
            if (key is "text" or "name" or "columns" or "rows")
            {
                return JsonValue.Create(text);
            }

            var parts = text.Split(',', StringSplitOptions.TrimEntries);
            var numbers = new double[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                {
                    return JsonValue.Create(text);
                }
            }

            if (numbers.Length == 1)
            {
                return JsonValue.Create(numbers[0]);
            }

            if (numbers.Length is 2 or 4)
            {
                return new JsonArray(numbers.Select(number => (JsonNode?)JsonValue.Create(number)).ToArray());
            }

            return JsonValue.Create(text);
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
            var current = 0;
            if (node[key] is JsonValue value && value.TryGetValue<string>(out var s))
            {
                current = Math.Max(0, Array.FindIndex(names, n => n.Equals(s, StringComparison.OrdinalIgnoreCase)));
            }

            combo.SelectedIndex = current;
            combo.SelectionChanged += (_, index) =>
            {
                JsonNode? chosen = null;
                if (index > 0)
                {
                    chosen = JsonValue.Create(names[index]);
                }

                UiDesignerDocument.Set(node, key, chosen);
            };
            return combo;
        }

        private static UiElement ListField(JsonObject node, string key)
        {
            var field = new TextBox();
            if (node[key] is JsonArray array)
            {
                field.Text = string.Join(", ", array.Select(item => item?.ToString()));
            }

            field.TextCommitted += (_, text) =>
            {
                JsonNode? items = null;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    items = new JsonArray(text.Split(',', StringSplitOptions.TrimEntries).Select(item => (JsonNode?)JsonValue.Create(item)).ToArray());
                }

                UiDesignerDocument.Set(node, key, items);
            };
            return field.Hint("a, b, c");
        }

        private static UiElement SlotNumber(JsonObject node, string slot, string key)
        {
            var field = new TextBox();
            if (node[slot] is JsonObject saved && saved[key] is { } value)
            {
                field.Text = value.ToJsonString();
            }

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

        // ----- bindings ---------------------------------------------------------------------------------

        /// <summary>
        /// The element's context and what its events run; its properties bind on their own rows. With a view model
        /// chosen, what it offers can be picked from; the preview reports any path it does not have.
        /// </summary>
        private static void Bindings(UiTheme theme, StackPanel list, JsonObject node, Type type)
        {
            list.Children.Add(Row(theme, "context", PathField(theme, node["context"], node, UiDesignerPathKind.Context, path =>
            {
                JsonNode? value = null;
                if (path.Length > 0)
                {
                    value = JsonValue.Create(path);
                }

                UiDesignerDocument.Set(node, "context", value);
            })).WithTooltip("Paths on this element and below are relative to this one"));

            if (node["on"] is JsonObject events)
            {
                foreach (var (name, action) in events.ToList())
                {
                    list.Children.Add(EventRow(theme, node, events, name, action));
                }
            }

            var eventNames = UiDesignerBindings.Events(type).Where(name => node["on"] is not JsonObject taken || !taken.ContainsKey(name)).ToList();
            if (eventNames.Count > 0)
            {
                var add = new ComboBox().Options(new[] { "+ run a command on an event" }.Concat(eventNames).ToArray());
                add.SelectedIndex = 0;
                add.SelectionChanged += (_, index) =>
                {
                    if (index > 0)
                    {
                        if (node["on"] is not JsonObject on)
                        {
                            on = new JsonObject();
                            node["on"] = on;
                        }

                        on[eventNames[index - 1]] = string.Empty;
                        UiDesignerDocument.Touch(tree: true);
                    }
                };
                list.Children.Add(add);
            }
        }

        // The path that drives the property, how, and what stands in for it in the designer, where no view model is running.
        private static UiElement BoundRow(UiTheme theme, JsonObject node, JsonObject binds, string label, string target, Func<UiElement>? designValue)
        {
            var spec = UiDesignerBindings.Read(binds[target]);

            // Read afresh: fields edited one after another must not put back what the row was built with.
            void Write(Func<UiDesignerBindingSpec, UiDesignerBindingSpec> change)
            {
                binds[target] = UiDesignerBindings.Write(change(UiDesignerBindings.Read(binds[target])));
                UiDesignerDocument.Touch();
            }

            var targetType = TargetOf(node, target);
            var kind = KindFor(targetType);
            var rows = new StackPanel().Vertical(2.0f);
            var remove = new ManaUI.Button().Label("x").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                binds.Remove(target);
                if (binds.Count == 0)
                {
                    node.Remove("bind");
                }

                UiDesignerDocument.Touch(tree: true);
            });
            remove.WithTooltip("Remove this binding");
            var name = new TextBlock().Text(label).FontSize(theme.FontSizeSmall).Foreground(theme.Accent).CenterV().Ellipsis();
            name.Tooltip = "Bound to the view model";
            rows.Children.Add(new Grid().Columns("104, *, Auto", theme.Spacing).Rows("Auto").Children(
                name.GridColumn(0),
                PathField(theme, JsonValue.Create(spec.Path), node, kind, path => Write(current => current with { Path = path }), targetType, target).GridColumn(1),
                remove.GridColumn(2)));
            if (kind != UiDesignerPathKind.Command)
            {
                // Only what can actually flow back is offered; a hand-written mode stays listed, so it can be seen and changed.
                var modes = Enum.GetNames<BindingMode>().Where(item =>
                    item is nameof(BindingMode.OneWay) or nameof(BindingMode.OneTime) || item == spec.Mode.ToString()
                    || (UiDesignerDocument.TypeOf(node) is { } owner && UiMarkup.CanWriteBack(owner, target))).ToArray();
                var mode = new ComboBox().Options(modes);
                mode.SelectedIndex = Array.IndexOf(modes, spec.Mode.ToString());
                mode.SelectionChanged += (_, index) =>
                {
                    if (index >= 0)
                    {
                        var chosen = Enum.Parse<BindingMode>(modes[index]);
                        Write(current => current with { Mode = chosen });
                    }
                };
                mode.WithTooltip("OneWay: the view model drives this. TwoWay: edits here write back. OneTime: read once. OneWayToSource: this drives the view model.");
                var format = new TextBox { Text = spec.Format ?? string.Empty }.Hint("format: {0:F0} pts");
                format.TextCommitted += (_, text) =>
                {
                    string? chosen = null;
                    if (!string.IsNullOrEmpty(text))
                    {
                        chosen = text;
                    }

                    Write(current => current with { Format = chosen });
                };
                var invert = new CheckBox().Label("Invert").Checked(spec.Invert);
                invert.OnChanged(value => Write(current => current with { Invert = value }));
                invert.WithTooltip("Use the opposite of the view model's true or false");

                // A flag is inverted, not formatted; anything else, only text can show a format.
                UiElement extra = new Border();
                if (UiDesignerDocument.ViewModelType is { } viewModel
                    && UiDesignerBindings.ScopeOf(viewModel, node, includeOwn: true) is { } scope
                    && UiDesignerBindings.PathType(scope, spec.Path) == typeof(bool))
                {
                    extra = invert;
                }
                else if (targetType == typeof(string) || spec.Format != null)
                {
                    extra = format;
                }

                rows.Children.Add(new Grid().Columns("104, *", theme.Spacing).Rows("Auto").Children(mode.GridColumn(0), extra.GridColumn(1)));
                if (designValue != null)
                {
                    rows.Children.Add(Row(theme, "in the designer", designValue()).WithTooltip("Shown here and in the file's own value; the view model's replaces it in play"));
                }
            }

            return rows;
        }

        private static UiDesignerPathKind KindFor(Type? target)
        {
            if (target != null && typeof(ICommand).IsAssignableFrom(target))
            {
                return UiDesignerPathKind.Command;
            }

            return UiDesignerPathKind.Property;
        }

        private static UiElement EventRow(UiTheme theme, JsonObject node, JsonObject events, string name, JsonNode? action)
        {
            var remove = new ManaUI.Button().Label("x").Variant(ButtonVariant.Subtle).OnClick(() =>
            {
                events.Remove(name);
                if (events.Count == 0)
                {
                    node.Remove("on");
                }

                UiDesignerDocument.Touch(tree: true);
            });
            var path = Display(action);
            if (action is JsonObject full)
            {
                path = full["command"]?.ToString() ?? string.Empty;
            }

            return new Grid().Columns("104, *, Auto", theme.Spacing).Rows("Auto").Children(
                new TextBlock().Text("on " + name).FontSize(theme.FontSizeSmall).CenterV().Ellipsis().GridColumn(0),
                PathField(theme, JsonValue.Create(path), node, UiDesignerPathKind.Command, chosen =>
                {
                    events[name] = chosen;
                    UiDesignerDocument.Touch();
                }).GridColumn(1),
                remove.GridColumn(2));
        }

        // The path as text, and with a view model known, a list of what it has to pick from.
        private static UiElement PathField(UiTheme theme, JsonNode? value, JsonObject node, UiDesignerPathKind kind, Action<string> write, Type? target = null, string? label = null)
        {
            var viewModel = UiDesignerDocument.ViewModelType;
            var hint = "Property.Path";
            if (kind == UiDesignerPathKind.Command)
            {
                hint = "PlayCommand";
            }

            var field = new TextBox { Text = Display(value) }.Hint(hint);
            field.TextCommitted += (_, text) => write(text.Trim());
            if (viewModel == null)
            {
                return field;
            }

            var pick = new ManaUI.Button().Label("...").Variant(ButtonVariant.Subtle);
            pick.OnClick(() => OpenPicker(pick, node, kind, field.Text, write, target, label));
            pick.WithTooltip($"Pick from what {viewModel.Name} offers");
            return new Grid().Columns("*, Auto", 2.0f).Rows("Auto").Children(field.GridColumn(0), pick.GridColumn(1));
        }

        private static void OpenPicker(UiElement anchor, JsonObject node, UiDesignerPathKind kind, string current, Action<string> write, Type? target = null, string? label = null)
        {
            if (anchor.Root is not { } root || UiDesignerDocument.ViewModelType is not { } viewModel)
            {
                return;
            }

            var scope = UiDesignerBindings.ScopeOf(viewModel, node, includeOwn: kind != UiDesignerPathKind.Context);
            UiDesignerPathPicker.Show(root, anchor, scope, kind, current, path =>
            {
                write(path);
                UiDesignerDocument.Touch(tree: true);
            }, target, label);
        }

        /// <summary>For scripts: the picker as a path field's "..." opens it, for the selected element.</summary>
        public static void OpenPickerForSelected(UiElement anchor, UiDesignerPathKind kind)
        {
            if (UiDesignerDocument.Selected is { } node)
            {
                OpenPicker(anchor, node, kind, string.Empty, _ => { });
            }
        }

        private static UiElement Row(UiTheme theme, string label, UiElement editor)
        {
            var name = new TextBlock().Text(label).FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Ellipsis().CenterV();
            name.Width = 104.0f;
            name.Tooltip = label;
            return new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Children(name.GridColumn(0), editor.GridColumn(1));
        }
    }

    /// <summary>One "bind" entry as the panel edits it.</summary>
    public readonly record struct UiDesignerBindingSpec(string Path, BindingMode Mode, string? Format, JsonObject? Rest, bool Invert = false);

    /// <summary>Reads and writes "bind" entries, and lists what elements and view models offer to them.</summary>
    public static class UiDesignerBindings
    {
        public static UiDesignerBindingSpec Read(JsonNode? source)
        {
            if (source is JsonObject full)
            {
                var mode = BindingMode.OneWay;
                if (full["mode"] is JsonValue modeValue && modeValue.TryGetValue<string>(out var text) && Enum.TryParse<BindingMode>(text, true, out var parsed))
                {
                    mode = parsed;
                }

                string? format = null;
                if (full["format"] is JsonValue formatValue)
                {
                    formatValue.TryGetValue(out format);
                }

                var invert = full["invert"] is JsonValue invertValue && invertValue.TryGetValue<bool>(out var inverted) && inverted;
                return new UiDesignerBindingSpec(full["path"]?.ToString() ?? string.Empty, mode, format, full, invert);
            }

            return new UiDesignerBindingSpec(source?.ToString() ?? string.Empty, BindingMode.OneWay, null, null);
        }

        /// <summary>The short form when that says it all, as a hand-written file would, and the settings it does not edit kept.</summary>
        public static JsonNode Write(UiDesignerBindingSpec spec)
        {
            var extras = spec.Rest?.Where(pair => pair.Key is not ("path" or "mode" or "format" or "invert")).ToList() ?? [];
            if (spec.Mode == BindingMode.OneWay && spec.Format == null && !spec.Invert && extras.Count == 0)
            {
                return JsonValue.Create(spec.Path);
            }

            var full = new JsonObject { ["path"] = spec.Path };
            if (spec.Mode != BindingMode.OneWay)
            {
                full["mode"] = spec.Mode.ToString();
            }

            if (spec.Format != null)
            {
                full["format"] = spec.Format;
            }

            if (spec.Invert)
            {
                full["invert"] = true;
            }

            foreach (var (key, value) in extras)
            {
                full[key] = value?.DeepClone();
            }

            return full;
        }

        /// <summary>The properties of an element a binding can drive.</summary>
        public static IReadOnlyList<string> Targets(Type type) =>
            UiMarkup.BindableProperties(type).Select(property => UiMarkup.KeyFor(property.Name)).ToList();

        public static IReadOnlyList<string> Events(Type type) =>
            type.GetEvents(BindingFlags.Public | BindingFlags.Instance).Select(item => UiMarkup.KeyFor(item.Name)).Order(StringComparer.Ordinal).ToList();

        /// <summary>
        /// What a view model offers, one group per view model class it reaches: its own members, then those of what it
        /// holds, with a list's first item standing for the rest. A context offers only what can be stepped into;
        /// with a target, only what can drive it.
        /// </summary>
        public static IReadOnlyList<UiDesignerMemberGroup> Groups(Type viewModel, UiDesignerPathKind kind, Type? target = null)
        {
            var groups = new List<UiDesignerMemberGroup>();
            void Walk(Type type, string prefix, int depth)
            {
                var members = new List<UiDesignerMember>();
                var inside = new List<(Type Type, string Prefix)>();
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(item => item.Name, StringComparer.Ordinal))
                {
                    var value = property.PropertyType;
                    var isCommand = typeof(ICommand).IsAssignableFrom(value);
                    var item = ItemOf(value);
                    var holder = IsHolder(value);
                    var itemHolder = item != null && IsHolder(item);
                    var path = prefix + property.Name;
                    var fits = target != null && UiMarkup.CanBind(value, target);

                    // A value is offered on its own, but a whole view model or a list of them only to what asks for one.
                    var isValue = !holder && !itemHolder && (target == null || fits);
                    var isAsked = target != null && target != typeof(string) && fits;
                    switch (kind)
                    {
                        case UiDesignerPathKind.Command when isCommand:
                        case UiDesignerPathKind.Property when !isCommand && (isValue || isAsked):
                        case UiDesignerPathKind.Context when holder:
                            members.Add(new UiDesignerMember(path, property.Name, value));
                            break;
                        case UiDesignerPathKind.Context when itemHolder && item != null:
                            members.Add(new UiDesignerMember(path + "[0]", property.Name + "[0]", item));
                            break;
                    }

                    if (holder)
                    {
                        inside.Add((value, path + "."));
                    }
                    else if (itemHolder && item != null)
                    {
                        inside.Add((item, path + "[0]."));
                    }
                }

                if (members.Count > 0)
                {
                    groups.Add(new UiDesignerMemberGroup(type.Name, prefix.TrimEnd('.'), members));
                }

                if (depth > 0)
                {
                    foreach (var (held, heldPrefix) in inside)
                    {
                        Walk(held, heldPrefix, depth - 1);
                    }
                }
            }

            Walk(viewModel, string.Empty, 2);
            return groups;
        }

        /// <summary>
        /// The view model an element's paths are relative to: the layout's, stepped through the "context" of each
        /// element above it (and its own, for its bindings). Null when a context leads nowhere.
        /// </summary>
        public static Type? ScopeOf(Type viewModel, JsonObject node, bool includeOwn)
        {
            var contexts = new List<string>();
            var at = node;
            if (!includeOwn)
            {
                at = UiDesignerDocument.ParentOf(node);
            }

            for (; at != null; at = UiDesignerDocument.ParentOf(at))
            {
                if (at["context"] is JsonValue value && value.TryGetValue<string>(out var path))
                {
                    contexts.Insert(0, path);
                }
            }

            Type? scope = viewModel;
            foreach (var context in contexts)
            {
                if (scope != null)
                {
                    scope = PathType(scope, context);
                }
            }

            return scope;
        }

        /// <summary>The type a path ends on, from a view model; null when it leads nowhere.</summary>
        public static Type? PathType(Type from, string path)
        {
            Type? end = from;
            foreach (var piece in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var open = piece.IndexOf('[');
                var name = piece;
                if (open > 0)
                {
                    name = piece[..open];
                }

                end = end?.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.PropertyType;
                if (open > 0 && end != null)
                {
                    end = ItemOf(end);
                }
            }

            return end;
        }

        // Text that is empty until a view model runs would leave a blank control to design around.
        private static readonly string[] WordedTargets = ["text", "title", "header", "placeholder", "hotkey", "countText", "chipText"];

        /// <summary>For the preview: bound text with no design value shows its path, as {Score}.</summary>
        public static void ShowPaths(UiMarkupResult built)
        {
            foreach (var (node, element) in built.Elements)
            {
                if (node["bind"] is not JsonObject binds)
                {
                    continue;
                }

                foreach (var (key, source) in binds)
                {
                    var path = Read(source).Path;
                    var property = element.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (path.Length > 0 && !node.ContainsKey(key) && property?.PropertyType == typeof(string) && property.SetMethod is { IsPublic: true }
                        && WordedTargets.Contains(key, StringComparer.OrdinalIgnoreCase))
                    {
                        property.SetValue(element, "{" + path + "}");
                    }
                }
            }
        }

        public static string TypeName(Type type)
        {
            if (type == typeof(float))
            {
                return "float";
            }

            if (type == typeof(int))
            {
                return "int";
            }

            if (type == typeof(bool))
            {
                return "bool";
            }

            if (type == typeof(string))
            {
                return "string";
            }

            if (type == typeof(double))
            {
                return "double";
            }

            if (typeof(ICommand).IsAssignableFrom(type))
            {
                return "command";
            }

            if (ItemOf(type) is { } item)
            {
                return TypeName(item) + "[]";
            }

            return type.Name;
        }

        private static Type? ItemOf(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type == typeof(string))
            {
                return null;
            }

            foreach (var face in type.GetInterfaces().Prepend(type))
            {
                if (face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return face.GetGenericArguments()[0];
                }
            }

            return null;
        }

        // Something with members of its own to step into, not a value: the framework's types are values here.
        private static bool IsHolder(Type type) =>
            (type.IsClass || type.IsInterface) && ItemOf(type) == null && !typeof(ICommand).IsAssignableFrom(type)
            && !typeof(Delegate).IsAssignableFrom(type) && type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true;
    }

    /// <summary>What a path is being picked for.</summary>
    public enum UiDesignerPathKind
    {
        Property,
        Command,
        Context
    }

    public sealed record UiDesignerMember(string Path, string Name, Type Type);

    /// <summary>One view model's members: the class they are on, and the path that reaches it ("" for the layout's own).</summary>
    public sealed record UiDesignerMemberGroup(string ViewModel, string Prefix, IReadOnlyList<UiDesignerMember> Members);

    /// <summary>
    /// Picks a path from what the view model offers, grouped by view model: wide enough to read, with a filter,
    /// since a HUD's view model can have fifty properties.
    /// </summary>
    public static class UiDesignerPathPicker
    {
        private const float PopupWidth = 360.0f;
        private const float ListHeight = 380.0f;

        public static UiPopup Show(UiRoot root, UiElement anchor, Type? scope, UiDesignerPathKind kind, string current, Action<string> pick, Type? target = null, string? label = null)
        {
            var theme = root.Theme;
            var popup = new UiPopup { Placement = PopupPlacement.Bottom, MaxHeight = ListHeight + 50.0f };
            var filter = new TextBox().Hint("Filter");
            var list = new StackPanel().Vertical(0.0f);
            void Choose(string path)
            {
                root.Popups.Close(popup);
                pick(path);
            }

            void Fill(string text)
            {
                list.Children.Clear();
                if (kind == UiDesignerPathKind.Context && current.Length > 0)
                {
                    list.Children.Add(new UiDesignerMemberRow(theme, "No context", "the layout's own", false, () => Choose(string.Empty)));
                }

                var groups = Array.Empty<UiDesignerMemberGroup>() as IReadOnlyList<UiDesignerMemberGroup>;
                if (scope != null)
                {
                    groups = UiDesignerBindings.Groups(scope, kind, target);
                }

                var shown = 0;
                foreach (var group in groups)
                {
                    var classMatches = group.ViewModel.Contains(text, StringComparison.OrdinalIgnoreCase);
                    var members = group.Members.Where(member => classMatches || member.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (members.Count == 0)
                    {
                        continue;
                    }

                    list.Children.Add(Header(theme, group));
                    foreach (var member in members)
                    {
                        list.Children.Add(new UiDesignerMemberRow(theme, member.Name, UiDesignerBindings.TypeName(member.Type), member.Path == current, () => Choose(member.Path)));
                        shown++;
                    }
                }

                if (shown == 0)
                {
                    list.Children.Add(new TextBlock().Text(EmptyMessage(scope, kind, text)).FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Margin(6.0f).Wrap());
                }
            }

            filter.TextChanged += (_, text) => Fill(text);
            Fill(string.Empty);

            var body = new StackPanel().Vertical(4.0f);
            if (label != null && target != null)
            {
                body.Children.Add(new TextBlock().Text($"Bind {label} ({UiDesignerBindings.TypeName(target)}) to").FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Margin(4.0f, 2.0f, 4.0f, 0.0f));
            }

            body.Children.Add(filter);
            body.Children.Add(list.Scrollable().Configure(viewer => viewer.MaxHeight = ListHeight));
            var frame = new Border().Fill(theme.SurfaceRaised).Outline(theme.BorderStrong, 1.0f).Rounded(theme.CornerRadius).Shadow(14.0f).PaddingOf(4.0f).Content(body);
            frame.Width = PopupWidth;
            popup.Child = frame;
            root.Popups.Show(popup, anchor);
            filter.Focus();
            return popup;
        }

        private static string EmptyMessage(Type? scope, UiDesignerPathKind kind, string filter)
        {
            if (filter.Length > 0)
            {
                return $"Nothing matches \"{filter}\".";
            }

            if (scope == null)
            {
                return "This element's context does not lead anywhere: fix it above.";
            }

            if (kind == UiDesignerPathKind.Context)
            {
                return $"{scope.Name} holds no view models to step into.";
            }

            return $"{scope.Name} has nothing that fits here.";
        }

        private static UiElement Header(UiTheme theme, UiDesignerMemberGroup group)
        {
            var row = new StackPanel().Horizontal(8.0f).Margin(4.0f, 6.0f, 4.0f, 2.0f);
            row.Children.Add(new TextBlock().Text(group.ViewModel).FontSize(theme.FontSizeSmall).Foreground(theme.Accent));
            if (group.Prefix.Length > 0)
            {
                var note = group.Prefix;
                if (group.Prefix.Contains('['))
                {
                    note += "  (edit the number for another)";
                }

                row.Children.Add(new TextBlock().Text(note).FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted));
            }

            return row;
        }
    }

    public sealed class UiDesignerMemberRow : Decorator
    {
        private readonly UiTheme _theme;
        private readonly bool _isCurrent;
        private readonly Action _chosen;

        public UiDesignerMemberRow(UiTheme theme, string name, string detail, bool isCurrent, Action chosen)
        {
            _theme = theme;
            _isCurrent = isCurrent;
            _chosen = chosen;
            Label = name;
            Height = 22.0f;
            Padding = new Thickness(14.0f, 0.0f, 6.0f, 0.0f);
            Child = new Grid().Columns("*, Auto", 8.0f).Rows("Auto").Children(
                new TextBlock().Text(name).FontSize(theme.FontSizeSmall).Foreground(theme.Text).Ellipsis().CenterV().GridColumn(0),
                new TextBlock().Text(detail).FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).CenterV().GridColumn(1));
        }

        public string Label { get; }

        protected override bool IsSelfHitTestVisible => true;

        // The row is the one thing to click: what it shows takes no pointer of its own.
        public override UiElement? HitTest(Vector2 point)
        {
            if (Visibility != Visibility.Visible || !Bounds.Contains(point))
            {
                return null;
            }

            return this;
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            if (IsPointerOver)
            {
                drawList.FillRect(Bounds, _theme.SurfaceAlt, new CornerRadius(2.0f));
            }
            else if (_isCurrent)
            {
                var fill = _theme.Accent;
                fill.W = 0.3f;
                drawList.FillRect(Bounds, fill, new CornerRadius(2.0f));
            }
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            e.Handled = true;
            _chosen();
        }
    }

    /// <summary>The file's elements as a tree of rows. Click selects, the arrow folds, and dragging a row moves it.</summary>
    public sealed class UiDesignerOutline(UiTheme theme, UiDesignerPreview preview)
    {
        private readonly HashSet<JsonObject> _folded = new(ReferenceEqualityComparer.Instance);
        private readonly List<UiDesignerOutlineRow> _rows = new();
        internal UiDesignerOutlineRow? Dragging { get; set; }

        internal UiTheme Theme => theme;

        public UiElement Rebuild()
        {
            _rows.Clear();
            var list = new UiDesignerOutlineList(this);
            list.Rows.Vertical(0.0f);
            void Add(JsonObject node, int depth)
            {
                var row = new UiDesignerOutlineRow(this, node, depth, UiDesignerDocument.Nested(node).Any(), _folded.Contains(node));
                _rows.Add(row);
                list.Rows.Children.Add(row);
                if (_folded.Contains(node))
                {
                    return;
                }

                foreach (var child in UiDesignerDocument.Nested(node))
                {
                    Add(child, depth + 1);
                }
            }

            Add(UiDesignerDocument.Root, 0);
            return list;
        }

        internal void Toggle(JsonObject node)
        {
            if (!_folded.Remove(node))
            {
                _folded.Add(node);
            }

            UiDesignerDocument.RefreshOutline();
        }

        // Keys go to the preview, where undo and the rest are handled, whichever side was last clicked.
        internal void Clicked(JsonObject node)
        {
            UiDesignerDocument.Select(node);
            preview.Focus();
        }

        /// <summary>Where a drop at this height would land, or null for nowhere: the row, and before, into or after it.</summary>
        internal (UiDesignerOutlineRow Row, UiDesignerDropKind Kind)? DropAt(Vector2 point)
        {
            foreach (var row in _rows)
            {
                if (point.Y < row.Bounds.Y || point.Y >= row.Bounds.Bottom)
                {
                    continue;
                }

                if (Dragging != null && UiDesignerDocument.IsWithin(row.Node, Dragging.Node))
                {
                    return null;
                }

                var share = (point.Y - row.Bounds.Y) / MathF.Max(row.Bounds.Height, 1);
                var holds = UiDesignerDocument.HoldsChildren(row.Node)
                    || (UiDesignerDocument.HoldsChild(row.Node) && row.Node[UiDesignerDocument.SlotKey(row.Node)] == null);
                if (holds && share is > 0.25f and < 0.75f)
                {
                    return (row, UiDesignerDropKind.Into);
                }

                // The root stands alone: nothing goes beside it.
                if (UiDesignerDocument.ParentOf(row.Node) == null)
                {
                    if (holds)
                    {
                        return (row, UiDesignerDropKind.Into);
                    }

                    return null;
                }

                if (share < 0.5f)
                {
                    return (row, UiDesignerDropKind.Before);
                }

                return (row, UiDesignerDropKind.After);
            }

            return null;
        }

        internal void Drop(Vector2 point)
        {
            if (Dragging is not { } dragged || DropAt(point) is not { } drop)
            {
                return;
            }

            if (drop.Kind == UiDesignerDropKind.Into)
            {
                UiDesignerDocument.Reparent(dragged.Node, drop.Row.Node, UiDesignerDocument.ChildrenCount(drop.Row.Node));
                return;
            }

            if (UiDesignerDocument.ParentOf(drop.Row.Node) is { } parent && drop.Row.Node.Parent is JsonArray siblings)
            {
                var index = siblings.IndexOf(drop.Row.Node);
                if (drop.Kind == UiDesignerDropKind.After)
                {
                    index++;
                }

                UiDesignerDocument.Reparent(dragged.Node, parent, index);
            }
        }
    }

    public enum UiDesignerDropKind
    {
        Before,
        Into,
        After
    }

    /// <summary>The rows, and over them the line showing where a dragged row would land.</summary>
    internal sealed class UiDesignerOutlineList : Decorator
    {
        private readonly UiDesignerOutline _outline;

        public UiDesignerOutlineList(UiDesignerOutline outline)
        {
            _outline = outline;
            Rows = new StackPanel();
            Child = Rows;
        }

        public StackPanel Rows { get; }

        public Vector2 Pointer { get; set; }

        protected override void OnRenderOverlay(ManaUiDrawList drawList)
        {
            if (_outline.Dragging == null || _outline.DropAt(Pointer) is not { } drop)
            {
                return;
            }

            var accent = _outline.Theme.Accent;
            var row = drop.Row.Bounds;
            if (drop.Kind == UiDesignerDropKind.Into)
            {
                drawList.DrawBorder(row, accent, 2.0f, new CornerRadius(2.0f));
                return;
            }

            var y = row.Y;
            if (drop.Kind == UiDesignerDropKind.After)
            {
                y = row.Bottom;
            }

            drawList.FillRect(new UiRect(row.X, y - 1.0f, row.Width, 2.0f), accent);
        }
    }

    public sealed class UiDesignerOutlineRow : Decorator
    {
        private const float Indent = 14.0f;
        private const float Arrow = 14.0f;

        private readonly UiDesignerOutline _outline;
        private readonly int _depth;
        private readonly bool _hasChildren;
        private readonly bool _folded;
        private bool _isSelected;
        private bool _pressed;
        private Vector2 _pressedAt;

        public UiDesignerOutlineRow(UiDesignerOutline outline, JsonObject node, int depth, bool hasChildren, bool folded)
        {
            _outline = outline;
            Node = node;
            _depth = depth;
            _hasChildren = hasChildren;
            _folded = folded;
            Height = 22.0f;
            Padding = new Thickness(depth * Indent + Arrow + 4.0f, 0.0f, 4.0f, 0.0f);
            var theme = outline.Theme;
            var label = new TextBlock().Text(Caption(node)).FontSize(theme.FontSizeSmall).CenterV();
            label.Foreground = theme.Text;
            if (IsHidden(node))
            {
                label.Foreground = theme.TextMuted;
            }

            Child = label;
            this.BindPolled(row => row.IsSelected, () => ReferenceEquals(UiDesignerDocument.Selected, Node));
        }

        public JsonObject Node { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetVisualProperty(ref _isSelected, value);
        }

        protected override bool IsSelfHitTestVisible => true;

        private static bool IsHidden(JsonObject node) =>
            node["visibility"] is JsonValue value && value.TryGetValue<string>(out var text) && !string.Equals(text, "Visible", StringComparison.OrdinalIgnoreCase);

        // What a person would call it: its name, or what it says, with marks for what is going on.
        private static string Caption(JsonObject node)
        {
            var caption = node["type"]?.GetValue<string>() ?? "?";
            if (node["name"] is JsonValue name && name.TryGetValue<string>(out var called))
            {
                caption += "  " + called;
            }
            else if (node["text"] is JsonValue text && text.TryGetValue<string>(out var said))
            {
                caption += "  \"" + said + "\"";
            }

            if (node["bind"] != null || node["on"] != null)
            {
                caption += "  ~";
            }

            if (IsHidden(node))
            {
                caption += "  (hidden)";
            }

            return caption;
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = _outline.Theme;
            if (_isSelected)
            {
                var fill = theme.Accent;
                fill.W = 0.35f;
                drawList.FillRect(Bounds, fill, new CornerRadius(2.0f));
            }
            else if (IsPointerOver)
            {
                drawList.FillRect(Bounds, theme.SurfaceRaised, new CornerRadius(2.0f));
            }

            if (_hasChildren)
            {
                var x = Bounds.X + _depth * Indent + Arrow * 0.5f;
                var y = Bounds.Y + Bounds.Height * 0.5f;
                if (_folded)
                {
                    drawList.DrawLine(new Vector2(x - 2, y - 4), new Vector2(x + 2, y), theme.TextMuted, 1.5f);
                    drawList.DrawLine(new Vector2(x + 2, y), new Vector2(x - 2, y + 4), theme.TextMuted, 1.5f);
                }
                else
                {
                    drawList.DrawLine(new Vector2(x - 4, y - 2), new Vector2(x, y + 2), theme.TextMuted, 1.5f);
                    drawList.DrawLine(new Vector2(x, y + 2), new Vector2(x + 4, y - 2), theme.TextMuted, 1.5f);
                }
            }
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            var arrowAt = Bounds.X + _depth * Indent;
            if (_hasChildren && e.Position.X >= arrowAt && e.Position.X < arrowAt + Arrow)
            {
                _outline.Toggle(Node);
                e.Handled = true;
                return;
            }

            _outline.Clicked(Node);
            _pressed = true;
            _pressedAt = e.Position;
            CapturePointer();
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (!_pressed)
            {
                return;
            }

            if (_outline.Dragging == null && Vector2.Distance(e.Position, _pressedAt) > 4.0f && UiDesignerDocument.ParentOf(Node) != null)
            {
                _outline.Dragging = this;
            }

            if (_outline.Dragging != null)
            {
                for (var at = Parent; at != null; at = at.Parent)
                {
                    if (at is UiDesignerOutlineList list)
                    {
                        list.Pointer = e.Position;
                        list.InvalidateVisual();
                        break;
                    }
                }
            }
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (!_pressed)
            {
                return;
            }

            _pressed = false;
            ReleasePointerCapture();
            if (_outline.Dragging != null)
            {
                _outline.Drop(e.Position);
                _outline.Dragging = null;
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// The live preview: the document built into elements at the chosen screen size and drawn fitted to the panel, the
    /// selected one outlined with handles. The controls are being laid out, not used, so nothing inside takes the
    /// pointer: a click selects, a drag moves, a handle resizes. A selected element the file hides is shown here, or
    /// there would be no way to work on a card only the game opens.
    /// </summary>
    public sealed class UiDesignerPreview : Decorator
    {
        private const float HandleSize = 8.0f;
        private const float DragStart = 3.0f;

        // Which edges each handle moves: left, top, right, bottom.
        private static readonly (float X, float Y, int Left, int Top, int Right, int Bottom)[] Handles =
        [
            (0.0f, 0.0f, 1, 1, 0, 0), (0.5f, 0.0f, 0, 1, 0, 0), (1.0f, 0.0f, 0, 1, 1, 0),
            (0.0f, 0.5f, 1, 0, 0, 0), (1.0f, 0.5f, 0, 0, 1, 0),
            (0.0f, 1.0f, 1, 0, 0, 1), (0.5f, 1.0f, 0, 0, 0, 1), (1.0f, 1.0f, 0, 0, 1, 1)
        ];

        private readonly UiTheme _theme;
        private object? _built;
        private object? _builtSelection;
        private UiMarkupResult? _result;

        /// <summary>The elements now built, by name: what the last edit produced.</summary>
        public UiMarkupResult? Built => _result;
        private Vector2 _designSize = new(1920, 1080);
        private bool _pressed;
        private Vector2 _pressedAt;
        private int _handle = -1;
        private UiDesignerDrag? _drag;
        private bool _dragging;

        public UiDesignerPreview(UiTheme theme)
        {
            _theme = theme;
            ClipToBounds = true;
            Focusable = true;

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

        /// <summary>The screen the layout is laid out on; zero fits the panel itself.</summary>
        public Vector2 DesignSize
        {
            get => _designSize;
            set
            {
                _designSize = value;
                InvalidateMeasure();
            }
        }

        protected override bool IsSelfHitTestVisible => true;

        // Nothing inside is live: what is under the pointer is decided by OnPointerPressed against the document.
        public override UiElement? HitTest(Vector2 point)
        {
            if (Visibility != Visibility.Visible || !Bounds.Contains(point))
            {
                return null;
            }

            return this;
        }

        private Vector2 ScreenSize
        {
            get
            {
                if (_designSize == Vector2.Zero)
                {
                    return new Vector2(MathF.Max(Bounds.Width - 16.0f, 1), MathF.Max(Bounds.Height - 16.0f, 1));
                }

                return _designSize;
            }
        }

        public float FitScale
        {
            get
            {
                var size = ScreenSize;
                return MathF.Min(1.0f, MathF.Min((Bounds.Width - 16.0f) / size.X, (Bounds.Height - 16.0f) / size.Y));
            }
        }

        // The layout is arranged at the panel's corner at full size, and drawn from there scaled and centred.
        private Vector2 Shift
        {
            get
            {
                var scale = FitScale;
                var size = ScreenSize * scale;
                var corner = new Vector2(Bounds.X + (Bounds.Width - size.X) * 0.5f, Bounds.Y + (Bounds.Height - size.Y) * 0.5f);
                return corner - new Vector2(Bounds.X, Bounds.Y) * scale;
            }
        }

        private Vector2 ToLayout(Vector2 point) => (point - Shift) / MathF.Max(FitScale, 0.001f);

        /// <summary>Where a rectangle of the layout is drawn in the panel.</summary>
        public UiRect ToPanel(UiRect rect)
        {
            var scale = FitScale;
            var shift = Shift;
            return new UiRect(rect.X * scale + shift.X, rect.Y * scale + shift.Y, rect.Width * scale, rect.Height * scale);
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            Child?.Measure(ScreenSize);
            return availableSize;
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            var size = ScreenSize;
            Child?.Arrange(new UiRect(Bounds.X, Bounds.Y, size.X, size.Y));
            return finalSize;
        }

        private void Rebuild()
        {
            if (ReferenceEquals(_built, UiDesignerDocument.PreviewToken) && ReferenceEquals(_builtSelection, UiDesignerDocument.SelectionToken))
            {
                return;
            }

            _built = UiDesignerDocument.PreviewToken;
            _builtSelection = UiDesignerDocument.SelectionToken;
            _result = UiMarkup.Load(UiDesignerDocument.Document, _theme, viewModelType: UiDesignerDocument.ViewModelType);
            UiDesignerBindings.ShowPaths(_result);
            Child = _result.Root;
            Reveal();
            if (_result.Problems.Count == 0)
            {
                Status = string.Empty;
            }
            else
            {
                Status = $"{_result.Problems.Count} problem(s): {_result.Problems[0]}";
            }

            StatusDetail = string.Join(Environment.NewLine, _result.Problems);
        }

        // Selecting what the file hides shows it, and what it sits in, for as long as it is selected.
        private void Reveal()
        {
            if (_result == null)
            {
                return;
            }

            for (var node = UiDesignerDocument.Selected; node != null; node = UiDesignerDocument.ParentOf(node))
            {
                if (_result.Elements.TryGetValue(node, out var element))
                {
                    element.Visibility = Visibility.Visible;
                }
            }
        }

        protected override void RenderChildren(ManaUiDrawList drawList)
        {
            var size = ScreenSize;
            var frame = ToPanel(new UiRect(Bounds.X, Bounds.Y, size.X, size.Y));

            // A plain dark screen, since a HUD is drawn to go over a game; the sky is not what is being designed.
            drawList.FillRect(frame, new Vector4(0.05f, 0.06f, 0.09f, 1.0f));
            drawList.PushScale(FitScale, Shift);
            base.RenderChildren(drawList);
            drawList.PopScale();
            drawList.DrawBorder(frame, _theme.Border, 1.0f, new CornerRadius(0.0f));
        }

        protected override void OnRenderOverlay(ManaUiDrawList drawList)
        {
            if (_result == null || UiDesignerDocument.Selected is not { } selected || !_result.Elements.TryGetValue(selected, out var element))
            {
                return;
            }

            var rect = ToPanel(element.Bounds);
            drawList.DrawBorder(rect, _theme.Accent, 1.5f, new CornerRadius(1.0f));
            for (var i = 0; i < Handles.Length; i++)
            {
                var handle = HandleRect(rect, i);
                drawList.FillRect(handle, new Vector4(1, 1, 1, 1));
                drawList.DrawBorder(handle, _theme.Accent, 1.0f, new CornerRadius(0.0f));
            }
        }

        private static UiRect HandleRect(UiRect rect, int index)
        {
            var handle = Handles[index];
            return new UiRect(rect.X + rect.Width * handle.X - HandleSize * 0.5f, rect.Y + rect.Height * handle.Y - HandleSize * 0.5f, HandleSize, HandleSize);
        }

        // The nearest handle, with the middle counting as one that moves: a small element's handles cover all of it.
        private static int HandleAt(UiRect rect, Vector2 point)
        {
            var found = -1;
            var nearest = Vector2.Distance(point, rect.Center);
            for (var i = 0; i < Handles.Length; i++)
            {
                var handle = HandleRect(rect, i);
                var distance = Vector2.Distance(point, handle.Center);
                if (distance < nearest && handle.Inflate(new Thickness(2.0f)).Contains(point))
                {
                    nearest = distance;
                    found = i;
                }
            }

            return found;
        }

        /// <summary>The element under a point of the panel: the deepest, and among equals the one drawn last.</summary>
        public JsonObject? PickAt(Vector2 point)
        {
            if (_result == null)
            {
                return null;
            }

            var inLayout = ToLayout(point);
            JsonObject? best = null;
            var bestDepth = -1;
            foreach (var (node, element) in _result.Elements)
            {
                if (element.Bounds.IsEmpty || !element.Bounds.Contains(inLayout) || !IsShowing(element))
                {
                    continue;
                }

                var depth = 0;
                for (var parent = element.Parent; parent != null; parent = parent.Parent)
                {
                    depth++;
                }

                if (depth >= bestDepth)
                {
                    bestDepth = depth;
                    best = node;
                }
            }

            return best;
        }

        private bool IsShowing(UiElement element)
        {
            for (UiElement? at = element; at != null && !ReferenceEquals(at, this); at = at.Parent)
            {
                if (at.Visibility != Visibility.Visible)
                {
                    return false;
                }
            }

            return true;
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (_result == null)
            {
                return;
            }

            Focus();
            _handle = -1;
            if (UiDesignerDocument.Selected is { } selected && _result.Elements.TryGetValue(selected, out var current))
            {
                _handle = HandleAt(ToPanel(current.Bounds), e.Position);
            }

            if (_handle < 0)
            {
                var picked = PickAt(e.Position);
                UiDesignerDocument.Select(picked ?? UiDesignerDocument.Root);
            }

            _pressed = true;
            _dragging = false;
            _drag = null;
            _pressedAt = e.Position;
            CapturePointer();
            e.Handled = true;
        }

        protected override void OnPointerMoved(UiPointerEventArgs e)
        {
            if (!_pressed || UiDesignerDocument.Selected is not { } node)
            {
                return;
            }

            if (!_dragging)
            {
                if (Vector2.Distance(e.Position, _pressedAt) < DragStart || UiDesignerDocument.ParentOf(node) == null
                    || _result == null || !_result.Elements.TryGetValue(node, out var element))
                {
                    return;
                }

                _drag = new UiDesignerDrag(node, element, UiDesignerDocument.ParentOf(node));
                if (_handle < 0 && !_drag.CanMove)
                {
                    _drag = null;
                    _pressed = false;
                    ReleasePointerCapture();
                    Status = "A stack places its children: reorder them in the outline, or move the stack itself.";
                    return;
                }

                _dragging = true;
            }

            if (_drag == null)
            {
                return;
            }

            var delta = (e.Position - _pressedAt) / MathF.Max(FitScale, 0.001f);
            if (_handle < 0)
            {
                _drag.Apply(delta.X, delta.Y, delta.X, delta.Y);
            }
            else
            {
                var handle = Handles[_handle];
                _drag.Apply(delta.X * handle.Left, delta.Y * handle.Top, delta.X * handle.Right, delta.Y * handle.Bottom);
            }
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (!_pressed)
            {
                return;
            }

            _pressed = false;
            ReleasePointerCapture();
            if (_dragging)
            {
                _dragging = false;
                UiDesignerDocument.Commit();
                UiDesignerDocument.Touch(tree: true);
            }
        }

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            var control = e.Modifiers.HasFlag(UiModifiers.Control);
            var shift = e.Modifiers.HasFlag(UiModifiers.Shift);
            switch (e.Key)
            {
                case UiKey.Z when control && shift:
                case UiKey.Y when control:
                    UiDesignerDocument.Redo();
                    break;
                case UiKey.Z when control:
                    UiDesignerDocument.Undo();
                    break;
                case UiKey.Delete when UiDesignerDocument.Selected is { } selected:
                    UiDesignerDocument.Delete(selected);
                    break;
                case UiKey.D when control && UiDesignerDocument.Selected is { } selected:
                    UiDesignerDocument.Duplicate(selected);
                    break;
                case UiKey.S when control:
                    if (UiDesignerDocument.Path != null)
                    {
                        UiDesignerDocument.Save();
                    }

                    break;
                case UiKey.Left:
                    Nudge(-1, 0, shift);
                    break;
                case UiKey.Right:
                    Nudge(1, 0, shift);
                    break;
                case UiKey.Up:
                    Nudge(0, -1, shift);
                    break;
                case UiKey.Down:
                    Nudge(0, 1, shift);
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        private void Nudge(int x, int y, bool far)
        {
            if (UiDesignerDocument.Selected is not { } node || _result == null || !_result.Elements.TryGetValue(node, out var element))
            {
                return;
            }

            var drag = new UiDesignerDrag(node, element, UiDesignerDocument.ParentOf(node));
            if (!drag.CanMove)
            {
                return;
            }

            var step = 1.0f;
            if (far)
            {
                step = 10.0f;
            }

            drag.Apply(x * step, y * step, x * step, y * step);
            UiDesignerDocument.Commit();
            UiDesignerDocument.Touch(tree: true);
        }
    }
}

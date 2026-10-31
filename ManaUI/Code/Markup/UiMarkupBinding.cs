using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;

namespace ManaUI
{
    /// <summary>A view-model path as a document writes it: "Title", "Player.Hull", "Weapons[2].Name"; "." is the context itself.</summary>
    internal readonly record struct UiMarkupPathPart(string Name, int? Index)
    {
        public static UiMarkupPathPart[] Parse(string path)
        {
            if (path is "" or ".")
            {
                return [];
            }

            var parts = new List<UiMarkupPathPart>();
            foreach (var piece in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var open = piece.IndexOf('[');
                if (open > 0 && piece.EndsWith(']') && int.TryParse(piece[(open + 1)..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
                {
                    parts.Add(new UiMarkupPathPart(piece[..open], index));
                }
                else
                {
                    parts.Add(new UiMarkupPathPart(piece, null));
                }
            }

            return parts.ToArray();
        }

        public static PropertyInfo? Property(Type owner, string name) =>
            owner.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        /// <summary>
        /// Checks a path against types alone, so a layout can be checked with no instance (the designer) and a
        /// typo is reported once rather than failing quietly every frame. Null when it is sound.
        /// </summary>
        public static string? Check(Type root, UiMarkupPathPart[] path, out Type? end, out bool observable)
        {
            end = root;
            observable = true;
            foreach (var part in path)
            {
                observable &= typeof(INotifyPropertyChanged).IsAssignableFrom(end);
                var property = Property(end!, part.Name);
                if (property == null)
                {
                    end = null;
                    return $"{root.Name} has no property '{part.Name}'.";
                }

                end = property.PropertyType;
                if (part.Index != null)
                {
                    end = ItemType(end);
                    if (end == typeof(object))
                    {
                        // A list of unknown things: the rest can only be known at run time.
                        return null;
                    }
                }
            }

            return null;
        }

        /// <summary>Whether a path ends on something that cannot be written to.</summary>
        public static bool IsReadOnly(Type root, UiMarkupPathPart[] path)
        {
            if (path.Length == 0 || path[^1].Index != null || Check(root, path[..^1], out var owner, out _) != null || owner == null)
            {
                return false;
            }

            return Property(owner, path[^1].Name) is { } property && property.SetMethod is not { IsPublic: true };
        }

        private static Type ItemType(Type list)
        {
            if (list.IsArray)
            {
                return list.GetElementType() ?? typeof(object);
            }

            foreach (var face in list.GetInterfaces().Prepend(list))
            {
                if (face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return face.GetGenericArguments()[0];
                }
            }

            return typeof(object);
        }
    }

    /// <summary>What a "bind" entry drives: an element's property, or its place in its parent, which is not one.</summary>
    internal sealed record UiMarkupTarget(string Name, Type Type, Func<object?> Get, Action<object?> Set)
    {
        /// <summary>A property a view model could hand a value to; templates and child elements come only from code.</summary>
        public static bool CanDrive(PropertyInfo property) =>
            property.SetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0
            && !typeof(Delegate).IsAssignableFrom(property.PropertyType) && !typeof(UiElement).IsAssignableFrom(property.PropertyType);

        public static Type? PlacementType(Type element, string key)
        {
            switch (key.ToLowerInvariant())
            {
                case "grid.row" or "grid.column" or "grid.rowspan" or "grid.columnspan" or "zindex":
                    return typeof(int);
                case "canvas.left" or "canvas.top" or "canvas.right" or "canvas.bottom":
                    return typeof(float);
                case "dock":
                    return typeof(Dock);
                case "columns" or "rows" when typeof(Grid).IsAssignableFrom(element):
                    return typeof(string);
                default:
                    return null;
            }
        }

        public static UiMarkupTarget? Find(UiElement element, string key)
        {
            var property = element.GetType().GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && CanDrive(property))
            {
                return new UiMarkupTarget(property.Name, property.PropertyType, () => property.GetValue(element), value => property.SetValue(element, value));
            }

            if (PlacementType(element.GetType(), key) is not { } type)
            {
                return null;
            }

            return new UiMarkupTarget(key, type, () => null, value => Place(element, key.ToLowerInvariant(), value));
        }

        private static void Place(UiElement element, string key, object? value)
        {
            var slot = element.EnsureLayoutSlot();
            switch (key)
            {
                case "grid.row":
                    slot.GridRow = (int)value!;
                    break;
                case "grid.column":
                    slot.GridColumn = (int)value!;
                    break;
                case "grid.rowspan":
                    slot.GridRowSpan = Math.Max(1, (int)value!);
                    break;
                case "grid.columnspan":
                    slot.GridColumnSpan = Math.Max(1, (int)value!);
                    break;
                case "canvas.left":
                    slot.CanvasLeft = (float)value!;
                    break;
                case "canvas.top":
                    slot.CanvasTop = (float)value!;
                    break;
                case "canvas.right":
                    slot.CanvasRight = (float)value!;
                    break;
                case "canvas.bottom":
                    slot.CanvasBottom = (float)value!;
                    break;
                case "dock":
                    slot.Dock = (Dock)value!;
                    break;
                case "zindex":
                    element.ZIndex((int)value!);
                    break;
                case "columns" when value is string columns:
                    ((Grid)element).SetColumns(columns);
                    break;
                case "rows" when value is string rows:
                    ((Grid)element).SetRows(rows);
                    break;
            }

            // A slot holds plain fields, so its parent has to be told it has more to lay out.
            element.InvalidateMeasure();
            element.Parent?.InvalidateMeasure();
        }
    }

    /// <summary>
    /// A document's "bind" entry, live: a target property kept up with a view-model path. The source announces
    /// its changes through <see cref="INotifyPropertyChanged"/>, every level of the path watched, so replacing
    /// "Player" re-reads "Player.Hull". A path through something that cannot announce is read each frame instead
    /// (registered as a polled binding), the same escape hatch as BindPolled.
    /// </summary>
    internal sealed class UiMarkupBinding : IUiBinding
    {
        private readonly UiMarkupTarget _target;
        private readonly UiElement _element;
        private readonly object _root;
        private readonly UiMarkupPathPart[] _path;
        private readonly BindingMode _mode;
        private readonly string? _format;
        private readonly bool _invert;
        private readonly UiTheme _theme;
        private readonly INotifyPropertyChanged?[] _watched;
        private readonly PropertyChangedEventHandler?[] _handlers;
        private object? _last;
        private bool _hasValue;
        private bool _active;
        private bool _disposed;
        private bool _applying;

        public UiMarkupBinding(UiMarkupTarget target, UiElement element, object root, UiMarkupPathPart[] path, BindingMode mode, string? format, bool invert, UiTheme theme)
        {
            _element = element;
            _target = target;
            _root = root;
            _path = path;
            _mode = mode;
            _format = format;
            _invert = invert;
            _theme = theme;
            _watched = new INotifyPropertyChanged?[path.Length];
            _handlers = new PropertyChangedEventHandler?[path.Length];

            if (mode == BindingMode.OneWayToSource)
            {
                PushToSource();
            }
            else
            {
                Refresh();
            }

            SetActive(true);
        }

        public void SetActive(bool active)
        {
            if (_disposed || _active == active)
            {
                return;
            }

            _active = active;
            if (_mode is not (BindingMode.OneWay or BindingMode.TwoWay))
            {
                return;
            }

            if (active)
            {
                Watch(0);
                Refresh();
            }
            else
            {
                Unwatch(0);
            }
        }

        // Walks the path from a level down, subscribing to each owner that can announce.
        private void Watch(int from)
        {
            Unwatch(from);
            var owner = OwnerAt(from);
            for (var i = from; i < _path.Length && owner != null; i++)
            {
                if (owner is INotifyPropertyChanged observable)
                {
                    var level = i;
                    PropertyChangedEventHandler handler = (_, e) => OnChanged(level, e.PropertyName);
                    observable.PropertyChanged += handler;
                    _watched[i] = observable;
                    _handlers[i] = handler;
                }

                owner = Step(owner, _path[i]);
            }
        }

        private void Unwatch(int from)
        {
            for (var i = from; i < _path.Length; i++)
            {
                if (_watched[i] is { } observable && _handlers[i] is { } handler)
                {
                    observable.PropertyChanged -= handler;
                }

                _watched[i] = null;
                _handlers[i] = null;
            }
        }

        private void OnChanged(int level, string? property)
        {
            // Null or empty is the framework's way of saying everything changed.
            if (!string.IsNullOrEmpty(property) && !string.Equals(property, _path[level].Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (level + 1 < _path.Length)
            {
                Watch(level + 1);
            }

            Refresh();
        }

        private object? OwnerAt(int level)
        {
            var owner = _root;
            for (var i = 0; i < level && owner != null; i++)
            {
                owner = Step(owner, _path[i])!;
            }

            return owner;
        }

        private static object? Step(object owner, UiMarkupPathPart part)
        {
            var value = UiMarkupPathPart.Property(owner.GetType(), part.Name)?.GetValue(owner);
            if (part.Index is not { } index)
            {
                return value;
            }

            if (value is IList list && index >= 0 && index < list.Count)
            {
                return list[index];
            }

            return null;
        }

        public void Refresh()
        {
            if (_disposed || _mode == BindingMode.OneWayToSource)
            {
                return;
            }

            var value = OwnerAt(_path.Length);
            if (_invert && value is bool flag)
            {
                value = !flag;
            }

            if (_format != null)
            {
                value = string.Format(CultureInfo.InvariantCulture, _format, value);
            }

            if (_hasValue && Equals(_last, value))
            {
                return;
            }

            _last = value;
            _hasValue = true;
            var converted = UiMarkup.Coerce(value, _target.Type, _theme);
            if (converted == null && _target.Type.IsValueType && Nullable.GetUnderlyingType(_target.Type) == null)
            {
                return;
            }

            if (converted == null && _target.Type == typeof(string))
            {
                converted = string.Empty;
            }

            _applying = true;
            _element.BeginBindingApply();
            try
            {
                _target.Set(converted);
            }
            finally
            {
                _element.EndBindingApply();
                _applying = false;
            }
        }

        /// <summary>The target changed: a person's edit, written back for TwoWay and OneWayToSource.</summary>
        public void PushToSource()
        {
            if (_disposed || _applying || _path.Length == 0 || _mode is not (BindingMode.TwoWay or BindingMode.OneWayToSource))
            {
                return;
            }

            var owner = OwnerAt(_path.Length - 1);
            var part = _path[^1];
            if (owner == null || part.Index != null || UiMarkupPathPart.Property(owner.GetType(), part.Name) is not { CanWrite: true } property)
            {
                return;
            }

            var value = _target.Get();
            if (_invert && value is bool flag)
            {
                value = !flag;
            }

            var converted = UiMarkup.Coerce(value, property.PropertyType, _theme);
            if (Equals(property.GetValue(owner), converted))
            {
                return;
            }

            // Remembered first, so the change this raises is not applied straight back over the edit.
            _last = value;
            _hasValue = true;
            property.SetValue(owner, converted);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                SetActive(false);
                Unwatch(0);
                _disposed = true;
            }
        }
    }

    /// <summary>What a document's "on" entries call: a command on the view model, or failing that a method.</summary>
    internal static class UiMarkupCommands
    {
        public static void Run(object root, UiMarkupPathPart[] path, object? parameter)
        {
            object? owner = root;
            for (var i = 0; i < path.Length - 1 && owner != null; i++)
            {
                owner = UiMarkupPathPart.Property(owner.GetType(), path[i].Name)?.GetValue(owner);
            }

            if (owner == null || path.Length == 0)
            {
                return;
            }

            var name = path[^1].Name;
            if (UiMarkupPathPart.Property(owner.GetType(), name)?.GetValue(owner) is ICommand command)
            {
                if (command.CanExecute(parameter))
                {
                    command.Execute(parameter);
                }

                return;
            }

            var method = owner.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase, Type.EmptyTypes);
            method?.Invoke(owner, null);
        }

        /// <summary>Whether a path names something an event can call, judged from types alone.</summary>
        public static string? Check(Type root, UiMarkupPathPart[] path)
        {
            if (path.Length == 0)
            {
                return $"An event must name a command on {root.Name}.";
            }

            var problem = UiMarkupPathPart.Check(root, path[..^1], out var owner, out _);
            if (problem != null || owner == null)
            {
                return problem;
            }

            var name = path[^1].Name;
            var property = UiMarkupPathPart.Property(owner, name);
            if (property != null && typeof(ICommand).IsAssignableFrom(property.PropertyType))
            {
                return null;
            }

            if (owner.GetMethod(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase, Type.EmptyTypes) != null)
            {
                return null;
            }

            return $"{owner.Name} has no command or method '{name}'.";
        }
    }

    /// <summary>
    /// The change event a control raises for an editable property: what lets TwoWay write back. Listed by hand,
    /// as <see cref="ControlBindingExtensions"/> are, because only the control knows which event means "edited"
    /// and because this runs where runtime code generation does not (the ahead-of-time web build).
    /// </summary>
    internal static class UiMarkupEdits
    {
        private sealed record Edit(Type Control, string Property, Action<UiElement, Action> Attach);

        private static Edit On<T>(string property, Action<T, Action> attach) where T : UiElement =>
            new(typeof(T), property, (element, changed) => attach((T)element, changed));

        private static readonly Edit[] Edits =
        [
            On<Slider>("value", (control, changed) => control.ValueChanged += (_, _) => changed()),
            On<NumericBox>("value", (control, changed) => control.ValueChanged += (_, _) => changed()),
            On<SliderBox>("value", (control, changed) => control.ValueChanged += (_, _) => changed()),
            On<CheckBox>("ischecked", (control, changed) => control.CheckedChanged += (_, _) => changed()),
            On<ComboBox>("selectedindex", (control, changed) => control.SelectionChanged += (_, _) => changed()),
            On<TabView>("selectedindex", (control, changed) => control.SelectionChanged += (_, _) => changed()),
            On<Expander>("isexpanded", (control, changed) => control.ExpandedChanged += (_, _) => changed()),
            On<ColorField>("color", (control, changed) => control.ColorChanged += (_, _) => changed()),
            On<ColorPicker>("color", (control, changed) => control.ColorChanged += (_, _) => changed()),
            On<ColorSpectrum>("color", (control, changed) => control.ColorChanged += (_, _) => changed()),
            On<VectorBox>("vector", (control, changed) => control.ValueChanged += (_, _) => changed()),
            On<VectorBox>("value", (control, changed) => control.ValueChanged += (_, _) => changed()),
            On<VectorBox>("value2", (control, changed) => control.ValueChanged += (_, _) => changed()),
            On<ScrollViewer>("offset", (control, changed) => control.ScrollChanged += (_, _) => changed()),
            On<ResizablePanel>("size", (control, changed) => control.SizeChanged += (_, _) => changed()),
            On<UiWindow>("position", (control, changed) => control.Dragging += (_, _) => changed()),
            On<TextBox>("text", (control, changed) => control.TextChanged += (_, _) => changed())
        ];

        private static Edit? Find(Type type, string property)
        {
            var key = property.ToLowerInvariant();
            return Edits.FirstOrDefault(edit => edit.Property == key && edit.Control.IsAssignableFrom(type));
        }

        public static bool CanHook(Type type, string property) => Find(type, property) != null;

        public static void Hook(UiElement element, string property, bool onCommit, Action changed)
        {
            if (onCommit && element is TextBox text && property.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                text.TextCommitted += (_, _) => changed();
                return;
            }

            Find(element.GetType(), property)?.Attach(element, changed);
        }
    }
}

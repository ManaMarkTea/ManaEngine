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

    /// <summary>
    /// A document's "bind" entry, live: a target property kept up with a view-model path. The source announces
    /// its changes through <see cref="INotifyPropertyChanged"/>, every level of the path watched, so replacing
    /// "Player" re-reads "Player.Hull". A path through something that cannot announce is read each frame instead
    /// (registered as a polled binding), the same escape hatch as BindPolled.
    /// </summary>
    internal sealed class UiMarkupBinding : IUiBinding
    {
        private readonly UiElement _element;
        private readonly PropertyInfo _target;
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

        public UiMarkupBinding(UiElement element, PropertyInfo target, object root, UiMarkupPathPart[] path, BindingMode mode, string? format, bool invert, UiTheme theme)
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
            var converted = UiMarkup.Coerce(value, _target.PropertyType, _theme);
            if (converted == null && _target.PropertyType.IsValueType && Nullable.GetUnderlyingType(_target.PropertyType) == null)
            {
                return;
            }

            if (converted == null && _target.PropertyType == typeof(string))
            {
                converted = string.Empty;
            }

            _applying = true;
            _element.BeginBindingApply();
            try
            {
                _target.SetValue(_element, converted);
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

            var value = _target.GetValue(_element);
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
        public static bool TryHook(UiElement element, string property, bool onCommit, Action changed)
        {
            switch (element, property.ToLowerInvariant())
            {
                case (Slider slider, "value"):
                    slider.ValueChanged += (_, _) => changed();
                    return true;
                case (NumericBox box, "value"):
                    box.ValueChanged += (_, _) => changed();
                    return true;
                case (CheckBox check, "ischecked"):
                    check.CheckedChanged += (_, _) => changed();
                    return true;
                case (ComboBox combo, "selectedindex"):
                    combo.SelectionChanged += (_, _) => changed();
                    return true;
                case (Expander expander, "isexpanded"):
                    expander.ExpandedChanged += (_, _) => changed();
                    return true;
                case (ColorField field, "color"):
                    field.ColorChanged += (_, _) => changed();
                    return true;
                case (TextBox text, "text") when onCommit:
                    text.TextCommitted += (_, _) => changed();
                    return true;
                case (TextBox text, "text"):
                    text.TextChanged += (_, _) => changed();
                    return true;
                default:
                    return false;
            }
        }
    }
}

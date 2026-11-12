using System.Reflection;
using Mana;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// The last resort: reads the subject's own properties and builds an editor for each one it recognises.
    /// <para>
    /// This is what a component gets when no widget claims it. A hand-written widget is still better -- it
    /// knows sensible ranges, which properties matter, and what to call them -- but "no inspector for this
    /// component yet" is a dead end, and most components are a handful of floats and flags that reflection
    /// can show perfectly well.
    /// </para>
    /// <para>
    /// Only properties declared below the base type are shown. Everything a component inherits from
    /// <c>ManaComponent</c> is plumbing -- its owner, its context -- and listing that on every component
    /// buries the two or three properties that actually distinguish it.
    /// </para>
    /// <para>
    /// Bound by polling, like the rest of the inspector. That covers a subject raising
    /// <c>INotifyPropertyChanged</c> and one that changes silently alike, which matters here because a
    /// reflected type is by definition one nobody has thought about.
    /// </para>
    /// </summary>
    public sealed class ReflectedPropertiesWidget : IInspectorWidget
    {
        /// <summary>Types whose members are plumbing rather than state. Properties from these are skipped.</summary>
        public HashSet<Type> IgnoredDeclaringTypes { get; } = new();

        /// <summary>The section's heading; by default the type's name, marked as reflected.</summary>
        public Func<object, string>? Title { get; set; }

        /// <summary>Property names never shown, whatever declares them.</summary>
        public HashSet<string> IgnoredNames { get; } = new(StringComparer.Ordinal);

        public bool CanInspect(object subject)
        {
            return subject != null && Editable(subject).Count > 0;
        }

        public UiElement Build(object subject, InspectorContext context)
        {
            var theme = context.Theme;
            var rows = new StackPanel().Vertical(theme.Spacing);
            string? category = null;

            foreach (var property in Editable(subject))
            {
                // [Category] groups a component's settings under a heading -- a sprite's picture apart from its look.
                var group = property.GetCustomAttribute<System.ComponentModel.CategoryAttribute>()?.Category;
                if (group != null && group != category)
                {
                    rows.Children.Add(new TextBlock().Text(group).Foreground(theme.Accent).FontSize(theme.FontSizeSmall)
                        .Configure(heading => heading.Margin = new Thickness(0.0f, category == null ? 0.0f : 6.0f, 0.0f, 0.0f)));
                }
                category = group ?? category;

                // The property's own doc comment, shown the way Unreal shows a UPROPERTY tooltip. Nothing
                // here was written for a UI, so the comment above the field is the only description that
                // exists -- and it is usually the right one.
                var row = InspectorControls.Row(
                    context,
                    Humanise(property.Name),
                    Editor(subject, property, theme, context.Engine, context.History),
                    XmlDocSummaries.For(property));

                // Saved script fields are the property names in camelCase, which is what an override's path holds.
                rows.Children.Add(subject is ManaComponent component
                    ? PrefabOverrideMarks.Mark(context, row, component.Owner, component, System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(property.Name))
                    : row);
            }

            // Named for what it is, so nobody mistakes a reflected list for a considered one.
            return new Expander()
                .Header(Title?.Invoke(subject) ?? $"{subject.GetType().Name} (reflected)")
                .Content(rows);
        }

        private List<PropertyInfo> Editable(object subject)
        {
            var found = new List<PropertyInfo>();

            foreach (var property in subject.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                if (IgnoredNames.Contains(property.Name))
                {
                    continue;
                }

                // [Browsable(false)] is the standard way for a component to say "not for the inspector" -- a forwarder
                // to another component's setting, say, which would only show the same value twice.
                if (property.GetCustomAttribute<System.ComponentModel.BrowsableAttribute>() is { Browsable: false })
                {
                    continue;
                }

                if (property.DeclaringType != null && IgnoredDeclaringTypes.Contains(property.DeclaringType))
                {
                    continue;
                }

                var isPictureList = property.PropertyType == typeof(string[])
                    && (property.GetCustomAttribute<Mana.Code.Components.PicturePathAttribute>() != null
                        || property.GetCustomAttribute<Mana.Code.Components.ModelPathAttribute>() != null);
                if (!IsSupported(property.PropertyType) && !isPictureList)
                {
                    continue;
                }

                found.Add(property);
            }

            // A type that groups its properties is shown as written -- each group in the order it is declared, the
            // component's own before what it inherits; one that does not keeps the alphabetical list.
            if (found.Any(property => property.GetCustomAttribute<System.ComponentModel.CategoryAttribute>() != null))
            {
                var groups = found
                    .OrderBy(property => Depth(subject.GetType(), property.DeclaringType))
                    .ThenBy(property => property.MetadataToken)
                    .GroupBy(property => property.GetCustomAttribute<System.ComponentModel.CategoryAttribute>()?.Category ?? string.Empty)
                    .OrderBy(group => group.Key.Length == 0 ? 0 : 1);
                return groups.SelectMany(group => group).ToList();
            }

            found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return found;
        }

        // How many steps up from the subject's own type a property is declared: 0 for its own.
        private static int Depth(Type type, Type? declaring)
        {
            var depth = 0;
            for (var current = type; current != null && current != declaring; current = current.BaseType)
            {
                depth++;
            }
            return depth;
        }

        private static bool IsSupported(Type type)
        {
            return type == typeof(bool)
                || type == typeof(int)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(string)
                || type == typeof(Vector2)
                || type == typeof(Vector3)
                || type == typeof(Vector4)
                || type == typeof(ManaComponentReference)
                || type == typeof(Mana.Code.Content.ManaAssetRef)
                || type.IsEnum;
        }

        /// <summary>
        /// A drop-down of the other components on the same node -- "(automatic)" first, for no explicit choice -- for a
        /// <see cref="ManaComponentReference"/> property. Only components of the kind the property's
        /// <see cref="ComponentReferenceAttribute"/> names are listed, and never the subject itself. Two of one kind
        /// are told apart by number, in the order they sit on the node.
        /// </summary>
        private static UiElement ComponentPicker(ManaComponent self, PropertyInfo property, Func<object?> read, Action<object?> write)
        {
            var required = property.GetCustomAttribute<ComponentReferenceAttribute>()?.RequiredType ?? typeof(ManaComponent);

            // What the list offers right now. Worked out again on every poll: names are given and changed while this
            // panel is on screen, and a list built once when the panel was created kept showing a component by the
            // name it had then -- or by "Trigger Volume #1" long after it was called something else.
            (List<ManaComponent?> Candidates, List<string> Labels) Snapshot()
            {
                var candidates = new List<ManaComponent?> { null };
                var labels = new List<string> { "(automatic)" };

                foreach (var component in self.Owner.Components)
                {
                    if (ReferenceEquals(component, self) || !required.IsInstanceOfType(component))
                    {
                        continue;
                    }

                    var kind = ComponentDisplayName.For(component.GetType());
                    var sameKind = candidates.Count(other => other != null && other.GetType() == component.GetType());
                    var ofThisKind = self.Owner.Components.Count(other => !ReferenceEquals(other, self) && other.GetType() == component.GetType());
                    candidates.Add(component);

                    // A named component is told apart by its name; unnamed ones by number when there are several.
                    labels.Add(!string.IsNullOrWhiteSpace(component.Label)
                        ? $"{component.Label} ({kind})"
                        : ofThisKind == 1 ? kind : $"{kind} #{sameKind + 1}");
                }

                return (candidates, labels);
            }

            var (options, names) = Snapshot();
            var refreshing = false;

            int SelectedIndex()
            {
                var target = read() is ManaComponentReference reference
                    ? reference.Resolve<ManaComponent>(self.Owner)
                    : null;
                var index = options.FindIndex(candidate => ReferenceEquals(candidate, target));
                return index < 0 ? 0 : index;
            }

            var combo = new ComboBox().Options(names.ToArray());
            combo.SelectedIndex = SelectedIndex();
            combo.SelectionChanged += (_, index) =>
            {
                if (refreshing || combo.IsApplyingBinding || index < 0 || index >= options.Count)
                {
                    return;
                }

                write(ManaComponentReference.To(options[index]));
            };

            combo.BindPolled(c => c.SelectedIndex, () =>
            {
                var (freshOptions, freshNames) = Snapshot();
                if (!freshNames.SequenceEqual(names) || !freshOptions.SequenceEqual(options))
                {
                    options = freshOptions;
                    names = freshNames;
                    refreshing = true;
                    combo.SetItems(names.ToArray());
                    refreshing = false;
                }

                return SelectedIndex();
            });
            return combo;
        }

        private static UiElement Editor(object subject, PropertyInfo property, UiTheme theme, ManaContext? engine, EditorUndoHistory? history)
        {
            var type = property.PropertyType;

            object? Read()
            {
                try
                {
                    return property.GetValue(subject);
                }
                catch (TargetInvocationException)
                {
                    // A property that throws is one the component did not expect to be read out of turn.
                    // One dead row beats a dead panel.
                    return null;
                }
            }

            void Set(object? value)
            {
                try
                {
                    property.SetValue(subject, value);
                }
                catch (TargetInvocationException)
                {
                }
                catch (ArgumentException)
                {
                }
            }

            // Every change goes on the undo history; a run of them on this property (a slider dragged) folds into one.
            void Write(object? value)
            {
                var before = Read();
                Set(value);
                var after = Read();
                if (history == null || Equals(before, after))
                {
                    return;
                }
                history.RecordMerging((subject, property), $"Change {Humanise(property.Name)}", () => Set(before), () => Set(after));
            }

            if (type == typeof(ManaComponentReference) && subject is ManaComponent self)
            {
                return ComponentPicker(self, property, Read, Write);
            }

            if (type == typeof(bool))
            {
                var box = new CheckBox().Checked(Read() is true);
                box.CheckedChanged += (_, value) =>
                {
                    if (!box.IsApplyingBinding)
                    {
                        Write(value);
                    }
                };
                box.BindPolled(b => b.IsChecked, () => Read() is true);
                return box;
            }

            if (type.IsEnum)
            {
                var names = Enum.GetNames(type);
                var combo = new ComboBox().Options(names);
                combo.SelectedIndex = Math.Max(0, Array.IndexOf(names, Read()?.ToString()));
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.IsApplyingBinding)
                    {
                        return;
                    }

                    if (combo.SelectedItem is string name && Enum.TryParse(type, name, out var parsed))
                    {
                        Write(parsed);
                    }
                };
                combo.BindPolled(c => c.SelectedIndex, () => Math.Max(0, Array.IndexOf(names, Read()?.ToString())));
                return combo;
            }

            if (type == typeof(Mana.Code.Content.ManaAssetRef))
            {
                var extension = property.GetCustomAttribute<Mana.Code.Content.AssetKindAttribute>()?.Extension ?? ".json";
                return AssetPicker.Build(theme, Humanise(property.Name), extension,
                    () => Read() as Mana.Code.Content.ManaAssetRef ?? Mana.Code.Content.ManaAssetRef.None, value => Write(value));
            }

            if (property.GetCustomAttribute<Mana.Code.Components.PicturePathAttribute>() != null)
            {
                if (type == typeof(string))
                {
                    return PicturePicker.Slot(theme, Humanise(property.Name), () => Read() as string ?? string.Empty, value => Write(value));
                }
                if (type == typeof(string[]))
                {
                    return PicturePicker.List(theme, Humanise(property.Name), () => Read() as string[] ?? [], value => Write(value));
                }
            }

            if (property.GetCustomAttribute<Mana.Code.Components.ModelPathAttribute>() != null)
            {
                if (type == typeof(string))
                {
                    return ModelPicker.Slot(theme, Humanise(property.Name), () => Read() as string ?? string.Empty, value => Write(value));
                }
                if (type == typeof(string[]))
                {
                    return ModelPicker.List(theme, Humanise(property.Name), () => Read() as string[] ?? [], value => Write(value));
                }
            }

            if (type == typeof(string) && property.GetCustomAttribute<Mana.Code.Components.SoundPathAttribute>() != null)
            {
                return SoundPicker.Slot(theme, Humanise(property.Name), engine, () => Read() as string ?? string.Empty, value => Write(value));
            }

            if (type == typeof(string))
            {
                var box = new TextBox().Value(Read() as string ?? string.Empty).Hint("empty");
                box.OnCommitted(text =>
                {
                    if (!box.IsApplyingBinding)
                    {
                        Write(text);
                    }
                });
                box.BindPolled(b => b.Text, () => Read() as string ?? string.Empty);
                return box;
            }

            // Integers keep the stepper, matching how a shader uniform's int is edited. A slider whose
            // step is a two-hundredth of its range is no way to pick a whole number.
            // [Range] on a property gives its true limits; without one a range is guessed from the value.
            var declared = property.GetCustomAttribute<System.ComponentModel.DataAnnotations.RangeAttribute>();
            if (type == typeof(int))
            {
                var box = new NumericBox().Integer().Value(Convert.ToSingle(Read() ?? 0));
                if (declared != null)
                {
                    box.Range(Convert.ToSingle(declared.Minimum), Convert.ToSingle(declared.Maximum));
                }

                box.ValueChanged += (_, value) =>
                {
                    if (!box.IsApplyingBinding)
                    {
                        Write((int)value);
                    }
                };
                box.BindPolled(b => b.Value, () => Convert.ToSingle(Read() ?? 0));
                return box;
            }

            // A slider with the number beside it, the same editor every hand-written widget uses for a
            // scalar. Nothing here declares a range, so one is guessed from the value in front of us and
            // then treated as soft: typing past either end widens it rather than clamping.
            if (type == typeof(float) || type == typeof(double))
            {
                var isDouble = type == typeof(double);
                var current = Convert.ToSingle(Read() ?? 0.0);
                var (minimum, maximum) = declared != null
                    ? (Convert.ToSingle(declared.Minimum), Convert.ToSingle(declared.Maximum))
                    : GuessRange(current);

                return InspectorControls.Scalar(
                    () => Convert.ToSingle(Read() ?? 0.0),
                    // Boxed explicitly on both sides. Without the casts the ternary unifies to double, so
                    // a float property is handed a boxed double and the reflected set throws -- silently,
                    // because the write is guarded, which looks exactly like a row that does nothing.
                    value => Write(isDouble ? (object)(double)value : value),
                    minimum,
                    maximum);
            }

            if (type == typeof(Vector2))
            {
                var box = new VectorBox().Components(2).Step(0.01f).Places(3);
                box.Value2 = Read() as Vector2? ?? Vector2.Zero;
                box.ValueChanged += (_, v) =>
                {
                    if (!box.IsApplyingBinding)
                    {
                        Write(new Vector2(v.X, v.Y));
                    }
                };
                box.BindPolled(b => b.Value2, () => Read() as Vector2? ?? Vector2.Zero);
                return box;
            }

            if (type == typeof(Vector3))
            {
                var box = new VectorBox().Step(0.01f).Places(3);
                box.Value = Read() as Vector3? ?? Vector3.Zero;
                box.ValueChanged += (_, v) =>
                {
                    if (!box.IsApplyingBinding)
                    {
                        Write(new Vector3(v.X, v.Y, v.Z));
                    }
                };
                box.BindPolled(b => b.Value, () => Read() as Vector3? ?? Vector3.Zero);
                return box;
            }

            // Vector4: a swatch when the name says colour, four numbers otherwise. Same heuristic the
            // shader-uniform editor uses, so the two agree about what a vector is.
            if (LooksLikeColour(property.Name))
            {
                return InspectorControls.Colour(
                    () => Read() as Vector4? ?? Vector4.Zero,
                    value => Write(value));
            }

            var vector = new VectorBox().Components(4).Step(0.01f).Places(3);
            vector.Vector = Read() as Vector4? ?? Vector4.Zero;
            vector.ValueChanged += (_, v) =>
            {
                if (!vector.IsApplyingBinding)
                {
                    Write(v);
                }
            };
            vector.BindPolled(b => b.Vector, () => Read() as Vector4? ?? Vector4.Zero);
            return vector;
        }

        /// <summary>
        /// A plausible slider range for a value nothing describes, rounded outwards to a round number so
        /// the track reads sensibly: 90 gives 0..100, 0.35 gives 0..0.5. Soft, so it is a starting point
        /// rather than a limit.
        /// </summary>
        private static (float Minimum, float Maximum) GuessRange(float value)
        {
            var magnitude = MathF.Abs(value);
            var maximum = magnitude <= float.Epsilon ? 1.0f : RoundOutwards(magnitude);

            return value < 0.0f ? (-maximum, maximum) : (0.0f, maximum);
        }

        /// <summary>Up to the next 1, 2 or 5 times a power of ten.</summary>
        private static float RoundOutwards(float value)
        {
            var power = MathF.Pow(10.0f, MathF.Floor(MathF.Log10(value)));
            var normalised = value / power;

            var stepped = normalised <= 1.0f ? 1.0f
                : normalised <= 2.0f ? 2.0f
                : normalised <= 5.0f ? 5.0f
                : 10.0f;

            return stepped * power;
        }

        private static bool LooksLikeColour(string name)
        {
            return name.Contains("color", StringComparison.OrdinalIgnoreCase)
                || name.Contains("colour", StringComparison.OrdinalIgnoreCase)
                || name.Contains("tint", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>"OutlineWidth" -> "Outline width". A property name is not a label.</summary>
        private static string Humanise(string name)
        {
            var text = new System.Text.StringBuilder(name.Length + 8);

            for (var i = 0; i < name.Length; i++)
            {
                var current = name[i];

                // A capital starting a new word, but not one inside a run of them -- "UVOffset" stays
                // "UV offset" rather than becoming "U V offset".
                if (i > 0
                    && char.IsUpper(current)
                    && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    text.Append(' ');
                    text.Append(char.ToLowerInvariant(current));
                    continue;
                }

                text.Append(i > 0 ? char.ToLowerInvariant(current) : current);
            }

            return text.ToString();
        }
    }
}

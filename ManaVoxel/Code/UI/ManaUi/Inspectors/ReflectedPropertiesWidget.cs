using System.Reflection;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
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

            foreach (var property in Editable(subject))
            {
                // The property's own doc comment, shown the way Unreal shows a UPROPERTY tooltip. Nothing
                // here was written for a UI, so the comment above the field is the only description that
                // exists -- and it is usually the right one.
                rows.Children.Add(InspectorControls.Row(
                    context,
                    Humanise(property.Name),
                    Editor(subject, property),
                    XmlDocSummaries.For(property)));
            }

            // Named for what it is, so nobody mistakes a reflected list for a considered one.
            return new Expander()
                .Header($"{subject.GetType().Name} (reflected)")
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

                if (!IsSupported(property.PropertyType))
                {
                    continue;
                }

                found.Add(property);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return found;
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
                || type.IsEnum;
        }

        private static UiElement Editor(object subject, PropertyInfo property)
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

            void Write(object? value)
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
            if (type == typeof(int))
            {
                var box = new NumericBox().Integer().Value(Convert.ToSingle(Read() ?? 0));
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
                var (minimum, maximum) = GuessRange(current);

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

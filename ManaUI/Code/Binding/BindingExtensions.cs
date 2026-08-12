using System.Linq.Expressions;

namespace ManaUI
{
    /// <summary>
    /// The binding surface. Every path is a lambda, so bindings are compile-checked and rename-safe --
    /// the main thing string-path binding gets wrong.
    /// </summary>
    public static class BindingExtensions
    {
        /// <summary>
        /// Binds a target property to a source property. Two-way requires the caller to say when the target
        /// changed, so prefer the per-control helpers (<see cref="ControlBindingExtensions"/>) which already
        /// know their own change event.
        /// </summary>
        public static TElement Bind<TElement, TSource, TValue>(
            this TElement element,
            Expression<Func<TElement, TValue>> targetProperty,
            TSource source,
            Expression<Func<TSource, TValue>> sourceProperty,
            BindingMode mode = BindingMode.OneWay)
            where TElement : UiElement
            where TSource : class
        {
            ArgumentNullException.ThrowIfNull(source);

            var target = PropertyPath.Resolve(targetProperty);
            if (target.Setter == null)
            {
                throw new ArgumentException(
                    $"'{targetProperty}' is read-only and cannot be a binding target.",
                    nameof(targetProperty));
            }

            var origin = PropertyPath.Resolve(sourceProperty);

            var binding = new UiBinding<TSource, TValue>(
                source,
                origin.Getter,
                origin.Setter,
                origin.Name,
                value => target.Setter(element, value),
                () => target.Getter(element),
                mode);

            element.AddBinding(binding);
            return element;
        }

        /// <summary>
        /// Binds a target property to an arbitrary expression re-read every frame.
        /// <para>
        /// This is the deliberate escape hatch for engine state that will never implement
        /// INotifyPropertyChanged -- frame timings, mesh counts, the currently hovered voxel. Without it,
        /// showing any of that would mean wrapping the engine in view models or bypassing binding entirely.
        /// </para>
        /// </summary>
        public static TElement BindPolled<TElement, TValue>(
            this TElement element,
            Expression<Func<TElement, TValue>> targetProperty,
            Func<TValue> read)
            where TElement : UiElement
        {
            ArgumentNullException.ThrowIfNull(read);

            var target = PropertyPath.Resolve(targetProperty);
            if (target.Setter == null)
            {
                throw new ArgumentException(
                    $"'{targetProperty}' is read-only and cannot be a binding target.",
                    nameof(targetProperty));
            }

            element.AddPolledBinding(new UiPolledBinding<TValue>(read, value => target.Setter(element, value)));
            return element;
        }

        /// <summary>
        /// Binds with a conversion, for when the source type is not the target type -- the common case of
        /// showing a number as text.
        /// </summary>
        public static TElement Bind<TElement, TSource, TValue, TTarget>(
            this TElement element,
            Expression<Func<TElement, TTarget>> targetProperty,
            TSource source,
            Expression<Func<TSource, TValue>> sourceProperty,
            Func<TValue, TTarget> convert)
            where TElement : UiElement
            where TSource : class
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(convert);

            var target = PropertyPath.Resolve(targetProperty);
            if (target.Setter == null)
            {
                throw new ArgumentException(
                    $"'{targetProperty}' is read-only and cannot be a binding target.",
                    nameof(targetProperty));
            }

            var origin = PropertyPath.Resolve(sourceProperty);

            // A converted binding is one-way by definition: the conversion has no inverse.
            var binding = new UiBinding<TSource, TValue>(
                source,
                origin.Getter,
                setValue: null,
                origin.Name,
                value => target.Setter(element, convert(value)),
                readTarget: null,
                BindingMode.OneWay);

            element.AddBinding(binding);
            return element;
        }
    }

    /// <summary>
    /// Two-way bindings for the built-in controls. Each one knows the control's own change event, which is
    /// the piece the generic <see cref="BindingExtensions.Bind{TElement,TSource,TValue}"/> cannot infer.
    /// </summary>
    public static class ControlBindingExtensions
    {
        public static Slider BindValue<TSource>(
            this Slider slider,
            TSource source,
            Expression<Func<TSource, float>> sourceProperty,
            BindingMode mode = BindingMode.TwoWay)
            where TSource : class
        {
            var origin = PropertyPath.Resolve(sourceProperty);

            var binding = new UiBinding<TSource, float>(
                source,
                origin.Getter,
                origin.Setter,
                origin.Name,
                value => slider.Value = value,
                () => slider.Value,
                mode);

            slider.ValueChanged += (_, _) => binding.PushToSource();
            slider.AddBinding(binding);
            return slider;
        }

        public static CheckBox BindIsChecked<TSource>(
            this CheckBox checkBox,
            TSource source,
            Expression<Func<TSource, bool>> sourceProperty,
            BindingMode mode = BindingMode.TwoWay)
            where TSource : class
        {
            var origin = PropertyPath.Resolve(sourceProperty);

            var binding = new UiBinding<TSource, bool>(
                source,
                origin.Getter,
                origin.Setter,
                origin.Name,
                value => checkBox.IsChecked = value,
                () => checkBox.IsChecked,
                mode);

            checkBox.CheckedChanged += (_, _) => binding.PushToSource();
            checkBox.AddBinding(binding);
            return checkBox;
        }

        public static TextBlock BindText<TSource>(
            this TextBlock textBlock,
            TSource source,
            Expression<Func<TSource, string>> sourceProperty)
            where TSource : class
        {
            return textBlock.Bind(t => t.Text, source, sourceProperty);
        }

        /// <summary>Binds a label to a non-string source with a formatter -- counts, sizes, percentages.</summary>
        public static TextBlock BindText<TSource, TValue>(
            this TextBlock textBlock,
            TSource source,
            Expression<Func<TSource, TValue>> sourceProperty,
            Func<TValue, string> format)
            where TSource : class
        {
            return textBlock.Bind(t => t.Text, source, sourceProperty, format);
        }

        /// <summary>Binds a label to an expression re-read every frame, formatted for display.</summary>
        public static TextBlock BindTextPolled<TValue>(
            this TextBlock textBlock,
            Func<TValue> read,
            Func<TValue, string> format)
        {
            return textBlock.BindPolled(t => t.Text, () => format(read()));
        }

        public static TextBlock BindTextPolled(this TextBlock textBlock, Func<string> read)
        {
            return textBlock.BindPolled(t => t.Text, read);
        }
    }
}

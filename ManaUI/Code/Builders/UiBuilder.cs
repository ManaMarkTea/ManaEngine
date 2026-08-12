using System.Windows.Input;

namespace ManaUI
{
    /// <summary>
    /// The fluent declaration surface.
    /// <para>
    /// Chosen over XAML so a layout is ordinary C#: compile-checked, rename-safe, steppable in a debugger,
    /// and able to use loops and conditionals directly rather than through a markup dialect. Every method
    /// returns the element it was called on, so a tree reads as one expression.
    /// </para>
    /// </summary>
    public static class UiBuilder
    {
        // ----- identity ------------------------------------------------------------------------------

        public static T Named<T>(this T element, string name) where T : UiElement
        {
            element.Name = name;
            return element;
        }

        public static T Tagged<T>(this T element, object? tag) where T : UiElement
        {
            element.Tag = tag;
            return element;
        }

        /// <summary>Escape hatch for anything the fluent surface does not cover.</summary>
        public static T Configure<T>(this T element, Action<T> configure) where T : UiElement
        {
            configure(element);
            return element;
        }

        // ----- sizing --------------------------------------------------------------------------------

        public static T Width<T>(this T element, float width) where T : UiElement
        {
            element.Width = width;
            return element;
        }

        public static T Height<T>(this T element, float height) where T : UiElement
        {
            element.Height = height;
            return element;
        }

        public static T Size<T>(this T element, float width, float height) where T : UiElement
        {
            element.Width = width;
            element.Height = height;
            return element;
        }

        public static T MinSize<T>(this T element, float minWidth, float minHeight) where T : UiElement
        {
            element.MinWidth = minWidth;
            element.MinHeight = minHeight;
            return element;
        }

        public static T MaxSize<T>(this T element, float maxWidth, float maxHeight) where T : UiElement
        {
            element.MaxWidth = maxWidth;
            element.MaxHeight = maxHeight;
            return element;
        }

        // ----- spacing -------------------------------------------------------------------------------

        public static T Margin<T>(this T element, float uniform) where T : UiElement
        {
            element.Margin = new Thickness(uniform);
            return element;
        }

        public static T Margin<T>(this T element, float horizontal, float vertical) where T : UiElement
        {
            element.Margin = new Thickness(horizontal, vertical);
            return element;
        }

        public static T Margin<T>(this T element, float left, float top, float right, float bottom)
            where T : UiElement
        {
            element.Margin = new Thickness(left, top, right, bottom);
            return element;
        }

        public static TPanel Padding<TPanel>(this TPanel panel, float uniform) where TPanel : Panel
        {
            panel.Padding = new Thickness(uniform);
            return panel;
        }

        public static TPanel Padding<TPanel>(this TPanel panel, float horizontal, float vertical)
            where TPanel : Panel
        {
            panel.Padding = new Thickness(horizontal, vertical);
            return panel;
        }

        public static TDecorator PaddingOf<TDecorator>(this TDecorator decorator, float uniform)
            where TDecorator : Decorator
        {
            decorator.Padding = new Thickness(uniform);
            return decorator;
        }

        public static TDecorator PaddingOf<TDecorator>(this TDecorator decorator, float horizontal, float vertical)
            where TDecorator : Decorator
        {
            decorator.Padding = new Thickness(horizontal, vertical);
            return decorator;
        }

        // ----- alignment -----------------------------------------------------------------------------

        public static T Left<T>(this T element) where T : UiElement
        {
            element.HorizontalAlignment = HorizontalAlignment.Left;
            return element;
        }

        public static T Right<T>(this T element) where T : UiElement
        {
            element.HorizontalAlignment = HorizontalAlignment.Right;
            return element;
        }

        public static T CenterH<T>(this T element) where T : UiElement
        {
            element.HorizontalAlignment = HorizontalAlignment.Center;
            return element;
        }

        public static T StretchH<T>(this T element) where T : UiElement
        {
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            return element;
        }

        public static T Top<T>(this T element) where T : UiElement
        {
            element.VerticalAlignment = VerticalAlignment.Top;
            return element;
        }

        public static T Bottom<T>(this T element) where T : UiElement
        {
            element.VerticalAlignment = VerticalAlignment.Bottom;
            return element;
        }

        public static T CenterV<T>(this T element) where T : UiElement
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            return element;
        }

        public static T StretchV<T>(this T element) where T : UiElement
        {
            element.VerticalAlignment = VerticalAlignment.Stretch;
            return element;
        }

        public static T Center<T>(this T element) where T : UiElement
        {
            element.HorizontalAlignment = HorizontalAlignment.Center;
            element.VerticalAlignment = VerticalAlignment.Center;
            return element;
        }

        // ----- state ---------------------------------------------------------------------------------

        public static T Enabled<T>(this T element, bool isEnabled) where T : UiElement
        {
            element.IsEnabled = isEnabled;
            return element;
        }

        public static T Visible<T>(this T element, bool isVisible) where T : UiElement
        {
            element.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            return element;
        }

        public static T Opacity<T>(this T element, float opacity) where T : UiElement
        {
            element.Opacity = opacity;
            return element;
        }

        public static T Clip<T>(this T element, bool clip = true) where T : UiElement
        {
            element.ClipToBounds = clip;
            return element;
        }

        public static T Focusable<T>(this T element, bool focusable = true) where T : UiElement
        {
            element.Focusable = focusable;
            return element;
        }

        // ----- panel-specific placement ----------------------------------------------------------------

        public static T GridCell<T>(this T element, int row, int column) where T : UiElement
        {
            var slot = element.EnsureLayoutSlot();
            slot.GridRow = row;
            slot.GridColumn = column;
            return element;
        }

        public static T GridRow<T>(this T element, int row, int span = 1) where T : UiElement
        {
            var slot = element.EnsureLayoutSlot();
            slot.GridRow = row;
            slot.GridRowSpan = span;
            return element;
        }

        public static T GridColumn<T>(this T element, int column, int span = 1) where T : UiElement
        {
            var slot = element.EnsureLayoutSlot();
            slot.GridColumn = column;
            slot.GridColumnSpan = span;
            return element;
        }

        public static T DockTo<T>(this T element, Dock dock) where T : UiElement
        {
            element.EnsureLayoutSlot().Dock = dock;
            return element;
        }

        /// <summary>Absolute placement inside a Canvas.</summary>
        public static T At<T>(this T element, float left, float top) where T : UiElement
        {
            var slot = element.EnsureLayoutSlot();
            slot.CanvasLeft = left;
            slot.CanvasTop = top;
            return element;
        }

        public static T Anchor<T>(
            this T element,
            float? left = null,
            float? top = null,
            float? right = null,
            float? bottom = null)
            where T : UiElement
        {
            var slot = element.EnsureLayoutSlot();
            slot.CanvasLeft = left ?? float.NaN;
            slot.CanvasTop = top ?? float.NaN;
            slot.CanvasRight = right ?? float.NaN;
            slot.CanvasBottom = bottom ?? float.NaN;
            return element;
        }

        public static T ZIndex<T>(this T element, int zIndex) where T : UiElement
        {
            element.EnsureLayoutSlot().ZIndex = zIndex;
            (element.Parent as Panel)?.Children.InvalidatePaintOrder();
            return element;
        }

        // ----- composition ---------------------------------------------------------------------------

        public static TPanel Children<TPanel>(this TPanel panel, params UiElement[] children) where TPanel : Panel
        {
            panel.Children.AddRange(children);
            return panel;
        }

        public static TPanel Children<TPanel>(this TPanel panel, IEnumerable<UiElement> children)
            where TPanel : Panel
        {
            panel.Children.AddRange(children);
            return panel;
        }

        public static TDecorator Content<TDecorator>(this TDecorator decorator, UiElement child)
            where TDecorator : Decorator
        {
            decorator.Child = child;
            return decorator;
        }

        // ----- panel configuration ---------------------------------------------------------------------

        public static StackPanel Vertical(this StackPanel panel, float spacing = 0.0f)
        {
            panel.Orientation = Orientation.Vertical;
            panel.Spacing = spacing;
            return panel;
        }

        public static StackPanel Horizontal(this StackPanel panel, float spacing = 0.0f)
        {
            panel.Orientation = Orientation.Horizontal;
            panel.Spacing = spacing;
            return panel;
        }

        public static TPanel Background<TPanel>(this TPanel panel, Vector4 color) where TPanel : Panel
        {
            panel.Background = color;
            return panel;
        }

        /// <summary>Defines columns from a XAML-style list, e.g. "Auto, *, 2*, 120".</summary>
        public static Grid Columns(this Grid grid, string definition, float spacing = 0.0f)
        {
            grid.SetColumns(definition);
            grid.ColumnSpacing = spacing;
            return grid;
        }

        public static Grid Rows(this Grid grid, string definition, float spacing = 0.0f)
        {
            grid.SetRows(definition);
            grid.RowSpacing = spacing;
            return grid;
        }

        public static DockPanel FillLast(this DockPanel panel, bool fill = true)
        {
            panel.LastChildFill = fill;
            return panel;
        }

        // ----- Border --------------------------------------------------------------------------------

        public static Border Fill(this Border border, Vector4 color)
        {
            border.Background = color;
            return border;
        }

        public static Border Outline(this Border border, Vector4 color, float thickness = 1.0f)
        {
            border.BorderColor = color;
            border.BorderThickness = thickness;
            return border;
        }

        public static Border Rounded(this Border border, float radius)
        {
            border.CornerRadius = new CornerRadius(radius);
            return border;
        }

        public static Border Rounded(this Border border, CornerRadius radius)
        {
            border.CornerRadius = radius;
            return border;
        }

        public static Border Shadow(this Border border, float blur, Vector4? color = null)
        {
            border.ShadowBlur = blur;
            if (color.HasValue)
            {
                border.ShadowColor = color.Value;
            }

            return border;
        }

        // ----- TextBlock -----------------------------------------------------------------------------

        public static TextBlock Text(this TextBlock textBlock, string text)
        {
            textBlock.Text = text;
            return textBlock;
        }

        public static TextBlock Foreground(this TextBlock textBlock, Vector4 color)
        {
            textBlock.Foreground = color;
            return textBlock;
        }

        public static TextBlock FontSize(this TextBlock textBlock, float size)
        {
            textBlock.FontSize = size;
            return textBlock;
        }

        public static TextBlock FontFamily(this TextBlock textBlock, string family)
        {
            textBlock.FontFamily = family;
            return textBlock;
        }

        public static TextBlock Wrap(this TextBlock textBlock, bool wrap = true)
        {
            textBlock.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            return textBlock;
        }

        public static TextBlock Ellipsis(this TextBlock textBlock, bool trim = true)
        {
            textBlock.TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None;
            return textBlock;
        }

        public static TextBlock Align(this TextBlock textBlock, TextAlignment alignment)
        {
            textBlock.TextAlignment = alignment;
            return textBlock;
        }

        // ----- Button --------------------------------------------------------------------------------

        public static Button Label(this Button button, string text)
        {
            button.Text = text;
            return button;
        }

        public static Button Variant(this Button button, ButtonVariant variant)
        {
            button.Variant = variant;
            return button;
        }

        public static Button OnClick(this Button button, Action action)
        {
            button.Click += (_, _) => action();
            return button;
        }

        public static Button Command(this Button button, ICommand command, object? parameter = null)
        {
            button.CommandParameter = parameter;
            button.Command = command;
            return button;
        }

        // ----- CheckBox ------------------------------------------------------------------------------

        public static CheckBox Label(this CheckBox checkBox, string text)
        {
            checkBox.Text = text;
            return checkBox;
        }

        public static CheckBox Checked(this CheckBox checkBox, bool isChecked = true)
        {
            checkBox.IsChecked = isChecked;
            return checkBox;
        }

        public static CheckBox OnChanged(this CheckBox checkBox, Action<bool> action)
        {
            checkBox.CheckedChanged += (_, value) => action(value);
            return checkBox;
        }

        // ----- ComboBox ------------------------------------------------------------------------------

        public static ComboBox Options<T>(
            this ComboBox comboBox,
            IEnumerable<T> items,
            Func<T, string>? display = null)
        {
            comboBox.SetItems(items, display);
            return comboBox;
        }

        public static ComboBox Selected(this ComboBox comboBox, int index)
        {
            comboBox.SelectedIndex = index;
            return comboBox;
        }

        public static ComboBox OnSelected(this ComboBox comboBox, Action<int> action)
        {
            comboBox.SelectionChanged += (_, index) => action(index);
            return comboBox;
        }

        // ----- TextBox -------------------------------------------------------------------------------

        public static TextBox Value(this TextBox textBox, string text)
        {
            textBox.Text = text;
            return textBox;
        }

        public static TextBox Hint(this TextBox textBox, string placeholder)
        {
            textBox.Placeholder = placeholder;
            return textBox;
        }

        public static TextBox ReadOnly(this TextBox textBox, bool readOnly = true)
        {
            textBox.IsReadOnly = readOnly;
            return textBox;
        }

        /// <summary>Restricts input to digits, sign and decimal point.</summary>
        public static TextBox Numeric(this TextBox textBox)
        {
            textBox.CharacterFilter = c => char.IsDigit(c) || c == '.' || c == '-';
            return textBox;
        }

        public static TextBox OnCommitted(this TextBox textBox, Action<string> action)
        {
            textBox.TextCommitted += (_, value) => action(value);
            return textBox;
        }

        // ----- NumericBox ----------------------------------------------------------------------------

        public static NumericBox Value(this NumericBox box, float value)
        {
            box.Value = value;
            return box;
        }

        /// <summary>Drag sensitivity per pixel, which doubles as the snap and arrow-key increment.</summary>
        public static NumericBox Step(this NumericBox box, float step)
        {
            box.Step = step;
            return box;
        }

        public static NumericBox Range(this NumericBox box, float minimum, float maximum)
        {
            box.Minimum = minimum;
            box.Maximum = maximum;
            return box;
        }

        public static NumericBox Places(this NumericBox box, int decimalPlaces)
        {
            box.DecimalPlaces = decimalPlaces;
            return box;
        }

        public static NumericBox Integer(this NumericBox box, bool isInteger = true)
        {
            box.IsInteger = isInteger;
            box.Step = box.Step < 1.0f ? 1.0f : box.Step;
            return box;
        }

        public static NumericBox OnChanged(this NumericBox box, Action<float> action)
        {
            box.ValueChanged += (_, value) => action(value);
            return box;
        }

        /// <summary>Fires only when an edit settles -- for work too expensive to run per drag pixel.</summary>
        public static NumericBox OnCommitted(this NumericBox box, Action<float> action)
        {
            box.ValueCommitted += (_, value) => action(value);
            return box;
        }

        // ----- VectorBox -----------------------------------------------------------------------------

        public static VectorBox Value(this VectorBox box, Vector3 value)
        {
            box.Value = value;
            return box;
        }

        public static VectorBox Components(this VectorBox box, int components)
        {
            box.Components = components;
            return box;
        }

        public static VectorBox Step(this VectorBox box, float step)
        {
            box.Step = step;
            return box;
        }

        public static VectorBox Places(this VectorBox box, int decimalPlaces)
        {
            box.DecimalPlaces = decimalPlaces;
            return box;
        }

        public static VectorBox OnChanged(this VectorBox box, Action<Vector3> action)
        {
            box.ValueChanged += (_, value) => action(new Vector3(value.X, value.Y, value.Z));
            return box;
        }

        // ----- ColorField / ColorPicker ---------------------------------------------------------------

        public static ColorField Value(this ColorField field, Vector4 color)
        {
            field.Color = color;
            return field;
        }

        public static ColorField WithAlpha(this ColorField field, bool showAlpha = true)
        {
            field.ShowAlpha = showAlpha;
            return field;
        }

        /// <summary>Drops the hex caption, leaving a bare swatch for a tight column.</summary>
        public static ColorField SwatchOnly(this ColorField field)
        {
            field.ShowHexLabel = false;
            return field;
        }

        public static ColorField OnChanged(this ColorField field, Action<Vector4> action)
        {
            field.ColorChanged += (_, color) => action(color);
            return field;
        }

        /// <summary>Fires only when an edit settles -- for work too expensive to run per drag pixel.</summary>
        public static ColorField OnCommitted(this ColorField field, Action<Vector4> action)
        {
            field.ColorCommitted += (_, color) => action(color);
            return field;
        }

        public static ColorPicker Value(this ColorPicker picker, Vector4 color)
        {
            picker.Color = color;
            return picker;
        }

        public static ColorPicker OnChanged(this ColorPicker picker, Action<Vector4> action)
        {
            picker.ColorChanged += (_, color) => action(color);
            return picker;
        }

        // ----- Expander ------------------------------------------------------------------------------

        public static Expander Header(this Expander expander, string header)
        {
            expander.Header = header;
            return expander;
        }

        public static Expander Expanded(this Expander expander, bool isExpanded = true)
        {
            expander.IsExpanded = isExpanded;
            return expander;
        }

        /// <summary>Wraps the children in a vertical stack, which is what a section almost always wants.</summary>
        public static Expander Section(this Expander expander, float spacing, params UiElement[] children)
        {
            expander.Content = new StackPanel().Vertical(spacing).Children(children);
            return expander;
        }

        public static Expander Content(this Expander expander, UiElement content)
        {
            expander.Content = content;
            return expander;
        }

        public static Expander Accessory(this Expander expander, UiElement accessory)
        {
            expander.HeaderAccessory = accessory;
            return expander;
        }

        // ----- DynamicContent ------------------------------------------------------------------------

        /// <summary>Sets the watched source and its template together, typed to save the cast at the use site.</summary>
        public static DynamicContent Showing<T>(
            this DynamicContent content,
            Func<T?> source,
            Func<T, UiElement?> template)
            where T : class
        {
            content.Template = value => value is T typed ? template(typed) : null;
            content.Source = () => source();
            return content;
        }

        public static DynamicContent Empty(this DynamicContent content, UiElement placeholder)
        {
            content.Placeholder = placeholder;

            // Re-run so an empty start shows the placeholder rather than nothing at all.
            content.Refresh();
            return content;
        }

        // ----- TreeView ------------------------------------------------------------------------------

        /// <summary>Sets the roots, the children accessor and the row template together.</summary>
        public static TreeView Tree<T>(
            this TreeView tree,
            IEnumerable<T> roots,
            Func<T, IEnumerable<T>?> children,
            Func<T, UiElement> template)
            where T : class
        {
            tree.ChildrenSelector = item => children((T)item);
            tree.ItemTemplate = item => template((T)item);
            tree.ItemsSource = (System.Collections.IEnumerable)roots;
            return tree;
        }

        /// <summary>
        /// A tree over a model with mixed item types, where children and template must handle each kind.
        /// </summary>
        public static TreeView Tree(
            this TreeView tree,
            System.Collections.IEnumerable roots,
            Func<object, System.Collections.IEnumerable?> children,
            Func<object, UiElement> template)
        {
            tree.ChildrenSelector = children;
            tree.ItemTemplate = template;
            tree.ItemsSource = roots;
            return tree;
        }

        /// <summary>Rebuilds the tree whenever this cheap signature changes -- for models with no events.</summary>
        public static TreeView RebuildWhen(this TreeView tree, Func<int> signature)
        {
            tree.StructureSignature = signature;
            return tree;
        }

        public static TreeView OnSelected(this TreeView tree, Action<object?> action)
        {
            tree.SelectionChanged += (_, item) => action(item);
            return tree;
        }

        public static TreeView OnActivated(this TreeView tree, Action<object> action)
        {
            tree.ItemActivated += (_, item) => action(item);
            return tree;
        }

        // ----- ScrollViewer ---------------------------------------------------------------------------

        /// <summary>Wraps an element in a vertically scrolling viewport.</summary>
        public static ScrollViewer Scrollable(this UiElement content)
        {
            return new ScrollViewer { Child = content };
        }

        public static ScrollViewer Scrolls(
            this ScrollViewer viewer,
            ScrollBarVisibility vertical = ScrollBarVisibility.Auto,
            ScrollBarVisibility horizontal = ScrollBarVisibility.Disabled)
        {
            viewer.VerticalScrollBar = vertical;
            viewer.HorizontalScrollBar = horizontal;
            return viewer;
        }

        // ----- ItemsControl --------------------------------------------------------------------------

        /// <summary>Sets the source and the per-item template together, which is how they are always used.</summary>
        public static ItemsControl Items<T>(
            this ItemsControl items,
            IEnumerable<T> source,
            Func<T, UiElement> template)
        {
            items.SetItemTemplate(template);
            items.ItemsSource = (System.Collections.IEnumerable)source;
            return items;
        }

        public static ItemsControl Spacing(this ItemsControl items, float spacing)
        {
            items.Spacing = spacing;
            return items;
        }

        public static ItemsControl Horizontal(this ItemsControl items)
        {
            items.Orientation = Orientation.Horizontal;
            return items;
        }

        public static ItemsControl WhenEmpty(this ItemsControl items, UiElement placeholder)
        {
            items.EmptyPlaceholder = placeholder;
            return items;
        }

        // ----- Slider --------------------------------------------------------------------------------

        public static Slider Range(this Slider slider, float minimum, float maximum, float step = 0.0f)
        {
            slider.Minimum = minimum;
            slider.Maximum = maximum;
            slider.Step = step;
            return slider;
        }

        public static Slider Value(this Slider slider, float value)
        {
            slider.Value = value;
            return slider;
        }

        public static Slider OnChanged(this Slider slider, Action<float> action)
        {
            slider.ValueChanged += (_, value) => action(value);
            return slider;
        }
    }
}

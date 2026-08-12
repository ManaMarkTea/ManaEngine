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

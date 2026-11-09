using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// The pieces every widget builds out of: a labelled row, and editors bound straight to the engine.
    /// <para>
    /// The editors poll as well as write. Nothing on the engine side raises a change notification -- a
    /// gizmo drag, an animation or a script moves a node without telling anyone -- and an inspector showing
    /// a stale value is worse than one showing none.
    /// </para>
    /// </summary>
    public static class InspectorControls
    {
        /// <summary>
        /// A labelled property row.
        /// <para>
        /// The name column is a fixed width shared by the whole panel, so labels line up and narrowing the
        /// dock takes width from the editor rather than from the name -- the name being the half you
        /// cannot work without. A divider between the two moves that width, because how much room a name
        /// needs depends entirely on what is being inspected.
        /// </para>
        /// </summary>
        public static UiElement Row(InspectorContext context, string label, UiElement editor)
        {
            return Row(context, label, editor, null, null);
        }

        /// <summary>A labelled row whose name carries an explanation on hover.</summary>
        public static UiElement Row(InspectorContext context, string label, UiElement editor, string? description)
        {
            return Row(context, label, editor, null, description);
        }

        /// <summary>A row with something after the editor -- a texture slot, typically.</summary>
        public static UiElement Row(
            InspectorContext context,
            string label,
            UiElement editor,
            UiElement? trailing,
            string? description = null)
        {
            var theme = context.Theme;
            var metrics = context.Metrics;

            var name = Label(theme, label, description);

            // Polled rather than set once: the divider writes to the shared width, and every row has to
            // follow it without the panel being rebuilt.
            name.BindPolled(l => l.Width, () => metrics.LabelWidth);

            var columns = trailing == null
                ? $"Auto, {InspectorSplitter.Thickness}, *"
                : $"Auto, {InspectorSplitter.Thickness}, *, Auto";

            var grid = new Grid().Columns(columns, 0.0f).Rows("Auto").Children(
                name.GridColumn(0),
                new InspectorSplitter(metrics).GridColumn(1),
                editor.Margin(theme.Spacing, 0.0f, 0.0f, 0.0f).GridColumn(2));

            if (trailing != null)
            {
                grid.Children.Add(trailing.Margin(theme.Spacing, 0.0f, 0.0f, 0.0f).GridColumn(3));
            }

            grid.Tag = new InspectorPropertySearch.PropertyLabel(label);
            return grid;
        }

        public static UiElement ReadOnly(UiTheme theme, string value)
        {
            return new TextBlock().Text(value).Foreground(theme.Text).Ellipsis().CenterV();
        }

        /// <summary>
        /// A ranged scalar: a slider with the number beside it. The range is soft, so a value typed past
        /// either end widens the slider rather than being clamped -- an emissive strength offering 0..25
        /// still accepts 200 and rescales around it.
        /// </summary>
        public static SliderBox Scalar(Func<float> read, Action<float> write, float minimum, float maximum)
        {
            var slider = new SliderBox
            {
                Minimum = minimum,
                Maximum = maximum,
                Step = (maximum - minimum) / 200.0f,
                DecimalPlaces = 3
            };

            slider.Value = read();
            slider.ValueChanged += (_, value) =>
            {
                if (slider.IsApplyingBinding)
                {
                    return;
                }

                write(value);
            };

            slider.BindPolled(s => s.Value, read);
            return slider;
        }

        /// <summary>A colour swatch that opens a picker.</summary>
        public static ColorField Colour(Func<Vector4> read, Action<Vector4> write)
        {
            var field = new ColorField { Color = read() };
            field.ColorChanged += (_, value) =>
            {
                if (field.IsApplyingBinding)
                {
                    return;
                }

                write(value);
            };

            field.BindPolled(f => f.Color, read);
            return field;
        }

        public static VectorBox Vector(Func<Vector3> read, Action<Vector3> write, float step, int places = 3)
        {
            var box = new VectorBox().Step(step).Places(places);
            box.Value = read();
            box.ValueChanged += (_, value) =>
            {
                // Nothing to write: the value arrived from the model a moment ago. Writing anyway would
                // round-trip it through the box's display precision -- quantising a node's position to
                // three decimals just because it was selected -- and re-run whatever the write triggers.
                if (box.IsApplyingBinding)
                {
                    return;
                }

                write(new Vector3(value.X, value.Y, value.Z));
            };

            box.BindPolled(b => b.Value, read);
            return box;
        }

        /// <summary>
        /// A property's name. Always carries the full text on hover: the column is fixed, so a long name
        /// is ellipsised, and an inspector where you cannot read what you are editing is no use.
        /// </summary>
        public static VectorBox Vector2(Func<Vector2> read, Action<Vector2> write, float step, int places = 3)
        {
            var box = new VectorBox().Components(2).Step(step).Places(places);
            box.Value2 = read();
            box.ValueChanged += (_, value) =>
            {
                if (box.IsApplyingBinding)
                {
                    return;
                }

                write(new Vector2(value.X, value.Y));
            };

            box.BindPolled(b => b.Value2, read);
            return box;
        }

        private static TextBlock Label(UiTheme theme, string text, string? description)
        {
            var tooltip = string.IsNullOrWhiteSpace(description)
                ? text
                : text + Environment.NewLine + Environment.NewLine + description;

            return new TextBlock()
                .Text(text)
                .Foreground(theme.TextMuted)
                .Ellipsis()
                .CenterV()
                .WithTooltip(tooltip);
        }
    }
}

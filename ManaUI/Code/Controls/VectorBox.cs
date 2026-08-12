namespace ManaUI
{
    /// <summary>
    /// Two to four <see cref="NumericBox"/> fields on one row, labelled X/Y/Z/W -- the transform editor the
    /// inspector is mostly made of.
    /// <para>
    /// A composite rather than a control: it owns no visuals of its own beyond the axis letters, and every
    /// gesture belongs to the field the pointer is over. That keeps drag-scrubbing, caret editing and focus
    /// behaving exactly as they do for a lone numeric field.
    /// </para>
    /// </summary>
    public class VectorBox : Decorator
    {
        private static readonly string[] AxisLabels = { "X", "Y", "Z", "W" };

        private readonly NumericBox[] _fields = new NumericBox[4];
        private readonly TextBlock[] _labels = new TextBlock[4];
        private readonly Grid _grid = new();

        private Vector4 _vector;
        private int _components = 3;

        public VectorBox()
        {
            for (var i = 0; i < _fields.Length; i++)
            {
                var index = i;

                _labels[i] = new TextBlock()
                    .Text(AxisLabels[i])
                    .CenterV();

                _fields[i] = new NumericBox();
                _fields[i].ValueChanged += (_, value) => OnFieldChanged(index, value);
                _fields[i].ValueCommitted += (_, _) => ValueCommitted?.Invoke(this, _vector);
            }

            Child = _grid;
            RebuildColumns();
        }

        /// <summary>Fires continuously while a component is dragged.</summary>
        public event EventHandler<Vector4>? ValueChanged;

        /// <summary>Fires when an edit settles -- drag release, Enter, or focus loss.</summary>
        public event EventHandler<Vector4>? ValueCommitted;

        /// <summary>How many components to show, 2 to 4.</summary>
        public int Components
        {
            get => _components;
            set
            {
                var clamped = Math.Clamp(value, 2, 4);
                if (_components == clamped)
                {
                    return;
                }

                _components = clamped;
                RebuildColumns();
            }
        }

        /// <summary>All four components regardless of how many are shown; the hidden ones keep their value.</summary>
        public Vector4 Vector
        {
            get => _vector;
            set => SetVector(value);
        }

        /// <summary>The common case -- position, rotation, scale. Leaves W untouched.</summary>
        public Vector3 Value
        {
            get => new(_vector.X, _vector.Y, _vector.Z);
            set => SetVector(new Vector4(value.X, value.Y, value.Z, _vector.W));
        }

        public Vector2 Value2
        {
            get => new(_vector.X, _vector.Y);
            set => SetVector(new Vector4(value.X, value.Y, _vector.Z, _vector.W));
        }

        /// <summary>Gap between the axis groups.</summary>
        public float Spacing
        {
            get => _grid.ColumnSpacing;
            set => _grid.ColumnSpacing = value;
        }

        /// <summary>Whether the X/Y/Z letters are shown. Off makes a compact field for tight columns.</summary>
        public bool ShowLabels { get; set; } = true;

        // ----- forwarded field settings ----------------------------------------------------------------

        public float Step
        {
            get => _fields[0].Step;
            set => ForEachField(box => box.Step = value);
        }

        public int DecimalPlaces
        {
            get => _fields[0].DecimalPlaces;
            set => ForEachField(box => box.DecimalPlaces = value);
        }

        public bool IsInteger
        {
            get => _fields[0].IsInteger;
            set => ForEachField(box => box.IsInteger = value);
        }

        public float Minimum
        {
            get => _fields[0].Minimum;
            set => ForEachField(box => box.Minimum = value);
        }

        public float Maximum
        {
            get => _fields[0].Maximum;
            set => ForEachField(box => box.Maximum = value);
        }

        public bool IsReadOnly
        {
            get => _fields[0].IsReadOnly;
            set => ForEachField(box => box.IsReadOnly = value);
        }

        /// <summary>The field for one component, for anything that needs to configure a single axis.</summary>
        public NumericBox Field(int index)
        {
            return _fields[Math.Clamp(index, 0, _fields.Length - 1)];
        }

        // ----- internals -------------------------------------------------------------------------------

        private void ForEachField(Action<NumericBox> apply)
        {
            foreach (var field in _fields)
            {
                apply(field);
            }
        }

        private void SetVector(Vector4 value)
        {
            if (_vector == value)
            {
                return;
            }

            _vector = value;
            PushToFields();

            // Read back rather than trusting what was written. A field can coerce -- clamp to its range,
            // round to an integer -- and when the coerced result happens to equal what the field already
            // held it raises nothing, so events alone would leave the vector disagreeing with the display.
            _vector = new Vector4(
                _fields[0].Value,
                _fields[1].Value,
                _fields[2].Value,
                _fields[3].Value);

            ValueChanged?.Invoke(this, _vector);
        }

        /// <summary>
        /// Each write here re-enters <see cref="OnFieldChanged"/>, which is why <see cref="_vector"/> is
        /// already up to date before the first one: the equality check there sees no change and stops,
        /// without needing a re-entrancy flag.
        /// </summary>
        private void PushToFields()
        {
            _fields[0].Value = _vector.X;
            _fields[1].Value = _vector.Y;
            _fields[2].Value = _vector.Z;
            _fields[3].Value = _vector.W;
        }

        private void OnFieldChanged(int index, float value)
        {
            // The argument is what the field settled on, already clamped and rounded. Taking it even during
            // a push is deliberate: an integer field handed 3.7 shows 4, and the vector has to agree with
            // what is on screen rather than with what was asked for.
            var updated = _vector;
            switch (index)
            {
                case 0:
                    updated.X = value;
                    break;
                case 1:
                    updated.Y = value;
                    break;
                case 2:
                    updated.Z = value;
                    break;
                default:
                    updated.W = value;
                    break;
            }

            if (updated == _vector)
            {
                return;
            }

            _vector = updated;
            ValueChanged?.Invoke(this, _vector);
        }

        /// <summary>
        /// Rebuilt rather than hidden: a collapsed element still occupies a column definition, and an
        /// unused star column would steal width from the components that are shown.
        /// </summary>
        private void RebuildColumns()
        {
            _grid.Children.Clear();

            var definition = string.Join(", ", Enumerable.Repeat(ShowLabels ? "Auto, *" : "*", _components));
            _grid.SetColumns(definition);
            _grid.SetRows("Auto");

            var column = 0;
            for (var i = 0; i < _components; i++)
            {
                if (ShowLabels)
                {
                    _grid.Children.Add(_labels[i].GridColumn(column++));
                }

                _grid.Children.Add(_fields[i].GridColumn(column++));
            }

            InvalidateMeasure();
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            base.OnRender(drawList);
            ApplyAxisColors();
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            ApplyAxisColors();
            return base.MeasureOverride(availableSize);
        }

        /// <summary>
        /// Applied late because the theme is only reachable once the element is in a tree, and a VectorBox
        /// is usually built before it is added to one.
        /// </summary>
        private void ApplyAxisColors()
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            for (var i = 0; i < _labels.Length; i++)
            {
                _labels[i].Foreground = theme.AxisColor(i);
                _labels[i].Margin = new Thickness(i == 0 ? 0.0f : 2.0f, 0.0f, 4.0f, 0.0f);
            }
        }
    }
}

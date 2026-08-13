namespace ManaUI
{
    /// <summary>
    /// A slider with a number beside it, for a value that has a range worth seeing.
    /// <para>
    /// The alternative -- <see cref="NumericBox"/>'s drag strip -- asks people to learn a gesture. A slider
    /// asks nothing: the thumb is where the value is, and dragging it does what it looks like. The field
    /// beside it is there for the times you know the number you want, and for reading it exactly.
    /// </para>
    /// <para>
    /// The range is soft. Typing a value past either end widens the range to include it rather than
    /// clamping, so a control offering 0..100 still accepts 1000 and rescales around it. Set
    /// <see cref="Minimum"/> and <see cref="Maximum"/> for limits that genuinely cannot be passed.
    /// </para>
    /// </summary>
    public class SliderBox : Decorator
    {
        private readonly Slider _slider = new();
        private readonly NumericBox _box = new();
        private readonly Grid _grid = new();

        private bool _syncing;

        public SliderBox()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Top;

            // No drag handle on the field: the slider is the drag, and offering two ways to scrub the same
            // number in one row is how a control ends up feeling fiddly.
            _box.DragHandle = NumericDragHandle.None;
            _box.MinimumFieldHeight = 0.0f;
            _box.Padding = new Thickness(6.0f, 1.0f);
            _box.SoftMinimum = 0.0f;
            _box.SoftMaximum = 1.0f;

            _slider.ValueChanged += (_, value) =>
            {
                if (_syncing)
                {
                    return;
                }

                _syncing = true;
                _box.Value = value;
                _syncing = false;
                Raise();
            };

            _box.ValueChanged += (_, value) =>
            {
                if (_syncing)
                {
                    return;
                }

                _syncing = true;
                PushToSlider(value);
                _syncing = false;
                Raise();
            };

            _box.ValueCommitted += (_, value) => ValueCommitted?.Invoke(this, value);

            // The field widens its own soft range when something outside it is typed, and the slider has to
            // rescale to match or the thumb would sit pinned at an end.
            _box.SoftRangeChanged += (_, _) => SyncSliderRange();

            _grid.SetRows("Auto");
            _grid.ColumnSpacing = 6.0f;
            _grid.SetColumns($"*, {FieldWidth}");
            _grid.Children.Add(_slider.GridColumn(0));
            _grid.Children.Add(_box.GridColumn(1));

            Child = _grid;
            SyncSliderRange();
        }

        /// <summary>Width given to the number. Wide enough for a signed value at three decimals.</summary>
        private const float FieldWidth = 64.0f;

        /// <summary>Fires continuously while either half moves.</summary>
        public event EventHandler<float>? ValueChanged;

        /// <summary>Fires when an edit settles -- Enter, or focus loss on the field.</summary>
        public event EventHandler<float>? ValueCommitted;

        public float Value
        {
            get => _box.Value;
            set
            {
                if (LayoutHelper.AreClose(_box.Value, value))
                {
                    return;
                }

                _syncing = true;
                _box.Value = value;
                PushToSlider(_box.Value);
                _syncing = false;
                Raise();
            }
        }

        /// <summary>Lower end of the slider. Soft: typing past it widens the range.</summary>
        public float Minimum
        {
            get => _box.SoftMinimum;
            set
            {
                _box.SoftMinimum = value;
                SyncSliderRange();
            }
        }

        /// <summary>Upper end of the slider. Soft: typing past it widens the range.</summary>
        public float Maximum
        {
            get => _box.SoftMaximum;
            set
            {
                _box.SoftMaximum = value;
                SyncSliderRange();
            }
        }

        /// <summary>A limit the value can never pass, whatever is typed. Unbounded by default.</summary>
        public float HardMinimum
        {
            get => _box.Minimum;
            set => _box.Minimum = value;
        }

        /// <summary>A limit the value can never pass, whatever is typed. Unbounded by default.</summary>
        public float HardMaximum
        {
            get => _box.Maximum;
            set => _box.Maximum = value;
        }

        /// <summary>Snap increment on both halves; zero is continuous.</summary>
        public float Step
        {
            get => _slider.Step;
            set
            {
                _slider.Step = value;
                _box.Step = value;
            }
        }

        public int DecimalPlaces
        {
            get => _box.DecimalPlaces;
            set => _box.DecimalPlaces = value;
        }

        public bool IsInteger
        {
            get => _box.IsInteger;
            set => _box.IsInteger = value;
        }

        /// <summary>The two halves, for anything needing to reach past this control's own surface.</summary>
        /// <summary>See <see cref="ManaUI.Slider.IsLogarithmic"/>.</summary>
        public bool IsLogarithmic
        {
            get => _slider.IsLogarithmic;
            set => _slider.IsLogarithmic = value;
        }

        public Slider Slider => _slider;

        public NumericBox Field => _box;

        private void PushToSlider(float value)
        {
            // Beyond the ends the thumb pins rather than lying about where the value is; the field is
            // still showing the true number, and the range grows on commit.
            _slider.Value = Math.Clamp(value, _slider.Minimum, _slider.Maximum);
        }

        private void SyncSliderRange()
        {
            var minimum = _box.EffectiveMinimum;
            var maximum = _box.EffectiveMaximum;

            // A slider needs two finite ends. Without them it would be a thumb with nowhere to travel, so
            // it falls back to the value's own neighbourhood rather than drawing something meaningless.
            if (float.IsInfinity(minimum) || float.IsInfinity(maximum) || maximum <= minimum)
            {
                minimum = MathF.Min(0.0f, _box.Value);
                maximum = MathF.Max(1.0f, _box.Value);
            }

            _syncing = true;
            _slider.Minimum = minimum;
            _slider.Maximum = maximum;
            _slider.Value = Math.Clamp(_box.Value, minimum, maximum);
            _syncing = false;

            InvalidateVisual();
        }

        private void Raise()
        {
            ValueChanged?.Invoke(this, _box.Value);
        }
    }
}

namespace ManaUI
{
    /// <summary>
    /// A colour editor: the spectrum, plus the numeric and hex fields people reach for when they know the
    /// value they want rather than wanting to hunt for it.
    /// <para>
    /// Every input edits one shared colour, so moving any of them updates the rest. The guard against that
    /// turning into a loop is a single flag rather than per-field bookkeeping -- there is one source of
    /// truth here, and everything else is a view of it.
    /// </para>
    /// </summary>
    public class ColorPicker : Decorator
    {
        private readonly ColorSpectrum _spectrum = new();
        private readonly NumericBox _red = Channel("R");
        private readonly NumericBox _green = Channel("G");
        private readonly NumericBox _blue = Channel("B");
        private readonly NumericBox _alpha = Channel("A");
        private readonly TextBox _hex = new() { MinimumFieldHeight = 0.0f, Padding = new Thickness(6.0f, 1.0f) };
        private readonly StackPanel _layout = new() { Orientation = Orientation.Vertical, Spacing = 6.0f };
        private readonly Grid _channels = new();

        private bool _syncing;

        public ColorPicker()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Top;

            _spectrum.ColorChanged += (_, _) => PushFromSpectrum();
            _spectrum.ColorCommitted += (_, color) => ColorCommitted?.Invoke(this, color);

            _red.ValueChanged += (_, _) => PushFromChannels();
            _green.ValueChanged += (_, _) => PushFromChannels();
            _blue.ValueChanged += (_, _) => PushFromChannels();
            _alpha.ValueChanged += (_, _) => PushFromChannels();

            _red.ValueCommitted += (_, _) => ColorCommitted?.Invoke(this, Color);
            _green.ValueCommitted += (_, _) => ColorCommitted?.Invoke(this, Color);
            _blue.ValueCommitted += (_, _) => ColorCommitted?.Invoke(this, Color);
            _alpha.ValueCommitted += (_, _) => ColorCommitted?.Invoke(this, Color);

            _hex.TextCommitted += (_, text) => PushFromHex(text);

            _channels.SetRows("Auto");
            _channels.ColumnSpacing = 4.0f;

            _layout.Children.Add(_spectrum);
            _layout.Children.Add(_channels);
            _layout.Children.Add(_hex);

            Child = _layout;
            RebuildChannels();
            SyncFromColor();
        }

        /// <summary>Fires continuously while any input moves.</summary>
        public event EventHandler<Vector4>? ColorChanged;

        /// <summary>Fires when an edit settles -- drag release, Enter, or focus loss.</summary>
        public event EventHandler<Vector4>? ColorCommitted;

        public Vector4 Color
        {
            get => _spectrum.Color;
            set
            {
                if (_spectrum.Color == value)
                {
                    return;
                }

                _spectrum.Color = value;
                SyncFromColor();
                ColorChanged?.Invoke(this, _spectrum.Color);
            }
        }

        /// <summary>Whether alpha is editable at all. Off hides both the strip and the A field.</summary>
        public bool ShowAlpha
        {
            get => _spectrum.ShowAlpha;
            set
            {
                if (_spectrum.ShowAlpha == value)
                {
                    return;
                }

                _spectrum.ShowAlpha = value;
                RebuildChannels();
                InvalidateMeasure();
            }
        }

        /// <summary>Height of the saturation/value square.</summary>
        public float SpectrumHeight
        {
            get => _spectrum.SquareHeight;
            set
            {
                _spectrum.SquareHeight = value;
                InvalidateMeasure();
            }
        }

        /// <summary>The spectrum itself, for anything that needs to reach past the picker's own surface.</summary>
        public ColorSpectrum Spectrum => _spectrum;

        // ----- internals -------------------------------------------------------------------------------

        /// <summary>
        /// Channels are 0..255 rather than 0..1. Editors talk in bytes, hex is bytes, and a field stepping
        /// in hundredths of a channel is finer than the colour can actually represent.
        /// </summary>
        private static NumericBox Channel(string label)
        {
            return new NumericBox
            {
                DragHandle = NumericDragHandle.Leading,
                ChipText = label,
                MinimumFieldHeight = 0.0f,
                Padding = new Thickness(4.0f, 1.0f),
                IsInteger = true,
                Step = 1.0f,
                Minimum = 0.0f,
                Maximum = 255.0f
            };
        }

        private void RebuildChannels()
        {
            _channels.Children.Clear();
            _channels.SetColumns(ShowAlpha ? "*, *, *, *" : "*, *, *");

            _channels.Children.Add(_red.GridColumn(0));
            _channels.Children.Add(_green.GridColumn(1));
            _channels.Children.Add(_blue.GridColumn(2));

            if (ShowAlpha)
            {
                _channels.Children.Add(_alpha.GridColumn(3));
            }
        }

        private void PushFromSpectrum()
        {
            if (_syncing)
            {
                return;
            }

            SyncFromColor();
            ColorChanged?.Invoke(this, _spectrum.Color);
        }

        private void PushFromChannels()
        {
            if (_syncing)
            {
                return;
            }

            _syncing = true;
            _spectrum.Color = new Vector4(
                _red.Value / 255.0f,
                _green.Value / 255.0f,
                _blue.Value / 255.0f,
                ShowAlpha ? _alpha.Value / 255.0f : 1.0f);
            _hex.Text = UiColor.ToHex(_spectrum.Color, ShowAlpha);
            _syncing = false;

            ColorChanged?.Invoke(this, _spectrum.Color);
        }

        private void PushFromHex(string text)
        {
            if (_syncing)
            {
                return;
            }

            Vector4 parsed;
            try
            {
                parsed = UiColor.FromHex(text);
            }
            catch (Exception)
            {
                // Unparseable text reverts rather than blanking the colour -- a half-typed "#3B" is a
                // legitimate intermediate state, not an instruction to become black.
                SyncFromColor();
                return;
            }

            _syncing = true;
            _spectrum.Color = parsed;
            SyncChannelsOnly();
            _syncing = false;

            ColorChanged?.Invoke(this, _spectrum.Color);
            ColorCommitted?.Invoke(this, _spectrum.Color);
        }

        private void SyncFromColor()
        {
            _syncing = true;
            SyncChannelsOnly();
            _hex.Text = UiColor.ToHex(_spectrum.Color, ShowAlpha);
            _syncing = false;
        }

        private void SyncChannelsOnly()
        {
            var color = _spectrum.Color;
            _red.Value = MathF.Round(color.X * 255.0f);
            _green.Value = MathF.Round(color.Y * 255.0f);
            _blue.Value = MathF.Round(color.Z * 255.0f);
            _alpha.Value = MathF.Round(color.W * 255.0f);
        }
    }
}

namespace ManaUI
{
    /// <summary>
    /// One square inventory or toolbar slot: a bevelled well holding an icon (any element, usually an
    /// <see cref="ItemIcon"/>), with a count in the bottom-right corner and an optional hotkey in the top-left. It can be
    /// selected (an outline), unavailable (a red well, as for a recipe you cannot afford) or show progress along its foot.
    /// </summary>
    public class ItemSlot : Control
    {
        private long? _count;
        private string? _countText;
        private string? _hotkey;
        private bool _isSelected;
        private bool _isUnavailable;
        private float? _progress;
        private float _size = 40;
        private SlotStyle _style = SlotStyle.Industrial;

        public ItemSlot()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
        }

        public ItemSlot(UiElement icon)
            : this()
        {
            Child = icon;
        }

        /// <summary>Raised on a click inside the slot, with the button (left or right) that made it.</summary>
        public event Action<ItemSlot, PointerButton>? Clicked;

        public UiElement? Icon
        {
            get => Child;
            set => Child = value;
        }

        /// <summary>The number in the corner, shortened as it grows (1.2k); null shows none.</summary>
        public long? Count
        {
            get => _count;
            set => SetVisualProperty(ref _count, value);
        }

        /// <summary>Text for the corner instead of a number (a fraction, a rate); wins over <see cref="Count"/>.</summary>
        public string? CountText
        {
            get => _countText;
            set => SetVisualProperty(ref _countText, value);
        }

        public string? Hotkey
        {
            get => _hotkey;
            set => SetVisualProperty(ref _hotkey, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetVisualProperty(ref _isSelected, value);
        }

        public bool IsUnavailable
        {
            get => _isUnavailable;
            set => SetVisualProperty(ref _isUnavailable, value);
        }

        /// <summary>0..1 along the foot of the slot (crafting, cooldown); null hides the bar.</summary>
        public float? Progress
        {
            get => _progress;
            set => SetVisualProperty(ref _progress, value is { } p ? Math.Clamp(p, 0, 1) : null);
        }

        /// <summary>The slot's side in layout units.</summary>
        public float Size
        {
            get => _size;
            set
            {
                if (_size != value)
                {
                    _size = value;
                    InvalidateMeasure();
                }
            }
        }

        public SlotStyle Style
        {
            get => _style;
            set => SetVisualProperty(ref _style, value);
        }

        /// <summary>The shown corner text: <see cref="CountText"/>, else the formatted <see cref="Count"/>.</summary>
        public string? CornerText => _countText ?? (_count is { } n ? SlotStyle.FormatCount(n) : null);

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            Child?.Measure(new Vector2(_size, _size));
            return new Vector2(_size, _size);
        }

        protected override Vector2 ArrangeOverride(Vector2 finalSize)
        {
            // The icon fills the well, short of its bevel.
            var inset = MathF.Max(3, _size * 0.08f);
            Child?.Arrange(new UiRect(Bounds.X + inset, Bounds.Y + inset, _size - inset * 2, _size - inset * 2));
            return new Vector2(_size, _size);
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var well = new UiRect(Bounds.X, Bounds.Y, _size, _size);
            var fill = _isUnavailable ? _style.Unavailable : IsPressed || IsHighlighted ? _style.SlotHover : _style.Slot;
            _style.DrawWell(drawList, well, fill);
        }

        protected override void OnRenderOverlay(ManaUiDrawList drawList)
        {
            var well = new UiRect(Bounds.X, Bounds.Y, _size, _size);
            if (_progress is { } progress)
            {
                drawList.FillRect(new UiRect(well.X + 2, well.Bottom - 4, (well.Width - 4) * progress, 2), _style.Progress);
            }
            if (Root is { } root)
            {
                if (CornerText is { Length: > 0 } corner)
                {
                    SlotStyle.DrawText(drawList, root, corner, new Vector2(well.Right - 3, well.Bottom - _style.CountSize - 4),
                        _style.CountSize, _style.Count, alignRight: true, shadow: _style.CountShadow);
                }
                if (_hotkey is { Length: > 0 } key)
                {
                    SlotStyle.DrawText(drawList, root, key, new Vector2(well.X + 3, well.Y + 1), _style.HotkeySize, _style.Hotkey, shadow: _style.CountShadow);
                }
            }
            if (_isSelected)
            {
                drawList.DrawBorder(well, _style.Selected, 2, CornerRadius.Zero);
            }
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button is not (PointerButton.Left or PointerButton.Right) || !IsEffectivelyEnabled)
            {
                return;
            }
            IsPressed = true;
            CapturePointer();
            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (e.Button is not (PointerButton.Left or PointerButton.Right))
            {
                return;
            }
            var wasPressed = IsPressed;
            IsPressed = false;
            ReleasePointerCapture();
            if (wasPressed && Bounds.Contains(e.Position) && IsEffectivelyEnabled)
            {
                Clicked?.Invoke(this, e.Button);
            }
            e.Handled = true;
        }
    }
}

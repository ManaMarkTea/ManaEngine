using System.Windows.Input;

namespace ManaUI
{
    public enum ButtonVariant
    {
        /// <summary>Accent-filled; the primary action in a group.</summary>
        Primary,

        /// <summary>Neutral fill with a border; the default.</summary>
        Standard,

        /// <summary>No fill until hovered -- for toolbars and dense rows.</summary>
        Subtle,

        /// <summary>Destructive actions.</summary>
        Danger
    }

    /// <summary>
    /// A clickable button. Fires <see cref="Click"/> and, when set, executes <see cref="Command"/>.
    /// </summary>
    public class Button : Control
    {
        private string _text = string.Empty;
        private ButtonVariant _variant = ButtonVariant.Standard;
        private ICommand? _command;
        private TextBlock? _label;

        public Button()
        {
            HorizontalAlignment = HorizontalAlignment.Left;
            VerticalAlignment = VerticalAlignment.Top;
        }

        public Button(string text)
            : this()
        {
            Text = text;
        }

        public event EventHandler? Click;

        /// <summary>Convenience for the common case of a text-only button; builds a TextBlock child.</summary>
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value)
                {
                    return;
                }

                _text = value ?? string.Empty;
                EnsureLabel().Text = _text;
            }
        }

        public ButtonVariant Variant
        {
            get => _variant;
            set => SetVisualProperty(ref _variant, value);
        }

        /// <summary>
        /// Optional command. When set, the button's enabled state follows CanExecute, which is what makes
        /// a view model the single source of truth for whether an action is available.
        /// </summary>
        public ICommand? Command
        {
            get => _command;
            set
            {
                if (ReferenceEquals(_command, value))
                {
                    return;
                }

                if (_command != null)
                {
                    _command.CanExecuteChanged -= OnCanExecuteChanged;
                }

                _command = value;

                if (_command != null)
                {
                    _command.CanExecuteChanged += OnCanExecuteChanged;
                }

                UpdateEnabledFromCommand();
            }
        }

        public object? CommandParameter { get; set; }

        public CornerRadius? CornerRadius { get; set; }

        /// <summary>
        /// Lets a button shrink-wrap its child with no inset at all.
        /// <para>
        /// A zero <see cref="UiElement.Padding"/> otherwise reads as "not set, use the theme's", which makes
        /// zero the one value a caller cannot ask for -- and an icon button that sets it gets the theme's
        /// 8x4 anyway, quietly squeezing its content into whatever is left.
        /// </para>
        /// </summary>
        public bool IgnoreThemePadding { get; set; }

        protected override Thickness ContentInset =>
            IgnoreThemePadding || !Padding.IsZero ? Padding : Theme.ControlPadding;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            var desired = base.MeasureOverride(availableSize);

            // Never shorter than the theme's control height, so a row of buttons lines up regardless of
            // whether each one happens to contain text, an icon, or nothing.
            return new Vector2(desired.X, MathF.Max(desired.Y, Theme.ControlHeight));
        }

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Theme;
            var corners = CornerRadius ?? theme.CornerRadius;
            var enabled = IsEffectivelyEnabled;

            var (fill, border, thickness) = ResolveColors(theme, enabled);

            drawList.DrawBox(Bounds, ApplyStateOverlay(fill), border, thickness, corners);
            RenderFocusRing(drawList, Bounds, corners);

            if (_label != null)
            {
                _label.Foreground = ResolveForeground(theme, enabled);
            }
        }

        private (Vector4 Fill, Vector4 Border, float Thickness) ResolveColors(UiTheme theme, bool enabled)
        {
            if (!enabled)
            {
                return (theme.SurfaceAlt, theme.Border, theme.BorderThickness);
            }

            return _variant switch
            {
                ButtonVariant.Primary => (
                    IsPressed ? theme.AccentPressed : IsPointerOver ? theme.AccentHover : theme.Accent,
                    UiColor.Transparent,
                    0.0f),
                ButtonVariant.Danger => (theme.Danger, UiColor.Transparent, 0.0f),
                ButtonVariant.Subtle => (UiColor.Transparent, UiColor.Transparent, 0.0f),
                _ => (theme.SurfaceRaised, theme.Border, theme.BorderThickness)
            };
        }

        private Vector4 ResolveForeground(UiTheme theme, bool enabled)
        {
            if (!enabled)
            {
                return theme.TextDisabled;
            }

            return _variant is ButtonVariant.Primary or ButtonVariant.Danger ? theme.TextOnAccent : theme.Text;
        }

        protected override void OnPointerPressed(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left || !IsEffectivelyEnabled)
            {
                return;
            }

            IsPressed = true;

            // Captured so the release still arrives here even if the pointer slides off before the button
            // comes up -- that is what lets the user abort a click by dragging away.
            CapturePointer();
            e.Handled = true;
        }

        protected override void OnPointerReleased(UiPointerEventArgs e)
        {
            if (e.Button != PointerButton.Left)
            {
                return;
            }

            var wasPressed = IsPressed;
            IsPressed = false;
            ReleasePointerCapture();

            if (wasPressed && Bounds.Contains(e.Position) && IsEffectivelyEnabled)
            {
                Invoke();
            }

            e.Handled = true;
        }

        protected override void OnKeyDown(UiKeyEventArgs e)
        {
            // Space and Enter activate the focused button, which is the baseline keyboard contract every
            // desktop toolkit honours.
            if (e.Key is UiKey.Space or UiKey.Enter && IsEffectivelyEnabled)
            {
                Invoke();
                e.Handled = true;
            }
        }

        protected virtual void Invoke()
        {
            Click?.Invoke(this, EventArgs.Empty);

            if (_command?.CanExecute(CommandParameter) == true)
            {
                _command.Execute(CommandParameter);
            }
        }

        private TextBlock EnsureLabel()
        {
            if (_label == null)
            {
                _label = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Child = _label;
            }

            return _label;
        }

        private void OnCanExecuteChanged(object? sender, EventArgs e)
        {
            UpdateEnabledFromCommand();
        }

        private void UpdateEnabledFromCommand()
        {
            if (_command != null)
            {
                IsEnabled = _command.CanExecute(CommandParameter);
            }
        }
    }
}

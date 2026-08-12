namespace ManaUI
{
    /// <summary>
    /// Base for interactive widgets: a single-child container that tracks pressed state and resolves its
    /// own visuals from the theme.
    /// <para>
    /// Visual state is expressed as a handful of booleans (hover / pressed / focused / disabled) that a
    /// control folds into a colour, rather than as a CSS-style selector engine. For an editor UI that
    /// covers the same ground and stays debuggable.
    /// </para>
    /// </summary>
    public abstract class Control : Decorator
    {
        private bool _isPressed;

        protected Control()
        {
            Focusable = true;
            IsHitTestVisible = true;
        }

        /// <summary>True while the pointer is held down on this control.</summary>
        public bool IsPressed
        {
            get => _isPressed;
            protected set => SetVisualProperty(ref _isPressed, value);
        }

        public UiTheme Theme => Root?.Theme ?? DefaultTheme;

        private static readonly UiTheme DefaultTheme = UiTheme.Dark();

        /// <summary>
        /// Controls are always hit targets, even where transparent -- a Subtle button has no fill until
        /// hovered, and it still has to be hoverable to get there.
        /// </summary>
        protected override bool IsSelfHitTestVisible => true;

        /// <summary>
        /// Blends the hover and pressed overlays onto a base fill. Overlaying rather than swapping in
        /// separate colours per state means a control keeps its own accent while still reacting.
        /// </summary>
        protected Vector4 ApplyStateOverlay(Vector4 baseColor)
        {
            var theme = Theme;
            var result = baseColor;

            if (!IsEffectivelyEnabled)
            {
                return result;
            }

            if (IsPressed)
            {
                result = Composite(result, theme.Pressed);
            }
            else if (IsPointerOver)
            {
                result = Composite(result, theme.Hover);
            }

            return result;
        }

        /// <summary>Source-over composite of a translucent overlay onto an opaque-ish base.</summary>
        protected static Vector4 Composite(Vector4 baseColor, Vector4 overlay)
        {
            var alpha = overlay.W;
            if (alpha <= 0.0f)
            {
                return baseColor;
            }

            return new Vector4(
                baseColor.X * (1.0f - alpha) + overlay.X * alpha,
                baseColor.Y * (1.0f - alpha) + overlay.Y * alpha,
                baseColor.Z * (1.0f - alpha) + overlay.Z * alpha,
                MathF.Max(baseColor.W, alpha));
        }

        /// <summary>Draws the keyboard focus ring. Only shown when focused, never on mere hover.</summary>
        protected void RenderFocusRing(ManaUiDrawList drawList, UiRect bounds, CornerRadius corners)
        {
            if (!IsFocused)
            {
                return;
            }

            var theme = Theme;

            // Drawn just outside the control so it never eats into the content box and shift layout.
            var ring = bounds.Inflate(new Thickness(theme.FocusRingThickness));
            drawList.DrawBorder(ring, theme.FocusRing, theme.FocusRingThickness, corners);
        }

        protected override void OnPointerExited(UiPointerEventArgs e)
        {
            // Losing the pointer without a release (window blur, element moved) must not leave the control
            // stuck looking pressed.
            if (!HasPointerCapture)
            {
                IsPressed = false;
            }
        }
    }
}

namespace ManaUI
{
    /// <summary>
    /// A small round indicator, lit or dark, coloured by meaning (a gate open or shut, a link up or down). Read-only
    /// like <see cref="ProgressBar"/>; pair it with a label.
    /// </summary>
    public class StatusLight : UiElement
    {
        private bool _isOn;
        private UiTone _tone = UiTone.Success;

        public bool IsOn
        {
            get => _isOn;
            set => SetVisualProperty(ref _isOn, value);
        }

        public UiTone Tone
        {
            get => _tone;
            set => SetVisualProperty(ref _tone, value);
        }

        public float Diameter { get; set; } = 18.0f;

        protected override Vector2 MeasureOverride(Vector2 availableSize) => new(Diameter, Diameter);

        protected override void OnRender(ManaUiDrawList drawList)
        {
            var theme = Root?.Theme;
            if (theme == null)
            {
                return;
            }

            var colour = _tone switch
            {
                UiTone.Success => theme.Success,
                UiTone.Warning => theme.Warning,
                UiTone.Danger => theme.Danger,
                _ => theme.Accent
            };

            var disc = new UiRect(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height);
            var round = new CornerRadius(Bounds.Height * 0.5f);
            drawList.DrawBox(disc, _isOn ? colour : theme.SurfaceAlt, _isOn ? theme.TextOnAccent.WithAlpha(0.7f) : theme.BorderStrong, 2.0f, round);

            if (_isOn)
            {
                // A small highlight, so a lit light reads as glass and not a flat dot.
                var shine = new UiRect(Bounds.X + Bounds.Width * 0.24f, Bounds.Y + Bounds.Height * 0.2f, Bounds.Width * 0.28f, Bounds.Height * 0.2f);
                drawList.FillRect(shine, new Vector4(1.0f, 1.0f, 1.0f, 0.55f), new CornerRadius(shine.Height * 0.5f));
            }
        }
    }
}

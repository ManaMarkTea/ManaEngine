namespace ManaUI
{
    /// <summary>The built-in palettes.</summary>
    public enum UiThemeKind
    {
        Dark,
        Light
    }

    /// <summary>
    /// The design tokens every control reads instead of hard-coding colours.
    /// <para>
    /// Deliberately a flat token set rather than a CSS-style selector engine: a selector engine is a
    /// second language to learn and debug, while a token set plus per-control style overrides covers the
    /// same ground for an editor UI and stays inspectable.
    /// </para>
    /// </summary>
    public class UiTheme
    {
        // ----- surfaces ------------------------------------------------------------------------------

        /// <summary>Furthest-back surface -- the window itself.</summary>
        public Vector4 Background { get; set; } = UiColor.FromHex("#1E1F22");

        /// <summary>Panels and cards sitting on the background.</summary>
        public Vector4 Surface { get; set; } = UiColor.FromHex("#2B2D30");

        /// <summary>Inputs, list rows and other recessed areas.</summary>
        public Vector4 SurfaceAlt { get; set; } = UiColor.FromHex("#1E1F22");

        /// <summary>Menus, popups and floating windows -- raised above everything else.</summary>
        public Vector4 SurfaceRaised { get; set; } = UiColor.FromHex("#3B3E43");

        /// <summary>Scrim behind modal content.</summary>
        public Vector4 Scrim { get; set; } = new Vector4(0.0f, 0.0f, 0.0f, 0.45f);

        // ----- content -------------------------------------------------------------------------------

        public Vector4 Text { get; set; } = UiColor.FromHex("#DFE1E5");
        public Vector4 TextMuted { get; set; } = UiColor.FromHex("#9DA0A8");
        public Vector4 TextDisabled { get; set; } = UiColor.FromHex("#6A6D75");

        /// <summary>Text drawn on top of an accent fill.</summary>
        public Vector4 TextOnAccent { get; set; } = UiColor.FromHex("#FFFFFF");

        // ----- accent --------------------------------------------------------------------------------

        public Vector4 Accent { get; set; } = UiColor.FromHex("#4A88E8");
        public Vector4 AccentHover { get; set; } = UiColor.FromHex("#5D97F0");
        public Vector4 AccentPressed { get; set; } = UiColor.FromHex("#3A72CC");

        // ----- lines and states ----------------------------------------------------------------------

        public Vector4 Border { get; set; } = UiColor.FromHex("#3B3E43");
        public Vector4 BorderStrong { get; set; } = UiColor.FromHex("#4E5158");
        public Vector4 Divider { get; set; } = UiColor.FromHex("#34363B");

        /// <summary>Overlaid on a control's own fill, so these carry deliberate low alpha.</summary>
        public Vector4 Hover { get; set; } = new Vector4(1.0f, 1.0f, 1.0f, 0.07f);

        public Vector4 Pressed { get; set; } = new Vector4(0.0f, 0.0f, 0.0f, 0.14f);
        public Vector4 Selected { get; set; } = UiColor.FromHex("#2F5788");
        public Vector4 FocusRing { get; set; } = UiColor.FromHex("#4A88E8");

        // ----- status --------------------------------------------------------------------------------

        public Vector4 Danger { get; set; } = UiColor.FromHex("#E5534B");
        public Vector4 Warning { get; set; } = UiColor.FromHex("#D9A441");
        public Vector4 Success { get; set; } = UiColor.FromHex("#57A75B");

        // ----- axes ----------------------------------------------------------------------------------
        // The red/green/blue convention every 3D tool shares, so a vector field's components read at a
        // glance. Muted well below the gizmo colours -- an inspector full of these should not shout.

        public Vector4 AxisX { get; set; } = UiColor.FromHex("#C75450");
        public Vector4 AxisY { get; set; } = UiColor.FromHex("#6DA05A");
        public Vector4 AxisZ { get; set; } = UiColor.FromHex("#5A87C7");
        public Vector4 AxisW { get; set; } = UiColor.FromHex("#9B7FC7");

        public Vector4 AxisColor(int index)
        {
            return index switch
            {
                0 => AxisX,
                1 => AxisY,
                2 => AxisZ,
                _ => AxisW
            };
        }

        // ----- metrics -------------------------------------------------------------------------------

        public CornerRadius CornerRadius { get; set; } = new CornerRadius(4.0f);
        public CornerRadius CornerRadiusLarge { get; set; } = new CornerRadius(8.0f);

        /// <summary>Standard height for buttons, inputs and rows. Everything else spaces off this.</summary>
        public float ControlHeight { get; set; } = 26.0f;

        public float BorderThickness { get; set; } = 1.0f;
        public float FocusRingThickness { get; set; } = 2.0f;

        /// <summary>Gap between sibling controls.</summary>
        public float Spacing { get; set; } = 6.0f;

        /// <summary>Inset from a container's edge to its content.</summary>
        public Thickness Padding { get; set; } = new Thickness(8.0f);

        public Thickness ControlPadding { get; set; } = new Thickness(8.0f, 4.0f);

        public float ScrollBarThickness { get; set; } = 10.0f;

        /// <summary>
        /// Clear space between scrolling content and the bar beside it. Reserving only the bar's own width
        /// leaves a value's editor ending flush against it, which reads as content running underneath.
        /// </summary>
        public float ScrollBarGap { get; set; } = 4.0f;

        // ----- typography ----------------------------------------------------------------------------

        public string FontFamily { get; set; } = "Roboto";
        public string FontFamilyMedium { get; set; } = "Roboto-Medium";
        public string FontFamilyMono { get; set; } = "RobotoMono-Regular";

        /// <summary>
        /// Multiplier on the font's own line height. Fonts like Roboto report a zero line gap, which makes
        /// ascent + descent exactly the em size and leaves consecutive lines touching -- so the comfortable
        /// default lives here rather than in the font.
        /// </summary>
        public float LineSpacing { get; set; } = 1.25f;

        public float FontSize { get; set; } = 15.0f;
        public float FontSizeSmall { get; set; } = 13.0f;
        public float FontSizeHeading { get; set; } = 18.0f;

        /// <summary>
        /// Which built-in palette this theme came from. Carried on the theme so a host can persist the
        /// choice without keeping a second copy of it that can drift out of step.
        /// </summary>
        public UiThemeKind Kind { get; private set; } = UiThemeKind.Dark;

        public static UiTheme Create(UiThemeKind kind)
        {
            return kind == UiThemeKind.Light ? Light() : Dark();
        }

        public static UiTheme Dark()
        {
            return new UiTheme();
        }

        public static UiTheme Light()
        {
            return new UiTheme
            {
                Kind = UiThemeKind.Light,

                Background = UiColor.FromHex("#F2F3F5"),
                Surface = UiColor.FromHex("#FFFFFF"),
                SurfaceAlt = UiColor.FromHex("#EDEEF0"),
                SurfaceRaised = UiColor.FromHex("#FFFFFF"),

                Text = UiColor.FromHex("#1F2024"),
                TextMuted = UiColor.FromHex("#5C5F66"),
                TextDisabled = UiColor.FromHex("#A2A5AC"),

                Border = UiColor.FromHex("#D6D8DC"),
                BorderStrong = UiColor.FromHex("#BCBFC5"),
                Divider = UiColor.FromHex("#E4E6E9"),

                Hover = new Vector4(0.0f, 0.0f, 0.0f, 0.05f),
                Pressed = new Vector4(0.0f, 0.0f, 0.0f, 0.10f),
                Selected = UiColor.FromHex("#CFE0FA")
            };
        }
    }
}

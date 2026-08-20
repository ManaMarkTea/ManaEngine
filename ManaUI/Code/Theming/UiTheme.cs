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

        /// <summary>
        /// The band on every other row of a table.
        /// <para>
        /// A token of its own rather than a reuse of SurfaceRaised, which is lighter than the surface in a
        /// dark theme and identical to it in a light one -- so a stripe drawn with it was white on white
        /// and the banding simply did not exist in light mode.
        /// </para>
        /// </summary>
        public Vector4 RowAlternate { get; set; } = UiColor.FromHex("#22262E");

        /// <summary>
        /// How far a raised surface lightens at the top and darkens at the bottom, as a fraction.
        /// <para>
        /// One number rather than a second colour per token: a gradient here is a light source overhead,
        /// so every raised thing wants the same shading and only the base colour differs. Zero gives the
        /// flat look back, which is what a theme that does not want this should set.
        /// </para>
        /// </summary>
        public float SurfaceGradient { get; set; } = 0.04f;

        /// <summary>The same, for the accent -- buttons carry it more strongly than panels do.</summary>
        public float AccentGradient { get; set; } = 0.10f;

        /// <summary>
        /// How far translucent surfaces blur what is behind them, in logical pixels. Zero leaves the scene
        /// sharp behind them, which is the old look and stays the default.
        /// <para>
        /// Off by default because it is not free: the backend has to snapshot and blur the frame before the
        /// UI draws, and it skips all of that entirely when nothing asks for it.
        /// </para>
        /// </summary>
        public float BackdropBlur { get; set; }

        /// <summary>How much of a frosted surface's own colour survives. Enough to read text against.</summary>
        public float FrostedOpacity { get; set; } = 0.82f;

        /// <summary>
        /// The fill for a surface floating over the scene. Translucent only when there is a blur behind it
        /// to be seen through -- an opaque panel over a blurred snapshot is an opaque panel that made the
        /// GPU work for nothing, and a translucent one with no blur is just hard to read.
        /// </summary>
        public Vector4 Frosted(Vector4 surface) =>
            BackdropBlur > 0.0f ? surface with { W = surface.W * FrostedOpacity } : surface;

        /// <summary>The top of a gradient over <paramref name="color"/>.</summary>
        public Vector4 GradientTop(Vector4 color, float strength) => UiColor.Lighten(color, strength * 0.5f);

        /// <summary>The bottom of a gradient over <paramref name="color"/>.</summary>
        public Vector4 GradientBottom(Vector4 color, float strength) => UiColor.Darken(color, strength * 0.5f);

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
        public float ControlHeight { get; set; } = 24.0f;

        public float BorderThickness { get; set; } = 1.0f;
        public float FocusRingThickness { get; set; } = 2.0f;

        /// <summary>
        /// Gap between sibling controls.
        /// <para>
        /// These four metrics -- this, Padding, ControlPadding and LineSpacing -- are where the editor's
        /// information density comes from, and they were set for comfort in a demo panel rather than for a
        /// working editor. Tightened after comparing a full-screen ManaVoxel against Unreal at the same
        /// window size: the viewport was actually the larger of the two, but every panel showed noticeably
        /// fewer rows, which is what made the window feel smaller. Restrained on purpose -- a couple of
        /// pixels per gap compounds across a docked layout, and going further starts costing legibility
        /// rather than buying rows.
        /// </para>
        /// </summary>
        public float Spacing { get; set; } = 4.0f;

        /// <summary>Inset from a container's edge to its content.</summary>
        public Thickness Padding { get; set; } = new Thickness(5.0f);

        public Thickness ControlPadding { get; set; } = new Thickness(7.0f, 3.0f);

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
        public float LineSpacing { get; set; } = 1.15f;

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

                // Cool rather than neutral grey, which is what makes ImGui's light style read as a tool
                // and not a document: every surface carries a little blue, and the accent is the same blue
                // saturated rather than a separate hue dropped on top.
                Background = UiColor.FromHex("#E8ECF2"),
                Surface = UiColor.FromHex("#F7F9FC"),
                SurfaceAlt = UiColor.FromHex("#DCE3ED"),
                SurfaceRaised = UiColor.FromHex("#FFFFFF"),

                // Darker than the surface, where the dark theme's is lighter. A stripe has to move away
                // from the background, and which way that is depends on the theme.
                RowAlternate = UiColor.FromHex("#DFE6F0"),

                Text = UiColor.FromHex("#12161C"),
                TextMuted = UiColor.FromHex("#54606F"),
                TextDisabled = UiColor.FromHex("#9AA4B2"),

                Border = UiColor.FromHex("#C3CDDB"),
                BorderStrong = UiColor.FromHex("#A3B0C2"),
                Divider = UiColor.FromHex("#D3DBE6"),

                SurfaceGradient = 0.06f,
                AccentGradient = 0.14f,

                Accent = UiColor.FromHex("#2C6FD6"),
                AccentHover = UiColor.FromHex("#3D80E6"),
                AccentPressed = UiColor.FromHex("#215BB5"),

                Hover = new Vector4(0.06f, 0.20f, 0.40f, 0.07f),
                Pressed = new Vector4(0.06f, 0.20f, 0.40f, 0.14f),
                Selected = UiColor.FromHex("#BBD4F5")
            };
        }
    }
}

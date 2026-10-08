namespace ManaUI
{
    /// <summary>The built-in palettes.</summary>
    public enum UiThemeKind
    {
        Dark,
        Light,
        Lcars,

        /// <summary>Navy and sky blue, dressed in Kenney's Space UI pack's pictures where the pack is available.</summary>
        Kenney
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

        /// <summary>Pictures controls are drawn in instead of flat colour, or null for a flat theme. Rebuilt on
        /// first use after the atlas holding it was reset, since its pictures would otherwise be someone else's.</summary>
        public UiSkin? Skin
        {
            get
            {
                if (_skin is { IsStale: true } stale)
                {
                    _skin = stale.Refresh?.Invoke();
                }

                return _skin;
            }
            set => _skin = value;
        }

        private UiSkin? _skin;
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

        // ----- 3D viewport ---------------------------------------------------------------------------
        // Not part of the 2D UI, but a skin as thorough as LCARS reaches the editor's ground-plane grid
        // too, not just its panels. Mana/Code/GameModes/VoxelEditorGameMode.cs owns the actual grid
        // quads; ManaUiDockDemo pushes these two into their material whenever the theme changes.

        /// <summary>Base tint of the 3D viewport's ground-plane grid -- the checkerboard cells.</summary>
        // Lifted off near-black (was #141518): the checkerboard's fixed 0.9645 odd/even ratio and the
        // sub-grid's fixed 15% line-tint blend (see grid.wgsl/grid.frag) are both small relative
        // differences, and 8-bit sRGB crushes small differences into invisibility down near 0 -- the same
        // percentage lands as a visible step once the floor isn't sitting at the very bottom of the range.
        // Dark's grid is a mid grey a step under Light's, not near-black: against dark panels the viewport reads as
        // a lit floor, and the alternating squares only separate (GridCheckerRatio) once the floor has room to fall.
        public Vector4 GridFloor { get; set; } = UiColor.FromHex("#9A9AA2");

        /// <summary>Tint of the grid lines drawn over <see cref="GridFloor"/>.</summary>
        // Brighter and cooler than the old #3A3D43: the 0.1-unit sub-grid only ever blends in 15% of this
        // colour (a dim tint, by design -- see grid.wgsl's minorLineColor comment), so it has to be
        // noticeably bright on its own for that diluted result to still read against GridFloor.
        public Vector4 GridLine { get; set; } = UiColor.FromHex("#70727C");

        /// <summary>How much darker the alternating checkerboard squares are than <see cref="GridFloor"/>: 1 is none, 0.95 is a gentle step.</summary>
        public float GridCheckerRatio { get; set; } = 0.95f;

        /// <summary>
        /// Multiplier on grid line width. 1 leaves the base thickness alone; a theme whose lines are meant
        /// to read as glowing (LCARS) wants them wider than a plain graph-paper grid does.
        /// <para>
        /// A grid material carries this via <c>ManaMaterial.EmissionStrength</c> (a separate double field),
        /// not <see cref="GridLine"/>'s own alpha -- WebGPU's per-object uniform is built generically from
        /// <c>ManaRenderMeshInstance</c>, which populates material_emission.a from EmissionStrength and
        /// ignores the material's own Emission.Value.W entirely, so stuffing this into GridLine's alpha
        /// worked only on OpenGL (ManaGridShader.BindShader pushes Emission.Value there directly) and left
        /// WebGPU reading a hardcoded 0 -- degenerate smoothstep(0, 0, x), no lines, every grid theme.
        /// </para>
        /// </summary>
        public float GridLineThickness { get; set; } = 1.0f;

        // ----- metrics -------------------------------------------------------------------------------

        public CornerRadius CornerRadius { get; set; } = new CornerRadius(4.0f);
        public CornerRadius CornerRadiusLarge { get; set; } = new CornerRadius(8.0f);

        /// <summary>
        /// Rounding for controls that are a perfect square -- an icon toggle, a texture-slot chip -- rather
        /// than a wide bar. Kept apart from <see cref="CornerRadius"/> because a theme is free to make that
        /// one a full capsule (radius equal to half <see cref="ControlHeight"/>), and a capsule radius on a
        /// square control is not a rounder square, it is a circle that swallows whatever icon sits inside
        /// it. Defaults to the same value as <see cref="CornerRadius"/> so a theme that never touches either
        /// token sees no change from before this existed.
        /// </summary>
        public CornerRadius CornerRadiusCompact { get; set; } = new CornerRadius(4.0f);

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

        /// <summary>Overrides both body and heading faces so a theme's original font does not linger.</summary>
        public void SetFontFamily(string family)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(family);
            FontFamily = family;
            FontFamilyMedium = family;
        }

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
            return kind switch
            {
                UiThemeKind.Light => Light(),
                UiThemeKind.Lcars => Lcars(),
                UiThemeKind.Kenney => Kenney(),
                _ => Dark()
            };
        }

        public static UiTheme Dark()
        {
            return new UiTheme();
        }

        /// <summary>
        /// Kenney's Space UI pack, as its own sample shows it: pale grey-lilac panels and buttons with dark type, blue
        /// header bars, and the pack's bright blue behind. The pictures themselves (<see cref="Skin"/>) come from the
        /// host once it has an atlas to put them in; these tokens carry the colours without them.
        /// </summary>
        public static UiTheme Kenney()
        {
            return new UiTheme
            {
                Kind = UiThemeKind.Kenney,
                // The wide cut at a small size reads better than the "narrow" one, whose letters are no narrower and
                // whose spacing is looser.
                FontFamily = "KenneyFuture",
                FontFamilyMedium = "KenneyFuture",
                FontSize = 14.0f,
                FontSizeSmall = 12.0f,
                FontSizeHeading = 17.0f,
                Background = UiColor.FromHex("#3399DD"),
                Surface = UiColor.FromHex("#DCDFEA"),
                SurfaceAlt = UiColor.FromHex("#A5A8B8"),
                SurfaceRaised = UiColor.FromHex("#E8EAF2"),
                RowAlternate = UiColor.FromHex("#D2D5E2"),
                Text = UiColor.FromHex("#3A3F50"),
                TextMuted = UiColor.FromHex("#686E84"),
                TextDisabled = UiColor.FromHex("#9A9FB2"),
                TextOnAccent = UiColor.FromHex("#FFFFFF"),
                Accent = UiColor.FromHex("#1FA0DC"),
                AccentHover = UiColor.FromHex("#3DB4EC"),
                AccentPressed = UiColor.FromHex("#1584B8"),
                Border = UiColor.FromHex("#B0B4C6"),
                BorderStrong = UiColor.FromHex("#8A90A8"),
                Hover = new Vector4(0.06f, 0.20f, 0.40f, 0.07f),
                Pressed = new Vector4(0.06f, 0.20f, 0.40f, 0.14f),
                Selected = UiColor.FromHex("#BBD4F5"),
                SurfaceGradient = 0.0f,
                AccentGradient = 0.0f,
                CornerRadius = new CornerRadius(5.0f),
                CornerRadiusLarge = new CornerRadius(8.0f),
                GridFloor = UiColor.FromHex("#C8CCDA"),
                GridLine = UiColor.FromHex("#9CA2B8"),
                GridCheckerRatio = 0.9645f
            };
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
                Selected = UiColor.FromHex("#BBD4F5"),

                // The classic light grey graph-paper grid -- unlike every other token here, this is what
                // the viewport has always looked like (it never read the theme before now), so it stays
                // put while Dark's own default gets to change underneath it. GridLine is the original
                // shader's own (fillEven+fillOdd)/2.4 line colour, computed out by hand -- an earlier,
                // wrong hand-computation of this (#545455) rendered roughly half as bright as the real
                // original line, which is what made every line look too dark even at close range,
                // regardless of anything the distance fade was doing.
                GridFloor = UiColor.FromHex("#C6C4C7"),
                GridLine = UiColor.FromHex("#A2A0A3"),
                GridCheckerRatio = 0.9645f
            };
        }

        /// <summary>LCARS console colours, with dark working areas and distinct coloured frames and controls.</summary>
        public static UiTheme Lcars()
        {
            return new UiTheme
            {
                Kind = UiThemeKind.Lcars,
                Background = UiColor.FromHex("#08090D"),
                Surface = UiColor.FromHex("#101117"),
                SurfaceAlt = UiColor.FromHex("#191922"),
                SurfaceRaised = UiColor.FromHex("#302A3C"),
                RowAlternate = UiColor.FromHex("#1B1925"),
                Text = UiColor.FromHex("#E6DFF2"),
                TextMuted = UiColor.FromHex("#B5A7C7"),
                TextDisabled = UiColor.FromHex("#796D89"),
                TextOnAccent = UiColor.FromHex("#08090D"),
                Accent = UiColor.FromHex("#FFAA55"),
                AccentHover = UiColor.FromHex("#FFCC88"),
                AccentPressed = UiColor.FromHex("#D98A40"),
                SurfaceGradient = 0.0f,
                AccentGradient = 0.0f,
                Border = UiColor.FromHex("#B49BD1"),
                BorderStrong = UiColor.FromHex("#D0B5EE"),
                Divider = UiColor.FromHex("#514360"),
                BorderThickness = 1.0f,
                Hover = new Vector4(1.0f, 1.0f, 1.0f, 0.08f),
                Pressed = new Vector4(0.0f, 0.0f, 0.0f, 0.20f),
                Selected = UiColor.FromHex("#493753"),
                FocusRing = UiColor.FromHex("#88CCEE"),
                Danger = UiColor.FromHex("#EE7777"),
                Warning = UiColor.FromHex("#FFCC88"),
                Success = UiColor.FromHex("#77CCCC"),
                ControlHeight = 28.0f,
                CornerRadius = new CornerRadius(4.0f),
                CornerRadiusLarge = new CornerRadius(12.0f),
                CornerRadiusCompact = new CornerRadius(4.0f),
                Spacing = 6.0f,
                Padding = new Thickness(10.0f, 8.0f),
                ControlPadding = new Thickness(10.0f, 4.0f),
                ScrollBarThickness = 10.0f,
                ScrollBarGap = 6.0f,
                FontFamily = "Antonio-SemiBold",
                FontFamilyMedium = "Antonio-Bold",
                FontSize = 17.0f,
                FontSizeSmall = 15.0f,
                FontSizeHeading = 24.0f,
                GridFloor = UiColor.FromHex("#0A0805"),
                GridLine = UiColor.FromHex("#FFCC66"),
                GridCheckerRatio = 0.9645f
            };
        }
    }
}

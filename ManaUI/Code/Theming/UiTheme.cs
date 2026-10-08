namespace ManaUI
{
    /// <summary>The built-in palettes.</summary>
    public enum UiThemeKind
    {
        Dark,
        Light,
        Lcars
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

        /// <summary>How much darker the alternating checkerboard squares are than <see cref="GridFloor"/>: 1 is none, 0.88 plain to see.</summary>
        public float GridCheckerRatio { get; set; } = 0.88f;

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
                _ => Dark()
            };
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

        /// <summary>
        /// Star Trek console styling: solid colour blocks on a black void, capsule controls, oversized type.
        /// <para>
        /// Buttons are full capsules -- radius half of <see cref="ControlHeight"/> -- which the SDF
        /// primitive already supports per-corner, so no shader work was needed for the shape itself. The
        /// one true LCARS trait this token set cannot express is the elbow panel (an L-bend with unequal
        /// arm widths sweeping into one rounded corner); that needs a dedicated primitive, not a theme.
        /// </para>
        /// <para>
        /// Structural lines carry real LCARS hues (lilac border, gold emphasis, periwinkle divider) rather
        /// than the black-on-black "no border" look a first pass used -- a real console reads as colourful
        /// because its frame lines are colourful, not just its buttons, and a near-invisible border was
        /// part of why that first pass read as flat and monochrome instead.
        /// </para>
        /// </summary>
        public static UiTheme Lcars()
        {
            return new UiTheme
            {
                Kind = UiThemeKind.Lcars,

                // Warm dark ambers rather than flat black -- a first pass made Background AND Surface both
                // pure #000000, which is where "too dark" really came from: a panel sitting on the window
                // is indistinguishable from the void behind it except for its border line, so the whole UI
                // read as one undifferentiated black field with orange buttons floating on it. Real LCARS
                // keeps black only for the empty space *between* its console blocks -- the blocks
                // themselves are always a definite, visible material. Each step here is a genuinely
                // brighter/more saturated amber than the last, so a panel now visibly sits apart from the
                // window, and a popup visibly sits apart from the panel it opened over.
                Background = UiColor.FromHex("#0D0805"),
                Surface = UiColor.FromHex("#2E1B0C"),
                SurfaceAlt = UiColor.FromHex("#1F1207"),
                SurfaceRaised = UiColor.FromHex("#3D2712"),
                RowAlternate = UiColor.FromHex("#33200F"),

                // Text sits on the black void in the cool pale blue real consoles use; text on a colour
                // block is always black, which is what TextOnAccent carries.
                Text = UiColor.FromHex("#CCCCFF"),
                TextMuted = UiColor.FromHex("#8888AA"),
                TextDisabled = UiColor.FromHex("#4A4A5C"),
                TextOnAccent = UiColor.FromHex("#000000"),

                // The classic LCARS orange block. Buttons are flat colour, not lit from above, so
                // AccentGradient stays at zero -- but SurfaceGradient (panels, not buttons) gets a little
                // life back rather than the flat zero a first pass gave both: with Surface now an actual
                // visible amber instead of black-on-black, a hint of top-lit shading reads as a physical
                // console panel instead of a flat colour swatch.
                Accent = UiColor.FromHex("#FF9900"),
                AccentHover = UiColor.FromHex("#FFB238"),
                AccentPressed = UiColor.FromHex("#CC7A00"),
                SurfaceGradient = 0.08f,
                AccentGradient = 0.0f,

                // Lilac frame, gold emphasis, periwinkle dividers -- the trio of secondary hues a real
                // console spreads across its line work so the console reads as colourful everywhere, not
                // just wherever a button happens to sit.
                Border = UiColor.FromHex("#CC99CC"),
                BorderStrong = UiColor.FromHex("#FFCC66"),
                Divider = UiColor.FromHex("#9999CC"),
                BorderThickness = 2.0f,

                Hover = new Vector4(1.0f, 1.0f, 1.0f, 0.10f),
                Pressed = new Vector4(0.0f, 0.0f, 0.0f, 0.25f),

                // LCARS "selected" reads as the salmon/red alert block, not a tinted accent.
                Selected = UiColor.FromHex("#CC6666"),
                FocusRing = UiColor.FromHex("#66CCFF"),

                Danger = UiColor.FromHex("#FF6666"),
                Warning = UiColor.FromHex("#FFCC66"),
                Success = UiColor.FromHex("#66CCCC"),

                // Chunky capsule controls, sized up from the base theme's density -- but only modestly:
                // a first pass went up to 2-2.5x the working-editor defaults across the board (ControlHeight
                // 36, Spacing 10, Padding 14/9, ControlPadding 18/7), which reads as "console" in isolation
                // but as wasted panel space next to a real docked layout stacked several rows deep -- every
                // one of those oversized gaps is multiplied by however many rows and panels are on screen
                // at once. Landed closer to 1.2-1.5x instead: still visibly chunkier than Dark/Light, still
                // reads as deliberately different, but not at the cost of half the panel being padding.
                ControlHeight = 30.0f,
                CornerRadius = new CornerRadius(15.0f),
                CornerRadiusLarge = new CornerRadius(24.0f),

                // A modest, non-circular round for icon toggles and texture-slot chips -- see
                // CornerRadiusCompact's own doc comment for why this can't just be CornerRadius.
                CornerRadiusCompact = new CornerRadius(8.0f),

                Spacing = 6.0f,
                Padding = new Thickness(8.0f, 6.0f),
                ControlPadding = new Thickness(10.0f, 4.0f),
                ScrollBarThickness = 11.0f,
                ScrollBarGap = 5.0f,

                // Bumped well past the base sizes -- LCARS type is a shouted, oversized display face, not
                // body copy. Antonio is an ultra-condensed display face in the same family as the
                // Swiss911/Okuda face real consoles use; Bold is its heaviest static weight.
                FontFamily = "Antonio-Bold",
                FontFamilyMedium = "Antonio-SemiBold",
                FontSize = 17.0f,
                FontSizeSmall = 15.0f,
                FontSizeHeading = 26.0f,

                // The holodeck floor: a near-black void carrying a warm cast rather than a neutral one, so
                // it reads as the same material as the gold grid glowing over it rather than a separate
                // grey floor with gold lines dropped on top.
                // Thickness left at the theme default (1) rather than doubled -- a fatter line is a fixed
                // fraction of the antialiasing derivative, which grows as the camera pulls back, so a
                // thicker constant made the grid clog up densely at a distance where the 1x default still
                // read cleanly. See grid.wgsl/grid.frag for the derivative-based line math this scales.
                GridFloor = UiColor.FromHex("#0A0805"),
                GridLine = UiColor.FromHex("#FFCC66"),
                GridCheckerRatio = 0.9645f
            };
        }
    }
}

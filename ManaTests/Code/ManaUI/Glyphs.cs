using ManaUI;

namespace ManaTests;

/// <summary>
/// Coverage for every character the library draws as an icon.
/// <para>
/// This suite exists because a missing glyph is invisible to the code that asks for it: stb maps an unknown
/// codepoint to glyph index 0, .notdef, which in most fonts is a hollow rectangle. It rasterises like any
/// other glyph, so every "fall back to a drawn shape if the font lacks this" path in the library was dead,
/// and the UI drew tofu boxes for its close buttons and tree arrows instead.
/// </para>
/// </summary>
public static class GlyphChecks
{
    /// <summary>
    /// Every non-ASCII character the library still asks a font for, with where it is used. Add a row when
    /// adding one, and this suite will say whether the shipped fonts can actually draw it.
    /// <para>
    /// The list is short on purpose. Arrows and ticks are drawn from primitives by <see cref="UiIcons"/>
    /// instead, because relying on a font for chrome means the chrome changes shape -- or vanishes into a
    /// tofu box -- whenever the theme's typeface does.
    /// </para>
    /// </summary>
    private static readonly (int Codepoint, string Where)[] IconGlyphs =
    {
        (0x00D7, "window and tab close buttons")
    };

    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var theme = UiTheme.Dark();
        var font = fonts.Get(theme.FontFamily, theme.FontSize);

        // ---------------------------------------------------------------- the detection itself
        Check("ASCII is present", font.HasGlyph('A') && font.HasGlyph('z') && font.HasGlyph('0'),
            "letters and digits");

        // A codepoint no font has. If this ever reports present, the detection is broken and every check
        // below is meaningless.
        Check("an unmapped codepoint is reported missing", !font.HasGlyph(0x10FFFD),
            "private use area");

        var tofu = font.GetGlyph(0x10FFFD);
        Check("a missing glyph is flagged", tofu.IsMissing, "IsMissing set");
        Check("and is not drawable even though it has pixels",
            !tofu.IsDrawable, $"HasPixels {tofu.HasPixels}, IsMissing {tofu.IsMissing}");

        var real = font.GetGlyph('A');
        Check("a real glyph is drawable", real.IsDrawable && !real.IsMissing, "A");

        // Whitespace has pixels nowhere but is not missing, so the two flags must not be conflated.
        var space = font.GetGlyph(' ');
        Check("a space is empty but not missing", !space.HasPixels && !space.IsMissing,
            $"advance {space.Advance:F2}");

        // ---------------------------------------------------------------- icon coverage
        // The point of the suite. Every icon the library draws has to exist in the font it asks, or the
        // control silently renders a box where its arrow should be.
        foreach (var (codepoint, where) in IconGlyphs)
        {
            Check(
                $"U+{codepoint:X4} available for {where}",
                font.HasGlyph(codepoint),
                $"'{char.ConvertFromUtf32(codepoint)}' in {theme.FontFamily}");
        }

        // The medium and mono families are used for headings, tabs and the debug readout, so anything drawn
        // in them needs the same coverage.
        foreach (var family in new[] { theme.FontFamilyMedium, theme.FontFamilyMono })
        {
            var other = fonts.Get(family, theme.FontSize);
            Check($"{family} covers ASCII", other.HasGlyph('A') && other.HasGlyph('9'), family);
        }

        // ---------------------------------------------------------------- drawn icons
        // The arrows and ticks the font cannot supply. These are the characters that used to render as
        // boxes, so the checks are that the drawn versions put ink on screen where the glyphs did not.
        var root = new UiRoot(fonts);
        var drawList = root.DrawList;

        foreach (var direction in Enum.GetValues<UiIconDirection>())
        {
            drawList.Reset();
            UiIcons.Triangle(drawList, new UiRect(0.0f, 0.0f, 10.0f, 10.0f), direction, UiColor.White);
            drawList.Finish();

            Check($"a {direction} triangle draws", drawList.InstanceCount > 3,
                $"{drawList.InstanceCount} instances");
        }

        drawList.Reset();
        UiIcons.Triangle(drawList, new UiRect(0.0f, 0.0f, 0.0f, 0.0f), UiIconDirection.Up, UiColor.White);
        drawList.Finish();
        Check("an empty box draws nothing", drawList.InstanceCount == 0, $"{drawList.InstanceCount}");

        drawList.Reset();
        UiIcons.Check(drawList, new UiRect(0.0f, 0.0f, 12.0f, 12.0f), UiColor.White);
        drawList.Finish();
        Check("a check mark draws", drawList.InstanceCount > 5, $"{drawList.InstanceCount} instances");

        drawList.Reset();
        UiIcons.Cross(drawList, new UiRect(0.0f, 0.0f, 10.0f, 10.0f), UiColor.White);
        drawList.Finish();
        Check("a cross draws", drawList.InstanceCount > 5, $"{drawList.InstanceCount} instances");

        // A triangle is stacked rows, so it must stay inside the box it was given -- a row that overhung
        // would poke out of whatever the icon sits in.
        drawList.Reset();
        var bounds = new UiRect(4.0f, 6.0f, 10.0f, 8.0f);
        UiIcons.Triangle(drawList, bounds, UiIconDirection.Right, UiColor.White);
        drawList.Finish();

        var inside = true;
        foreach (var instance in drawList.Instances)
        {
            // Rect is (minX, minY, maxX, maxY), not position and size. Half a pixel of slack on the far
            // edges, because consecutive rows overlap slightly so no seam shows between them.
            inside &= instance.Rect.X >= bounds.X - 0.01f
                && instance.Rect.Y >= bounds.Y - 0.01f
                && instance.Rect.Z <= bounds.Right + 0.6f
                && instance.Rect.W <= bounds.Bottom + 0.6f;
        }

        Check("a triangle stays inside its box", inside, bounds.ToString());
    }
}

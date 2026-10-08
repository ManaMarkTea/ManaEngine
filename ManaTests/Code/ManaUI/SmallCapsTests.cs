using ManaUI;

namespace ManaTests;

/// <summary>A capitals-only face draws lower case as small capitals, at whatever sizes it is asked for.</summary>
public static class SmallCapsChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        var theme = UiTheme.Kenney();
        theme.SetFontFamily("Roboto");
        check("changing Kenney's font replaces both body and heading faces",
            theme.FontFamily == "Roboto" && theme.FontFamilyMedium == "Roboto", "");
        check("neither overridden face retains small capitals",
            fonts.Get(theme.FontFamily, theme.FontSize).SmallCapsFont == null
            && fonts.Get(theme.FontFamilyMedium, theme.FontSizeHeading).SmallCapsFont == null, "");

        ManaFont big;
        try
        {
            // Asked in an order that once broke it: the size a small-caps sibling takes (12 for a 15) is requested first.
            fonts.Get("KenneyFuture", 12.0f);
            big = fonts.Get("KenneyFuture", 15.0f);
        }
        catch (FileNotFoundException)
        {
            check("the Kenney fonts are found (skipped: not in the search path)", true, "");
            return;
        }

        var twelve = fonts.Get("KenneyFuture", 12.0f);
        check("each size of a capitals-only face has its small-capitals sibling", big.SmallCapsFont != null && twelve.SmallCapsFont != null, "");
        check("a lower-case letter is narrower than its capital", big.GetGlyph('a').Advance < big.GetGlyph('A').Advance - 0.5f,
            $"{big.GetGlyph('a').Advance} vs {big.GetGlyph('A').Advance}");
        check("a capital is not shrunk", MathF.Abs(big.GetGlyph('A').Advance - fonts.Get("KenneyFuture", 15.0f).GetGlyph('A').Advance) < 0.01f, "");
        check("an ordinary face is left alone", fonts.Get("Roboto", 15.0f).SmallCapsFont == null, "");
    }
}

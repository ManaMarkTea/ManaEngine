using ManaUI;

namespace ManaTests;

/// <summary>The multi-line editor: typing, lines, movement, selection, indent, undo and the hooks a host uses.</summary>
public static class TextAreaChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var area = new TextArea { Width = 400, Height = 200 };
        root.Content = new StackPanel().Children(area);
        root.UpdateLayout(new Vector2(600, 400));
        root.FocusManager.Focus(area);

        void Type(string text) => root.Input.InjectTextInput(text);
        void Key(UiKey key, UiModifiers modifiers = UiModifiers.None) => root.Input.InjectKeyDown(key, modifiers);
        void Reset(string text)
        {
            area.Text = text;
            area.CaretIndex = text.Length;
        }

        // -------------------------------------------------------------- typing and lines
        Reset(string.Empty);
        Type("ab");
        Key(UiKey.Enter);
        Type("cd");
        Check("typing and Enter build lines", area.Text == "ab\ncd" && area.LineCount == 2, area.Text);
        Check("the caret reports a 1-based line and column", area.CaretLine == 2 && area.CaretColumn == 3, $"{area.CaretLine}:{area.CaretColumn}");

        Reset("a\r\nb\tc");
        Check("line ends are normalised and tabs become spaces", area.Text == "a\nb    c" && area.LineCount == 2, area.Text.Replace("\n", "|"));

        // -------------------------------------------------------------- movement
        Reset("abc\nde\nfghij");
        area.CaretIndex = 2;
        Key(UiKey.Down);
        Check("Down keeps the column where it can", area.CaretLine == 2 && area.CaretColumn == 3, $"{area.CaretLine}:{area.CaretColumn}");
        Key(UiKey.Down);
        Check("passing a short line and on remembers the wider column", area.CaretLine == 3 && area.CaretColumn == 3, $"{area.CaretLine}:{area.CaretColumn}");
        Key(UiKey.Home);
        Check("Home goes to the line start", area.CaretColumn == 1);
        Key(UiKey.End);
        Check("End goes to the line end", area.CaretColumn == 6);
        Key(UiKey.Up);
        Key(UiKey.Up);
        Key(UiKey.Up);
        Check("Up from the first line goes to the very start", area.CaretIndex == 0);

        Reset("one two  three");
        area.CaretIndex = 0;
        Key(UiKey.Right, UiModifiers.Control);
        Check("Ctrl+Right jumps a word", area.CaretIndex == 3, area.CaretIndex.ToString());
        Key(UiKey.Right, UiModifiers.Control);
        Key(UiKey.Left, UiModifiers.Control);
        Check("Ctrl+Left jumps back to a word start", area.CaretIndex == 4, area.CaretIndex.ToString());

        // -------------------------------------------------------------- selection and editing
        // One repeated letter, so the columns line up whether or not the test font is monospace.
        Reset("xxxxx\nxxxxx");
        area.CaretIndex = 3;
        Key(UiKey.Down, UiModifiers.Shift);
        Check("Shift+Down selects across lines", area.HasSelection && area.SelectedText == "xx\nxxx", area.SelectedText.Replace("\n", "|"));
        Type("X");
        Check("typing replaces the selection", area.Text == "xxxXxx", area.Text);

        Reset("abc");
        Key(UiKey.A, UiModifiers.Control);
        Check("Ctrl+A selects all", area.SelectedText == "abc");
        Key(UiKey.Backspace);
        Check("Backspace deletes the selection", area.Text.Length == 0);

        Reset("a\nb");
        area.CaretIndex = 2;
        Key(UiKey.Backspace);
        Check("Backspace at a line start joins lines", area.Text == "ab" && area.LineCount == 1, area.Text);
        area.CaretIndex = 0;
        Key(UiKey.Delete);
        Check("Delete removes the next character", area.Text == "b", area.Text);

        // -------------------------------------------------------------- indentation
        Reset("    x");
        Key(UiKey.Enter);
        Check("Enter keeps the line's indent", area.Text == "    x\n    " && area.CaretColumn == 5, area.Text.Replace("\n", "|"));

        Reset("for {");
        Key(UiKey.Enter);
        Check("Enter after { goes one level deeper", area.Text == "for {\n    " && area.CaretColumn == 5, area.Text.Replace("\n", "|"));

        Reset("f {}");
        area.CaretIndex = 3;
        Key(UiKey.Enter);
        Check("Enter between {} opens the braces onto their own lines",
            area.Text == "f {\n    \n}" && area.CaretLine == 2 && area.CaretColumn == 5, area.Text.Replace("\n", "|"));

        Reset("f {\n    ");
        Type("}");
        Check("typing } on an indent-only line pulls it back a level", area.Text == "f {\n}", area.Text.Replace("\n", "|"));

        Reset("x");
        area.CaretIndex = 0;
        Key(UiKey.Tab);
        Check("Tab inserts spaces to the next tab stop", area.Text == "    x", area.Text);
        Reset("        x");
        area.CaretIndex = 8;
        Key(UiKey.Backspace);
        Check("Backspace in indent removes a level at once", area.Text == "    x", area.Text);

        Reset("a\nb\nc");
        Key(UiKey.A, UiModifiers.Control);
        Key(UiKey.Tab);
        Check("Tab over several lines indents each", area.Text == "    a\n    b\n    c", area.Text.Replace("\n", "|"));
        Key(UiKey.Tab, UiModifiers.Shift);
        Check("Shift+Tab dedents them", area.Text == "a\nb\nc", area.Text.Replace("\n", "|"));

        // -------------------------------------------------------------- undo
        Reset(string.Empty);
        Type("a");
        Type("b");
        Type("c");
        Key(UiKey.Z, UiModifiers.Control);
        Check("a run of typing undoes as one step", area.Text.Length == 0, area.Text);
        Key(UiKey.Y, UiModifiers.Control);
        Check("and redoes", area.Text == "abc", area.Text);

        Reset("keep");
        area.CaretIndex = 4;
        Key(UiKey.Enter);
        Type("x");
        Key(UiKey.Z, UiModifiers.Control);
        Check("a new line and what follows are separate steps", area.Text == "keep\n", area.Text.Replace("\n", "|"));
        Key(UiKey.Z, UiModifiers.Control);
        Check("undo reaches the original", area.Text == "keep", area.Text);

        Reset("let a = ra");
        area.ReplaceRange(8, 2, "radial");
        Check("ReplaceRange swaps a span and puts the caret after it", area.Text == "let a = radial" && area.CaretIndex == 14, $"{area.Text} {area.CaretIndex}");
        Key(UiKey.Z, UiModifiers.Control);
        Check("and is a single undo step", area.Text == "let a = ra", area.Text);

        // The commands a menu calls, which are the same ones the shortcuts reach.
        Reset("one two");
        area.CaretIndex = 0;
        Key(UiKey.Right, UiModifiers.Shift);
        Key(UiKey.Right, UiModifiers.Shift);
        Key(UiKey.Right, UiModifiers.Shift);
        area.Cut();
        Check("Cut removes the selection", area.Text == " two", area.Text);
        area.CaretIndex = area.Text.Length;
        area.Paste();
        Check("Paste inserts what Cut took", area.Text == " twoone", area.Text);
        Check("an edit makes Undo available", area.CanUndo);
        area.Undo();
        area.Undo();
        Check("Undo steps back through the edits", area.Text == "one two", area.Text);
        Check("and Redo becomes available", area.CanRedo);
        area.Redo();
        Check("Redo replays them", area.Text == " two", area.Text);
        area.SelectAll();
        area.Copy();
        Check("Copy puts the selection on the clipboard", TextArea.Clipboard.Text == " two", TextArea.Clipboard.Text);

        // -------------------------------------------------------------- caret position and hover
        Reset("abc\ndef");
        root.UpdateLayout(new Vector2(600, 400));
        root.Render();
        area.CaretIndex = 0;
        var topLeft = area.CaretBounds;
        area.CaretIndex = 4;
        var lineTwoStart = area.CaretBounds;
        area.CaretIndex = 7;
        var lineTwoEnd = area.CaretBounds;
        Check("CaretBounds follows the caret across and down",
            topLeft != null && lineTwoStart != null && lineTwoEnd != null
            && lineTwoStart.Value.Y > topLeft.Value.Y && lineTwoEnd.Value.X > lineTwoStart.Value.X && lineTwoEnd.Value.Y == lineTwoStart.Value.Y,
            $"{topLeft} {lineTwoStart} {lineTwoEnd}");
        // -------------------------------------------------------------- inline swatches
        Reset("c = #fff end");
        root.UpdateLayout(new Vector2(600, 400));
        root.Render();
        area.CaretIndex = 12;
        var plainEnd = area.CaretBounds!.Value.X;
        area.CaretIndex = 7;
        var plainBeforeSwatch = area.CaretBounds!.Value.X;
        area.LineSwatches = line => line.Contains("#fff") ? new[] { new TextSwatch(8, new Vector4(1, 1, 1, 1)) } : Array.Empty<TextSwatch>();
        root.UpdateLayout(new Vector2(600, 400));
        root.Render();
        area.CaretIndex = 12;
        var swatchedEnd = area.CaretBounds!.Value.X;
        area.CaretIndex = 7;
        var swatchedBefore = area.CaretBounds!.Value.X;
        Check("a swatch pushes the rest of the line along", swatchedEnd > plainEnd + 8.0f, $"{plainEnd} -> {swatchedEnd}");
        Check("and leaves what comes before it where it was", Math.Abs(swatchedBefore - plainBeforeSwatch) < 0.01f);

        var clicks = new List<TextSwatchClick>();
        area.SwatchClicked += (_, click) => clicks.Add(click);
        var lineY = area.Bounds.Y + 12;
        var swatchX = area.Bounds.X + (plainBeforeSwatch - area.Bounds.X) + 4.0f + 14.0f;
        area.CaretIndex = 0;
        root.Input.InjectPointerMoved(new Vector2(swatchX, lineY));
        root.Input.InjectPointerPressed(new Vector2(swatchX, lineY), PointerButton.Left, 100.0);
        root.Input.InjectPointerReleased(new Vector2(swatchX, lineY), PointerButton.Left);
        Check("clicking a swatch reports it instead of moving the caret", clicks.Count == 1 && clicks[0].Index == 8 && clicks[0].Line == 0 && area.CaretIndex == 0, $"{clicks.Count} caret {area.CaretIndex}");
        area.LineSwatches = null;

        Check("the hover tint is on by default and can be switched off", area.ShowHoverTint);
        area.ShowHoverTint = false;
        Check("switching the hover tint off sticks", !area.ShowHoverTint);
        area.ShowHoverTint = true;

        // -------------------------------------------------------------- host hooks
        var changed = 0;
        area.TextChanged += (_, _) => changed++;
        Reset(string.Empty);
        changed = 0;
        Type("q");
        Check("TextChanged fires on an edit", changed == 1, changed.ToString());

        area.IsReadOnly = true;
        Type("zzz");
        Key(UiKey.Backspace);
        Check("a read-only area refuses edits", area.Text == "q", area.Text);
        area.IsReadOnly = false;

        area.ErrorLine = 3;
        Check("an error line can be set and cleared", area.ErrorLine == 3);
        area.ErrorLine = 0;

        var coloured = 0;
        area.LineColorizer = line =>
        {
            coloured++;
            return new[] { new TextSpanColor(0, Math.Min(1, line.Length), new Vector4(1, 0, 0, 1)) };
        };
        Reset("one\ntwo");
        root.UpdateLayout(new Vector2(600, 400));
        root.Render();
        Check("the colorizer is asked for each visible line when drawn", coloured >= 2, coloured.ToString());

        area.GoToLine(2);
        Check("GoToLine puts the caret at that line's start", area.CaretLine == 2 && area.CaretColumn == 1);

        // -------------------------------------------------------------- pointer
        area.LineColorizer = null;
        Reset("alpha\nbeta\ngamma");
        root.UpdateLayout(new Vector2(600, 400));
        root.Render();
        var inside = new Vector2(area.Bounds.X + 200, area.Bounds.Y + 12);
        root.Input.InjectPointerMoved(inside);
        root.Input.InjectPointerPressed(inside, PointerButton.Left, 100.0);
        root.Input.InjectPointerReleased(inside, PointerButton.Left);
        Check("clicking past a line's end puts the caret at its end", area.CaretLine == 1 && area.CaretColumn == 6, $"{area.CaretLine}:{area.CaretColumn}");

        var longText = string.Join("\n", Enumerable.Range(0, 400).Select(i => "line " + i));
        Reset(longText);
        root.UpdateLayout(new Vector2(600, 400));
        root.Render();
        Check("a long document is held without trouble", area.LineCount == 400 && area.CaretLine == 400, area.LineCount.ToString());
    }
}

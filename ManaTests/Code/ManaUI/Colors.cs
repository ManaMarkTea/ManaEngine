using ManaUI;

namespace ManaTests;

/// <summary>
/// Colour conversion, the spectrum, and the swatch-plus-popup field the inspector uses. The conversions get
/// the most attention: everything above them is a view of the same three numbers, so a rounding error there
/// shows up as a colour that drifts every time it is looked at.
/// </summary>
public static class ColorChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var time = 0.0;

        // ============================================================ conversion
        Console.WriteLine("  -- conversion --");

        var red = UiColor.FromHsv(0.0f, 1.0f, 1.0f);
        Check("hue 0 is red", red.X == 1.0f && red.Y == 0.0f && red.Z == 0.0f, red.ToString());

        var green = UiColor.FromHsv(120.0f, 1.0f, 1.0f);
        Check("hue 120 is green", green.Y == 1.0f && green.X == 0.0f && green.Z == 0.0f, green.ToString());

        var blue = UiColor.FromHsv(240.0f, 1.0f, 1.0f);
        Check("hue 240 is blue", blue.Z == 1.0f && blue.X == 0.0f && blue.Y == 0.0f, blue.ToString());

        Check("zero saturation is grey",
            UiColor.FromHsv(200.0f, 0.0f, 0.5f) is { X: 0.5f, Y: 0.5f, Z: 0.5f }, "grey");
        Check("zero value is black",
            UiColor.FromHsv(200.0f, 1.0f, 0.0f) is { X: 0.0f, Y: 0.0f, Z: 0.0f }, "black");

        // Round-tripping is what everything above these two functions depends on.
        var worst = 0.0f;
        for (var h = 0; h < 360; h += 7)
        {
            for (var s = 0; s <= 10; s++)
            {
                for (var v = 1; v <= 10; v++)
                {
                    var original = UiColor.FromHsv(h, s / 10.0f, v / 10.0f);
                    var hsv = UiColor.ToHsv(original);
                    var back = UiColor.FromHsv(hsv.X, hsv.Y, hsv.Z);

                    worst = MathF.Max(worst, MathF.Abs(back.X - original.X));
                    worst = MathF.Max(worst, MathF.Abs(back.Y - original.Y));
                    worst = MathF.Max(worst, MathF.Abs(back.Z - original.Z));
                }
            }
        }

        Check("RGB survives a round trip through HSV", worst < 0.001f, $"worst channel drift {worst:F6}");

        Check("hue wraps rather than clamping",
            UiColor.FromHsv(360.0f + 120.0f, 1.0f, 1.0f) == green, "480 is 120");
        Check("negative hue wraps too", UiColor.FromHsv(-120.0f, 1.0f, 1.0f) == blue, "-120 is 240");

        // Hex
        Check("hex round-trips", UiColor.ToHex(UiColor.FromHex("#3B82F6")) == "#3B82F6",
            UiColor.ToHex(UiColor.FromHex("#3B82F6")));
        Check("an opaque colour omits its alpha", !UiColor.ToHex(UiColor.White).Contains("FF", StringComparison.Ordinal)
            || UiColor.ToHex(UiColor.White).Length == 7, UiColor.ToHex(UiColor.White));
        Check("a translucent colour keeps its alpha",
            UiColor.ToHex(UiColor.White.WithAlpha(0.5f)).Length == 9,
            UiColor.ToHex(UiColor.White.WithAlpha(0.5f)));

        // ============================================================ spectrum
        Console.WriteLine("  -- spectrum --");

        var spectrum = new ColorSpectrum { Width = 200.0f };
        root.Content = spectrum;
        root.UpdateLayout(new Vector2(300, 300));

        Check("the square sits above the strips",
            spectrum.SquareRect.Bottom <= spectrum.HueRect.Y, "square then hue");
        Check("the alpha strip is last",
            spectrum.AlphaRect.Y >= spectrum.HueRect.Bottom, "hue then alpha");

        spectrum.Color = UiColor.FromHex("#3B82F6");
        var changes = 0;
        spectrum.ColorChanged += (_, _) => changes++;

        // Dragging the hue strip must move hue and leave saturation and value alone.
        var saturationBefore = spectrum.Saturation;
        var valueBefore = spectrum.Value;
        var huePoint = new Vector2(spectrum.HueRect.X + spectrum.HueRect.Width * 0.25f, spectrum.HueRect.Center.Y);

        root.Input.InjectPointerMoved(huePoint);
        root.Input.InjectPointerPressed(huePoint, PointerButton.Left, time);
        Check("clicking the hue strip sets the hue",
            MathF.Abs(spectrum.Hue - 90.0f) < 2.0f, $"{spectrum.Hue:F1} degrees");
        Check("hue alone moved",
            LayoutHelper.AreClose(spectrum.Saturation, saturationBefore)
            && LayoutHelper.AreClose(spectrum.Value, valueBefore),
            $"s {spectrum.Saturation:F3}, v {spectrum.Value:F3}");

        // A drag that wanders off the strip keeps editing the strip it started in. Moving up into the
        // square must still steer hue by the pointer's X, and must not touch saturation or value.
        var strayed = new Vector2(
            spectrum.HueRect.X + spectrum.HueRect.Width * 0.75f,
            spectrum.SquareRect.Center.Y);

        root.Input.InjectPointerMoved(strayed);
        Check("a drag that leaves the strip still steers it",
            MathF.Abs(spectrum.Hue - 270.0f) < 2.0f, $"{spectrum.Hue:F1} degrees");
        Check("and does not start editing what it strayed over",
            LayoutHelper.AreClose(spectrum.Saturation, saturationBefore)
            && LayoutHelper.AreClose(spectrum.Value, valueBefore),
            $"s {spectrum.Saturation:F3}, v {spectrum.Value:F3}");

        // Back to a known hue for the checks below.
        root.Input.InjectPointerMoved(huePoint);

        var committed = 0;
        spectrum.ColorCommitted += (_, _) => committed++;
        root.Input.InjectPointerReleased(huePoint, PointerButton.Left);
        Check("release commits once", committed == 1, $"{committed}");

        // The square: left is grey, right is saturated; top is bright, bottom is black.
        var square = spectrum.SquareRect;
        root.Input.InjectPointerMoved(new Vector2(square.Right - 1.0f, square.Y + 1.0f));
        root.Input.InjectPointerPressed(new Vector2(square.Right - 1.0f, square.Y + 1.0f), PointerButton.Left, time += 1.0);
        Check("the top-right corner is fully saturated and bright",
            spectrum.Saturation > 0.95f && spectrum.Value > 0.95f,
            $"s {spectrum.Saturation:F2}, v {spectrum.Value:F2}");

        // Dragged well past the corner, not to a pixel inside it. Clamping then gives exactly zero
        // saturation and value -- and only exact black loses its hue, so anything short of this passes
        // whether or not the hue is actually being preserved.
        var pastCorner = new Vector2(square.X - 50.0f, square.Bottom + 50.0f);
        root.Input.InjectPointerMoved(pastCorner);
        Check("dragging past the corner clamps to black",
            spectrum.Saturation == 0.0f && spectrum.Value == 0.0f,
            $"s {spectrum.Saturation}, v {spectrum.Value}");

        // Black has no hue to recover, so a picker that derived hue from RGB would reset here -- and the
        // corner is exactly where people drag to.
        var hueAtEdge = spectrum.Hue;
        Check("the hue survives a drag into black",
            MathF.Abs(hueAtEdge - 90.0f) < 2.0f, $"{hueAtEdge:F1} degrees");
        root.Input.InjectPointerReleased(pastCorner, PointerButton.Left);

        root.Input.InjectPointerMoved(new Vector2(square.Right - 1.0f, square.Y + 1.0f));
        root.Input.InjectPointerPressed(new Vector2(square.Right - 1.0f, square.Y + 1.0f), PointerButton.Left, time += 1.0);
        Check("and comes back with it", MathF.Abs(spectrum.Hue - hueAtEdge) < 0.01f, $"{spectrum.Hue:F1}");
        root.Input.InjectPointerReleased(new Vector2(square.Right - 1.0f, square.Y + 1.0f), PointerButton.Left);

        Check("the spectrum raised changes while dragging", changes > 3, $"{changes} changes");
        Check("the spectrum renders", root.Render().InstanceCount > 100,
            $"{root.Render().InstanceCount} instances");
        Check("still one draw call", root.Render().Batches.Count == 1,
            $"{root.Render().Batches.Count} batches");

        // ============================================================ picker
        Console.WriteLine("  -- picker --");

        var picker = new ColorPicker { Width = 220.0f, Color = UiColor.FromHex("#3B82F6") };
        root.Content = null;
        root.Content = picker;
        root.UpdateLayout(new Vector2(300, 400));

        var channels = FindAll<NumericBox>(picker);
        Check("the picker has a field per channel", channels.Count == 4, $"{channels.Count} fields");
        Check("channels are shown as bytes",
            MathF.Abs(channels[0].Value - 59.0f) < 1.0f, $"R {channels[0].Value}");

        // Editing a channel must move the spectrum and the hex together -- they are all one colour.
        channels[0].Value = 255.0f;
        Check("editing a channel updates the colour",
            MathF.Abs(picker.Color.X - 1.0f) < 0.01f, picker.Color.ToString());

        var hexBox = FindAll<TextBox>(picker).First(box => box is not NumericBox);
        Check("the hex field follows a channel edit",
            hexBox.Text.StartsWith("#FF", StringComparison.OrdinalIgnoreCase), hexBox.Text);

        // And the other way round.
        hexBox.Text = "#00FF00";
        hexBox.Focus();
        root.Input.InjectKeyDown(UiKey.Enter);
        Check("typing hex updates the colour",
            picker.Color.Y > 0.99f && picker.Color.X < 0.01f, picker.Color.ToString());
        Check("the channels follow the hex",
            MathF.Abs(channels[1].Value - 255.0f) < 1.0f, $"G {channels[1].Value}");
        root.FocusManager.Focus(null);

        // Junk in the hex field reverts rather than blanking the colour.
        var beforeJunk = picker.Color;
        hexBox.Text = "not a colour";
        hexBox.Focus();
        root.Input.InjectKeyDown(UiKey.Enter);
        Check("unparseable hex reverts", picker.Color == beforeJunk, picker.Color.ToString());
        Check("and the field is put back", hexBox.Text.StartsWith('#'), hexBox.Text);
        root.FocusManager.Focus(null);

        picker.ShowAlpha = false;
        root.UpdateLayout(new Vector2(300, 400));
        Check("hiding alpha drops its field", FindAll<NumericBox>(picker).Count == 3,
            $"{FindAll<NumericBox>(picker).Count} fields");

        // ============================================================ field and popup
        Console.WriteLine("  -- field --");

        var field = new ColorField { Color = UiColor.FromHex("#E5534B"), Width = 140.0f };
        root.Content = null;
        root.Content = field;
        root.UpdateLayout(new Vector2(400, 300));

        Check("the field starts closed", !field.IsOpen);
        Check("the field renders its swatch", root.Render().InstanceCount > 0,
            $"{root.Render().InstanceCount} instances");

        var fieldChanges = 0;
        Vector4 lastColor = default;
        field.ColorChanged += (_, color) =>
        {
            fieldChanges++;
            lastColor = color;
        };

        root.Input.InjectPointerMoved(field.Bounds.Center);
        root.Input.InjectPointerPressed(field.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(field.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(400, 300));

        Check("clicking opens the picker", field.IsOpen);
        Check("the popup renders over the field", root.Render().InstanceCount > 100,
            $"{root.Render().InstanceCount} instances");

        var popupSpectrum = FindAll<ColorSpectrum>(root).FirstOrDefault();
        Check("the popup contains a spectrum", popupSpectrum != null);

        if (popupSpectrum != null)
        {
            // Editing in the popup writes back through the field, which is what a bound property sees.
            popupSpectrum.Hue = 200.0f;
            Check("editing the popup updates the field", fieldChanges > 0, $"{fieldChanges} changes");
            Check("the field carries the new colour", field.Color == lastColor, field.Color.ToString());
        }

        root.Input.InjectKeyDown(UiKey.Escape);
        root.UpdateLayout(new Vector2(400, 300));
        Check("escape closes the picker", !field.IsOpen);

        // A value set from outside must reach an open picker rather than being overwritten by it.
        field.Open();
        root.UpdateLayout(new Vector2(400, 300));
        field.Color = UiColor.FromHex("#57A75B");

        var openSpectrum = FindAll<ColorSpectrum>(root).FirstOrDefault();
        Check("a colour set while open reaches the picker",
            openSpectrum != null && openSpectrum.Color.Y > openSpectrum.Color.X,
            openSpectrum?.Color.ToString() ?? "no spectrum");

        field.Close();
        root.UpdateLayout(new Vector2(400, 300));
        Check("closing settles the value", !field.IsOpen);
    }

    private static List<T> FindAll<T>(UiElement? element) where T : UiElement
    {
        var found = new List<T>();
        Walk(element, found);
        return found;
    }

    private static void Walk<T>(UiElement? element, List<T> found) where T : UiElement
    {
        if (element == null)
        {
            return;
        }

        if (element is T match)
        {
            found.Add(match);
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            Walk(element.GetVisualChild(i), found);
        }
    }
}

/// <summary>
/// Theme tokens that have to differ from each other to do their job. A colour that reads correctly in one
/// theme and vanishes in the other is the failure mode here, so both are checked.
/// </summary>
public static class ThemeContrastChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        foreach (var (name, theme) in new (string, UiTheme)[] { ("dark", UiTheme.Dark()), ("light", UiTheme.Light()) })
        {
            // The one that was broken: SurfaceRaised is lighter than the surface in a dark theme and the
            // same white in a light one, so a stripe drawn with it was invisible in light mode.
            Check($"{name}: an alternating row is visible against the surface",
                Distance(theme.RowAlternate, theme.Surface) > 0.01f,
                $"{Hex(theme.RowAlternate)} vs {Hex(theme.Surface)}");

            Check($"{name}: and against the panel background",
                Distance(theme.RowAlternate, theme.Background) > 0.005f,
                $"{Hex(theme.RowAlternate)} vs {Hex(theme.Background)}");

            // A heading row has to be distinguishable from both the stripe and the plain rows.
            Check($"{name}: a table heading stands apart from the rows",
                Distance(theme.SurfaceAlt, theme.RowAlternate) > 0.005f,
                $"{Hex(theme.SurfaceAlt)} vs {Hex(theme.RowAlternate)}");

            Check($"{name}: text is readable on the surface",
                Distance(theme.Text, theme.Surface) > 0.3f, $"{Hex(theme.Text)} on {Hex(theme.Surface)}");

            Check($"{name}: muted text is still readable",
                Distance(theme.TextMuted, theme.Surface) > 0.15f,
                $"{Hex(theme.TextMuted)} on {Hex(theme.Surface)}");

            Check($"{name}: borders are visible against the surface",
                Distance(theme.Border, theme.Surface) > 0.01f, $"{Hex(theme.Border)} vs {Hex(theme.Surface)}");

            Check($"{name}: text on the accent is readable",
                Distance(theme.TextOnAccent, theme.Accent) > 0.3f,
                $"{Hex(theme.TextOnAccent)} on {Hex(theme.Accent)}");
        }

        // The light theme is meant to read as a tool rather than a document, which means every surface
        // carries a little blue rather than being neutral grey.
        var light = UiTheme.Light();
        Check("the light theme is cool rather than neutral",
            light.Background.Z > light.Background.X + 0.01f,
            $"{Hex(light.Background)} has more blue than red");

        Check("and its accent is a saturated version of that, not a separate hue",
            light.Accent.Z > light.Accent.X + 0.2f, Hex(light.Accent));
    }

    /// <summary>Straight RGB distance. Crude next to a perceptual metric, and enough to catch a token
    /// that is literally the same colour as what it sits on.</summary>
    private static float Distance(Vector4 a, Vector4 b)
    {
        var dr = a.X - b.X;
        var dg = a.Y - b.Y;
        var db = a.Z - b.Z;
        return MathF.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private static string Hex(Vector4 c) =>
        $"#{(int)(c.X * 255):X2}{(int)(c.Y * 255):X2}{(int)(c.Z * 255):X2}";
}

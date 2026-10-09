using ManaUI;

namespace ManaTests;

/// <summary>
/// Headless exercise of the input layer. The point of ManaUI having no graphics dependency: routing,
/// capture and focus can all be driven and asserted without a window.
/// </summary>
public static class InputChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var time = 0.0;

        // -------------------------------------------------------------- icon buttons
        var iconOnly = new Button().Icon(new Border { Width = 16, Height = 16 });
        Check("an icon alone is the button's content", iconOnly.Child is Border);
        var withText = new Button("Buy").Icon(new Border { Width = 16, Height = 16 });
        Check("an icon and text sit in a row, the icon first",
            withText.Child is StackPanel { Children.Count: 2 } row && row.Children[0] is Border && row.Children[1] is TextBlock);
        withText.Icon = null;
        Check("dropping the icon leaves the text", withText.Child is TextBlock { Text: "Buy" });
        withText.Highlighted();
        Check("a highlighted control reads as selected without a pointer", withText.IsHighlighted && !withText.IsPointerOver);

        // -------------------------------------------------------------- hover + click
        var button = new Button("Click me") { Width = 120, Height = 30 };
        var canvas = new Canvas();
        button.EnsureLayoutSlot().CanvasLeft = 50;
        button.EnsureLayoutSlot().CanvasTop = 40;
        canvas.Children.Add(button);
        root.Content = canvas;
        root.UpdateLayout(new Vector2(400, 300));

        Check("button arranged", button.Bounds == new UiRect(50, 40, 120, 30), button.Bounds.ToString());
        Check("hit testing can claim a control before hover updates",
            ReferenceEquals(root.HitTest(100, 50), button),
            root.HitTest(100, 50)?.GetType().Name ?? "null");

        var clicks = 0;
        button.Click += (_, _) => clicks++;

        root.Input.InjectPointerMoved(new Vector2(10, 10));
        Check("no hover off the button", !button.IsPointerOver && root.Input.HoveredElement == null);
        Check("IsPointerOverUi false over empty space", !root.Input.IsPointerOverUi);

        root.Input.InjectPointerMoved(new Vector2(100, 50));
        Check("hover enters button", button.IsPointerOver, root.Input.HoveredElement?.ToString() ?? "null");
        Check("IsPointerOverUi true over button", root.Input.IsPointerOverUi);

        root.Input.InjectPointerPressed(new Vector2(100, 50), PointerButton.Left, time);
        Check("press sets IsPressed", button.IsPressed);
        Check("press captures pointer", root.PointerCaptureTarget == button);
        Check("no click until release", clicks == 0);

        root.Input.InjectPointerReleased(new Vector2(100, 50), PointerButton.Left);
        Check("release fires Click", clicks == 1, $"{clicks}");
        Check("release clears pressed", !button.IsPressed);
        Check("release frees capture", root.PointerCaptureTarget == null);

        // Dragging off before releasing must abort, the standard desktop contract.
        root.Input.InjectPointerPressed(new Vector2(100, 50), PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(new Vector2(300, 250), PointerButton.Left);
        Check("drag-off aborts the click", clicks == 1, $"{clicks}");

        // IsPointerOverUi is what tells the layer underneath -- ImGui, and the game mode's own picking --
        // to stand down. It has to stay true for the whole of a drag, not just while the pointer is over
        // something: ImGui polls the mouse rather than consuming events, so a gap here means a click that
        // started on a ManaUI control also lands in whatever was behind it.
        root.Input.InjectPointerMoved(new Vector2(100, 50));
        root.Input.InjectPointerPressed(new Vector2(100, 50), PointerButton.Left, time += 1.0);
        root.Input.InjectPointerMoved(new Vector2(320, 260));
        Check("a captured drag still owns the pointer off the control", root.Input.IsPointerOverUi,
            $"hover {root.Input.HoveredElement?.GetType().Name ?? "null"}, "
            + $"capture {root.PointerCaptureTarget?.GetType().Name ?? "null"}");

        root.Input.InjectPointerReleased(new Vector2(320, 260), PointerButton.Left);
        root.Input.InjectPointerMoved(new Vector2(320, 260));
        Check("and hands it back on release", !root.Input.IsPointerOverUi,
            $"capture {root.PointerCaptureTarget?.GetType().Name ?? "null"}");

        // -------------------------------------------------------------- double click
        root.Input.InjectPointerMoved(new Vector2(100, 50));
        var lastCount = 0;
        button.PointerPressed += (_, e) => lastCount = e.ClickCount;

        root.Input.InjectPointerPressed(new Vector2(100, 50), PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(new Vector2(100, 50), PointerButton.Left);
        Check("first click counts as 1", lastCount == 1, $"{lastCount}");

        root.Input.InjectPointerPressed(new Vector2(100, 50), PointerButton.Left, time += 0.1);
        root.Input.InjectPointerReleased(new Vector2(100, 50), PointerButton.Left);
        Check("quick second click counts as 2", lastCount == 2, $"{lastCount}");

        root.Input.InjectPointerPressed(new Vector2(100, 50), PointerButton.Left, time += 5.0);
        root.Input.InjectPointerReleased(new Vector2(100, 50), PointerButton.Left);
        Check("slow click resets to 1", lastCount == 1, $"{lastCount}");

        // -------------------------------------------------------------- routing order
        var order = new List<string>();
        var outer = new Canvas { Background = UiColor.White };
        var inner = new Button("inner") { Width = 80, Height = 24 };
        inner.EnsureLayoutSlot().CanvasLeft = 10;
        inner.EnsureLayoutSlot().CanvasTop = 10;
        outer.Children.Add(inner);
        root.Content = null;
        root.Content = outer;
        root.UpdateLayout(new Vector2(400, 300));

        outer.PreviewPointerPressed += (_, _) => order.Add("outer-preview");
        inner.PreviewPointerPressed += (_, _) => order.Add("inner-preview");
        inner.PointerPressed += (_, _) => order.Add("inner-bubble");
        outer.PointerPressed += (_, _) => order.Add("outer-bubble");

        root.Input.InjectPointerMoved(new Vector2(50, 20));
        root.Input.InjectPointerPressed(new Vector2(50, 20), PointerButton.Left, time += 5.0);

        // Button marks the press handled, so the outer bubble handler must not run.
        Check("tunnels root->target then bubbles",
            order.Count >= 3 && order[0] == "outer-preview" && order[1] == "inner-preview" && order[2] == "inner-bubble",
            string.Join(" > ", order));
        Check("handled stops the bubble", !order.Contains("outer-bubble"), string.Join(" > ", order));

        root.Input.InjectPointerReleased(new Vector2(50, 20), PointerButton.Left);

        // An ancestor can swallow an event before the child ever sees it.
        order.Clear();
        var swallow = new EventHandler<UiPointerEventArgs>((_, e) => e.Handled = true);
        outer.PreviewPointerPressed += swallow;
        root.Input.InjectPointerPressed(new Vector2(50, 20), PointerButton.Left, time += 5.0);
        Check("preview handler can swallow", !order.Contains("inner-preview") && !order.Contains("inner-bubble"),
            string.Join(" > ", order));
        outer.PreviewPointerPressed -= swallow;
        root.Input.InjectPointerReleased(new Vector2(50, 20), PointerButton.Left);

        // -------------------------------------------------------------- focus + keyboard
        var b1 = new Button("one") { Width = 60, Height = 24 };
        var b2 = new Button("two") { Width = 60, Height = 24 };
        var b3 = new CheckBox("three");
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(b1);
        stack.Children.Add(b2);
        stack.Children.Add(b3);
        root.Content = null;
        root.Content = stack;
        root.UpdateLayout(new Vector2(400, 300));

        Check("nothing focused initially", root.FocusManager.Focused == null);

        root.Input.InjectKeyDown(UiKey.Tab);
        Check("tab focuses first", root.FocusManager.Focused == b1, root.FocusManager.Focused?.ToString() ?? "null");
        Check("focused element knows it", b1.IsFocused);

        root.Input.InjectKeyDown(UiKey.Tab);
        Check("tab advances", root.FocusManager.Focused == b2);
        Check("previous lost focus", !b1.IsFocused);

        root.Input.InjectKeyDown(UiKey.Tab, UiModifiers.Shift);
        Check("shift-tab goes back", root.FocusManager.Focused == b1);

        root.Input.InjectKeyDown(UiKey.Tab, UiModifiers.Shift);
        Check("shift-tab wraps to last", root.FocusManager.Focused == b3, root.FocusManager.Focused?.ToString() ?? "null");

        var b1Clicks = 0;
        b1.Click += (_, _) => b1Clicks++;
        root.FocusManager.Focus(b1);
        root.Input.InjectKeyDown(UiKey.Space);
        Check("space activates focused button", b1Clicks == 1, $"{b1Clicks}");
        root.Input.InjectKeyDown(UiKey.Enter);
        Check("enter activates focused button", b1Clicks == 2, $"{b1Clicks}");

        // Disabled controls are skipped entirely.
        b2.IsEnabled = false;
        root.FocusManager.Focus(b1);
        root.Input.InjectKeyDown(UiKey.Tab);
        Check("tab skips disabled", root.FocusManager.Focused == b3, root.FocusManager.Focused?.ToString() ?? "null");

        var b2Clicks = 0;
        b2.Click += (_, _) => b2Clicks++;
        root.UpdateLayout(new Vector2(400, 300));
        root.Input.InjectPointerMoved(b2.Bounds.Center);
        root.Input.InjectPointerPressed(b2.Bounds.Center, PointerButton.Left, time += 5.0);
        root.Input.InjectPointerReleased(b2.Bounds.Center, PointerButton.Left);
        Check("disabled button ignores clicks", b2Clicks == 0, $"{b2Clicks}");

        // Focus must not survive its holder being removed from the tree.
        root.FocusManager.Focus(b1);
        stack.Children.Remove(b1);
        root.UpdateLayout(new Vector2(400, 300));
        Check("focus drops when element detached", root.FocusManager.Focused == null,
            root.FocusManager.Focused?.ToString() ?? "null");

        // Text and numeric editors must navigate despite capturing keyboard input for typing.
        var scale = new VectorBox();
        var nextText = new TextBox();
        var fields = new StackPanel();
        fields.Children.Add(scale);
        fields.Children.Add(nextText);
        root.Content = fields;
        root.UpdateLayout(new Vector2(400, 300));
        root.FocusManager.Focus(scale.Field(0));
        scale.Field(0).SelectAll();
        root.Input.InjectTextInput("2.5");
        var commits = 0;
        scale.ValueCommitted += (_, _) => commits++;
        Check("Tab from X is consumed", root.Input.InjectKeyDown(UiKey.Tab));
        Check("Tab moves X to Y and commits the edited value",
            root.FocusManager.Focused == scale.Field(1) && scale.Value.X == 2.5f && commits == 1);
        root.Input.InjectKeyDown(UiKey.Tab);
        Check("Tab moves Y to Z", root.FocusManager.Focused == scale.Field(2));
        root.Input.InjectKeyDown(UiKey.Tab);
        Check("Tab moves Z to the next text box", root.FocusManager.Focused == nextText);
        root.Input.InjectKeyDown(UiKey.Tab, UiModifiers.Shift);
        Check("Shift-Tab moves text box back to Z", root.FocusManager.Focused == scale.Field(2));
        root.Input.InjectKeyDown(UiKey.Tab, UiModifiers.Shift);
        Check("Shift-Tab moves Z to Y", root.FocusManager.Focused == scale.Field(1));
        root.Input.InjectKeyDown(UiKey.Tab, UiModifiers.Shift);
        Check("Shift-Tab moves Y to X", root.FocusManager.Focused == scale.Field(0));
        Check("text editing still suppresses host shortcuts", scale.Field(0).WantsAllKeyboardInput);

        var handleTab = new EventHandler<UiKeyEventArgs>((_, e) =>
        {
            if (e.Key == UiKey.Tab) e.Handled = true;
        });
        scale.Field(0).KeyDown += handleTab;
        Check("handled Tab stays in the editor",
            root.Input.InjectKeyDown(UiKey.Tab) && root.FocusManager.Focused == scale.Field(0));
        scale.Field(0).KeyDown -= handleTab;

        // -------------------------------------------------------------- checkbox
        var box = new CheckBox("toggle");
        var changes = 0;
        box.CheckedChanged += (_, _) => changes++;
        root.Content = null;
        root.Content = box;
        root.UpdateLayout(new Vector2(400, 300));

        root.Input.InjectPointerMoved(box.Bounds.Center);
        root.Input.InjectPointerPressed(box.Bounds.Center, PointerButton.Left, time += 5.0);
        root.Input.InjectPointerReleased(box.Bounds.Center, PointerButton.Left);
        Check("click toggles checkbox", box.IsChecked && changes == 1, $"checked={box.IsChecked} changes={changes}");

        root.Input.InjectPointerPressed(box.Bounds.Center, PointerButton.Left, time += 5.0);
        root.Input.InjectPointerReleased(box.Bounds.Center, PointerButton.Left);
        Check("second click untoggles", !box.IsChecked && changes == 2, $"checked={box.IsChecked} changes={changes}");

        // -------------------------------------------------------------- slider + capture
        var slider = new Slider { Minimum = 0, Maximum = 100, Width = 200, Height = 24 };
        root.Content = null;
        root.Content = slider;
        root.UpdateLayout(new Vector2(400, 300));

        var track = slider.Bounds;
        root.Input.InjectPointerMoved(track.Center);
        root.Input.InjectPointerPressed(track.Center, PointerButton.Left, time += 5.0);
        Check("slider jumps to click", MathF.Abs(slider.Value - 50.0f) < 2.0f, $"{slider.Value:F2}");
        Check("slider captures pointer", root.PointerCaptureTarget == slider);

        // The decisive capture test: the pointer leaves the control entirely and must still be tracked.
        root.Input.InjectPointerMoved(new Vector2(track.Right + 500, track.Y - 400));
        Check("drag beyond bounds still tracks", slider.Value >= 99.0f, $"{slider.Value:F2}");

        root.Input.InjectPointerMoved(new Vector2(track.X - 500, track.Y));
        Check("drag past the other end clamps", slider.Value <= 1.0f, $"{slider.Value:F2}");

        root.Input.InjectPointerReleased(new Vector2(track.X - 500, track.Y), PointerButton.Left);
        Check("release frees slider capture", root.PointerCaptureTarget == null);

        slider.Value = 50.0f;
        slider.Step = 10.0f;
        slider.Value = 53.0f;
        Check("step snaps value", MathF.Abs(slider.Value - 50.0f) < 0.01f, $"{slider.Value:F2}");

        root.FocusManager.Focus(slider);
        root.Input.InjectKeyDown(UiKey.Right);
        Check("arrow key steps slider", MathF.Abs(slider.Value - 60.0f) < 0.01f, $"{slider.Value:F2}");
        root.Input.InjectKeyDown(UiKey.Home);
        Check("home jumps to minimum", MathF.Abs(slider.Value) < 0.01f, $"{slider.Value:F2}");

        // -------------------------------------------------------------- hover chain
        var panel = new Canvas { Background = UiColor.White };
        var childA = new Button("a") { Width = 50, Height = 20 };
        var childB = new Button("b") { Width = 50, Height = 20 };
        childA.EnsureLayoutSlot().CanvasLeft = 0;
        childB.EnsureLayoutSlot().CanvasLeft = 100;
        panel.Children.Add(childA);
        panel.Children.Add(childB);
        root.Content = null;
        root.Content = panel;
        root.UpdateLayout(new Vector2(400, 300));

        var panelExits = 0;
        panel.PointerExited += (_, _) => panelExits++;

        root.Input.InjectPointerMoved(childA.Bounds.Center);
        root.Input.InjectPointerMoved(childB.Bounds.Center);
        Check("moving between siblings keeps parent hovered", panelExits == 0 && panel.IsPointerOver, $"exits={panelExits}");
        Check("hover moved to sibling", childB.IsPointerOver && !childA.IsPointerOver);
    }
}

/// <summary>
/// Wheel routing. Scrolling moves what is under the pointer without moving the pointer, which is the one
/// case a hover cache cannot notice on its own.
/// </summary>
public static class WheelChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);

        var items = Enumerable.Range(0, 200).Select(i => (object)$"Row {i}").ToList();

        var tree = new TreeView
        {
            ItemsSource = items,
            ItemTemplate = item => new TextBlock().Text(item.ToString() ?? string.Empty)
        };

        var viewer = tree.Scrollable();
        root.Content = viewer;
        root.UpdateLayout(new Vector2(300, 200));
        root.Render();

        var point = new Vector2(150.0f, 100.0f);
        root.Input.InjectPointerMoved(point);

        var start = viewer.Offset.Y;

        root.Input.InjectPointerWheel(new Vector2(0.0f, -1.0f));
        root.UpdateLayout(new Vector2(300, 200));
        root.Render();

        var afterFirst = viewer.Offset.Y;
        Check("a wheel notch scrolls", afterFirst > start, $"{start:0} -> {afterFirst:0}");

        // The pointer has not moved, so the hover is never recomputed -- and a virtualizing list has just
        // recycled the row that was hovered. The second notch used to dispatch into a detached element and
        // do nothing until the mouse was nudged a pixel.
        root.Input.InjectPointerWheel(new Vector2(0.0f, -1.0f));
        root.UpdateLayout(new Vector2(300, 200));
        root.Render();

        var afterSecond = viewer.Offset.Y;
        Check("and so does the next one without moving the mouse",
            afterSecond > afterFirst, $"{afterFirst:0} -> {afterSecond:0}");

        // Several in a row, because one extra notch could pass on a stale hover that happened to survive.
        for (var i = 0; i < 5; i++)
        {
            root.Input.InjectPointerWheel(new Vector2(0.0f, -1.0f));
            root.UpdateLayout(new Vector2(300, 200));
            root.Render();
        }

        Check("and it keeps scrolling", viewer.Offset.Y > afterSecond,
            $"{afterSecond:0} -> {viewer.Offset.Y:0}");
    }
}

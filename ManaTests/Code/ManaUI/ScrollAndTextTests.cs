using Mana.Code.UI.ManaUi;
using ManaUI;

namespace ManaTests;

public static class ScrollAndTextChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);

        // ============================================================ ScrollViewer
        Console.WriteLine("  -- scroll viewer --");

        var stack = new StackPanel().Vertical();
        for (var i = 0; i < 50; i++)
        {
            stack.Children.Add(new Border().Fill(UiColor.White).Height(20.0f).Tagged(i));
        }

        var scroller = new ScrollViewer { Child = stack, Height = 200.0f };
        root.Content = scroller;
        root.UpdateLayout(new Vector2(300, 400));

        Check("viewport is the offered size", MathF.Abs(scroller.Bounds.Height - 200.0f) < 0.01f,
            $"{scroller.Bounds.Height}");
        Check("extent is the content size", MathF.Abs(scroller.Extent.Y - 1000.0f) < 0.01f,
            $"{scroller.Extent.Y}");
        Check("scrollable range computed", MathF.Abs(scroller.ScrollableSize.Y - 800.0f) < 0.01f,
            $"{scroller.ScrollableSize.Y}");
        Check("content not squashed to viewport", MathF.Abs(stack.Bounds.Height - 1000.0f) < 0.01f,
            $"{stack.Bounds.Height}");

        var firstRowTop = stack.Children[0].Bounds.Y;
        scroller.Offset = new Vector2(0.0f, 100.0f);
        root.UpdateLayout(new Vector2(300, 400));

        Check("scrolling moves content up",
            MathF.Abs(stack.Children[0].Bounds.Y - (firstRowTop - 100.0f)) < 0.01f,
            $"{stack.Children[0].Bounds.Y}");

        scroller.Offset = new Vector2(0.0f, 99999.0f);
        Check("offset clamps to range", MathF.Abs(scroller.Offset.Y - 800.0f) < 0.01f, $"{scroller.Offset.Y}");

        scroller.Offset = new Vector2(0.0f, -50.0f);
        Check("offset clamps at zero", scroller.Offset.Y == 0.0f, $"{scroller.Offset.Y}");

        // Culling is the whole point of clipping a long list.
        root.UpdateLayout(new Vector2(300, 400));
        var drawn = root.Render();
        Check("offscreen rows culled", drawn.InstanceCount < 20, $"{drawn.InstanceCount} of 50 rows drawn");

        // Wheel
        root.Input.InjectPointerMoved(scroller.Bounds.Center);
        root.Input.InjectPointerWheel(new Vector2(0.0f, -1.0f));
        Check("wheel scrolls down", scroller.Offset.Y > 0.0f, $"{scroller.Offset.Y}");

        var atTop = scroller.Offset.Y;
        root.Input.InjectPointerWheel(new Vector2(0.0f, 1.0f));
        Check("wheel scrolls up", scroller.Offset.Y < atTop, $"{scroller.Offset.Y}");

        // At the limit the wheel must not be claimed, so a parent scroller can take over.
        scroller.Offset = new Vector2(0.0f, 0.0f);
        var args = new UiPointerWheelEventArgs();
        root.Input.InjectPointerWheel(new Vector2(0.0f, 1.0f));
        Check("wheel at limit leaves offset alone", scroller.Offset.Y == 0.0f, $"{scroller.Offset.Y}");

        // Keyboard
        scroller.Focus();
        root.Input.InjectKeyDown(UiKey.End);
        Check("End scrolls to bottom", MathF.Abs(scroller.Offset.Y - 800.0f) < 0.01f, $"{scroller.Offset.Y}");
        root.Input.InjectKeyDown(UiKey.Home);
        Check("Home scrolls to top", scroller.Offset.Y == 0.0f, $"{scroller.Offset.Y}");
        root.Input.InjectKeyDown(UiKey.PageDown);
        Check("PageDown scrolls one viewport", MathF.Abs(scroller.Offset.Y - 200.0f) < 0.01f,
            $"{scroller.Offset.Y}");

        // Thumb drag, the interaction most likely to be scaled wrong.
        scroller.Offset = Vector2.Zero;
        root.UpdateLayout(new Vector2(300, 400));
        var thumbX = scroller.Bounds.Right - 5.0f;
        root.Input.InjectPointerMoved(new Vector2(thumbX, scroller.Bounds.Y + 10.0f));
        root.Input.InjectPointerPressed(new Vector2(thumbX, scroller.Bounds.Y + 10.0f), PointerButton.Left, 0.0);
        Check("thumb press captures", root.PointerCaptureTarget == scroller);

        root.Input.InjectPointerMoved(new Vector2(thumbX, scroller.Bounds.Y + 110.0f));
        Check("thumb drag scrolls", scroller.Offset.Y > 0.0f, $"{scroller.Offset.Y}");

        // Dragging the thumb the full track length must reach the bottom exactly, not overshoot or fall short.
        root.Input.InjectPointerMoved(new Vector2(thumbX, scroller.Bounds.Bottom + 500.0f));
        Check("full thumb travel reaches the end",
            MathF.Abs(scroller.Offset.Y - 800.0f) < 0.5f, $"{scroller.Offset.Y}");

        root.Input.InjectPointerReleased(new Vector2(thumbX, scroller.Bounds.Bottom), PointerButton.Left);
        Check("thumb release frees capture", root.PointerCaptureTarget == null);

        // Content that fits must not scroll at all.
        var small = new ScrollViewer { Child = new Border().Fill(UiColor.White).Height(50.0f), Height = 200.0f };
        root.Content = null;
        root.Content = small;
        root.UpdateLayout(new Vector2(300, 400));
        Check("short content is not scrollable", !small.CanScrollVertically && small.ScrollableSize.Y == 0.0f,
            $"{small.ScrollableSize.Y}");

        // A scroll viewer only scrolls if something bounds its height. Dropping one into a vertical stack
        // measures it with unbounded height, so it sizes to its content and shows no bar -- which is how the
        // scene outliner ended up with no scroll bars. The fix belongs at the use site, not in ScrollViewer:
        // sizing to content when unconstrained is what makes a viewer usable inside a popup or a tooltip.
        var freeRows = new StackPanel().Vertical();
        for (var i = 0; i < 40; i++)
        {
            freeRows.Children.Add(new Border().Fill(UiColor.White).Height(20.0f));
        }

        var unbounded = new ScrollViewer { Child = freeRows };
        var stackHost = new StackPanel().Vertical().Children(new TextBlock().Text("Header"), unbounded);
        root.Content = null;
        root.Content = stackHost;
        root.UpdateLayout(new Vector2(300, 200));
        Check("a viewer in a vertical stack grows instead of scrolling",
            !unbounded.CanScrollVertically && unbounded.Bounds.Height > 700.0f,
            $"{unbounded.Bounds.Height:F0}px tall, scrollable {unbounded.ScrollableSize.Y:F0}");

        // The same content in a star row is bounded by the leftover space, so it scrolls and draws a bar.
        var boundedRows = new StackPanel().Vertical();
        for (var i = 0; i < 40; i++)
        {
            boundedRows.Children.Add(new Border().Fill(UiColor.White).Height(20.0f));
        }

        var bounded = new ScrollViewer { Child = boundedRows };
        var gridHost = new Grid().Rows("Auto, *").Columns("*").Children(
            new TextBlock().Text("Header").GridRow(0),
            bounded.GridRow(1));
        root.Content = null;
        root.Content = gridHost;
        root.UpdateLayout(new Vector2(300, 200));
        Check("a viewer in a star grid row scrolls",
            bounded.CanScrollVertically && bounded.Bounds.Height < 200.0f,
            $"{bounded.Bounds.Height:F0}px tall, scrollable {bounded.ScrollableSize.Y:F0}");

        var barInstances = root.Render().InstanceCount;
        bounded.VerticalScrollBar = ScrollBarVisibility.Disabled;
        root.UpdateLayout(new Vector2(300, 200));
        Check("the bar is actually drawn", root.Render().InstanceCount < barInstances,
            $"{barInstances} instances with the bar, {root.Render().InstanceCount} without");

        // The bar is drawn over the content but the hit test finds the content underneath it, so the viewer
        // has to claim the press on the way down. Otherwise grabbing the thumb selects the tree row behind
        // it -- which is exactly what a list of clickable rows made happen.
        var treeRoots = Enumerable.Range(0, 60).Select(i => $"Row {i}").ToList();
        var overBarTree = new TreeView()
            .Tree(treeRoots, _ => null, item => new TextBlock().Text(item));

        var overBars = (ScrollViewer)overBarTree.Scrollable();
        var barHost = new Grid().Rows("*").Columns("*").Children(overBars);
        root.Content = null;
        root.Content = barHost;
        root.UpdateLayout(new Vector2(300, 200));

        Check("the list is scrollable", overBars.CanScrollVertically,
            $"scrollable {overBars.ScrollableSize.Y:F0}");

        // A point on the thumb, which sits directly over a row that would otherwise take the click.
        Vector2 thumbPoint = new Vector2(
            overBars.Bounds.Right - root.Theme.ScrollBarThickness * 0.5f,
            overBars.Bounds.Y + 10.0f);

        // The bar is drawn over the content and swallows presses over itself, so content laid out beneath
        // it is both unreadable and unclickable. A visible bar takes its width out of the content area
        // rather than sitting on top of it.
        Check("a visible bar is reserved out of the viewport",
            overBars.Viewport.X <= overBars.Bounds.Width - root.Theme.ScrollBarThickness + 0.5f,
            $"viewport {overBars.Viewport.X:F1} of {overBars.Bounds.Width:F1}");
        Check("the content stops short of the bar",
            overBarTree.Bounds.Right <= overBars.Bounds.Right - root.Theme.ScrollBarThickness + 0.5f,
            $"content ends at {overBarTree.Bounds.Right:F1}, viewer at {overBars.Bounds.Right:F1}");

        // Nothing to reserve when nothing is shown, or a pane that fits would lose width for no reason.
        var noBars = new ScrollViewer { Child = new Border().Fill(UiColor.White).Height(20.0f) };
        root.Content = null;
        root.Content = new Grid().Rows("*").Columns("*").Children(noBars);
        root.UpdateLayout(new Vector2(300, 200));
        Check("content that fits keeps the full width",
            MathF.Abs(noBars.Viewport.X - noBars.Bounds.Width) < 0.5f,
            $"viewport {noBars.Viewport.X:F1} of {noBars.Bounds.Width:F1}");

        root.Content = null;
        root.Content = barHost;
        root.UpdateLayout(new Vector2(300, 200));

        // Reserving the gutter means content no longer reaches the thumb at all, which is the better fix.
        // The tunnel-phase grab still has to work for a viewer that overlays instead -- so the rest of this
        // section turns reservation off and checks the case it was written for.
        Check("reserving keeps the content clear of the thumb",
            root.HitTest(thumbPoint) is ScrollViewer,
            root.HitTest(thumbPoint)?.GetType().Name ?? "null");

        overBars.ReserveScrollBarSpace = false;
        root.UpdateLayout(new Vector2(300, 200));
        thumbPoint = new Vector2(
            overBars.Bounds.Right - root.Theme.ScrollBarThickness * 0.5f,
            overBars.Bounds.Y + 10.0f);

        Check("an overlaid bar has the tree underneath it",
            root.HitTest(thumbPoint) is not ScrollViewer,
            root.HitTest(thumbPoint)?.GetType().Name ?? "null");

        root.Input.InjectPointerMoved(thumbPoint);
        Check("the bar overrides the content's cursor", root.RequestedCursor == UiCursor.Arrow,
            root.RequestedCursor.ToString());

        root.Input.InjectPointerPressed(thumbPoint, PointerButton.Left, 0.0);
        Check("the viewer captures the thumb press", root.PointerCaptureTarget == overBars,
            root.PointerCaptureTarget?.GetType().Name ?? "null");

        root.Input.InjectPointerMoved(thumbPoint + new Vector2(0.0f, 60.0f));
        root.UpdateLayout(new Vector2(300, 200));
        Check("dragging the thumb scrolls", overBars.Offset.Y > 0.0f, $"{overBars.Offset.Y:F0}");

        root.Input.InjectPointerReleased(thumbPoint + new Vector2(0.0f, 60.0f), PointerButton.Left);
        Check("the row underneath was never selected", overBarTree.SelectedItem == null,
            overBarTree.SelectedItem?.ToString() ?? "nothing selected");

        // ScrollIntoView
        var tallStack = new StackPanel().Vertical();
        for (var i = 0; i < 50; i++)
        {
            tallStack.Children.Add(new Border().Fill(UiColor.White).Height(20.0f));
        }

        var intoView = new ScrollViewer { Child = tallStack, Height = 200.0f };
        root.Content = null;
        root.Content = intoView;
        root.UpdateLayout(new Vector2(300, 400));

        var target = tallStack.Children[40];
        intoView.ScrollIntoView(target);
        root.UpdateLayout(new Vector2(300, 400));
        Check("ScrollIntoView reveals the element",
            target.Bounds.Top >= intoView.Bounds.Top - 0.5f && target.Bounds.Bottom <= intoView.Bounds.Bottom + 0.5f,
            $"row {target.Bounds} in view {intoView.Bounds}");

        // ============================================================ TextBox
        Console.WriteLine("  -- text box --");

        var box = new TextBox().Width(200.0f);
        root.Content = null;
        root.Content = box;
        root.UpdateLayout(new Vector2(400, 300));

        box.Text = "existing";
        box.Focus();
        Check("text box takes focus", box.IsFocused);
        Check("focus selects all text", box.HasSelection && box.SelectedText == box.Text, $"'{box.SelectedText}'");
        root.FocusManager.Focus(null);
        box.Text = string.Empty;
        box.Focus();
        Check("text box wants raw keys", box.WantsAllKeyboardInput);
        Check("keyboard reported as captured", root.Input.IsKeyboardCaptured);

        root.Input.InjectTextInput("Hello");
        Check("typing inserts", box.Text == "Hello", box.Text);
        Check("caret follows typing", box.CaretIndex == 5, $"{box.CaretIndex}");

        root.Input.InjectKeyDown(UiKey.Backspace);
        Check("backspace deletes before caret", box.Text == "Hell", box.Text);

        root.Input.InjectKeyDown(UiKey.Home);
        Check("home moves caret to start", box.CaretIndex == 0, $"{box.CaretIndex}");

        root.Input.InjectTextInput("S");
        Check("insert at caret", box.Text == "SHell", box.Text);

        root.Input.InjectKeyDown(UiKey.Delete);
        Check("delete removes after caret", box.Text == "Sell", box.Text);

        root.Input.InjectKeyDown(UiKey.End);
        root.Input.InjectKeyDown(UiKey.Left, UiModifiers.Shift);
        root.Input.InjectKeyDown(UiKey.Left, UiModifiers.Shift);
        Check("shift-arrow selects", box.SelectedText == "ll", $"'{box.SelectedText}'");

        root.Input.InjectTextInput("a");
        Check("typing replaces selection", box.Text == "Sea", box.Text);

        root.Input.InjectKeyDown(UiKey.A, UiModifiers.Control);
        Check("ctrl-A selects all", box.SelectedText == "Sea", $"'{box.SelectedText}'");

        root.Input.InjectKeyDown(UiKey.C, UiModifiers.Control);
        Check("ctrl-C copies", TextBox.Clipboard.Text == "Sea", TextBox.Clipboard.Text);

        root.Input.InjectKeyDown(UiKey.End);
        root.Input.InjectKeyDown(UiKey.V, UiModifiers.Control);
        Check("ctrl-V pastes", box.Text == "SeaSea", box.Text);

        root.Input.InjectKeyDown(UiKey.A, UiModifiers.Control);
        root.Input.InjectKeyDown(UiKey.X, UiModifiers.Control);
        Check("ctrl-X cuts", box.Text.Length == 0 && TextBox.Clipboard.Text == "SeaSea", $"'{box.Text}'");

        // MaxLength
        var limited = new TextBox { MaxLength = 3 }.Width(100.0f);
        root.Content = null;
        root.Content = limited;
        root.UpdateLayout(new Vector2(400, 300));
        limited.Focus();
        root.Input.InjectTextInput("abcdef");
        Check("MaxLength enforced", limited.Text == "abc", limited.Text);

        // Filter
        var numeric = new TextBox().Numeric().Width(100.0f);
        root.Content = null;
        root.Content = numeric;
        root.UpdateLayout(new Vector2(400, 300));
        numeric.Focus();
        root.Input.InjectTextInput("12a3.4x");
        Check("character filter applied", numeric.Text == "123.4", numeric.Text);

        // Read-only
        var readOnly = new TextBox().Value("fixed").ReadOnly().Width(100.0f);
        root.Content = null;
        root.Content = readOnly;
        root.UpdateLayout(new Vector2(400, 300));
        readOnly.Focus();
        root.Input.InjectTextInput("zzz");
        root.Input.InjectKeyDown(UiKey.Backspace);
        Check("read-only rejects edits", readOnly.Text == "fixed", readOnly.Text);

        // Two-way binding, both write-back policies.
        var vm = new DemoViewModel { Title = "bound" };
        var live = new TextBox().Width(200.0f).BindText(vm, v => v.Title);
        root.Content = null;
        root.Content = live;
        root.UpdateLayout(new Vector2(400, 300));

        Check("text box pulls initial value", live.Text == "bound", live.Text);
        vm.Title = "from source";
        Check("source -> text box", live.Text == "from source", live.Text);

        live.Focus();
        root.Input.InjectKeyDown(UiKey.End);
        root.Input.InjectTextInput("!");
        Check("text box -> source per keystroke", vm.Title == "from source!", vm.Title);

        var commitVm = new DemoViewModel { Title = "start" };
        var deferred = new TextBox().Width(200.0f).BindText(commitVm, v => v.Title, commitOnly: true);
        root.Content = null;
        root.Content = deferred;
        root.UpdateLayout(new Vector2(400, 300));
        deferred.Focus();
        root.Input.InjectKeyDown(UiKey.End);
        root.Input.InjectTextInput("X");
        Check("commitOnly holds the source", commitVm.Title == "start", commitVm.Title);

        root.Input.InjectKeyDown(UiKey.Enter);
        Check("Enter commits", commitVm.Title == "startX", commitVm.Title);

        // Renders without throwing, caret and all.
        root.UpdateLayout(new Vector2(400, 300));
        var textDraw = root.Render();
        Check("text box renders", textDraw.InstanceCount > 3, $"{textDraw.InstanceCount} instances");

        // ---------------------------------------------------------------- key auto-repeat
        // Silk's GLFW backend drops the platform's key-repeat action entirely, so holding Backspace
        // delivered exactly one event and deleted one character. Repeat is synthesised by the router.
        Console.WriteLine("  -- key repeat --");

        var repeat = new TextBox().Value("abcdefghij").Width(200.0f);
        root.Content = null;
        root.Content = repeat;
        root.UpdateLayout(new Vector2(400, 300));
        repeat.Focus();
        root.Input.InjectKeyDown(UiKey.End);

        root.Input.InjectKeyDown(UiKey.Backspace);
        Check("press deletes one character", repeat.Text == "abcdefghi", repeat.Text);

        // Nothing should repeat before the delay elapses.
        root.Input.TickKeyRepeat(0.2);
        Check("no repeat before the delay", repeat.Text == "abcdefghi", repeat.Text);

        // Past the delay, repeats fire. The count is however many the elapsed time owed, not exactly one:
        // a long frame delivers the repeats it missed rather than dropping them.
        root.Input.TickKeyRepeat(0.3);
        Check("repeat starts after the delay", repeat.Text.Length < 9, $"'{repeat.Text}'");

        // Holding through several intervals should delete several more.
        root.Input.TickKeyRepeat(0.1);
        Check("repeat continues while held", repeat.Text.Length < 8, $"'{repeat.Text}'");

        // A stalled frame must not fire an unbounded burst; the catch-up loop is capped.
        var beforeStall = repeat.Text.Length;
        root.Input.TickKeyRepeat(30.0);
        Check("a long stall does not fire unbounded repeats",
            beforeStall - repeat.Text.Length <= 32, $"{beforeStall - repeat.Text.Length} repeats");

        repeat.Text = "abcdefghij";
        root.Input.InjectKeyDown(UiKey.End);
        root.Input.TickKeyRepeat(0.5);

        var beforeRelease = repeat.Text;
        root.Input.InjectKeyUp(UiKey.Backspace);
        root.Input.TickKeyRepeat(1.0);
        Check("release stops the repeat", repeat.Text == beforeRelease, repeat.Text);

        // Delete repeats too, from the other direction.
        var forward = new TextBox().Value("123456").Width(200.0f);
        root.Content = null;
        root.Content = forward;
        root.UpdateLayout(new Vector2(400, 300));
        forward.Focus();
        root.Input.InjectKeyDown(UiKey.Home);
        root.Input.InjectKeyDown(UiKey.Delete);
        root.Input.TickKeyRepeat(0.5);
        root.Input.TickKeyRepeat(0.1);
        Check("delete repeats too", forward.Text.Length < 5, $"'{forward.Text}'");
        root.Input.InjectKeyUp(UiKey.Delete);

        // Arrows repeat for caret movement.
        var arrows = new TextBox().Value("abcdefghij").Width(200.0f);
        root.Content = null;
        root.Content = arrows;
        root.UpdateLayout(new Vector2(400, 300));
        arrows.Focus();
        root.Input.InjectKeyDown(UiKey.End);
        root.Input.InjectKeyDown(UiKey.Left);
        var caretAfterPress = arrows.CaretIndex;
        root.Input.TickKeyRepeat(0.5);
        root.Input.TickKeyRepeat(0.1);
        Check("arrow keys repeat", arrows.CaretIndex < caretAfterPress,
            $"{caretAfterPress} -> {arrows.CaretIndex}");
        root.Input.InjectKeyUp(UiKey.Left);

        // The trap: character keys must NOT repeat here, because the platform's character callback
        // already does. Repeating both would insert every held letter twice.
        var typed = new TextBox().Width(200.0f);
        root.Content = null;
        root.Content = typed;
        root.UpdateLayout(new Vector2(400, 300));
        typed.Focus();
        root.Input.InjectTextInput("x");
        root.Input.InjectKeyDown(UiKey.A);
        root.Input.TickKeyRepeat(2.0);
        Check("character keys do not double-repeat", typed.Text == "x", $"'{typed.Text}'");

        // A non-repeating key cancels a previous hold rather than leaving it running.
        var cancel = new TextBox().Value("hello").Width(200.0f);
        root.Content = null;
        root.Content = cancel;
        root.UpdateLayout(new Vector2(400, 300));
        cancel.Focus();
        root.Input.InjectKeyDown(UiKey.End);
        root.Input.InjectKeyDown(UiKey.Backspace);
        root.Input.InjectKeyDown(UiKey.Escape);
        var afterCancel = cancel.Text;
        root.Input.TickKeyRepeat(2.0);
        Check("another key cancels the hold", cancel.Text == afterCancel, cancel.Text);

        // The OS mapping is pure arithmetic, so it can be checked without touching the machine's settings.
        Check("windows delay index 0 is 250ms",
            MathF.Abs((float)ManaUiKeyboardSettings.DelayFromWindowsIndex(0) - 0.25f) < 0.001f,
            $"{ManaUiKeyboardSettings.DelayFromWindowsIndex(0)}");
        Check("windows delay index 1 is 500ms",
            MathF.Abs((float)ManaUiKeyboardSettings.DelayFromWindowsIndex(1) - 0.5f) < 0.001f,
            $"{ManaUiKeyboardSettings.DelayFromWindowsIndex(1)}");
        Check("windows delay index 3 is 1000ms",
            MathF.Abs((float)ManaUiKeyboardSettings.DelayFromWindowsIndex(3) - 1.0f) < 0.001f,
            $"{ManaUiKeyboardSettings.DelayFromWindowsIndex(3)}");
        Check("out-of-range delay index clamps",
            ManaUiKeyboardSettings.DelayFromWindowsIndex(99) <= 1.0
            && ManaUiKeyboardSettings.DelayFromWindowsIndex(-5) >= 0.25,
            "clamped");

        Check("windows speed 31 is ~30 per second",
            MathF.Abs((float)ManaUiKeyboardSettings.IntervalFromWindowsSpeed(31) - 1.0f / 30.0f) < 0.001f,
            $"{ManaUiKeyboardSettings.IntervalFromWindowsSpeed(31):F4}s");
        Check("windows speed 0 is ~2.5 per second",
            MathF.Abs((float)ManaUiKeyboardSettings.IntervalFromWindowsSpeed(0) - 1.0f / 2.5f) < 0.001f,
            $"{ManaUiKeyboardSettings.IntervalFromWindowsSpeed(0):F4}s");
        Check("faster speed index means shorter interval",
            ManaUiKeyboardSettings.IntervalFromWindowsSpeed(31)
            < ManaUiKeyboardSettings.IntervalFromWindowsSpeed(10),
            "monotonic");

        Check("macOS ticks convert at 60Hz",
            MathF.Abs((float)ManaUiKeyboardSettings.TicksToSeconds(30) - 0.5f) < 0.001f,
            $"{ManaUiKeyboardSettings.TicksToSeconds(30)}");

        // Whatever the OS reports must be usable: never zero, never absurd.
        var (osDelay, osInterval) = ManaUiKeyboardSettings.Read();
        Check("OS settings are in a sane range",
            osDelay is >= 0.1 and <= 2.0 && osInterval is >= 0.01 and <= 1.0,
            $"delay {osDelay:F3}s, interval {osInterval:F4}s");

        // Losing focus mid-hold must not leave a key repeating forever.
        var blurred = new TextBox().Value("abcdefgh").Width(200.0f);
        root.Content = null;
        root.Content = blurred;
        root.UpdateLayout(new Vector2(400, 300));
        blurred.Focus();
        root.Input.InjectKeyDown(UiKey.End);
        root.Input.InjectKeyDown(UiKey.Backspace);
        root.FocusManager.Focus(null);
        var afterBlur = blurred.Text;
        root.Input.TickKeyRepeat(5.0);
        Check("focus loss stops the repeat", blurred.Text == afterBlur, blurred.Text);
    }
}

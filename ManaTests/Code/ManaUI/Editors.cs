using ManaUI;

namespace ManaTests;

/// <summary>
/// The inspector's vocabulary: drag-or-type numeric fields, vector rows and collapsible sections. These
/// exist to replace ImGui's DragFloat, DragFloat3 and CollapsingHeader, so the checks are mostly about the
/// gestures matching what people already have in their fingers.
/// </summary>
public static class EditorChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var time = 0.0;

        // ============================================================ NumericBox
        Console.WriteLine("  -- numeric box --");

        var numeric = new NumericBox { Value = 2.5f, Step = 0.1f, Width = 160.0f };
        root.Content = numeric;
        root.UpdateLayout(new Vector2(400, 200));

        Check("value formats at the step's precision", numeric.Text == "2.5", numeric.Text);

        // The whole reason a stable digit count is enough to stop the jitter: the theme font's digits are
        // tabular, so equal length means equal width. If that ever stops holding, padding is not the fix
        // and this says so rather than the field quietly going wobbly again.
        var digitFont = fonts.Get(root.Theme.FontFamily, root.Theme.FontSize);
        var zeroWidth = digitFont.GetGlyph('0').Advance;
        var tabular = "0123456789".All(d => MathF.Abs(digitFont.GetGlyph(d).Advance - zeroWidth) < 0.01f);
        Check("the theme font has tabular digits", tabular, $"'0' advances {zeroWidth:F3}px");

        // Padded, not trimmed to "2". A drag stepping 2.4, 2.5, 2.6 must not change the text's width as it
        // goes, or the number visibly jitters under the pointer.
        numeric.Value = 2.0f;
        Check("a whole number keeps the step's decimals", numeric.Text == "2.0", numeric.Text);

        numeric.Step = 0.25f;
        numeric.Value = 3.0f;
        Check("a quarter step shows two decimals", numeric.Text == "3.00", numeric.Text);
        numeric.Value = 3.25f;
        Check("and holds them across the step", numeric.Text == "3.25", numeric.Text);
        numeric.Value = 3.5f;
        Check("so the width never changes mid-drag", numeric.Text == "3.50", numeric.Text);

        numeric.Step = 1.0f;
        numeric.Value = 4.0f;
        Check("a whole step drops the decimals entirely", numeric.Text == "4", numeric.Text);

        // Order must not matter: the step decides the precision, so setting it after the value has to
        // rewrite the text rather than leave it formatted for the old step.
        var reordered = new NumericBox { Value = 2.5f, Step = 0.1f };
        Check("setting the step after the value reformats", reordered.Text == "2.5", reordered.Text);

        numeric.Step = 0.01f;
        numeric.DecimalPlaces = 2;
        numeric.Value = 1.0f / 3.0f;
        Check("rounds to the requested places", numeric.Text == "0.33", numeric.Text);

        // ---------------------------------------------------------------- drag to scrub
        numeric.DecimalPlaces = 3;
        numeric.Value = 0.0f;
        numeric.Step = 0.1f;

        // The two gestures live in separate places: the strip along the bottom edge scrubs, everything above
        // it is an ordinary text field. Sharing one surface meant every attempt to select part of a number
        // was also a drag, and no amount of threshold tuning can guess which one was meant.
        var start = numeric.DragStripRect.Center;
        var textPoint = new Vector2(numeric.Bounds.Center.X, numeric.Bounds.Y + 6.0f);

        Check("the strip sits inside the field", numeric.Bounds.Contains(start),
            $"strip {numeric.DragStripRect}, field {numeric.Bounds}");
        Check("the text area is not the strip", !numeric.DragStripRect.Contains(textPoint),
            $"{textPoint} vs strip {numeric.DragStripRect}");

        root.Input.InjectPointerMoved(start);
        Check("the strip advertises the drag", numeric.GetCursor(start) == UiCursor.SizeWE,
            numeric.GetCursor(start).ToString());
        Check("the text advertises selection", numeric.GetCursor(textPoint) == UiCursor.IBeam,
            numeric.GetCursor(textPoint).ToString());

        root.Input.InjectPointerPressed(start, PointerButton.Left, time);
        Check("pressing the strip does not focus the text", !numeric.IsFocused, "no caret");

        // Inside the threshold nothing should move, or a slightly shaky click would edit the value.
        root.Input.InjectPointerMoved(start + new Vector2(2.0f, 0.0f));
        Check("a jitter under the threshold does not scrub", numeric.Value == 0.0f, $"{numeric.Value}");

        // Unbounded, so the drag is paced by the step: 50px at 2px per step is 25 steps of 0.1. A pixel is
        // deliberately NOT a step -- one step per pixel made a small hand movement throw the value across
        // its range, which is what made the field impossible to aim.
        root.Input.InjectPointerMoved(start + new Vector2(50.0f, 0.0f));
        Check("dragging right advances whole steps",
            MathF.Abs(numeric.Value - 2.5f) < 0.001f, $"{numeric.Value:F3}");
        Check("the field is dragging", numeric.IsDragging);

        Check("the value lands on an exact multiple of the step",
            MathF.Abs(numeric.Value / numeric.Step - MathF.Round(numeric.Value / numeric.Step)) < 0.001f,
            $"{numeric.Value:F3} / {numeric.Step}");

        // Measured from the press, not the previous move -- coming back must land back where it started.
        root.Input.InjectPointerMoved(start + new Vector2(-50.0f, 0.0f));
        Check("dragging back past the origin goes negative",
            MathF.Abs(numeric.Value + 2.5f) < 0.001f, $"{numeric.Value:F3}");

        root.Input.InjectPointerMoved(start);
        Check("returning to the origin restores the value", MathF.Abs(numeric.Value) < 0.001f,
            $"{numeric.Value:F4}");

        var committed = 0;
        numeric.ValueCommitted += (_, _) => committed++;
        root.Input.InjectPointerReleased(start, PointerButton.Left);
        Check("release commits once", committed == 1, $"{committed}");
        Check("a completed drag does not start an edit", !numeric.IsFocused, "no caret");

        // ---------------------------------------------------------------- the text half selects
        // The whole point of the split: a press on the text is a text press, so selecting part of a number
        // works exactly as it does in any other field rather than scrubbing it.
        numeric.Value = 1234.5f;
        var beforeSelect = numeric.Value;
        var textLeft = new Vector2(numeric.Bounds.X + 2.0f, textPoint.Y);
        var textRight = new Vector2(numeric.Bounds.Right - 2.0f, textPoint.Y);

        root.Input.InjectPointerMoved(textLeft);
        root.Input.InjectPointerPressed(textLeft, PointerButton.Left, time += 1.0);
        Check("clicking the text focuses it", numeric.IsFocused);
        Check("clicking the text does not scrub", numeric.Value == beforeSelect, $"{numeric.Value}");
        Check("clicking the text is not a drag", !numeric.IsDragging, "not a scrub");
        Check("the caret went to the start", numeric.CaretIndex == 0, $"{numeric.CaretIndex}");

        // Dragging across the text extends a selection instead of moving the value.
        root.Input.InjectPointerMoved(textRight);
        Check("dragging the text selects", numeric.HasSelection, $"'{numeric.SelectedText}'");
        Check("and still does not scrub", numeric.Value == beforeSelect, $"{numeric.Value}");
        root.Input.InjectPointerReleased(textRight, PointerButton.Left);
        root.FocusManager.Focus(null);
        numeric.Value = 0.0f;

        // ---------------------------------------------------------------- sweeping a bounded range
        // A field with limits is a slider wearing a number: one field width of travel crosses the whole
        // range, so the gesture matches the range track under the text. Pacing this one by the step would
        // take 400 pixels to cross 0..1 in hundredths, which is what made it feel stuck.
        var sweep = new NumericBox { Minimum = 0.0f, Maximum = 1.0f, Step = 0.01f, Width = 200.0f };
        root.Content = null;
        root.Content = sweep;
        root.UpdateLayout(new Vector2(400, 200));

        Check("a bounded field sweeps its range", sweep.SweepsRange);
        Check("an unbounded one has nothing to sweep", !new NumericBox().SweepsRange,
            "no limits, so it counts steps instead");

        // A point at the given fraction along the strip.
        Vector2 At(float fraction) =>
            new(sweep.Bounds.X + sweep.Bounds.Width * fraction, sweep.DragStripRect.Center.Y);

        // The gesture in full: sitting at 75%, press at 25%, drag to 10%. Because the strip does nothing but
        // scrub, the jump can happen on the press itself -- there is no click-to-type here to protect.
        sweep.Value = 0.75f;
        root.Input.InjectPointerMoved(At(0.25f));
        root.Input.InjectPointerPressed(At(0.25f), PointerButton.Left, time += 1.0);
        Check("the press jumps straight to the pointer",
            MathF.Abs(sweep.Value - 0.25f) < 0.02f, $"{sweep.Value:F3}");

        root.Input.InjectPointerMoved(At(0.10f));
        Check("and tracks it from there",
            MathF.Abs(sweep.Value - 0.10f) < 0.02f, $"{sweep.Value:F3}");

        Check("the swept value still lands on the step",
            MathF.Abs(sweep.Value * 100.0f - MathF.Round(sweep.Value * 100.0f)) < 0.01f,
            $"{sweep.Value:F4}");

        root.Input.InjectPointerMoved(At(1.0f));
        Check("the far end reaches the maximum", sweep.Value == 1.0f, $"{sweep.Value}");
        root.Input.InjectPointerMoved(At(0.5f) + new Vector2(-sweep.Bounds.Width * 2.0f, 0.0f));
        Check("sweeping past the end clamps", sweep.Value == 0.0f, $"{sweep.Value}");
        root.Input.InjectPointerReleased(At(0.0f), PointerButton.Left);

        // The step decides resolution, not speed: a coarser step must land in the same place.
        sweep.Step = 0.25f;
        sweep.Value = 0.0f;
        root.Input.InjectPointerPressed(At(0.5f), PointerButton.Left, time += 1.0);
        root.Input.InjectPointerMoved(At(0.5f) + new Vector2(6.0f, 0.0f));
        Check("a coarser step reaches the same place",
            MathF.Abs(sweep.Value - 0.5f) < 0.06f, $"{sweep.Value:F3}");
        root.Input.InjectPointerReleased(At(0.5f), PointerButton.Left);

        // Fine adjustment has to be relative -- at one field width per range there is nowhere for absolute
        // positioning to put the extra precision. Holding the modifier re-anchors rather than jumping, so
        // the value carries on from where it already was.
        sweep.Step = 0.01f;
        sweep.Value = 0.5f;
        // Held on the press, so the jump is suppressed: that gesture is asking to nudge the value it has,
        // not to reposition it. Without the modifier this same press would land on 0.9.
        root.Input.InjectPointerPressed(At(0.9f), PointerButton.Left, time += 1.0, UiModifiers.Control);
        root.Input.InjectPointerMoved(At(0.9f) + new Vector2(6.0f, 0.0f), UiModifiers.Control);
        Check("holding the fine modifier suppresses the jump",
            MathF.Abs(sweep.Value - 0.5f) < 0.05f, $"{sweep.Value:F3}");

        root.Input.InjectPointerMoved(At(0.9f) + new Vector2(40.0f, 0.0f), UiModifiers.Control);
        var fineValue = sweep.Value;
        Check("fine adjustment still moves the value", fineValue > 0.5f, $"{fineValue:F3}");
        Check("but far less than an absolute sweep would", fineValue < 0.7f,
            $"{fineValue:F3}, versus ~0.9 unmodified");
        root.Input.InjectPointerReleased(At(0.9f), PointerButton.Left);

        // Back to the unbounded field for the editing checks.
        root.Content = null;
        root.Content = numeric;
        root.UpdateLayout(new Vector2(400, 200));
        start = numeric.Bounds.Center;

        // ---------------------------------------------------------------- click to type
        root.Input.InjectPointerPressed(textPoint, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(textPoint, PointerButton.Left);
        Check("clicking the text starts an edit", numeric.IsFocused, "focused");

        // Ctrl+A rather than the click selecting everything for you: the text half behaves like a text box,
        // and a text box places a caret where you clicked.
        root.Input.InjectKeyDown(UiKey.A, UiModifiers.Control);
        Check("select-all works in the field", numeric.SelectedText == numeric.Text, numeric.SelectedText);

        root.Input.InjectTextInput("12.5");
        Check("typing replaces the selection", numeric.Text == "12.5", numeric.Text);
        Check("the value waits for the commit", numeric.Value == 0.0f, $"{numeric.Value}");

        root.Input.InjectKeyDown(UiKey.Enter);
        Check("enter commits the typed value", numeric.Value == 12.5f, $"{numeric.Value}");

        // Letters cannot reach a numeric field at all, so there is nothing to reject later.
        root.Input.InjectTextInput("abc");
        Check("letters are filtered out", numeric.Text == "12.5", numeric.Text);

        // A half-typed number is a legitimate state, but committing one must not destroy the value.
        numeric.SelectAll();
        root.Input.InjectTextInput("-");
        Check("a lone minus is allowed while typing", numeric.Text == "-", numeric.Text);
        root.Input.InjectKeyDown(UiKey.Enter);
        Check("committing junk reverts rather than zeroing", numeric.Value == 12.5f, $"{numeric.Value}");
        Check("the text reverts with it", numeric.Text == "12.5", numeric.Text);

        // ---------------------------------------------------------------- keyboard and limits
        numeric.Step = 1.0f;
        numeric.Value = 5.0f;
        numeric.Focus();
        root.Input.InjectKeyDown(UiKey.Up);
        Check("up steps the value", numeric.Value == 6.0f, $"{numeric.Value}");
        root.Input.InjectKeyDown(UiKey.Down);
        root.Input.InjectKeyDown(UiKey.Down);
        Check("down steps back", numeric.Value == 4.0f, $"{numeric.Value}");

        numeric.Minimum = 0.0f;
        numeric.Maximum = 10.0f;
        numeric.Value = 99.0f;
        Check("clamped to maximum", numeric.Value == 10.0f, $"{numeric.Value}");
        numeric.Value = -99.0f;
        Check("clamped to minimum", numeric.Value == 0.0f, $"{numeric.Value}");

        // The range track is an idle-state affordance, and it only appears when the value is somewhere
        // inside a range bounded on both sides. Focus adds a ring of its own, so every comparison below
        // holds focus constant and varies only the bounds.
        numeric.Value = 5.0f;
        root.FocusManager.Focus(null);

        var withStrip = root.Render().InstanceCount;
        numeric.DragStripHeight = 0.0f;
        root.UpdateLayout(new Vector2(400, 200));
        var withoutStrip = root.Render().InstanceCount;

        // Two instances: the strip's track and its fill. A field is only scrubbable if the strip is there,
        // so a strip that draws nothing is a drag target nobody can find.
        Check("the strip draws a track and a fill", withStrip == withoutStrip + 2,
            $"{withStrip} with strip, {withoutStrip} without");
        Check("removing the strip shortens the field",
            numeric.DesiredSize.Y < root.Theme.ControlHeight + 1.0f, $"{numeric.DesiredSize.Y:F1}px");
        Check("and leaves nothing to drag", numeric.GetCursor(numeric.Bounds.Center) == UiCursor.IBeam,
            numeric.GetCursor(numeric.Bounds.Center).ToString());

        numeric.DragStripHeight = 7.0f;
        root.UpdateLayout(new Vector2(400, 200));
        root.FocusManager.Focus(null);

        var integer = new NumericBox { IsInteger = true, Step = 1.0f, Value = 3.7f };
        Check("integer fields round", integer.Value == 4.0f, $"{integer.Value}");
        Check("integer fields format without a point", integer.Text == "4", integer.Text);

        // ============================================================ VectorBox
        Console.WriteLine("  -- vector box --");

        var vector = new VectorBox { Value = new Vector3(1.0f, 2.0f, 3.0f), Step = 0.5f };
        root.Content = null;
        root.Content = vector;
        root.UpdateLayout(new Vector2(600, 200));

        Check("three fields by default", vector.Components == 3, $"{vector.Components}");
        Check("components reach their fields",
            vector.Field(0).Value == 1.0f && vector.Field(1).Value == 2.0f && vector.Field(2).Value == 3.0f,
            $"{vector.Field(0).Text}, {vector.Field(1).Text}, {vector.Field(2).Text}");
        Check("the step forwards to every field",
            vector.Field(0).Step == 0.5f && vector.Field(2).Step == 0.5f);

        var vectorChanges = 0;
        Vector4 lastVector = default;
        vector.ValueChanged += (_, value) =>
        {
            vectorChanges++;
            lastVector = value;
        };

        vector.Field(1).Value = 9.0f;
        Check("editing one field updates the vector", vector.Value == new Vector3(1.0f, 9.0f, 3.0f),
            vector.Value.ToString());
        Check("the change is raised once", vectorChanges == 1, $"{vectorChanges}");
        Check("the raised value matches", lastVector.Y == 9.0f, $"{lastVector}");

        // Writing the whole vector pushes into three fields, each of which raises -- one change, not four.
        vectorChanges = 0;
        vector.Value = new Vector3(4.0f, 5.0f, 6.0f);
        Check("writing the vector raises once", vectorChanges == 1, $"{vectorChanges}");
        Check("every field followed",
            vector.Field(0).Value == 4.0f && vector.Field(1).Value == 5.0f && vector.Field(2).Value == 6.0f,
            $"{vector.Field(0).Text}, {vector.Field(1).Text}, {vector.Field(2).Text}");

        // A field that coerces what it is handed must drag the vector with it, or the row would display one
        // number and report another.
        var rounding = new VectorBox { IsInteger = true };
        rounding.Value = new Vector3(3.7f, -1.2f, 0.5f);
        Check("the vector follows what the fields coerced to",
            rounding.Value == new Vector3(4.0f, -1.0f, 1.0f), rounding.Value.ToString());
        Check("the fields show the same numbers",
            rounding.Field(0).Text == "4" && rounding.Field(1).Text == "-1",
            $"{rounding.Field(0).Text}, {rounding.Field(1).Text}");

        vector.Components = 4;
        root.UpdateLayout(new Vector2(600, 200));
        Check("a fourth field appears", vector.Field(3).Bounds.Width > 0.0f,
            vector.Field(3).Bounds.ToString());
        Check("fields share the width",
            MathF.Abs(vector.Field(0).Bounds.Width - vector.Field(3).Bounds.Width) < 1.0f,
            $"{vector.Field(0).Bounds.Width:F1} vs {vector.Field(3).Bounds.Width:F1}");

        vector.Components = 2;
        root.UpdateLayout(new Vector2(600, 200));
        Check("dropping to two widens the survivors", vector.Field(0).Bounds.Width > 200.0f,
            $"{vector.Field(0).Bounds.Width:F1}");
        Check("hidden components keep their value", vector.Vector.Z == 6.0f, $"{vector.Vector}");

        // Dragging one component must leave the others exactly alone.
        vector.Components = 3;
        vector.Value = new Vector3(0.0f, 0.0f, 0.0f);
        vector.Step = 0.1f;
        root.UpdateLayout(new Vector2(600, 200));

        var middle = vector.Field(1).DragStripRect.Center;
        root.Input.InjectPointerMoved(middle);
        root.Input.InjectPointerPressed(middle, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerMoved(middle + new Vector2(40.0f, 0.0f));
        root.Input.InjectPointerReleased(middle + new Vector2(40.0f, 0.0f), PointerButton.Left);

        // Unbounded, so paced by the step: 40px at 2px per step is 20 steps of 0.1.
        Check("dragging Y moves only Y",
            MathF.Abs(vector.Value.Y - 2.0f) < 0.001f && vector.Value.X == 0.0f && vector.Value.Z == 0.0f,
            vector.Value.ToString());

        // Three field boxes, three axis letters, three value glyphs -- nine is the floor for a row that is
        // actually drawing all of its parts rather than silently dropping some.
        Check("the vector row renders every part", root.Render().InstanceCount >= 9,
            $"{root.Render().InstanceCount} instances");
        Check("still one draw call", root.Render().Batches.Count == 1,
            $"{root.Render().Batches.Count} batches");

        // ============================================================ Expander
        Console.WriteLine("  -- expander --");

        var body = new StackPanel().Vertical();
        for (var i = 0; i < 5; i++)
        {
            body.Children.Add(new Border().Fill(UiColor.White).Height(20.0f));
        }

        var expander = new Expander { Header = "Transform", Content = body };
        root.Content = null;
        root.Content = expander;
        root.UpdateLayout(new Vector2(400, 400));

        var expandedHeight = expander.Bounds.Height;
        Check("expanded by default", expander.IsExpanded);
        Check("content contributes height", expandedHeight > 100.0f, $"{expandedHeight:F0}px");

        var toggles = 0;
        expander.ExpandedChanged += (_, _) => toggles++;

        expander.IsExpanded = false;
        root.UpdateLayout(new Vector2(400, 400));
        Check("collapsing shrinks to the header", expander.Bounds.Height < 40.0f,
            $"{expander.Bounds.Height:F0}px");
        Check("collapse raises the event", toggles == 1, $"{toggles}");
        Check("collapsed content is not rendered",
            root.Render().InstanceCount < 12, $"{root.Render().InstanceCount} instances");

        // Kept in the tree rather than removed, so state inside a folded section survives.
        Check("collapsed content stays in the tree", ReferenceEquals(expander.Content, body));

        expander.IsExpanded = true;
        root.UpdateLayout(new Vector2(400, 400));
        Check("re-expanding restores the height",
            MathF.Abs(expander.Bounds.Height - expandedHeight) < 0.5f, $"{expander.Bounds.Height:F0}px");

        // ---------------------------------------------------------------- header interaction
        var headerPoint = new Vector2(expander.Bounds.Center.X, expander.Bounds.Y + 10.0f);
        root.Input.InjectPointerMoved(headerPoint);
        root.Input.InjectPointerPressed(headerPoint, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(headerPoint, PointerButton.Left);
        root.UpdateLayout(new Vector2(400, 400));
        Check("clicking the header collapses it", !expander.IsExpanded);

        // A press that wanders off the header is a cancelled click, as everywhere else in the library.
        root.Input.InjectPointerPressed(headerPoint, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(headerPoint + new Vector2(0.0f, 400.0f), PointerButton.Left);
        Check("releasing away from the header cancels", !expander.IsExpanded, "still collapsed");

        root.Input.InjectKeyDown(UiKey.Right);
        Check("right expands from the header", expander.IsExpanded);
        root.Input.InjectKeyDown(UiKey.Left);
        Check("left collapses again", !expander.IsExpanded);

        // Clicking inside the content must not toggle the section it lives in.
        expander.IsExpanded = true;
        root.UpdateLayout(new Vector2(400, 400));
        var insidePoint = body.Children[2].Bounds.Center;
        root.Input.InjectPointerMoved(insidePoint);
        root.Input.InjectPointerPressed(insidePoint, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(insidePoint, PointerButton.Left);
        Check("clicking the content leaves the section open", expander.IsExpanded);

        // ============================================================ binding
        Console.WriteLine("  -- binding --");

        var vm = new DemoViewModel();

        var boundVector = new VectorBox().BindValue(vm, v => v.Origin);
        var boundNumeric = new NumericBox().Step(0.5f).BindValue(vm, v => v.Radius);
        var boundSection = new Expander { Header = "Bound" }.BindIsExpanded(vm, v => v.SectionExpanded);

        root.Content = null;
        root.Content = new StackPanel().Vertical(4.0f).Children(boundVector, boundNumeric, boundSection);
        root.UpdateLayout(new Vector2(500, 400));

        Check("vector pulls the initial value", boundVector.Value == new Vector3(1.0f, 2.0f, 3.0f),
            boundVector.Value.ToString());
        Check("numeric pulls the initial value", boundNumeric.Value == vm.Radius, $"{boundNumeric.Value}");
        Check("expander pulls the initial state", boundSection.IsExpanded == vm.SectionExpanded);

        vm.Origin = new Vector3(7.0f, 8.0f, 9.0f);
        Check("a model change reaches the vector", boundVector.Value == new Vector3(7.0f, 8.0f, 9.0f),
            boundVector.Value.ToString());
        // Two decimals because the default step is 0.01, and the field never trims below its step.
        Check("each field shows its component", boundVector.Field(2).Text == "9.00",
            boundVector.Field(2).Text);

        boundVector.Field(0).Value = -1.0f;
        Check("editing a field writes back to the model", vm.Origin.X == -1.0f, vm.Origin.ToString());
        Check("the other components survive the write-back",
            vm.Origin.Y == 8.0f && vm.Origin.Z == 9.0f, vm.Origin.ToString());

        vm.Radius = 12.0f;
        Check("a model change reaches the numeric field", boundNumeric.Value == 12.0f, boundNumeric.Text);
        boundNumeric.Value = 3.0f;
        Check("the numeric field writes back", MathF.Abs(vm.Radius - 3.0f) < 0.001f, $"{vm.Radius}");

        boundSection.IsExpanded = false;
        Check("collapsing writes back", !vm.SectionExpanded);
        vm.SectionExpanded = true;
        Check("the model can reopen the section", boundSection.IsExpanded);
    }
}

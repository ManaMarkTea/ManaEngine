using ManaUI;

using Button = ManaUI.Button;

namespace ManaTests;

public static class PopupChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var time = 0.0;

        // ============================================================ placement and dismissal
        var anchor = new Button("Open").Size(120.0f, 26.0f);
        var other = new Button("Elsewhere").Size(120.0f, 26.0f);
        var canvas = new Canvas().Children(
            anchor.At(100.0f, 300.0f),
            other.At(600.0f, 300.0f));

        root.Content = canvas;
        root.UpdateLayout(new Vector2(1000, 700));

        var popup = new UiPopup
        {
            Child = new Border().Fill(UiColor.White).Size(200.0f, 120.0f)
        };

        root.Popups.Show(popup, anchor);
        root.UpdateLayout(new Vector2(1000, 700));

        Check("popup opens", popup.IsOpen && root.Popups.HasOpenPopups);
        Check("popup lives in the popup layer", root.PopupLayer.Children.Contains(popup));
        Check("popup sits below its anchor", popup.Bounds.Y >= anchor.Bounds.Bottom,
            $"{popup.Bounds.Y} vs {anchor.Bounds.Bottom}");
        Check("popup aligns to the anchor's left", MathF.Abs(popup.Bounds.X - anchor.Bounds.X) < 0.01f,
            $"{popup.Bounds.X} vs {anchor.Bounds.X}");

        // Clicking inside must not dismiss.
        root.Input.InjectPointerMoved(popup.Bounds.Center);
        root.Input.InjectPointerPressed(popup.Bounds.Center, PointerButton.Left, time);
        root.Input.InjectPointerReleased(popup.Bounds.Center, PointerButton.Left);
        Check("clicking inside keeps it open", popup.IsOpen);

        // Clicking outside dismisses, and the click still reaches what was under it.
        var otherClicks = 0;
        other.Click += (_, _) => otherClicks++;
        root.Input.InjectPointerMoved(other.Bounds.Center);
        root.Input.InjectPointerPressed(other.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(other.Bounds.Center, PointerButton.Left);

        Check("clicking outside dismisses", !popup.IsOpen && !root.Popups.HasOpenPopups);
        Check("the dismissing click still lands", otherClicks == 1, $"{otherClicks}");

        // StaysOpen opts out.
        var sticky = new UiPopup
        {
            StaysOpen = true,
            Child = new Border().Fill(UiColor.White).Size(80.0f, 40.0f)
        };
        root.Popups.Show(sticky, anchor);
        root.UpdateLayout(new Vector2(1000, 700));
        root.Input.InjectPointerMoved(other.Bounds.Center);
        root.Input.InjectPointerPressed(other.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(other.Bounds.Center, PointerButton.Left);
        Check("StaysOpen survives an outside click", sticky.IsOpen);
        root.Popups.Close(sticky);

        // Escape closes the topmost.
        var escapable = new UiPopup { Child = new Border().Fill(UiColor.White).Size(80.0f, 40.0f) };
        root.Popups.Show(escapable, anchor);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("escape closes the popup",
            root.Input.InjectKeyDown(UiKey.Escape) && !escapable.IsOpen);
        Check("dismissing escape suppresses polling host shortcuts", root.Input.IsKeyboardCaptured);
        Check("held escape stays consumed", root.Input.InjectKeyDown(UiKey.Escape));
        Check("escape release clears shortcut capture",
            root.Input.InjectKeyUp(UiKey.Escape) && !root.Input.IsKeyboardCaptured);
        Check("next escape without a popup reaches the host", !root.Input.InjectKeyDown(UiKey.Escape));
        root.Input.InjectKeyUp(UiKey.Escape);

        // Flipping: an anchor near the bottom should push the popup above it instead of off-screen.
        var lowAnchor = new Button("Low").Size(100.0f, 26.0f);
        root.Content = null;
        root.Content = new Canvas().Children(lowAnchor.At(100.0f, 660.0f));
        root.UpdateLayout(new Vector2(1000, 700));

        var flipped = new UiPopup { Child = new Border().Fill(UiColor.White).Size(160.0f, 200.0f) };
        root.Popups.Show(flipped, lowAnchor);
        root.UpdateLayout(new Vector2(1000, 700));

        Check("popup flips above when it would overflow",
            flipped.Bounds.Bottom <= lowAnchor.Bounds.Y + 0.01f,
            $"popup {flipped.Bounds} vs anchor {lowAnchor.Bounds}");
        Check("flipped popup stays on screen", flipped.Bounds.Y >= 0.0f, $"{flipped.Bounds.Y}");
        root.Popups.CloseAll();

        // A popup wider than the space to its right is clamped, never left hanging off the edge.
        var rightAnchor = new Button("Right").Size(100.0f, 26.0f);
        root.Content = null;
        root.Content = new Canvas().Children(rightAnchor.At(920.0f, 100.0f));
        root.UpdateLayout(new Vector2(1000, 700));

        var clamped = new UiPopup { Child = new Border().Fill(UiColor.White).Size(300.0f, 100.0f) };
        root.Popups.Show(clamped, rightAnchor);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("popup clamps inside the screen", clamped.Bounds.Right <= 1000.01f, clamped.Bounds.ToString());
        root.Popups.CloseAll();

        // An anchor removed from the tree takes its popup with it.
        var doomedAnchor = new Button("Doomed").Size(100.0f, 26.0f);
        var holder = new Canvas().Children(doomedAnchor.At(100.0f, 100.0f));
        root.Content = null;
        root.Content = holder;
        root.UpdateLayout(new Vector2(1000, 700));

        var orphan = new UiPopup { Child = new Border().Fill(UiColor.White).Size(80.0f, 40.0f) };
        root.Popups.Show(orphan, doomedAnchor);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("popup open before the anchor goes", orphan.IsOpen);

        holder.Children.Remove(doomedAnchor);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("popup closes when its anchor leaves the tree", !orphan.IsOpen);

        // ============================================================ ComboBox
        Console.WriteLine("  -- combo box --");

        var options = new[] { "Place", "Erase", "Paint", "Select", "Text" };
        var combo = new ComboBox().Options(options).Selected(0).Width(200.0f);
        root.Content = null;
        root.Content = new Canvas().Children(combo.At(100.0f, 100.0f));
        root.UpdateLayout(new Vector2(1000, 700));

        Check("combo starts closed", !combo.IsDropDownOpen);
        Check("combo has its items", combo.Items.Count == 5, $"{combo.Items.Count}");
        Check("combo has a selection", combo.SelectedItem as string == "Place", combo.SelectedItem?.ToString() ?? "null");

        root.Input.InjectPointerMoved(combo.Bounds.Center);
        root.Input.InjectPointerPressed(combo.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(combo.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 700));

        Check("clicking opens the drop down", combo.IsDropDownOpen);
        Check("drop down matches the anchor width",
            MathF.Abs(root.Popups.Topmost!.Bounds.Width - combo.Bounds.Width) < 0.01f,
            $"{root.Popups.Topmost.Bounds.Width} vs {combo.Bounds.Width}");

        // The classic bug: clicking the combo again must close it, not close-and-reopen.
        root.Input.InjectPointerMoved(combo.Bounds.Center);
        root.Input.InjectPointerPressed(combo.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(combo.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("clicking the anchor again closes it", !combo.IsDropDownOpen);

        // Picking an option.
        root.Input.InjectPointerMoved(combo.Bounds.Center);
        root.Input.InjectPointerPressed(combo.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(combo.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 700));

        var dropDown = root.Popups.Topmost!;
        var rows = FindAll<Control>(dropDown).Where(c => c.GetType().Name == "ComboBoxOption").ToList();
        Check("drop down lists every option", rows.Count == 5, $"{rows.Count} rows");

        var thirdRow = rows[2];
        root.Input.InjectPointerMoved(thirdRow.Bounds.Center);
        root.Input.InjectPointerPressed(thirdRow.Bounds.Center, PointerButton.Left, time += 1.0);
        root.Input.InjectPointerReleased(thirdRow.Bounds.Center, PointerButton.Left);
        root.UpdateLayout(new Vector2(1000, 700));

        Check("picking an option selects it", combo.SelectedItem as string == "Paint",
            combo.SelectedItem?.ToString() ?? "null");
        Check("picking closes the drop down", !combo.IsDropDownOpen);

        // Keyboard, closed.
        combo.Focus();
        var beforeKey = combo.SelectedIndex;
        root.Input.InjectKeyDown(UiKey.Down);
        Check("down arrow advances the selection", combo.SelectedIndex == beforeKey + 1,
            $"{beforeKey} -> {combo.SelectedIndex}");
        root.Input.InjectKeyDown(UiKey.Up);
        Check("up arrow goes back", combo.SelectedIndex == beforeKey, $"{combo.SelectedIndex}");

        root.Input.InjectKeyDown(UiKey.Enter);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("enter opens the drop down", combo.IsDropDownOpen);
        root.Input.InjectKeyDown(UiKey.Escape);
        root.UpdateLayout(new Vector2(1000, 700));
        Check("escape closes the drop down", !combo.IsDropDownOpen);

        // Wheel only when focused, so it cannot hijack panel scrolling.
        var unfocused = new ComboBox().Options(options).Selected(2).Width(200.0f);
        root.Content = null;
        root.Content = new Canvas().Children(unfocused.At(100.0f, 100.0f));
        root.UpdateLayout(new Vector2(1000, 700));
        root.Input.InjectPointerMoved(unfocused.Bounds.Center);
        root.Input.InjectPointerWheel(new Vector2(0.0f, 1.0f));
        Check("wheel ignored when unfocused", unfocused.SelectedIndex == 2, $"{unfocused.SelectedIndex}");

        unfocused.Focus();
        root.Input.InjectPointerWheel(new Vector2(0.0f, 1.0f));
        Check("wheel changes selection when focused", unfocused.SelectedIndex == 1,
            $"{unfocused.SelectedIndex}");

        // Two-way binding.
        var vm = new DemoViewModel();
        var bound = new ComboBox().Options(options).Width(200.0f).BindSelectedIndex(vm, v => v.VoxelCount);
        root.Content = null;
        root.Content = new Canvas().Children(bound.At(100.0f, 100.0f));
        root.UpdateLayout(new Vector2(1000, 700));

        Check("combo pulls initial value", bound.SelectedIndex == 0, $"{bound.SelectedIndex}");
        vm.VoxelCount = 3;
        Check("source -> combo", bound.SelectedIndex == 3, $"{bound.SelectedIndex}");
        bound.SelectedIndex = 1;
        Check("combo -> source", vm.VoxelCount == 1, $"{vm.VoxelCount}");

        // Rendering. A fresh combo rather than re-hosting an existing one -- the framework rejects
        // re-parenting an element that still has a parent, which is the correct behaviour.
        var rendered = new ComboBox().Options(options).Selected(1).Width(200.0f);
        root.Content = null;
        root.Content = new Canvas().Children(rendered.At(100.0f, 100.0f));
        root.UpdateLayout(new Vector2(1000, 700));
        rendered.OpenDropDown();
        root.UpdateLayout(new Vector2(1000, 700));

        var drawn = root.Render();
        Check("open combo renders", drawn.InstanceCount > 20, $"{drawn.InstanceCount} instances");
        Check("still one draw call", drawn.Batches.Count == 1, $"{drawn.Batches.Count} batches");
        rendered.CloseDropDown();

        Tooltips(root, Check);
    }

    /// <summary>
    /// Tooltips, which are about dwell rather than arrival: the pointer has to rest before one appears, and
    /// leaving takes it away again.
    /// </summary>
    private static void Tooltips(UiRoot root, Action<string, bool, string> Check)
    {
        var described = new Button().Label("Slot").Size(60.0f, 30.0f);
        described.Tooltip = "Assets/Textures/MaterialTextures/rock_albedo.png";

        // Wrapped, to prove the nearest ancestor carrying one wins -- the pointer lands on the label inside
        // the button, not on the button itself.
        var plain = new Button().Label("Plain").Size(60.0f, 30.0f);

        root.Content = null;
        root.Content = new Canvas().Children(
            described.At(100.0f, 100.0f),
            plain.At(300.0f, 100.0f));

        root.UpdateLayout(new Vector2(1000, 700));

        var openBefore = root.Popups.Open.Count;

        root.Input.InjectPointerMoved(described.Bounds.Center);
        root.Popups.Tick(0.1);
        root.UpdateLayout(new Vector2(1000, 700));

        Check("a tooltip does not appear the moment the pointer arrives",
            root.Popups.Open.Count == openBefore, $"{root.Popups.Open.Count} popups");

        // Past the dwell.
        root.Popups.Tick(0.6);
        root.UpdateLayout(new Vector2(1000, 700));

        var tip = root.Popups.Open.LastOrDefault();
        Check("resting on it shows one", root.Popups.Open.Count == openBefore + 1,
            $"{root.Popups.Open.Count} popups");

        Check("and it carries the text",
            tip != null && FindAll<TextBlock>(tip.Child!).Any(t => t.Text.Contains("rock_albedo.png")),
            tip == null ? "no popup" : "text found");

        // A tooltip is transparent to the pointer. Hit-testable by default, one appearing under the cursor
        // would take the hover from the thing it describes -- which closes it, which gives the hover back,
        // forever -- and eat the click as well.
        // Asserted by aiming at the tooltip itself rather than by reading the flag: the pointer is resting
        // above it here, so a hit-testable tooltip would not be caught by watching the hover from outside.
        var inside = tip?.Bounds.Center ?? Vector2.Zero;
        var underneath = root.HitTest(inside);

        Check("the pointer passes straight through it",
            tip != null && !ReferenceEquals(underneath, tip) && !IsWithin2(underneath, tip),
            underneath?.GetType().Name ?? "nothing");

        Check("and the control it describes keeps the hover",
            root.Input.HoveredElement != null && IsWithin(root.Input.HoveredElement, described),
            root.Input.HoveredElement?.GetType().Name ?? "null");

        // Moving away takes it back.
        root.Input.InjectPointerMoved(plain.Bounds.Center);
        root.Popups.Tick(0.1);
        root.UpdateLayout(new Vector2(1000, 700));

        Check("moving off closes it", root.Popups.Open.Count == openBefore,
            $"{root.Popups.Open.Count} popups");

        // An element with no tooltip never opens one, however long the pointer rests.
        root.Popups.Tick(2.0);
        root.UpdateLayout(new Vector2(1000, 700));

        Check("something with no tooltip stays quiet", root.Popups.Open.Count == openBefore,
            $"{root.Popups.Open.Count} popups");

        root.Input.InjectPointerMoved(new Vector2(900.0f, 600.0f));
        root.Popups.Tick(0.1);

        // A plain label with a tooltip. This is the case that did not work: hover is decided by the hit
        // test, and a TextBlock deliberately lets the pointer through to whatever is behind it -- so an
        // inspector full of labelled rows showed no tooltips at all while a Button showed its own fine.
        var quiet = new TextBlock().Text("Degrees per second").Size(120.0f, 20.0f);
        quiet.Tooltip = "How fast it turns.";

        root.Content = null;
        root.Content = new Canvas().Children(quiet.At(100.0f, 100.0f));
        root.UpdateLayout(new Vector2(1000, 700));

        root.Input.InjectPointerMoved(quiet.Bounds.Center);
        Check("a label carrying a tooltip is found by the hit test",
            root.Input.HoveredElement is TextBlock, root.Input.HoveredElement?.GetType().Name ?? "nothing");

        var beforeLabel = root.Popups.Open.Count;
        root.Popups.Tick(0.1);
        root.Popups.Tick(1.0);
        root.UpdateLayout(new Vector2(1000, 700));

        Check("so resting on it shows its tooltip", root.Popups.Open.Count == beforeLabel + 1,
            $"{root.Popups.Open.Count} popups");

        // Still transparent when it has nothing to say, so an ordinary label does not start swallowing
        // hover from whatever is behind it.
        var silent = new TextBlock().Text("Plain").Size(120.0f, 20.0f);
        root.Content = null;
        root.Content = new Canvas().Children(silent.At(100.0f, 100.0f));
        root.UpdateLayout(new Vector2(1000, 700));

        root.Input.InjectPointerMoved(silent.Bounds.Center);
        Check("a label without one stays transparent to the pointer",
            root.Input.HoveredElement == null, root.Input.HoveredElement?.GetType().Name ?? "nothing");

        root.Popups.CloseAll();
        root.Input.InjectPointerMoved(new Vector2(900.0f, 600.0f));
        root.Popups.Tick(0.1);
    }

    private static bool IsWithin2(UiElement? element, UiElement ancestor)
    {
        return element != null && IsWithin(element, ancestor);
    }

    private static bool IsWithin(UiElement element, UiElement ancestor)
    {
        for (var current = element; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<T> FindAll<T>(UiElement element) where T : UiElement
    {
        if (element is T typed)
        {
            yield return typed;
        }

        var count = element.VisualChildCount;
        for (var i = 0; i < count; i++)
        {
            foreach (var found in FindAll<T>(element.GetVisualChild(i)))
            {
                yield return found;
            }
        }
    }
}

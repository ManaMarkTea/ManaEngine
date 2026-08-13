using ManaUI;
using ManaVoxel.Code.Tools;
using ManaVoxel.Code.UI;
using ManaVoxel.Code.UI.ManaUi;

namespace ManaTests;

/// <summary>
/// Drives the ported voxel tool palette against the editor's real view model.
/// <para>
/// This is the check that matters most for the framework: not that a demo panel works, but that a panel
/// built for an immediate-mode UI -- public mutable fields, no change notification -- can be driven from
/// ManaUI without the model being rewritten first.
/// </para>
/// </summary>
public static class PortedPaletteChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var vm = new VoxelEditorViewModel();

        UiElement? palette = null;
        try
        {
            palette = VoxelToolPaletteManaView.Build(root.Theme, vm);
            Check("ported palette constructs", true);
        }
        catch (Exception ex)
        {
            Check("ported palette constructs", false, ex.Message);
            return;
        }

        root.Content = palette;
        root.UpdateLayout(new Vector2(420, 900));

        Check("palette lays out", palette.Bounds.Width > 100 && palette.Bounds.Height > 100,
            palette.Bounds.ToString());

        var drawn = root.Render();
        Check("palette renders", drawn.InstanceCount > 30, $"{drawn.InstanceCount} instances");
        Check("still one draw call", drawn.Batches.Count == 1, $"{drawn.Batches.Count} batches");

        // ---------------------------------------------------------------- model -> UI (polled)
        var toolCombo = FindAll<ComboBox>(root).First();
        vm.SelectedToolIndex = 3;
        root.UpdateLayout(new Vector2(420, 900));
        Check("model change reaches the combo", toolCombo.SelectedIndex == 3, $"{toolCombo.SelectedIndex}");

        var selectBox = FindAll<CheckBox>(root).First();
        vm.IsSelectToolActive = false;
        root.UpdateLayout(new Vector2(420, 900));
        Check("model change reaches the checkbox", !selectBox.IsChecked, $"{selectBox.IsChecked}");

        // Polled bindings only apply on a layout tick, which is the documented trade for reading a model
        // that cannot announce its own changes.
        vm.IsSelectToolActive = true;
        Check("polled read waits for the tick", !selectBox.IsChecked, "unchanged before UpdateLayout");
        root.UpdateLayout(new Vector2(420, 900));
        Check("polled read applies on the tick", selectBox.IsChecked, "updated after UpdateLayout");

        // ---------------------------------------------------------------- UI -> model (events)
        toolCombo.SelectedIndex = 1;
        Check("combo writes back to the model", vm.SelectedToolIndex == 1, $"{vm.SelectedToolIndex}");

        selectBox.IsChecked = false;
        Check("checkbox writes back to the model", !vm.IsSelectToolActive, $"{vm.IsSelectToolActive}");

        var planeCombo = FindAll<ComboBox>(root).Skip(1).First();
        planeCombo.SelectedIndex = 2;
        Check("work plane writes back", vm.SelectedWorkPlaneIndex == 2, $"{vm.SelectedWorkPlaneIndex}");

        // ---------------------------------------------------------------- conditional sections
        vm.SelectedToolIndex = VoxelToolCatalog.GetToolIndex(VoxelToolKind.Stroke);
        root.UpdateLayout(new Vector2(420, 900));

        var radiusVisible = IsSliderVisible(root, 0, 12);
        Check("radius shown for stroke tools", radiusVisible, "radius slider laid out");

        vm.SelectedToolIndex = VoxelToolCatalog.GetToolIndex(VoxelToolKind.Sphere);
        root.UpdateLayout(new Vector2(420, 900));
        Check("radius hidden for sphere", !IsSliderVisible(root, 0, 12), "radius slider collapsed");

        var textBox = FindAll<TextBox>(root).First();
        Check("text field hidden outside the text tool", textBox.Bounds.IsEmpty, textBox.Bounds.ToString());

        vm.SelectedToolIndex = VoxelToolCatalog.GetToolIndex(VoxelToolKind.Text);
        root.UpdateLayout(new Vector2(420, 900));
        Check("text field shown for the text tool", !textBox.Bounds.IsEmpty, textBox.Bounds.ToString());

        // ---------------------------------------------------------------- values
        var scaleSlider = FindSlider(root, 1, 8);
        Check("scale slider found", scaleSlider != null, "1..8");
        scaleSlider!.Value = 5.0f;
        Check("scale writes back as an integer", vm.TextScale == 5, $"{vm.TextScale}");

        vm.TextDepth = 9;
        root.UpdateLayout(new Vector2(420, 900));
        var depthSlider = FindSlider(root, 1, 16);
        Check("depth reads back from the model",
            depthSlider != null && MathF.Abs(depthSlider.Value - 9.0f) < 0.01f, $"{depthSlider?.Value}");

        // Colour channels round-trip through a Vector4 field.
        vm.PaintVoxelColor = new Vector4(0.25f, 0.5f, 0.75f, 1.0f);
        root.UpdateLayout(new Vector2(420, 900));

        var channels = FindAll<Slider>(root).Where(s => s.Minimum == 0.0f && s.Maximum == 1.0f).ToList();
        Check("three colour channels", channels.Count == 3, $"{channels.Count}");
        Check("red channel reads the model", MathF.Abs(channels[0].Value - 0.25f) < 0.01f,
            $"{channels[0].Value}");

        channels[1].Value = 0.9f;
        Check("green channel writes the model", MathF.Abs(vm.PaintVoxelColor.Y - 0.9f) < 0.01f,
            $"{vm.PaintVoxelColor.Y}");
        Check("writing one channel leaves the others",
            MathF.Abs(vm.PaintVoxelColor.X - 0.25f) < 0.01f
            && MathF.Abs(vm.PaintVoxelColor.Z - 0.75f) < 0.01f,
            vm.PaintVoxelColor.ToString());

        // The swatch follows the colour, which is the whole point of showing it. It reads through a polled
        // binding, so it needs a tick before it can have caught up.
        root.UpdateLayout(new Vector2(420, 900));
        var swatch = FindAll<ColorField>(root).FirstOrDefault();
        Check("swatch tracks the paint colour",
            swatch != null && MathF.Abs(swatch.Color.Y - 0.9f) < 0.01f,
            swatch?.Color.ToString() ?? "not found");

        // And the other way: the swatch opens a real picker now, and choosing there writes the model that
        // the ImGui palette and the eyedropper share.
        if (swatch != null)
        {
            swatch.Color = new Vector4(0.1f, 0.2f, 0.8f, 1.0f);
            Check("picking a colour writes the model",
                MathF.Abs(vm.PaintVoxelColor.Z - 0.8f) < 0.01f, vm.PaintVoxelColor.ToString());

            // Voxels are opaque, so offering an alpha control there would be a lie.
            Check("the palette hides alpha", !swatch.ShowAlpha, "opaque voxels");

            root.UpdateLayout(new Vector2(420, 900));
            root.Input.InjectPointerMoved(swatch.Bounds.Center);
            root.Input.InjectPointerPressed(swatch.Bounds.Center, PointerButton.Left, 9.0);
            root.Input.InjectPointerReleased(swatch.Bounds.Center, PointerButton.Left);
            root.UpdateLayout(new Vector2(420, 900));

            Check("clicking the swatch opens the picker", swatch.IsOpen);
            swatch.Close();
            root.UpdateLayout(new Vector2(420, 900));
        }

        // ---------------------------------------------------------------- end to end
        root.UpdateLayout(new Vector2(420, 900));
        var final = root.Render();
        Check("palette still renders after edits", final.InstanceCount > 30, $"{final.InstanceCount}");

        ToolPanel(fonts, check);
    }

    private static bool IsSliderVisible(UiRoot root, float minimum, float maximum)
    {
        var slider = FindSlider(root, minimum, maximum);
        return slider != null && !slider.Bounds.IsEmpty;
    }

    private static Slider? FindSlider(UiRoot root, float minimum, float maximum)
    {
        return FindAll<Slider>(root)
            .FirstOrDefault(s => MathF.Abs(s.Minimum - minimum) < 0.01f && MathF.Abs(s.Maximum - maximum) < 0.01f);
    }

    /// <summary>
    /// The editor's tool panels, ported. One per subject rather than one panel with a tab bar, because the
    /// dock already tabs anything sharing a slot -- so this gives the same arrangement by default and lets
    /// them be pulled apart.
    /// </summary>
    public static void ToolPanel(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var vm = new VoxelEditorViewModel();

        UiElement? stats = null;
        UiElement? placement = null;
        UiElement? physics = null;
        UiElement? debug = null;

        try
        {
            stats = VoxelEditorPanelsManaView.BuildStats(root.Theme, vm);
            placement = VoxelEditorPanelsManaView.BuildPlacement(root.Theme, vm);
            physics = VoxelEditorPanelsManaView.BuildPhysics(root.Theme, vm);
            debug = VoxelEditorPanelsManaView.BuildDebug(root.Theme, vm);
            Check("the ported tool panels construct", true);
        }
        catch (Exception ex)
        {
            Check("the ported tool panels construct", false, ex.Message);
            return;
        }

        // Each stands on its own, which is the point of splitting them: any of these can be the only thing
        // in a dock slot.
        foreach (var (name, panel) in new (string, UiElement)[]
        {
            ("Voxel Stats", stats),
            ("Placement", placement),
            ("Physics", physics),
            ("Debug", debug)
        })
        {
            root.Content = null;
            root.Content = panel;
            root.UpdateLayout(new Vector2(420, 900));

            Check($"{name} renders on its own", root.Render().InstanceCount > 5,
                $"{root.Render().InstanceCount} instances");
            Check($"and {name} is one draw call", root.Render().Batches.Count == 1,
                $"{root.Render().Batches.Count} batches");
        }

        // ---------------------------------------------------------------- stats
        root.Content = null;
        root.Content = stats;
        root.UpdateLayout(new Vector2(420, 900));

        Check("stats shows the session counters",
            FindAll<Expander>(stats).Any(e => e.Header == "Session Stats"),
            string.Join(", ", FindAll<Expander>(stats).Select(e => e.Header)));

        Check("and a frame graph", FindAll<Sparkline>(stats).Any(), "sparkline present");

        // ---------------------------------------------------------------- physics
        root.Content = null;
        root.Content = physics;
        root.UpdateLayout(new Vector2(420, 900));

        var sliders = FindAll<SliderBox>(physics).Where(s => s.Bounds.Width > 0.0f).ToList();
        Check("physics has its tuning sliders", sliders.Count >= 8, $"{sliders.Count} sliders");

        var strength = sliders.FirstOrDefault(s => MathF.Abs(s.Value - vm.BakeExplosionStrength) < 0.001f);
        if (strength != null)
        {
            var before = vm.BakeExplosionStrength;
            strength.Value = before + 5.0f;
            Check("and dragging one writes to the editor",
                MathF.Abs(vm.BakeExplosionStrength - (before + 5.0f)) < 0.01f, $"{vm.BakeExplosionStrength}");
        }

        var explode = FindAll<Button>(physics).FirstOrDefault(b => b.Text == "Explode All Baked Voxels");
        Check("an action needing baked voxels is disabled without them",
            explode != null && !explode.IsEnabled, $"{explode?.IsEnabled}");

        // ---------------------------------------------------------------- placement
        root.Content = null;
        root.Content = placement;
        root.UpdateLayout(new Vector2(420, 900));

        Check("with nothing hovered the placement readout says so",
            FindAll<TextBlock>(placement).Any(t => t.Text == "No hovered voxel"), "empty state");
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

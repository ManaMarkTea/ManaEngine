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
            // Composed the way the editor toolbar composes it -- tools, then the work plane pinned after
            // them -- rather than through a wrapper that exists only for this test to call.
            palette = new StackPanel().Horizontal(root.Theme.Spacing).Children(
                VoxelToolPaletteManaView.BuildTools(root.Theme, vm),
                VoxelToolPaletteManaView.BuildWorkPlane(root.Theme, vm));
            Check("ported palette constructs", true);
        }
        catch (Exception ex)
        {
            Check("ported palette constructs", false, ex.Message);
            return;
        }

        root.Content = palette;

        // ---------------------------------------------------------------- the strip's height is the point
        // This sits directly above the viewport, so every pixel it asks for is one the viewport does not get.
        // Captions used to stack above their controls, which made the strip two rows tall to label a single
        // row of them; inline they cost nothing, because the control is already the tallest thing in the row.
        // Asserted rather than merely measured, because the regression is invisible -- the toolbar stretches
        // to fill whatever strip it is given, so growing back would just quietly eat viewport.
        // The bound is one control row (24) plus the strip's padding and its horizontal scroll bar's
        // allowance, with a couple of pixels of slack -- tight enough that a return to stacked captions (67px
        // when this was written) fails it, loose enough that a control gaining a pixel of padding does not.
        palette.Measure(new Vector2(1600, 10000.0f));
        Check("the toolbar asks for about one control row, not two",
            palette.DesiredSize.Y <= 44.0f, $"{palette.DesiredSize.Y:F1}px");

        root.UpdateLayout(new Vector2(1600, 120));

        Check("palette lays out", palette.Bounds.Width > 100 && palette.Bounds.Height > 100,
            palette.Bounds.ToString());

        var drawn = root.Render();
        Check("palette renders", drawn.InstanceCount > 30, $"{drawn.InstanceCount} instances");
        Check("still one draw call", drawn.Batches.Count == 1, $"{drawn.Batches.Count} batches");

        // ---------------------------------------------------------------- a toolbar strip's real shape
        // This lives in a wide, thin strip under the menu now, not a tall side column -- laid out at that
        // shape, its groups have to sit side by side rather than stacked, or the strip is just as useless
        // as it was before the redesign.
        root.UpdateLayout(new Vector2(1400, 56));

        var selectButtonBefore = FindAll<Button>(root).First(b => b.Tooltip == "Select");
        var swatchBefore = FindAll<ColorField>(root).First();
        Check("the tool row and the colour swatch sit side by side, not stacked",
            MathF.Abs(selectButtonBefore.Bounds.Y - swatchBefore.Bounds.Y) < swatchBefore.Bounds.Height
                && swatchBefore.Bounds.X > selectButtonBefore.Bounds.Right,
            $"select {selectButtonBefore.Bounds}, swatch {swatchBefore.Bounds}");

        root.UpdateLayout(new Vector2(1600, 120));

        // ---------------------------------------------------------------- model -> UI (polled)
        // Icon toggles rather than a dropdown, like the ImGui palette -- a tool's own button carries the
        // "current tool" state as its Variant instead of a combo box's SelectedIndex.
        // Select mode is exclusive with the tools now, so a tool button only shows selected once select
        // mode is off -- exactly the state a real tool pick leaves the model in.
        var strokeButton = FindAll<Button>(root).First(b => b.Tooltip == "Stroke");
        vm.SelectedToolIndex = VoxelToolCatalog.GetToolIndex(VoxelToolKind.Stroke);
        vm.IsSelectToolActive = false;
        root.UpdateLayout(new Vector2(1600, 120));
        Check("model change reaches the tool button",
            strokeButton.Variant == ButtonVariant.Primary, $"{strokeButton.Variant}");

        var selectButton = FindAll<Button>(root).First(b => b.Tooltip == "Select");
        vm.IsSelectToolActive = false;
        root.UpdateLayout(new Vector2(1600, 120));
        Check("model change reaches the select button",
            selectButton.Variant != ButtonVariant.Primary, $"{selectButton.Variant}");

        // Polled bindings only apply on a layout tick, which is the documented trade for reading a model
        // that cannot announce its own changes.
        vm.IsSelectToolActive = true;
        Check("polled read waits for the tick",
            selectButton.Variant != ButtonVariant.Primary, "unchanged before UpdateLayout");
        root.UpdateLayout(new Vector2(1600, 120));
        Check("polled read applies on the tick",
            selectButton.Variant == ButtonVariant.Primary, "updated after UpdateLayout");

        // ---------------------------------------------------------------- UI -> model (events)
        var lineButton = FindAll<Button>(root).First(b => b.Tooltip == "Line");
        Press(root, lineButton.Bounds.Center);
        Check("clicking a tool button writes back to the model and leaves select mode",
            vm.SelectedToolIndex == VoxelToolCatalog.GetToolIndex(VoxelToolKind.Line) && !vm.IsSelectToolActive,
            $"tool {vm.SelectedToolIndex}, select {vm.IsSelectToolActive}");

        // Select is a mode of its own now, not a toggle -- clicking it always turns it on rather than
        // flipping it, the same way clicking a tool always makes that tool current rather than clearing it.
        Press(root, selectButton.Bounds.Center);
        Check("clicking select turns select mode on, leaving the last tool as-is underneath it",
            vm.IsSelectToolActive && vm.SelectedToolIndex == VoxelToolCatalog.GetToolIndex(VoxelToolKind.Line),
            $"select {vm.IsSelectToolActive}, tool {vm.SelectedToolIndex}");

        Press(root, lineButton.Bounds.Center);
        Check("and picking a tool again leaves select mode",
            !vm.IsSelectToolActive, $"select {vm.IsSelectToolActive}");

        var planeCombo = FindAll<ComboBox>(root).First();
        planeCombo.SelectedIndex = 2;
        Check("work plane writes back", vm.SelectedWorkPlaneIndex == 2, $"{vm.SelectedWorkPlaneIndex}");

        // ---------------------------------------------------------------- conditional sections
        vm.SelectedToolIndex = VoxelToolCatalog.GetToolIndex(VoxelToolKind.Stroke);
        root.UpdateLayout(new Vector2(1600, 120));

        var radiusVisible = IsSliderVisible(root, 0, 12);
        Check("radius shown for stroke tools", radiusVisible, "radius slider laid out");

        vm.SelectedToolIndex = VoxelToolCatalog.GetToolIndex(VoxelToolKind.Sphere);
        root.UpdateLayout(new Vector2(1600, 120));
        Check("radius hidden for sphere", !IsSliderVisible(root, 0, 12), "radius slider collapsed");

        var textBox = FindAll<TextBox>(root).First();
        Check("text field hidden outside the text tool", textBox.Bounds.IsEmpty, textBox.Bounds.ToString());

        vm.SelectedToolIndex = VoxelToolCatalog.GetToolIndex(VoxelToolKind.Text);
        root.UpdateLayout(new Vector2(1600, 120));
        Check("text field shown for the text tool", !textBox.Bounds.IsEmpty, textBox.Bounds.ToString());

        // ---------------------------------------------------------------- values
        var scaleSlider = FindSlider(root, 1, 8);
        Check("scale slider found", scaleSlider != null, "1..8");
        scaleSlider!.Value = 5.0f;
        Check("scale writes back as an integer", vm.TextScale == 5, $"{vm.TextScale}");

        vm.TextDepth = 9;
        root.UpdateLayout(new Vector2(1600, 120));
        var depthSlider = FindSlider(root, 1, 16);
        Check("depth reads back from the model",
            depthSlider != null && MathF.Abs(depthSlider.Value - 9.0f) < 0.01f, $"{depthSlider?.Value}");

        // Colour lives entirely behind the swatch's own picker now -- the toolbar has no room for a copy of
        // it, so a Vector4 field round-trips through the swatch rather than through channel sliders.
        vm.PaintVoxelColor = new Vector4(0.25f, 0.9f, 0.75f, 1.0f);

        // The swatch follows the colour, which is the whole point of showing it. It reads through a polled
        // binding, so it needs a tick before it can have caught up.
        root.UpdateLayout(new Vector2(1600, 120));
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

            root.UpdateLayout(new Vector2(1600, 120));
            root.Input.InjectPointerMoved(swatch.Bounds.Center);
            root.Input.InjectPointerPressed(swatch.Bounds.Center, PointerButton.Left, 9.0);
            root.Input.InjectPointerReleased(swatch.Bounds.Center, PointerButton.Left);
            root.UpdateLayout(new Vector2(1600, 120));

            Check("clicking the swatch opens the picker", swatch.IsOpen);
            swatch.Close();
            root.UpdateLayout(new Vector2(1600, 120));
        }

        // ---------------------------------------------------------------- end to end
        root.UpdateLayout(new Vector2(1600, 120));
        var final = root.Render();
        Check("palette still renders after edits", final.InstanceCount > 30, $"{final.InstanceCount}");

        ToolPanel(fonts, check);
        Lighting(fonts, check);
        About(fonts, check);
    }

    /// <summary>
    /// The lighting and PBR debug panel, ported. Everything writes to the settings object the renderer
    /// reads each frame, and everything polls it back -- these are changed by more than this panel.
    /// </summary>
    public static void Lighting(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var settings = new ManaCore.Graphics.ManaIrradianceSettings();

        UiElement? panel = null;
        try
        {
            panel = IrradianceSettingsManaView.Build(root.Theme, settings);
            Check("the lighting panel constructs", true);
        }
        catch (Exception ex)
        {
            Check("the lighting panel constructs", false, ex.Message);
            return;
        }

        root.Content = panel;
        root.UpdateLayout(new Vector2(420, 900));

        Check("it renders in one draw call", root.Render().Batches.Count == 1,
            $"{root.Render().Batches.Count} batches");

        // Writes reach the renderer's settings.
        var exposure = FindAll<SliderBox>(panel)
            .FirstOrDefault(s => MathF.Abs(s.Value - settings.Exposure) < 0.001f && s.Maximum > 2.9f);

        Check("the exposure slider shows the current value", exposure != null, $"{settings.Exposure}");

        if (exposure != null)
        {
            exposure.Value = 2.0f;
            Check("and moving it changes the render settings",
                MathF.Abs(settings.Exposure - 2.0f) < 0.01f, $"{settings.Exposure}");
        }

        // And changes made elsewhere show up, since the renderer and other panels write these too.
        settings.Exposure = 1.25f;
        root.UpdateLayout(new Vector2(420, 900));
        Check("a change made elsewhere shows up here",
            exposure != null && MathF.Abs(exposure.Value - 1.25f) < 0.01f, $"{exposure?.Value}");

        // The debug mode index is what reaches the shader, so the order of the list is not cosmetic.
        var modes = FindAll<ComboBox>(panel).FirstOrDefault();
        Check("the PBR debug outputs are offered", modes != null && modes.Items.Count > 10,
            $"{modes?.Items.Count} modes");

        if (modes != null)
        {
            Check("with Off first, as the shader numbers them",
                modes.Items[0] as string == "Off", modes.Items[0]?.ToString() ?? "none");

            modes.SelectedIndex = 2;
            Check("and choosing one reaches the shader settings",
                (int)settings.WebGpuDebugMode == 2, $"{settings.WebGpuDebugMode}");
        }

        // Reset means what the engine ships with, not what happened to be set when the panel was built.
        settings.Exposure = 3.0f;
        settings.WebGpuDebugMode = 5;

        var reset = FindAll<Button>(panel).FirstOrDefault(b => b.Text == "Reset to defaults");
        Check("a reset is offered", reset != null);

        if (reset != null)
        {
            root.UpdateLayout(new Vector2(420, 900));
            root.Input.InjectPointerMoved(reset.Bounds.Center);
            root.Input.InjectPointerPressed(reset.Bounds.Center, PointerButton.Left, 5.0);
            root.Input.InjectPointerReleased(reset.Bounds.Center, PointerButton.Left);

            var fresh = new ManaCore.Graphics.ManaIrradianceSettings();
            Check("and it restores the shipped defaults",
                MathF.Abs(settings.Exposure - fresh.Exposure) < 0.001f
                    && (int)settings.WebGpuDebugMode == (int)fresh.WebGpuDebugMode,
                $"exposure {settings.Exposure}, mode {settings.WebGpuDebugMode}");
        }
    }

    /// <summary>
    /// The diagnostics snapshot, ported. A snapshot rather than a live readout, which the panel says --
    /// these are the values the backend reported at start-up.
    /// </summary>
    public static void About(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);

        var snapshot = new ManaCore.Graphics.ManaGraphicsDiagnosticsSnapshot(
            DateTimeOffset.Now,
            new[]
            {
                new ManaCore.Graphics.ManaGraphicsDiagnosticSection(
                    "Device",
                    new[]
                    {
                        new ManaCore.Graphics.ManaGraphicsDiagnosticEntry("Adapter", "Test GPU"),
                        new ManaCore.Graphics.ManaGraphicsDiagnosticEntry(
                            "Notes",
                            "Budget and usage are assigned by the operating system and can change "
                            + "dynamically, so these figures are a snapshot rather than a limit."),
                        new ManaCore.Graphics.ManaGraphicsDiagnosticEntry(
                            "Max texture size", "16384", ManaCore.Graphics.ManaGraphicsDiagnosticScope.WebGpu)
                    }),

                new ManaCore.Graphics.ManaGraphicsDiagnosticSection(
                    "Limits",
                    new[]
                    {
                        new ManaCore.Graphics.ManaGraphicsDiagnosticEntry(
                            "Bind groups", "4", ManaCore.Graphics.ManaGraphicsDiagnosticScope.WebGpu, "8")
                    },
                    valueHeading: "WebGPU",
                    comparisonHeading: "OpenGL")
            },
            "startup");

        var panel = AboutManaView.Build(root.Theme, snapshot, "WebGPU");
        root.Content = panel;
        root.UpdateLayout(new Vector2(560, 900));

        Check("the about panel renders", root.Render().InstanceCount > 20,
            $"{root.Render().InstanceCount} instances");
        Check("in one draw call", root.Render().Batches.Count == 1, $"{root.Render().Batches.Count} batches");

        var sections = FindAll<Expander>(panel).Select(e => e.Header).ToList();
        Check("a section per group, with its count",
            sections.Any(h => h.StartsWith("Device (3)", StringComparison.Ordinal))
                && sections.Any(h => h.StartsWith("Limits (1)", StringComparison.Ordinal)),
            string.Join(", ", sections));

        var texts = FindAll<TextBlock>(panel).Select(t => t.Text).ToList();
        Check("it says when the snapshot was taken",
            texts.Any(t => t.Contains("Captured", StringComparison.Ordinal)
                && t.Contains("startup", StringComparison.Ordinal)),
            texts.FirstOrDefault(t => t.Contains("Captured", StringComparison.Ordinal)) ?? "no capture line");

        Check("and names the backend",
            texts.Any(t => t.Contains("WebGPU diagnostics", StringComparison.Ordinal)), "backend named");

        // A comparison section gets both columns, with its own headings rather than "Value".
        Check("a comparison section labels both columns",
            texts.Contains("WebGPU") && texts.Contains("OpenGL"), string.Join(", ", texts.Take(12)));

        // The ImGui table had borders and banded rows, and losing them made a dense table hard to read
        // across. Checked by what is actually drawn rather than by the elements built.
        var drawn = root.Render().Instances.ToArray();

        var bands = drawn.Count(i => i.Rect.Z - i.Rect.X > 200.0f && i.Rect.W - i.Rect.Y is > 8.0f and < 40.0f);
        Check("rows are banded", bands >= 3, $"{bands} filled row bands");

        var rules = drawn.Count(i => i.Rect.Z - i.Rect.X <= 1.5f && i.Rect.W - i.Rect.Y > 8.0f);
        Check("and columns are separated by a rule", rules >= 3, $"{rules} vertical rules");

        // A long value wraps rather than being cut off. Checked by height: a wrapped cell is taller than a
        // single line, which a truncated one never is.
        var notes = FindAll<TextBlock>(panel).FirstOrDefault(t => t.Text.StartsWith("Budget and usage", StringComparison.Ordinal));
        var adapter = FindAll<TextBlock>(panel).FirstOrDefault(t => t.Text == "Test GPU");

        Check("a long value is present in full", notes != null,
            notes?.Text[..Math.Min(40, notes.Text.Length)] ?? "missing");

        Check("and it wraps onto more than one line rather than being cut off",
            notes != null && adapter != null && notes.Bounds.Height > adapter.Bounds.Height * 1.5f,
            $"{notes?.Bounds.Height:F0}px vs {adapter?.Bounds.Height:F0}px for one line");

        Check("without spilling out of its column",
            notes != null && notes.Bounds.Right <= panel.Bounds.Right + 0.5f,
            $"{notes?.Bounds.Right:F0} within {panel.Bounds.Right:F0}");

        // The full text is still on hover, for anything that wraps to something unwieldy.
        Check("values carry their full text on hover",
            FindAll<TextBlock>(panel).Count(t => t.Tooltip != null) >= 6,
            $"{FindAll<TextBlock>(panel).Count(t => t.Tooltip != null)} with tooltips");

        // No snapshot is a normal state early in start-up, and it says so rather than showing an empty
        // frame that looks broken.
        root.Content = null;
        root.Content = AboutManaView.Build(root.Theme, null, "OpenGL");
        root.UpdateLayout(new Vector2(560, 900));

        Check("with no snapshot it says so",
            FindAll<TextBlock>(root).Any(t => t.Text.Contains("No diagnostics", StringComparison.Ordinal)),
            "empty state");
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

    private static void Press(UiRoot root, Vector2 point)
    {
        root.Input.InjectPointerMoved(point);
        root.Input.InjectPointerPressed(point, PointerButton.Left, 0.0);
        root.Input.InjectPointerReleased(point, PointerButton.Left);
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

        try
        {
            stats = VoxelEditorPanelsManaView.BuildStats(root.Theme, vm);
            placement = VoxelEditorPanelsManaView.BuildPlacement(root.Theme, vm);
            physics = VoxelEditorPanelsManaView.BuildPhysics(root.Theme, vm);
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

        Check("voxel stats exclude frame graphs", !FindAll<Sparkline>(stats).Any(), "no sparkline");

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

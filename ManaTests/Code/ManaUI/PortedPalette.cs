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
        var swatch = FindAll<Border>(root).FirstOrDefault(b => MathF.Abs(b.Height - 28.0f) < 0.01f);
        Check("swatch tracks the paint colour",
            swatch != null && MathF.Abs(swatch.Background.Y - 0.9f) < 0.01f,
            swatch?.Background.ToString() ?? "not found");

        // ---------------------------------------------------------------- end to end
        root.UpdateLayout(new Vector2(420, 900));
        var final = root.Render();
        Check("palette still renders after edits", final.InstanceCount > 30, $"{final.InstanceCount}");
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

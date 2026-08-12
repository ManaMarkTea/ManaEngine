using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManaUI;

namespace ManaTests;

/// <summary>A view model exactly as application code would write one: source-generated observables.</summary>
public partial class DemoViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Initial";

    [ObservableProperty]
    private float _radius = 4.0f;

    [ObservableProperty]
    private bool _wireframe;

    [ObservableProperty]
    private int _voxelCount;

    public ObservableCollection<LayerViewModel> Layers { get; } = new();

    public int ExecuteCount { get; private set; }

    [RelayCommand(CanExecute = nameof(CanBake))]
    private void Bake() => ExecuteCount++;

    public bool CanBakeValue = true;
    private bool CanBake() => CanBakeValue;
}

public partial class LayerViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    public LayerViewModel(string name) => _name = name;
}

public static class BindingChecks
{
    public static void Run(ManaFontManager fonts, Action<string, bool, string> check)
    {
        void Check(string what, bool ok, string detail = "") => check(what, ok, detail);

        var root = new UiRoot(fonts);
        var vm = new DemoViewModel();

        // ---------------------------------------------------------------- one-way
        var label = new TextBlock().BindText(vm, v => v.Title);
        root.Content = label;
        root.UpdateLayout(new Vector2(400, 300));

        Check("one-way applies initial value", label.Text == "Initial", label.Text);

        vm.Title = "Changed";
        Check("one-way tracks source change", label.Text == "Changed", label.Text);

        // ---------------------------------------------------------------- converter
        var counter = new TextBlock().BindText(vm, v => v.VoxelCount, n => $"{n:N0} voxels");
        root.Content = null;
        root.Content = counter;
        root.UpdateLayout(new Vector2(400, 300));

        Check("converted binding formats", counter.Text == "0 voxels", counter.Text);
        vm.VoxelCount = 12345;
        Check("converted binding updates", counter.Text == "12,345 voxels", counter.Text);

        // ---------------------------------------------------------------- two-way
        var slider = new Slider().Range(0, 10).BindValue(vm, v => v.Radius);
        var toggle = new CheckBox("Wireframe").BindIsChecked(vm, v => v.Wireframe);
        var panel = new StackPanel().Vertical(4).Children(slider, toggle);
        root.Content = null;
        root.Content = panel;
        root.UpdateLayout(new Vector2(400, 300));

        Check("two-way pulls initial", MathF.Abs(slider.Value - 4.0f) < 0.01f, $"{slider.Value}");

        vm.Radius = 7.5f;
        Check("two-way source -> target", MathF.Abs(slider.Value - 7.5f) < 0.01f, $"{slider.Value}");

        slider.Value = 2.5f;
        Check("two-way target -> source", MathF.Abs(vm.Radius - 2.5f) < 0.01f, $"{vm.Radius}");

        Check("checkbox two-way initial", !toggle.IsChecked);
        vm.Wireframe = true;
        Check("checkbox source -> target", toggle.IsChecked);
        toggle.IsChecked = false;
        Check("checkbox target -> source", !vm.Wireframe);

        // A two-way binding must not oscillate: setting the source once produces exactly one write back.
        var writes = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Radius)) writes++; };
        vm.Radius = 9.0f;
        Check("two-way does not feedback loop", writes == 1, $"{writes} source notifications");

        // ---------------------------------------------------------------- polled
        var frame = 0;
        var fps = new TextBlock().BindTextPolled(() => frame, n => $"frame {n}");
        root.Content = null;
        root.Content = fps;
        root.UpdateLayout(new Vector2(400, 300));

        Check("polled reads at bind time", fps.Text == "frame 0", fps.Text);

        frame = 42;
        Check("polled does not update without a tick", fps.Text == "frame 0", fps.Text);

        root.UpdateLayout(new Vector2(400, 300));
        Check("polled updates on layout tick", fps.Text == "frame 42", fps.Text);

        // Polled bindings feed layout, so a longer string must actually re-measure.
        var before = fps.Bounds.Width;
        frame = 1000000;
        root.UpdateLayout(new Vector2(400, 300));
        Check("polled change re-measures", fps.Bounds.Width > before, $"{before:F1} -> {fps.Bounds.Width:F1}");

        // ---------------------------------------------------------------- commands
        var button = new Button("Bake").Command(vm.BakeCommand);
        root.Content = null;
        root.Content = button;
        root.UpdateLayout(new Vector2(400, 300));

        Check("command button starts enabled", button.IsEnabled);
        root.Input.InjectPointerMoved(button.Bounds.Center);
        root.Input.InjectPointerPressed(button.Bounds.Center, PointerButton.Left, 0.0);
        root.Input.InjectPointerReleased(button.Bounds.Center, PointerButton.Left);
        Check("click executes command", vm.ExecuteCount == 1, $"{vm.ExecuteCount}");

        vm.CanBakeValue = false;
        vm.BakeCommand.NotifyCanExecuteChanged();
        Check("CanExecute disables the button", !button.IsEnabled);

        root.Input.InjectPointerPressed(button.Bounds.Center, PointerButton.Left, 1.0);
        root.Input.InjectPointerReleased(button.Bounds.Center, PointerButton.Left);
        Check("disabled command does not execute", vm.ExecuteCount == 1, $"{vm.ExecuteCount}");

        // ---------------------------------------------------------------- ItemsControl
        vm.Layers.Add(new LayerViewModel("Terrain"));
        vm.Layers.Add(new LayerViewModel("Props"));

        var list = new ItemsControl()
            .Items(vm.Layers, layer => new TextBlock().BindText(layer, l => l.Name).Height(20))
            .Spacing(2);

        root.Content = null;
        root.Content = list;
        root.UpdateLayout(new Vector2(400, 300));

        Check("items rendered", list.Children.Count == 2, $"{list.Children.Count}");
        Check("item template bound", ((TextBlock)list.Children[0]).Text == "Terrain",
            ((TextBlock)list.Children[0]).Text);

        vm.Layers.Add(new LayerViewModel("Water"));
        Check("add is incremental", list.Children.Count == 3, $"{list.Children.Count}");
        Check("appended item bound", ((TextBlock)list.Children[2]).Text == "Water",
            ((TextBlock)list.Children[2]).Text);

        // Per-item bindings must stay live, not just render once.
        vm.Layers[0].Name = "Terrain (edited)";
        Check("item property change updates row",
            ((TextBlock)list.Children[0]).Text == "Terrain (edited)",
            ((TextBlock)list.Children[0]).Text);

        var removed = vm.Layers[1];
        vm.Layers.Remove(removed);
        Check("remove is incremental", list.Children.Count == 2, $"{list.Children.Count}");
        Check("correct row removed", ((TextBlock)list.Children[1]).Text == "Water",
            ((TextBlock)list.Children[1]).Text);

        vm.Layers.Insert(0, new LayerViewModel("Sky"));
        Check("insert respects index", ((TextBlock)list.Children[0]).Text == "Sky",
            ((TextBlock)list.Children[0]).Text);

        root.UpdateLayout(new Vector2(400, 300));
        Check("rows stack in order",
            list.Children[0].Bounds.Y < list.Children[1].Bounds.Y,
            $"{list.Children[0].Bounds.Y} vs {list.Children[1].Bounds.Y}");

        vm.Layers.Clear();
        Check("clear empties the list", list.Children.Count == 0, $"{list.Children.Count}");

        // ---------------------------------------------------------------- lifetime
        // The critical leak case: a removed element must stop reacting to its source.
        var orphanVm = new DemoViewModel { Title = "Live" };
        var host = new StackPanel();
        var orphan = new TextBlock().BindText(orphanVm, v => v.Title);
        host.Children.Add(orphan);
        root.Content = null;
        root.Content = host;
        root.UpdateLayout(new Vector2(400, 300));

        Check("bound before removal", orphan.Text == "Live", orphan.Text);

        host.Children.Remove(orphan);
        orphanVm.Title = "After removal";
        Check("removed element stops tracking", orphan.Text == "Live", orphan.Text);

        // And a removed element must not be kept alive by the view model's handler list.
        var weakVm = new DemoViewModel();
        var container = new StackPanel();
        root.Content = null;
        root.Content = container;

        var reference = AddAndDrop(container, weakVm);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Check("removed element is collectable", !reference.IsAlive,
            reference.IsAlive ? "still rooted by the view model" : "collected");

        // ---------------------------------------------------------------- guardrails
        try
        {
            _ = new TextBlock().Bind(t => t.Text, vm, v => v.Layers.Count.ToString());
            Check("nested path rejected", false, "no exception");
        }
        catch (ArgumentException)
        {
            Check("nested path rejected", true, "throws with guidance");
        }
    }

    /// <summary>
    /// Binds an element, then removes it, returning only a weak reference. Kept in its own method so the
    /// strong local cannot linger on the caller's stack frame and defeat the collection check.
    /// </summary>
    private static WeakReference AddAndDrop(StackPanel container, DemoViewModel vm)
    {
        var element = new TextBlock().BindText(vm, v => v.Title);
        container.Children.Add(element);
        container.Children.Remove(element);
        return new WeakReference(element);
    }
}

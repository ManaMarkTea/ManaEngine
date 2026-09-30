using ManaUI;
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi;

/// <summary>A viewport-side drawer; only its tab intercepts input while closed.</summary>
public sealed class ViewportToolsDrawer : Panel
{
    private readonly Button _tab;
    private readonly Border _body;
    private bool _isOpen;

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            _isOpen = value;
            _tab.Text = value ? "Tools  >" : "<  Tools";
            _body.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            InvalidateMeasure();
        }
    }

    public ViewportToolsDrawer(UiTheme theme, VoxelEditorViewModel editor)
    {
        _tab = new Button("<  Tools");
        _tab.Click += (_, _) => IsOpen = !IsOpen;

        var size = new SliderBox
        {
            Minimum = 0.25f, Maximum = 3.0f, Step = 0.05f,
            HardMinimum = 0.25f, HardMaximum = 3.0f,
            DecimalPlaces = 2, Value = editor.Snap.GizmoSize
        };
        size.ValueChanged += (_, value) =>
        {
            if (!size.IsApplyingBinding) editor.Snap.GizmoSize = value;
        };
        size.BindPolled(s => s.Value, () => editor.Snap.GizmoSize);
        var reset = new Button("Reset size");
        reset.Click += (_, _) => editor.Snap.GizmoSize = 1.0f;

        _body = new Border().Fill(theme.SurfaceRaised).Rounded(theme.CornerRadius).PaddingOf(8);
        _body.Child = new StackPanel().Vertical(8).Children(
            new TextBlock().Text("Gizmo size").FontFamily(theme.FontFamilyMedium),
            size,
            new TextBlock().Text("Arrow length in world units").FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted),
            new TextBlock().Text("Drag a square to move on two axes. Or hold Shift when grabbing an arrow.").FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Wrap(),
            reset,
            PreferencesManaView.Build(theme, editor).Height(210)).Scrollable();
        Children.Add(_body);
        Children.Add(_tab);
        IsOpen = false;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _tab.Measure(new Vector2(88, 28));
        if (IsOpen) _body.Measure(new Vector2(availableSize.X, MathF.Max(0, availableSize.Y - 32)));
        return availableSize;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _tab.Arrange(new UiRect(Bounds.Right - 88, Bounds.Y, 88, 28));
        _body.Arrange(IsOpen
            ? new UiRect(Bounds.X, Bounds.Y + 32, finalSize.X, MathF.Max(0, finalSize.Y - 32))
            : UiRect.Empty);
        return finalSize;
    }
}

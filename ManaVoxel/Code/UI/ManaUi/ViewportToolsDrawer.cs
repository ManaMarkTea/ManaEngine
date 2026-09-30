using ManaUI;
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi;

/// <summary>A viewport-side drawer; only its tab intercepts input while closed.</summary>
public sealed class ViewportToolsDrawer : Panel
{
    private readonly Button _tab;
    private readonly Border _body;
    private bool _isOpen;

    /// <summary>The drawer most recently built, for the command file's open_tools.</summary>
    public static ViewportToolsDrawer? Current { get; private set; }

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
        var reset = ToolbarIcons.ActionButton(theme, ToolbarIcons.DrawUndo, "Back to the default size.", () => editor.Snap.GizmoSize = 1.0f);

        // One scroll for the whole drawer: the preferences panel's own scroll, nested inside, clipped its
        // rows. Just the snap rows are shared with it now.
        _body = new Border().Fill(theme.SurfaceRaised).Rounded(theme.CornerRadius).PaddingOf(10);
        _body.Child = new StackPanel().Vertical(8).Children(
            PreferencesManaView.Heading(theme, "Gizmo"),
            new Grid().Columns("52, *, Auto", 4.0f).Rows("Auto").Children(
                new TextBlock().Text("Size").Foreground(theme.TextMuted).CenterV().GridColumn(0),
                size.GridColumn(1),
                reset.CenterV().GridColumn(2)),
            PreferencesManaView.Heading(theme, "Snap"),
            PreferencesManaView.SnapRows(theme, editor.Snap),
            new TextBlock().Text("Shift-drag an arrow, or drag a square, to move on a plane.")
                .FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Wrap()).Scrollable();
        Current = this;
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

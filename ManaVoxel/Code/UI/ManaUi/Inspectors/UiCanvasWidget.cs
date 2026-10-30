using Mana;
using Mana.Code.Components;
using Mana.Code.Content;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors;

public sealed class UiCanvasWidget : InspectorWidget<ManaUiCanvasComponent>
{
    protected override UiElement Build(ManaUiCanvasComponent canvas, InspectorContext context)
    {
        var fields = new ReflectedPropertiesWidget { Title = _ => "UI Canvas" };
        fields.IgnoredDeclaringTypes.Add(typeof(ManaComponent));
        fields.IgnoredDeclaringTypes.Add(typeof(Mana.Code.ManaObject));
        var rows = new StackPanel().Vertical(context.Theme.Spacing);
        rows.Children.Add(fields.Build(canvas, context));
        string? error = null;
        rows.Children.Add(new ManaUI.Button().Label("Edit layout in UI Designer").OnClick(() =>
        {
            var file = ManaContentAssets.Resolve(canvas.Layout, UiMarkup.Extension);
            if (file == null)
            {
                error = "Choose a .manaui layout first.";
                return;
            }
            if (UiDesignerDocument.TryOpen(file, out error) && context.Engine != null)
            {
                EditorWorkspace.RequestPanel(context.Engine.World, UiDesignerManaView.PanelId);
            }
        }));
        var status = new TextBlock();
        status.BindPolled(t => t.Text, () => error ?? "");
        rows.Children.Add(status);
        return rows;
    }
}

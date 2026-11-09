using Mana;
using Mana.Code.Components;
using Mana.Code.Content;
using ManaUI;
using ManaEditor.Code.Levels;

namespace ManaEditor.Code.UI.ManaUi.Inspectors;

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
                error = "Choose a .manaui layout first, or make a new one.";
                return;
            }

            if (UiDesignerDocument.TryOpen(file, out error) && context.Engine != null)
            {
                // A layout that does not yet say what it binds to learns it from the canvas showing it.
                if (UiDesignerDocument.ViewModelSource.Length == 0 && canvas.BindingSource.Length > 0)
                {
                    UiDesignerDocument.ViewModelSource = canvas.BindingSource;
                }

                EditorWorkspace.RequestPanel(context.Engine.World, UiDesignerManaView.PanelId);
            }
        }));
        rows.Children.Add(new ManaUI.Button().Label("New layout for this canvas").OnClick(() =>
        {
            try
            {
                var name = "Layout";
                if (!string.IsNullOrWhiteSpace(canvas.Owner.Name))
                {
                    name = canvas.Owner.Name;
                }

                UiDesignerDocument.Replace(UiDesignerDocument.NewDocument(name), null);
                if (canvas.BindingSource.Length > 0)
                {
                    UiDesignerDocument.ViewModelSource = canvas.BindingSource;
                }

                var path = UiDesignerDocument.Save(name);
                var id = UiDesignerDocument.Document["id"]?.GetValue<string>() ?? string.Empty;
                canvas.Layout = new ManaAssetRef(id, ManaContentMounts.RelativePath(ManaContentAssets.Root, path).Replace('\\', '/'));
                error = null;
                if (context.Engine != null)
                {
                    EditorWorkspace.RequestPanel(context.Engine.World, UiDesignerManaView.PanelId);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = exception.Message;
            }
        }));
        var status = new TextBlock();
        status.BindPolled(t => t.Text, () => error ?? "");
        rows.Children.Add(status);
        return rows;
    }
}

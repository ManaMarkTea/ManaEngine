using Mana.Code.ManaShaders;
using Mana.Code.Materials;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// What every material has regardless of its shader: a name and the shader it runs. What the shader
    /// itself exposes is a separate widget -- see <see cref="PbrShaderWidget"/>.
    /// </summary>
    public sealed class MaterialWidget : InspectorWidget<ManaMaterial>
    {
        protected override UiElement Build(ManaMaterial material, InspectorContext context)
        {
            var theme = context.Theme;

            var rows = new StackPanel().Vertical(theme.Spacing).Children(
                InspectorControls.Row(context, "Name", new TextBox()
                    .Value(material.Name ?? string.Empty)
                    .Hint("unnamed")
                    .OnCommitted(text => material.Name = text)),

                InspectorControls.Row(context, "Shader",
                    InspectorControls.ReadOnly(theme, material.Shader?.GetType().Name ?? "none")));

            if (material.Shader is ManaPbrShader)
            {
                rows.Children.Add(InspectorControls.Row(context, "Preset", MaterialPicker.Build(material, context)));
            }

            // Only show channel controls for shaders that read them.
            if (material.Shader is not ManaPbrShader)
            {
                rows.Children.Add(new TextBlock()
                    .Text("This shader ignores the standard PBR channels. See Shader Uniforms.")
                    .Wrap()
                    .Foreground(theme.TextMuted));
            }

            return new Expander().Header("Material").Content(rows);
        }
    }
}

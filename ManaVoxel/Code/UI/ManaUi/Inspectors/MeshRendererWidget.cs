using Mana;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// A mesh renderer, followed by whatever the registry has to say about its material.
    /// <para>
    /// The material's sections are siblings rather than nested. Materials are shared between renderers, so
    /// burying one inside a component's section implies an ownership that is not there.
    /// </para>
    /// </summary>
    public sealed class MeshRendererWidget : InspectorWidget<MeshRendererComponent>
    {
        protected override UiElement Build(MeshRendererComponent renderer, InspectorContext context)
        {
            var theme = context.Theme;

            var section = new Expander()
                .Header("Mesh Renderer")
                .Section(theme.Spacing,
                    InspectorControls.Row(context, "Mesh",
                        InspectorControls.ReadOnly(theme, renderer.Mesh?.GetType().Name ?? "none")),

                    new CheckBox().Label("Draw after scene")
                        .Checked(renderer.DrawAfterScene)
                        .OnChanged(value => renderer.DrawAfterScene = value),

                    new CheckBox().Label("Outline")
                        .Checked(renderer.OutlineEnabled)
                        .OnChanged(value => renderer.OutlineEnabled = value),

                    InspectorControls.Row(context, "Width", InspectorControls.Scalar(
                        () => renderer.OutlineWidth,
                        value => renderer.OutlineWidth = value,
                        1.0f,
                        4.0f)));

            var material = context.BuildAll(renderer.Material);
            if (material.Count == 0)
            {
                return section;
            }

            var stack = new StackPanel().Vertical(theme.Spacing).Children(section);
            foreach (var part in material)
            {
                stack.Children.Add(part);
            }

            return stack;
        }
    }
}

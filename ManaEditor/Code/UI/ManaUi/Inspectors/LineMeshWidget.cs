using Mana;
using Mana.Code.Nodes;
using Mana.Code.Shapes;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>A procedural line's own settings -- thickness is geometry, color and glow are material.</summary>
    public sealed class LineMeshWidget : InspectorWidget<ManaMeshSceneNode>
    {
        protected override bool Accepts(ManaMeshSceneNode subject) => subject.Mesh is ManaLineMesh;

        protected override UiElement Build(ManaMeshSceneNode node, InspectorContext context)
        {
            var theme = context.Theme;
            var line = (ManaLineMesh)node.Mesh;
            var material = node.MeshRenderer.Material;

            return new Expander()
                .Header("Line")
                .Section(theme.Spacing,
                    // A real geometry rebuild, same as the ImGui version -- there is no vertex buffer wide
                    // enough to cover every thickness, so the ribbon is regenerated for the one asked for.
                    InspectorControls.Row(context, "Thickness", InspectorControls.Scalar(
                        () => line.Thickness,
                        value => line.SetThickness(value),
                        0.01f,
                        5.0f)),

                    InspectorControls.Row(context, "Color", InspectorControls.Colour(
                        () => material.Albedo.Value,
                        value =>
                        {
                            material.Albedo.Value = value;
                            material.Emission.Value = value;
                        })),

                    InspectorControls.Row(context, "Emission strength", InspectorControls.Scalar(
                        () => (float)material.EmissionStrength,
                        value => material.EmissionStrength = value,
                        0.0f,
                        10.0f)));
        }
    }
}

using Mana.Code.Nodes;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>What a node is: its name and its type.</summary>
    public sealed class NodeIdentityWidget : InspectorWidget<ManaSceneNode>
    {
        protected override UiElement Build(ManaSceneNode node, InspectorContext context)
        {
            var theme = context.Theme;

            return new Expander()
                .Header("Node")
                .Section(theme.Spacing,
                    InspectorControls.Row(context, "Name", new TextBox()
                        .Value(node.Name ?? string.Empty)
                        .Hint(node.GetType().Name)
                        .OnCommitted(text => node.Name = text)),

                    InspectorControls.Row(context, "Type", InspectorControls.ReadOnly(theme, node.GetType().Name)));
        }
    }
}

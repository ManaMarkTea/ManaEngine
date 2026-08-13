using Mana;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>Position, rotation and scale, written straight through to the node.</summary>
    public sealed class TransformWidget : InspectorWidget<ManaSceneNode>
    {
        protected override UiElement Build(ManaSceneNode node, InspectorContext context)
        {
            var theme = context.Theme;

            return new Expander()
                .Header("Transform")
                .Section(theme.Spacing,
                    InspectorControls.Row(context, "Position", InspectorControls.Vector(
                        () => node.Position,
                        value =>
                        {
                            node.Position = value;
                            node.UpdateTransform();
                        },
                        step: 0.1f)),

                    InspectorControls.Row(context, "Rotation", Rotation(node)),

                    InspectorControls.Row(context, "Scale", InspectorControls.Vector(
                        () => node.Scale,
                        value =>
                        {
                            node.Scale = value;
                            node.UpdateTransform();
                        },
                        step: 0.05f)));
        }

        /// <summary>
        /// Rotation is stored in radians and shown in degrees, so it round-trips through a conversion
        /// rather than binding straight to the property the way position and scale do.
        /// </summary>
        private static VectorBox Rotation(ManaSceneNode node)
        {
            var box = new VectorBox().Step(1.0f).Places(2);
            box.Value = node.RotationDegrees;

            box.ValueChanged += (_, value) =>
            {
                // UpdateTransform ticks every component on the node, so writing back on a value that
                // merely arrived from the poll would tick them a second time every frame -- which is how
                // selecting a spinning pickup made it spin at double speed.
                if (box.IsApplyingBinding)
                {
                    return;
                }

                node.Rotation = new Vector3(
                    value.X.ToRadians(),
                    value.Y.ToRadians(),
                    value.Z.ToRadians());
                node.UpdateTransform();
            };

            box.BindPolled(b => b.Value, () => node.RotationDegrees);
            return box;
        }
    }
}

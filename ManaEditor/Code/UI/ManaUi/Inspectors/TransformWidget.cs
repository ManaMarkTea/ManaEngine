using Mana;
using Mana.Code.Nodes;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
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
                    PrefabOverrideMarks.Mark(context, InspectorControls.Row(context, "Position", TransformVector(
                        node,
                        context,
                        () => node.Position,
                        value => node.Position = value,
                        step: 0.1f,
                        "Move")), node, null, "position"),

                    PrefabOverrideMarks.Mark(context, InspectorControls.Row(context, "Rotation", Rotation(node, context)), node, null, "rotation"),

                    PrefabOverrideMarks.Mark(context, InspectorControls.Row(context, "Scale", TransformVector(
                        node,
                        context,
                        () => node.Scale,
                        value => node.Scale = value,
                        step: 0.05f,
                        "Scale")), node, null, "scale"));
        }

        /// <summary>
        /// Rotation is stored in radians and shown in degrees, so it round-trips through a conversion
        /// rather than binding straight to the property the way position and scale do.
        /// </summary>
        private static VectorBox Rotation(ManaSceneNode node, InspectorContext context)
        {
            return TransformVector(
                node,
                context,
                () => node.RotationDegrees,
                value => node.Rotation = new Vector3(
                    value.X.ToRadians(),
                    value.Y.ToRadians(),
                    value.Z.ToRadians()),
                step: 1.0f,
                "Rotate",
                places: 2);
        }

        private static VectorBox TransformVector(
            ManaSceneNode node,
            InspectorContext context,
            Func<Vector3> read,
            Action<Vector3> write,
            float step,
            string verb,
            int places = 3)
        {
            var box = new VectorBox().Step(step).Places(places);
            box.Value = read();
            SceneNodeTransformState? editStart = null;

            box.ValueChanged += (_, value) =>
            {
                // UpdateTransform ticks every component on the node, so writing back on a value that
                // merely arrived from the poll would tick them a second time every frame -- which is how
                // selecting a spinning pickup made it spin at double speed.
                if (box.IsApplyingBinding)
                {
                    return;
                }

                editStart ??= SceneNodeTransformState.Capture(node);
                write(new Vector3(value.X, value.Y, value.Z));
                node.UpdateTransform();
            };

            box.ValueCommitted += (_, _) =>
            {
                if (!editStart.HasValue)
                {
                    return;
                }

                context.History?.RecordTransform(
                    node,
                    editStart.Value,
                    SceneNodeTransformState.Capture(node),
                    $"{verb} {node.Name}",
                    context.Selection);
                editStart = null;
            };

            box.BindPolled(b => b.Value, read);
            return box;
        }
    }
}

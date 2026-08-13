using Mana.Code.Components;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    public sealed class LightWidget : InspectorWidget<ManaLightSourceComponent>
    {
        protected override UiElement Build(ManaLightSourceComponent light, InspectorContext context)
        {
            var theme = context.Theme;

            return new Expander()
                .Header("Light Source")
                .Section(theme.Spacing,
                    new CheckBox().Label("Enabled")
                        .Checked(light.Enabled)
                        .OnChanged(value => light.Enabled = value),

                    InspectorControls.Row(context, "Intensity", InspectorControls.Scalar(
                        () => light.Intensity,
                        value => light.Intensity = value,
                        0.0f,
                        20.0f)),

                    InspectorControls.Row(context, "Direction", InspectorControls.Vector(
                        () => light.Direction,
                        value => light.Direction = value,
                        step: 0.05f)),

                    InspectorControls.Row(context, "Colour",
                        InspectorControls.Colour(() => light.Color, value => light.Color = value)));
        }
    }
}

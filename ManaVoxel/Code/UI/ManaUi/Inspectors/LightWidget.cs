using Mana.Code.Components;
using ManaCore.Graphics;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    public sealed class LightWidget : InspectorWidget<ManaLightSourceComponent>
    {
        protected override UiElement Build(ManaLightSourceComponent light, InspectorContext context)
        {
            var theme = context.Theme;

            var typeNames = Enum.GetNames<ManaLightType>();
            var typeCombo = new ComboBox().Options(typeNames);
            typeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(typeNames, light.LightType.ToString()));
            typeCombo.SelectionChanged += (_, _) =>
            {
                if (!typeCombo.IsApplyingBinding
                    && typeCombo.SelectedItem is string name
                    && Enum.TryParse<ManaLightType>(name, out var parsed))
                {
                    light.LightType = parsed;
                }
            };
            typeCombo.BindPolled(
                c => c.SelectedIndex,
                () => Math.Max(0, Array.IndexOf(typeNames, light.LightType.ToString())));

            return new Expander()
                .Header("Light Source")
                .Section(theme.Spacing,
                    new CheckBox().Label("Enabled")
                        .Checked(light.Enabled)
                        .OnChanged(value => light.Enabled = value),

                    InspectorControls.Row(context, "Type", typeCombo, null,
                        "Point lights fill the 4-light grid the PBR shader samples every pixel. Directional "
                        + "is a single sun -- only the first enabled one in the scene is used, and it can "
                        + "cast cascaded shadow maps."),

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
                        InspectorControls.Colour(() => light.Color, value => light.Color = value)),

                    new CheckBox().Label("Casts Shadows")
                        .Checked(light.CastsShadows)
                        .OnChanged(value => light.CastsShadows = value));
        }
    }
}

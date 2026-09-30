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

            // Each row marked when a prefab instance or variant changes it from its source.
            UiElement Marked(string field, UiElement row) => PrefabOverrideMarks.Mark(context, row, light.Owner, light, field);

            return new Expander()
                .Header("Light Source")
                .Section(theme.Spacing,
                    Marked("enabled", new CheckBox().Label("Enabled")
                        .Checked(light.Enabled)
                        .OnChanged(value => light.Enabled = value)),

                    Marked("lightType", InspectorControls.Row(context, "Type", typeCombo, null,
                        "Point lights fill the 4-light grid the PBR shader samples every pixel. Directional "
                        + "is a single sun -- only the first enabled one in the scene is used, and it can "
                        + "cast cascaded shadow maps.")),

                    Marked("intensity", InspectorControls.Row(context, "Intensity", InspectorControls.Scalar(
                        () => light.Intensity,
                        value => light.Intensity = value,
                        0.0f,
                        20.0f))),

                    Marked("range", InspectorControls.Row(context, "Range", InspectorControls.Scalar(
                        () => light.Range,
                        value => light.Range = value,
                        0.1f,
                        50.0f), null, "How far a point light reaches, in metres. It fades smoothly to nothing there.")),

                    Marked("direction", InspectorControls.Row(context, "Direction", InspectorControls.Vector(
                        () => light.Direction,
                        value => light.Direction = value,
                        step: 0.05f))),

                    Marked("color", InspectorControls.Row(context, "Colour",
                        InspectorControls.Colour(() => light.Color, value => light.Color = value))),

                    Marked("castsShadows", new CheckBox().Label("Casts Shadows")
                        .Checked(light.CastsShadows)
                        .OnChanged(value => light.CastsShadows = value)));
        }
    }
}

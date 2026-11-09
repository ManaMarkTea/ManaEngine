using ManaGeoTools.Code.Planet;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    /// <summary>The two live planet-wide settings: how tall the terrain stands, and whether it manages its own detail.</summary>
    public sealed class PlanetWidget : InspectorWidget<PlanetNode>
    {
        protected override UiElement Build(PlanetNode planet, InspectorContext context)
        {
            var theme = context.Theme;

            return new Expander()
                .Header("Planet")
                .Section(theme.Spacing,
                    InspectorControls.Row(context, "Height exaggeration", InspectorControls.Scalar(
                        () => planet.HeightExaggeration,
                        value => planet.HeightExaggeration = value,
                        0.0f,
                        2000.0f)),

                    new CheckBox().Label("Auto LOD")
                        .Checked(planet.AutoLodEnabled)
                        .OnChanged(value => planet.AutoLodEnabled = value));
        }
    }
}

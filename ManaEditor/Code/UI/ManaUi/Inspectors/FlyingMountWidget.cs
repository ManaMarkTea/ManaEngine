using ManaUI;
using ManaEditor.Code.Gameplay.Flight;

namespace ManaEditor.Code.UI.ManaUi.Inspectors
{
    public sealed class FlyingMountWidget : InspectorWidget<FlyingMountComponent>
    {
        protected override UiElement Build(FlyingMountComponent mount, InspectorContext context)
        {
            return new Expander()
                .Header("Dragonflight")
                .Section(context.Theme.Spacing,
                    InspectorControls.Row(context, "Speed FOV", InspectorControls.Scalar(
                        () => mount.SpeedFovDegrees, value => mount.SpeedFovDegrees = Math.Clamp(value, 0.0f, 30.0f), 0.0f, 30.0f),
                        "Extra field of view at top speed, in degrees. Set this and Surge FOV to zero to disable lens effects."),
                    InspectorControls.Row(context, "Surge FOV", InspectorControls.Scalar(
                        () => mount.SurgeFovDegrees, value => mount.SurgeFovDegrees = Math.Clamp(value, 0.0f, 30.0f), 0.0f, 30.0f),
                        "Additional field of view during Surge Forward, in degrees."),
                    InspectorControls.Row(context, "Surge FOV duration", InspectorControls.Scalar(
                        () => mount.SurgeFovSeconds, value => mount.SurgeFovSeconds = Math.Clamp(value, 0.05f, 3.0f), 0.05f, 3.0f),
                        "Seconds for the surge lens effect to fade."),
                    InspectorControls.Row(context, "FOV response", InspectorControls.Scalar(
                        () => mount.FovResponsePerSecond, value => mount.FovResponsePerSecond = Math.Clamp(value, 0.5f, 20.0f), 0.5f, 20.0f),
                        "Higher values make lens changes faster."),
                    InspectorControls.Row(context, "Flight distance", InspectorControls.Scalar(
                        () => mount.FlightCameraDistance, value => mount.FlightCameraDistance = Math.Clamp(value, 2.0f, 40.0f), 2.0f, 40.0f),
                        "Minimum camera distance at takeoff for a standard-size rider. Mouse-wheel zoom is retained on landing."),
                    InspectorControls.Row(context, "Turn speed", InspectorControls.Scalar(
                        () => mount.TurnRateDegreesPerSecond, value => mount.TurnRateDegreesPerSecond = Math.Clamp(value, 30.0f, 360.0f), 30.0f, 360.0f),
                        "Maximum flight heading change, in degrees per second."));
        }
    }
}

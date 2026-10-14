using Mana;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi.Inspectors
{
    /// <summary>
    /// A camera, as an inspector widget rather than a panel of its own.
    /// <para>
    /// The ImGui version was a window you opened separately, which meant the camera was the one thing in
    /// the scene you could not simply select and look at. As a widget it appears when its node is selected,
    /// like every other component.
    /// </para>
    /// </summary>
    public sealed class CameraWidget : InspectorWidget<CameraComponent>
    {
        protected override UiElement Build(CameraComponent camera, InspectorContext context)
        {
            var theme = context.Theme;

            var projection = new StackPanel().Horizontal(theme.Spacing).Children(
                Preset(theme, "Perspective", () => camera.SetProjectionMode(false), () => !camera.OrthoGraphic),
                Preset(theme, "Ortho", () => camera.SetProjectionMode(true), () => camera.OrthoGraphic));

            // Only one of these applies at a time, and the one that does not is hidden rather than
            // disabled: a field that cannot matter is noise, not information.
            var fieldOfView = InspectorControls.Row(context, "Field of view", InspectorControls.Scalar(
                () => camera.FieldOfViewDegrees,
                value => camera.FieldOfViewDegrees = value,
                1.0f,
                150.0f));

            fieldOfView.BindPolled(
                r => r.Visibility,
                () => camera.OrthoGraphic ? Visibility.Collapsed : Visibility.Visible);

            var orthoHeight = InspectorControls.Row(context, "Ortho height", InspectorControls.Scalar(
                () => camera.OrthographicHeight,
                value => camera.OrthographicHeight = value,
                1.0f,
                1000.0f));

            orthoHeight.BindPolled(
                r => r.Visibility,
                () => camera.OrthoGraphic ? Visibility.Visible : Visibility.Collapsed);

            return new Expander()
                .Header("Camera")
                .Section(theme.Spacing,
                    InspectorControls.Row(context, "Projection", projection,
                        "Perspective for a view of the world; orthographic for measuring it."),

                    fieldOfView,
                    orthoHeight,

                    // The clip planes span six orders of magnitude between a voxel and a planet, so these
                    // are soft ranges -- typing past either end widens them rather than clamping.
                    InspectorControls.Row(context, "Near clip", InspectorControls.Scalar(
                        () => camera.NearClipPlane,
                        value => camera.NearClipPlane = value,
                        0.0001f,
                        10.0f),
                        "Anything closer than this is not drawn. Too small and the depth buffer loses precision."),

                    InspectorControls.Row(context, "Far clip", InspectorControls.Scalar(
                        () => camera.FarClipPlane,
                        value => camera.FarClipPlane = value,
                        1.0f,
                        100000.0f)),

                    InspectorControls.Row(context, "Speed", InspectorControls.Scalar(
                        () => camera.MovementSpeed,
                        value => camera.MovementSpeed = value,
                        0.25f,
                        100.0f)),

                    InspectorControls.Row(context, "Pan speed", InspectorControls.Scalar(
                        () => camera.PanSpeed,
                        value => camera.PanSpeed = value,
                        0.05f,
                        5.0f)),

                    new CheckBox().Label("Touchpad horizontal pan in ortho")
                        .Checked(camera.TouchpadPanInOrthographic)
                        .OnChanged(value => camera.TouchpadPanInOrthographic = value),

                    ViewPresets(theme, camera));
        }

        /// <summary>
        /// The six axis views. A grid rather than a row: six buttons side by side in an inspector column
        /// end up too narrow to read.
        /// </summary>
        private static UiElement ViewPresets(UiTheme theme, CameraComponent camera)
        {
            var grid = new Grid().Columns("*, *, *", theme.Spacing).Rows("Auto, Auto", theme.Spacing);

            var presets = new (string Label, CameraViewPreset Preset)[]
            {
                ("Front", CameraViewPreset.Front),
                ("Back", CameraViewPreset.Back),
                ("Left", CameraViewPreset.Left),
                ("Right", CameraViewPreset.Right),
                ("Top", CameraViewPreset.Top),
                ("Bottom", CameraViewPreset.Bottom)
            };

            for (var i = 0; i < presets.Length; i++)
            {
                var (label, preset) = presets[i];

                grid.Children.Add(new Button()
                    .Label(label)
                    .Variant(ButtonVariant.Subtle)
                    .StretchH()
                    .OnClick(() => camera.SetViewPreset(preset))
                    .GridColumn(i % 3)
                    .GridRow(i / 3));
            }

            return grid;
        }

        private static Button Preset(UiTheme theme, string label, Action choose, Func<bool> isCurrent)
        {
            var button = new Button().Label(label).StretchH().OnClick(choose);

            // The current projection is the accented one, so which mode you are in is visible rather than
            // something to work out from the fields below.
            button.BindPolled(
                b => b.Variant,
                () => isCurrent() ? ButtonVariant.Primary : ButtonVariant.Subtle);

            return button;
        }
    }
}

using ManaCore.Graphics;
using ManaUI;

// ManaVoxel has a global `using Silk.NET.Input`, which also defines Button.
using Button = ManaUI.Button;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// Lighting and the PBR debug views, ported from ImGui.
    /// <para>
    /// Every row writes straight to the settings object the renderer reads each frame, and every row polls
    /// it back -- these are changed by more than this panel, and a slider showing a stale exposure while
    /// the scene shows the real one is worse than no slider.
    /// </para>
    /// </summary>
    public static class IrradianceSettingsManaView
    {
        private static readonly string[] DebugModes = ManaIrradianceSettings.DebugModeNames;

        public static UiElement Build(UiTheme theme, ManaIrradianceSettings settings)
        {
            var defaults = new ManaIrradianceSettings();
            var constellations = new CheckBox().Label("Constellation lines")
                .Checked(settings.ConstellationLines)
                .OnChanged(value => settings.ConstellationLines = value);
            constellations.Tooltip = "At night, show constellation figures when looking toward them. Hold Alt to show all figures.";
            constellations.BindPolled(box => box.IsChecked, () => settings.ConstellationLines);

            var debugMode = new ComboBox().Options(DebugModes);
            debugMode.SelectedIndex = Clamp((int)settings.WebGpuDebugMode);
            debugMode.SelectionChanged += (_, _) =>
            {
                if (!debugMode.IsApplyingBinding)
                {
                    settings.WebGpuDebugMode = debugMode.SelectedIndex;
                }
            };
            debugMode.BindPolled(c => c.SelectedIndex, () => Clamp((int)settings.WebGpuDebugMode));

            return new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                Section(theme, "Lighting"),

                Row(theme, "Light intensity", Scalar(
                    () => settings.LightIntensity, v => settings.LightIntensity = v, 0.0f, 8.0f)),

                Row(theme, "Light colour", Colour(
                    () => settings.LightColor, v => settings.LightColor = v)),

                Row(theme, "Exposure", Scalar(
                    () => settings.Exposure, v => settings.Exposure = v, 0.25f, 3.0f)),

                Row(theme, "IBL intensity", Scalar(
                    () => settings.IblIntensity, v => settings.IblIntensity = v, 0.0f, 4.0f)),

                Row(theme, "Ambient occlusion", Scalar(
                    () => settings.AmbientOcclusion, v => settings.AmbientOcclusion = v, 0.0f, 2.0f)),

                Row(theme, "AO radius (m)", Scalar(
                    () => settings.AmbientOcclusionRadius, v => settings.AmbientOcclusionRadius = v, 0.1f, 3.0f)),

                Section(theme, "Wind"),

                Row(theme, "Speed (m/s)", Scalar(
                    () => settings.WindSpeed, v => settings.WindSpeed = v, 0.0f, ManaWind.MaximumSpeed)),

                Row(theme, "Direction", Scalar(
                    () => settings.WindDirection, v => settings.WindDirection = v, 0.0f, 360.0f)),

                Row(theme, "Gustiness", Scalar(
                    () => settings.Gustiness, v => settings.Gustiness = v, 0.0f, 1.0f)),

                Section(theme, "Night sky"),

                constellations,

                Section(theme, "Debug world"),

                new CheckBox().Label("Draw debug world")
                    .Checked(settings.DrawDebugWorld)
                    .OnChanged(value => settings.DrawDebugWorld = value),

                Row(theme, "Metallic", Scalar(
                    () => settings.DebugMetallic, v => settings.DebugMetallic = v, 0.0f, 1.0f)),

                Row(theme, "Roughness", Scalar(
                    () => settings.DebugRoughness, v => settings.DebugRoughness = v, 0.0f, 1.0f)),

                Row(theme, "Ambience", Scalar(
                    () => settings.DebugAmbient, v => settings.DebugAmbient = v, 0.0f, 1.0f)),

                Row(theme, "Albedo", Colour(
                    () => settings.DebugAlbedo, v => settings.DebugAlbedo = v)),

                Row(theme, "Scene scale", Scalar(
                    () => settings.DebugSceneScale, v => settings.DebugSceneScale = v, 0.35f, 2.75f)),

                Section(theme, "Shader output"),

                Row(theme, "PBR debug", debugMode),

                new Button()
                    .Label("Reset to defaults")
                    .Variant(ButtonVariant.Subtle)
                    .StretchH()
                    .OnClick(() => Reset(settings, defaults)));
        }

        private static int Clamp(int mode) => Math.Clamp(mode, 0, DebugModes.Length - 1);

        /// <summary>
        /// Restores from a fresh settings object rather than a remembered snapshot: "defaults" here means
        /// what the engine ships with, not what happened to be set when this panel was built.
        /// </summary>
        private static void Reset(ManaIrradianceSettings settings, ManaIrradianceSettings defaults)
        {
            settings.DrawDebugWorld = defaults.DrawDebugWorld;
            settings.DebugMetallic = defaults.DebugMetallic;
            settings.DebugRoughness = defaults.DebugRoughness;
            settings.DebugAmbient = defaults.DebugAmbient;
            settings.DebugAlbedo = defaults.DebugAlbedo;
            settings.LightIntensity = defaults.LightIntensity;
            settings.LightColor = defaults.LightColor;
            settings.Exposure = defaults.Exposure;
            settings.IblIntensity = defaults.IblIntensity;
            settings.AmbientOcclusion = defaults.AmbientOcclusion;
            settings.AmbientOcclusionRadius = defaults.AmbientOcclusionRadius;
            settings.DebugSceneScale = defaults.DebugSceneScale;
            settings.WebGpuDebugMode = defaults.WebGpuDebugMode;
            settings.WindSpeed = defaults.WindSpeed;
            settings.WindDirection = defaults.WindDirection;
            settings.Gustiness = defaults.Gustiness;
            settings.ConstellationLines = defaults.ConstellationLines;
        }

        private static UiElement Row(UiTheme theme, string label, UiElement editor)
        {
            return new Grid().Columns("120, *", theme.Spacing).Rows("Auto").Children(
                new TextBlock().Text(label).Foreground(theme.TextMuted).Ellipsis().CenterV().GridColumn(0),
                editor.GridColumn(1));
        }

        private static SliderBox Scalar(Func<float> read, Action<float> write, float minimum, float maximum)
        {
            var slider = new SliderBox
            {
                Minimum = minimum,
                Maximum = maximum,
                Step = (maximum - minimum) / 200.0f,
                DecimalPlaces = 3
            };

            slider.Value = read();
            slider.ValueChanged += (_, value) =>
            {
                if (!slider.IsApplyingBinding)
                {
                    write(value);
                }
            };

            slider.BindPolled(s => s.Value, read);
            return slider;
        }

        private static ColorField Colour(Func<Vector4> read, Action<Vector4> write)
        {
            var field = new ColorField { Color = read() };

            field.ColorChanged += (_, value) =>
            {
                if (!field.IsApplyingBinding)
                {
                    write(value);
                }
            };

            field.BindPolled(f => f.Color, read);
            return field;
        }

        private static UiElement Section(UiTheme theme, string title)
        {
            return new Grid().Columns("Auto, *", theme.Spacing).Rows("Auto").Margin(0.0f, 6.0f, 0.0f, 0.0f).Children(
                new TextBlock()
                    .Text(title)
                    .Foreground(theme.TextMuted)
                    .FontSize(theme.FontSizeSmall)
                    .FontFamily(theme.FontFamilyMedium)
                    .GridColumn(0),

                new Border().Fill(theme.Divider).Height(1.0f).CenterV().GridColumn(1));
        }
    }
}

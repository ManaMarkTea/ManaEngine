using ImGuiNET;
using ManaCore.Graphics;

namespace Mana.Code.UI
{
    public readonly struct IrradianceSettingsPanelResult
    {
        public bool DebugWorldChanged { get; init; }
        public bool DebugMaterialChanged { get; init; }
        public bool LightSettingsChanged { get; init; }
        public bool DebugSceneScaleChanged { get; init; }

        public bool AnyChanged =>
            DebugWorldChanged ||
            DebugMaterialChanged ||
            LightSettingsChanged ||
            DebugSceneScaleChanged;
    }

    public sealed class IrradianceSettingsPanelView
    {
        public IrradianceSettingsPanelResult Draw(ManaIrradianceSettings settings)
        {
            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

            ImGui.Begin("Irradiance");

            var debugWorldChanged = false;
            var debugMaterialChanged = false;
            var lightSettingsChanged = false;
            var debugSceneScaleChanged = false;

            var drawDebugWorld = settings.DrawDebugWorld;
            if (ImGui.Checkbox("Debug World", ref drawDebugWorld))
            {
                settings.DrawDebugWorld = drawDebugWorld;
                debugWorldChanged = true;
            }

            var debugMetallic = settings.DebugMetallic;
            if (ImGui.SliderFloat("Metallic", ref debugMetallic, 0.0f, 1.0f))
            {
                settings.DebugMetallic = debugMetallic;
                debugMaterialChanged = true;
            }

            var debugRoughness = settings.DebugRoughness;
            if (ImGui.SliderFloat("Roughness", ref debugRoughness, 0.0f, 1.0f))
            {
                settings.DebugRoughness = debugRoughness;
                debugMaterialChanged = true;
            }

            var debugAmbient = settings.DebugAmbient;
            if (ImGui.SliderFloat("Ambience", ref debugAmbient, 0.0f, 1.0f))
            {
                settings.DebugAmbient = debugAmbient;
                debugMaterialChanged = true;
            }

            var debugAlbedo = settings.DebugAlbedo;
            if (ImGui.ColorEdit4("Albedo", ref debugAlbedo))
            {
                settings.DebugAlbedo = debugAlbedo;
                debugMaterialChanged = true;
            }

            var lightIntensity = settings.LightIntensity;
            if (ImGui.SliderFloat("Light Intensity", ref lightIntensity, 0.0f, 8.0f))
            {
                settings.LightIntensity = lightIntensity;
                lightSettingsChanged = true;
            }

            var lightColor = settings.LightColor;
            if (ImGui.ColorEdit4("Light Color", ref lightColor))
            {
                settings.LightColor = lightColor;
                lightSettingsChanged = true;
            }

            var exposure = settings.Exposure;
            if (ImGui.SliderFloat("Exposure", ref exposure, 0.25f, 3.0f))
            {
                settings.Exposure = exposure;
                lightSettingsChanged = true;
            }

            var iblIntensity = settings.IblIntensity;
            if (ImGui.SliderFloat("IBL Intensity", ref iblIntensity, 0.0f, 4.0f))
            {
                settings.IblIntensity = iblIntensity;
                lightSettingsChanged = true;
            }

            var debugSceneScale = settings.DebugSceneScale;
            if (ImGui.SliderFloat("Debug Scene Scale", ref debugSceneScale, 0.35f, 2.75f))
            {
                settings.DebugSceneScale = debugSceneScale;
                debugSceneScaleChanged = true;
            }

            var debugMode = (int)settings.WebGpuDebugMode;
            string[] debugModes = { "Off", "View Vector (V)", "World Normal (N)", "Reflection (R)", "Cubemap Dir", "Reflection Radiance", "BRDF Scale" };
            if (ImGui.Combo("PBR Debug", ref debugMode, debugModes, debugModes.Length))
            {
                settings.WebGpuDebugMode = debugMode;
                lightSettingsChanged = true;
            }

            if (ImGui.Button("Reset"))
            {
                var defaults = new ManaIrradianceSettings();
                settings.DrawDebugWorld = defaults.DrawDebugWorld;
                settings.DebugMetallic = defaults.DebugMetallic;
                settings.DebugRoughness = defaults.DebugRoughness;
                settings.DebugAmbient = defaults.DebugAmbient;
                settings.DebugAlbedo = defaults.DebugAlbedo;
                settings.Exposure = defaults.Exposure;
                settings.IblIntensity = defaults.IblIntensity;
                settings.LightIntensity = defaults.LightIntensity;
                settings.LightColor = defaults.LightColor;
                settings.LightDirection = defaults.LightDirection;
                settings.DebugSceneScale = defaults.DebugSceneScale;
                settings.WebGpuDebugMode = defaults.WebGpuDebugMode;

                debugWorldChanged = true;
                debugMaterialChanged = true;
                lightSettingsChanged = true;
                debugSceneScaleChanged = true;
            }

            ImGui.End();

            return new IrradianceSettingsPanelResult
            {
                DebugWorldChanged = debugWorldChanged,
                DebugMaterialChanged = debugMaterialChanged,
                LightSettingsChanged = lightSettingsChanged,
                DebugSceneScaleChanged = debugSceneScaleChanged
            };
        }
    }
}

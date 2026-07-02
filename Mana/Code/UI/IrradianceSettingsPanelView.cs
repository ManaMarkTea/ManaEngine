using ImGuiNET;
using ManaCore.Graphics;
using System.Numerics;

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

            string[] debugModes =
            {
                "Off",
                "View Vector (V)",
                "World Normal (N)",
                "Reflection (R)",
                "Cubemap Dir",
                "Reflection Radiance",
                "BRDF Scale",
                "Linear Albedo",
                "F0",
                "Fresnel",
                "BRDF LUT",
                "Diffuse IBL",
                "Specular IBL",
                "Direct Lighting",
                "Final Linear"
            };

            if (BeginPropertyTable("IrradianceProperties"))
            {
                var drawDebugWorld = settings.DrawDebugWorld;
                if (DrawCheckboxProperty("Debug World", ref drawDebugWorld))
                {
                    settings.DrawDebugWorld = drawDebugWorld;
                    debugWorldChanged = true;
                }

                var debugMetallic = settings.DebugMetallic;
                if (DrawSliderFloatProperty("Metallic", ref debugMetallic, 0.0f, 1.0f))
                {
                    settings.DebugMetallic = debugMetallic;
                    debugMaterialChanged = true;
                }

                var debugRoughness = settings.DebugRoughness;
                if (DrawSliderFloatProperty("Roughness", ref debugRoughness, 0.0f, 1.0f))
                {
                    settings.DebugRoughness = debugRoughness;
                    debugMaterialChanged = true;
                }

                var debugAmbient = settings.DebugAmbient;
                if (DrawSliderFloatProperty("Ambience", ref debugAmbient, 0.0f, 1.0f))
                {
                    settings.DebugAmbient = debugAmbient;
                    debugMaterialChanged = true;
                }

                var debugAlbedo = settings.DebugAlbedo;
                if (DrawColorProperty("Albedo", ref debugAlbedo))
                {
                    settings.DebugAlbedo = debugAlbedo;
                    debugMaterialChanged = true;
                }

                var lightIntensity = settings.LightIntensity;
                if (DrawSliderFloatProperty("Light Intensity", ref lightIntensity, 0.0f, 8.0f))
                {
                    settings.LightIntensity = lightIntensity;
                    lightSettingsChanged = true;
                }

                var lightColor = settings.LightColor;
                if (DrawColorProperty("Light Color", ref lightColor))
                {
                    settings.LightColor = lightColor;
                    lightSettingsChanged = true;
                }

                var exposure = settings.Exposure;
                if (DrawSliderFloatProperty("Exposure", ref exposure, 0.25f, 3.0f))
                {
                    settings.Exposure = exposure;
                    lightSettingsChanged = true;
                }

                var iblIntensity = settings.IblIntensity;
                if (DrawSliderFloatProperty("IBL Intensity", ref iblIntensity, 0.0f, 4.0f))
                {
                    settings.IblIntensity = iblIntensity;
                    lightSettingsChanged = true;
                }

                var debugSceneScale = settings.DebugSceneScale;
                if (DrawSliderFloatProperty("Debug Scene Scale", ref debugSceneScale, 0.35f, 2.75f))
                {
                    settings.DebugSceneScale = debugSceneScale;
                    debugSceneScaleChanged = true;
                }

                var debugMode = (int)settings.WebGpuDebugMode;
                if (DrawComboProperty("PBR Debug", ref debugMode, debugModes))
                {
                    settings.WebGpuDebugMode = debugMode;
                    lightSettingsChanged = true;
                }

                ImGui.EndTable();
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

        private static bool BeginPropertyTable(string id)
        {
            const ImGuiTableFlags flags =
                ImGuiTableFlags.SizingStretchProp |
                ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.NoSavedSettings;

            var style = ImGui.GetStyle();
            var compactCellPadding = new Vector2(
                style.CellPadding.X,
                ManaUiDensity.PropertyTableCellPaddingY);
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, compactCellPadding);

            if (!ImGui.BeginTable(id, 2, flags))
            {
                ImGui.PopStyleVar();
                return false;
            }

            ImGui.PopStyleVar();
            ImGui.TableSetupColumn("Property", ImGuiTableColumnFlags.WidthFixed, 112.0f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            return true;
        }

        private static void BeginPropertyRow(string label)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(label);
            ImGui.TableNextColumn();
        }

        private static bool DrawCheckboxProperty(string label, ref bool value)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            PushCompactValueStyle();
            var changed = ImGui.Checkbox("##Value", ref value);
            PopCompactValueStyle();
            ImGui.PopID();
            return changed;
        }

        private static bool DrawSliderFloatProperty(string label, ref float value, float min, float max)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            ImGui.SetNextItemWidth(-1.0f);
            PushCompactValueStyle();
            var changed = ImGui.SliderFloat("##Value", ref value, min, max);
            PopCompactValueStyle();
            ImGui.PopID();
            return changed;
        }

        private static bool DrawColorProperty(string label, ref Vector4 color)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            var changed = ImGuiExt.DrawColorValue(ref color);
            ImGui.PopID();
            return changed;
        }

        private static bool DrawComboProperty(string label, ref int value, string[] options)
        {
            ImGui.PushID(label);
            BeginPropertyRow(label);
            ImGui.SetNextItemWidth(-1.0f);
            PushCompactValueStyle();
            var changed = ImGui.Combo("##Value", ref value, options, options.Length);
            PopCompactValueStyle();
            ImGui.PopID();
            return changed;
        }

        private static void PushCompactValueStyle()
        {
            var style = ImGui.GetStyle();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(style.FramePadding.X, ManaUiDensity.CompactValueFramePaddingY));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(style.ItemSpacing.X, ManaUiDensity.CompactValueItemSpacingY));
        }

        private static void PopCompactValueStyle()
        {
            ImGui.PopStyleVar(2);
        }
    }
}

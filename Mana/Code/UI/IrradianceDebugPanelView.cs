using ImGuiNET;

namespace Mana.Code.UI
{
    public class IrradianceDebugPanelView
    {
        public void Draw(
            ref bool drawDebugWorld,
            ref float debugMetallic,
            ref float debugRoughness,
            ref float debugAmbient,
            ref Vector4 debugAlbedo,
            ref float debugLightIntensity,
            ref Vector4 debugLightColor,
            ref float debugSceneScale)
        {
            if (BaseGameMode.UtilityDockId != 0)
            {
                ImGui.SetNextWindowDockID(BaseGameMode.UtilityDockId, ImGuiCond.Always);
            }

            ImGui.Begin("Irradiance");
            ImGui.Checkbox("Debug World", ref drawDebugWorld);
            ImGui.SliderFloat("Metallic", ref debugMetallic, 0.0f, 1.0f);
            ImGui.SliderFloat("Roughness", ref debugRoughness, 0.0f, 1.0f);
            ImGui.SliderFloat("Ambience", ref debugAmbient, 0.0f, 1.0f);
            ImGui.ColorEdit4("Albedo", ref debugAlbedo);
            ImGui.SliderFloat("Light Intensity", ref debugLightIntensity, 0.0f, 100.0f);
            ImGui.ColorEdit4("Light Color", ref debugLightColor);
            ImGui.SliderFloat("Debug Scene Scale", ref debugSceneScale, 0.35f, 2.75f);
            ImGui.End();
        }
    }
}

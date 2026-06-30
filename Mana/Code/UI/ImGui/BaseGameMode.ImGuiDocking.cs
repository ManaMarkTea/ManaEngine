using ImGuiNET;
using ManaCore.Graphics;
using System.Numerics;

namespace Mana.Code
{
    public abstract partial class BaseGameMode
    {
        public static uint UtilityDockId { get; set; }
        public static uint OutlinerDockId { get; set; }
        private bool _dockLayoutInitialized;
        private bool _dockLayoutResetRequested;
        public void ResetImGuiLayoutToDefaults()
        {
            ManaImGuiSettingsStorage.DeleteIniFile();
            ImGui.LoadIniSettingsFromMemory(string.Empty);
            _dockLayoutInitialized = false;
            _dockLayoutResetRequested = true;
            UtilityDockId = 0;
            OutlinerDockId = 0;
        }

        protected void DrawDockSpace()
        {
            var viewport = ImGui.GetMainViewport();
            var dockspaceFlags = ImGuiDockNodeFlags.PassthruCentralNode;
            var windowFlags = ImGuiWindowFlags.NoDocking |
                              ImGuiWindowFlags.NoTitleBar |
                              ImGuiWindowFlags.NoCollapse |
                              ImGuiWindowFlags.NoResize |
                              ImGuiWindowFlags.NoMove |
                              ImGuiWindowFlags.NoBringToFrontOnFocus |
                              ImGuiWindowFlags.NoNavFocus |
                              ImGuiWindowFlags.NoBackground;

            ImGui.SetNextWindowPos(viewport.WorkPos);
            ImGui.SetNextWindowSize(viewport.WorkSize);
            ImGui.SetNextWindowViewport(viewport.ID);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

            ImGui.Begin("MainDockSpaceHost", windowFlags);
            ImGui.PopStyleVar(3);

            var dockspaceId = ImGui.GetID("MainDockSpace");
            ImGui.DockSpace(dockspaceId, Vector2.Zero, dockspaceFlags);

            if (!_dockLayoutInitialized)
            {
                var useDefaultLayout = _dockLayoutResetRequested || !File.Exists(ManaImGuiSettingsStorage.IniFilePath);
                if (useDefaultLayout)
                {
                    if (_dockLayoutResetRequested)
                    {
                        ImGuiDockNative.DockBuilderRemoveNode(dockspaceId);
                        ImGuiDockNative.DockBuilderAddNode(dockspaceId, dockspaceFlags);
                    }

                    InitializeDockLayout(dockspaceId, viewport.WorkSize);
                    ImGui.SaveIniSettingsToDisk(ManaImGuiSettingsStorage.IniFilePath);
                }

                _dockLayoutResetRequested = false;
                _dockLayoutInitialized = true;
            }

            ImGui.End();
        }

    }
}


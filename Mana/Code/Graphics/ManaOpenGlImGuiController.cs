using System;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using ManaCore.Graphics;

namespace Mana.Code.Graphics
{
    public class ManaOpenGlImGuiController : IManaImGuiController
    {
        private readonly ImGuiController _controller;
        private nint _iniFilename;

        public ManaOpenGlImGuiController(GL gl, IWindow window, IInputContext input)
        {
            _controller = new ImGuiController(gl, window, input);
            ConfigureIniStorage();
        }

        public void Update(float deltaTime)
        {
            _controller.Update(deltaTime);
            SavePendingIniSettings();
        }

        public void Render()
        {
            _controller.Render();
            SavePendingIniSettings();
        }

        public void RecreateFontDeviceTexture()
        {
            var method = _controller.GetType().GetMethod(
                "RecreateFontDeviceTexture",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            method?.Invoke(_controller, null);
        }

        public void Dispose()
        {
            SaveIniSettings();
            _controller.Dispose();
            if (_iniFilename != 0)
            {
                Marshal.FreeHGlobal(_iniFilename);
                _iniFilename = 0;
            }
        }

        private static void SavePendingIniSettings()
        {
            var io = ImGui.GetIO();
            if (!io.WantSaveIniSettings)
            {
                return;
            }

            SaveIniSettings();
            io.WantSaveIniSettings = false;
        }

        private static void SaveIniSettings()
        {
            ImGui.SaveIniSettingsToDisk(ManaImGuiSettingsStorage.IniFilePath);
        }

        private unsafe void ConfigureIniStorage()
        {
            var iniPath = ManaImGuiSettingsStorage.IniFilePath;
            var io = ImGui.GetIO();
            _iniFilename = Marshal.StringToHGlobalAnsi(iniPath);
            io.NativePtr->IniFilename = (byte*)_iniFilename;
            if (File.Exists(iniPath))
            {
                ImGui.LoadIniSettingsFromDisk(iniPath);
            }
        }
    }
}

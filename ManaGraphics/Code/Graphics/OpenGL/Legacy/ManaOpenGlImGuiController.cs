using System;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;
using ManaGraphics.Code.Graphics;
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
        private readonly IMouse? _mouse;
        private readonly ManaImGuiClipboardBridge _clipboardBridge;

        public ManaOpenGlImGuiController(GL gl, IWindow window, IInputContext input)
        {
            _controller = new ImGuiController(gl, window, input);
            _mouse = input.Mice.FirstOrDefault();
            _clipboardBridge = new ManaImGuiClipboardBridge(input.Keyboards.FirstOrDefault());
            ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.HasMouseCursors;
            ConfigureIniStorage();
        }

        public void Update(float deltaTime)
        {
            _controller.Update(deltaTime);
            UpdateMouseCursor();
            SavePendingIniSettings();
        }

        public void Render()
        {
            _controller.Render();
            UpdateMouseCursor();
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
            _clipboardBridge.Dispose();
            _controller.Dispose();
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

        private void UpdateMouseCursor()
        {
            if (_mouse == null)
            {
                return;
            }

            var io = ImGui.GetIO();
            if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0)
            {
                return;
            }

            try
            {
                var imguiCursor = ImGui.GetMouseCursor();
                if (io.MouseDrawCursor || imguiCursor == ImGuiMouseCursor.None)
                {
                    if (_mouse.Cursor.IsSupported(CursorMode.Hidden))
                    {
                        _mouse.Cursor.CursorMode = CursorMode.Hidden;
                    }

                    return;
                }

                if (_mouse.Cursor.IsSupported(CursorMode.Normal))
                {
                    _mouse.Cursor.CursorMode = CursorMode.Normal;
                }

                var standardCursor = imguiCursor switch
                {
                    ImGuiMouseCursor.TextInput => StandardCursor.IBeam,
                    ImGuiMouseCursor.ResizeAll => StandardCursor.ResizeAll,
                    ImGuiMouseCursor.ResizeNS => StandardCursor.VResize,
                    ImGuiMouseCursor.ResizeEW => StandardCursor.HResize,
                    ImGuiMouseCursor.ResizeNESW => StandardCursor.NeswResize,
                    ImGuiMouseCursor.ResizeNWSE => StandardCursor.NwseResize,
                    ImGuiMouseCursor.Hand => StandardCursor.Hand,
                    ImGuiMouseCursor.NotAllowed => StandardCursor.NotAllowed,
                    _ => StandardCursor.Arrow
                };

                if (!_mouse.Cursor.IsSupported(standardCursor))
                {
                    standardCursor = StandardCursor.Arrow;
                }

                _mouse.Cursor.Type = CursorType.Standard;
                _mouse.Cursor.StandardCursor = standardCursor;
            }
            catch
            {
                // Cursor changes are cosmetic and must never interrupt rendering.
            }
        }

        private static void SaveIniSettings()
        {
            ImGui.SaveIniSettingsToDisk(ManaImGuiSettingsStorage.IniFilePath);
        }

        private unsafe void ConfigureIniStorage()
        {
            var iniPath = ManaImGuiSettingsStorage.IniFilePath;
            var io = ImGui.GetIO();

            // IniFilename left null on purpose: setting it makes ImGui auto-save to that ONE fixed path
            // for the life of the context, which can't follow ManaImGuiSettingsStorage's now per-UI-scale
            // ini path. Save and load are both already driven explicitly from that property (see
            // SaveIniSettings and the load below), so the native filename was a redundant second
            // mechanism -- dropping it leaves exactly one, and it's the one that tracks the scale.
            io.NativePtr->IniFilename = null;
            if (File.Exists(iniPath))
            {
                ImGui.LoadIniSettingsFromDisk(iniPath);
            }
        }
    }
}

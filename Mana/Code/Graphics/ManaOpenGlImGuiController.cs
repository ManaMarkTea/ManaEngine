using System;
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

        public ManaOpenGlImGuiController(GL gl, IWindow window, IInputContext input)
        {
            _controller = new ImGuiController(gl, window, input);
        }

        public void Update(float deltaTime)
        {
            _controller.Update(deltaTime);
        }

        public void Render()
        {
            _controller.Render();
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
            _controller.Dispose();
        }
    }
}

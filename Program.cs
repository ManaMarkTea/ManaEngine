// This file is part of Silk.NET.
//
// You may modify and distribute Silk.NET under the terms
// of the MIT license. See the LICENSE file for details.

using System.Drawing;
using Silk.NET.Windowing;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using System.Numerics;
using Silk.NET.Maths;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace Mana
{
    public class Program
    {                           
        static void Main(string[] args)
        {
            Game game = new Game();
            game.Run();        
        }
    }

    public class Game
    {
        public int Frame = 0;
        public static Game Instance;
        public ImGuiController _ImGui;
        public IWindow _Window;
        public IInputContext _Input;
        public GL gl;

        public ManaWorld _World;

        public Game()
        {
            Instance = this;
            _Window = Window.Create(WindowOptions.Default);
            _Window.Load += Load;
            _Window.FramebufferResize += Resize;
            _Window.Update += Update;
            _Window.Render += Render;
                        
        }

        public void Run()
        {
            _Window.Run();
        }

        private static DebugProc _debugProcCallback = DebugCallback;
        private static GCHandle _debugProcCallbackHandle;
        private static void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, IntPtr message, IntPtr userParam)
        {
            string messageString = Marshal.PtrToStringAnsi(message, length);
            Trace.WriteLine($"{severity} {type} | {messageString}");

            if (type == GLEnum.DebugTypeError)
            {
                throw new Exception(messageString);
            }
        }

        public void Load()
        {
            gl = _Window.CreateOpenGL();
            _Input = _Window.CreateInput();

            gl.Enable(EnableCap.DepthTest);
            gl.Enable(EnableCap.DebugOutput);
            gl.Enable(EnableCap.DebugOutputSynchronous);
            gl.DebugMessageControl(DebugSource.DontCare, DebugType.DontCare, DebugSeverity.DontCare, 0, new uint[0], true);

            _debugProcCallbackHandle = GCHandle.Alloc(_debugProcCallback);
            gl.DebugMessageCallback(DebugCallback, IntPtr.Zero);


            _ImGui = new ImGuiController(gl, _Window, _Input);

            gl.ClearColor(100 / 255.0f, 149 / 255.0f, 237 / 255.0f, 255 / 255.0f);

            _World = new ManaWorld();
            //_World.LoadFromGltf("Assets/Models/FlightHelmet/glTF/FlightHelmet.gltf");
            _World.Update();

        }

        public void Resize(Vector2D<int> size)
        {
            // Adjust the viewport to the new window size
            gl.Viewport(size);
        }

        public void Update(double delta)
        {
            // Make sure ImGui is up-to-date
            _ImGui.Update((float)delta);

            _World.Update();
        }

        public void Render(double delta)
        {
            Frame++;
            _World.Draw();


            // This is where you'll do all of your ImGUi rendering
            // Here, we're just showing the ImGui built-in demo window.
            ImGuiNET.ImGui.ShowDemoWindow();

            // Make sure ImGui renders too!
            _ImGui.Render();
        }

        public void Closing()
        {
            _ImGui?.Dispose();
            _Input?.Dispose();
            gl?.Dispose();
        }

    }


}
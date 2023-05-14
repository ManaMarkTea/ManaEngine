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
using Mana.Code;
using Mana.Code.Shapes;
using ImGuiNET;

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
        

        public ManaContext Context;

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

            if (type == GLEnum.DebugTypeOther)
            {
                return;
            }

            Trace.WriteLine($"{severity} {type} | {messageString}");

            if (type == GLEnum.DebugTypeError)
            {
                if (messageString.Contains("UniformMatrix*"))
                {
                    return;
                }

                throw new Exception(messageString);
            }
        }

        public void Load()
        {           
            var gl = _Window.CreateOpenGL();
            var input = _Window.CreateInput();
            var world = new ManaWorld();
            
            Context = new ManaContext(gl, input, world);
            world.Initialize(Context);

            gl.Enable(EnableCap.DepthTest);
            gl.Enable(EnableCap.DebugOutput);
            gl.Enable(EnableCap.DebugOutputSynchronous);
            gl.DebugMessageControl(DebugSource.DontCare, DebugType.DontCare, DebugSeverity.DontCare, 0, new uint[0], true);

            _debugProcCallbackHandle = GCHandle.Alloc(_debugProcCallback);
            gl.DebugMessageCallback(DebugCallback, IntPtr.Zero);


            _ImGui = new ImGuiController(gl, _Window, input);

                        
            var sun = world.AddSphere(new Vector3(5, 5, -5), Colors.Yellow, 2.0f);

            var sunMaterial = (sun.Components[0] as MeshRendererComponent).DynamicMaterial;
            sunMaterial.Emission.Value = Colors.Gold;
            sunMaterial.EmissionStrength = 10.0f;

            world.LoadFromGltf("Assets/Models/FlightHelmet/glTF/FlightHelmet.gltf");
            var helmetNode = world.Scenes[1].Children[0];
            helmetNode.Position = new Vector3(2, 0, 2);
            world.Update();
        }

        public void Resize(Vector2D<int> size)
        {
            // Adjust the viewport to the new window size
            Context.gl.Viewport(size);
        }

        public void Update(double delta)
        {
            // Make sure ImGui is up-to-date
            _ImGui.Update((float)delta);
            ManaTime.GlobalTime.UpdateTime(delta);



            ImGui.SetNextWindowPos( new Vector2(this._Window.Size.X - 200, 15));
            ImGui.Begin("Mana");
            ImGui.Text(ManaTime.GlobalTime.DeltaTimeAvg.TotalMilliseconds.ToString(" 0") + " ms");
            ImGui.Text(ManaTime.GlobalTime.FramesPerSecondAvg.ToString(" 0.00") + " fps");
            ImGui.End();

            

            Context.World.Update();
        }

        public void Render(double delta)
        {
            Frame++;
            Context.World.Draw();

            // This is where you'll do all of your ImGUi rendering
            // Here, we're just showing the ImGui built-in demo window.
            //ImGuiNET.ImGui.ShowDemoWindow();

            // Make sure ImGui renders too!
            _ImGui.Render();
        }

        public void Closing()
        {
            _ImGui?.Dispose();
            Context.Input?.Dispose();
            Context.gl?.Dispose();
        }

    }


}
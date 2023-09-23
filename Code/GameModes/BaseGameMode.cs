using Silk.NET.GLFW;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mana.Code
{
    public abstract class BaseGameMode
    {
        public long UpdateFrame = 0;
        public long RenderFrame = 0;

        public ImGuiController _ImGui;
        public IWindow _Window;
        public GL GL;
        public GameModeGraphicsLevelDebugger _GlDebugger;
        public ManaContext Context;


        public BaseGameMode()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var glfw = GlfwProvider.GLFW.Value;
                glfw.WindowHint(WindowHintBool.OpenGLForwardCompat, true);
                glfw.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
            }

            WindowOptions optionOpenGL = new WindowOptions(
                isVisible: true,
                new Vector2D<int>(50, 50),
                new Vector2D<int>(1280, 720), 0.0, 0.0,
                GraphicsAPI.Default, "ManaGL - SceneViewerMode",
                WindowState.Normal,
                WindowBorder.Resizable,
                isVSync: true,
                shouldSwapAutomatically: true,
                new Silk.NET.Windowing.VideoMode(60));

            _Window = Window.Create(optionOpenGL);
            _Window.Load += Load;
            _Window.FramebufferResize += Resize;
            _Window.Update += update;
            _Window.Render += Render;

        }

        public virtual void Load()
        {
            GL = _Window.CreateOpenGL();
            var input = _Window.CreateInput();

            var majorVersion = GL.GetInteger(GLEnum.MajorVersion);
            var minorVersion = GL.GetInteger(GLEnum.MinorVersion);
            Console.WriteLine($"Mana: GL {majorVersion}.{minorVersion}");

            var world = new ManaWorld();
            Context = new ManaContext(_Window, GL, input, world);

            world.Initialize(Context);

            GL.Enable(EnableCap.DepthTest);

            _GlDebugger = new GameModeGraphicsLevelDebugger(GL);
            _ImGui = new ImGuiController(GL, _Window, input);
        }

        public void Resize(Vector2D<int> size)
        {
            // Adjust the viewport to the new window size
            Context.gl.Viewport(size);
        }

        public virtual void Run()
        {
            _Window.Run();
        }

        public void update(double deltaTime)
        {
            UpdateFrame++;

            // Make sure ImGui is up-to-date
            _ImGui.Update((float)deltaTime);

            ManaTime.GlobalTime.UpdateTime(deltaTime);

            Update(deltaTime);

            Context.World.Update();
        }

        public abstract void Update(double deltaTime);

        public virtual void Render(double deltaTime)
        {
            RenderFrame++;
            Context.World.Draw();
            _ImGui.Render();
        }

        public virtual void Closing()
        {
            _ImGui?.Dispose();
            Context.Input?.Dispose();
            Context.gl?.Dispose();
        }
    }


    public class GameModeGraphicsLevelDebugger
    {
        private DebugProc _debugProcCallback;
        private GCHandle _debugProcCallbackHandle;

        public GameModeGraphicsLevelDebugger(GL GL)
        {
            _debugProcCallback = DebugCallback;
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // Mac os only supports 4.1, debugging in in 4.3
                GL.Enable(EnableCap.DebugOutput);
                GL.Enable(EnableCap.DebugOutputSynchronous);
                GL.DebugMessageControl(DebugSource.DontCare, DebugType.DontCare, DebugSeverity.DontCare, 0, new uint[0],
                    true);

                _debugProcCallbackHandle = GCHandle.Alloc(_debugProcCallback);
                GL.DebugMessageCallback(DebugCallback, IntPtr.Zero);
            }
        }

        private void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, IntPtr message, IntPtr userParam)
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
    }

}



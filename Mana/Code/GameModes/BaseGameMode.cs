using ImGuiNET;
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
                new Vector2D<int>(1280, 720), 90, 90,
                GraphicsAPI.Default, "ManaGL - SceneViewerMode",
                WindowState.Normal,
                WindowBorder.Resizable,
                isVSync: true,
                shouldSwapAutomatically: true,
                new Silk.NET.Windowing.VideoMode(90),
                preferredDepthBufferBits:32,
                preferredStencilBufferBits:8);

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
            ConfigImStyle();
        }

        void ConfigImStyle()
        {
            Vector3 color_for_text = new Vector3(236.0f / 255.0f, 240.0f / 255.0f, 241.0f / 255.0f);
            Vector3 color_for_head = new Vector3(61.0f / 255.0f, 128.0f / 255.0f, 185.0f / 255.0f);
            Vector3 color_for_area = new Vector3(57.0f / 255.0f, 79.0f / 255.0f, 105.0f / 255.0f);
            Vector3 color_for_body = new Vector3(44.0f / 255.0f, 62.0f / 255.0f, 80.0f / 255.0f);
            Vector3 color_for_pops = new Vector3(33.0f / 255.0f, 46.0f / 255.0f, 60.0f / 255.0f);
            ConfigImStyle(color_for_text, color_for_head, color_for_area, color_for_body, color_for_pops);
        }

        public void ConfigImStyle(Vector3 color_for_text, Vector3 color_for_head, Vector3 color_for_area, Vector3 color_for_body, Vector3 color_for_pops)
        {
            ImGuiStylePtr style = ImGui.GetStyle();

            style.Colors[(int)ImGuiCol.Text] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 1.00f);
            style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.58f);
            style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(color_for_body.X, color_for_body.Y, color_for_body.Z, 0.95f);
            style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 0.58f);
            style.Colors[(int)ImGuiCol.Border] = new Vector4(color_for_body.X, color_for_body.Y, color_for_body.Z, 0.00f);
            style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(color_for_body.X, color_for_body.Y, color_for_body.Z, 0.00f);
            style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.78f);
            style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 0.75f);
            style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 0.47f);
            style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.21f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.78f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            
            style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.80f);
            style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.50f);
            style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.Button] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.50f);
            style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.86f);
            style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.Header] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.76f);
            style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.86f);
            style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            
            style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.63f);
            style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.63f);
            style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.43f);
            style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(color_for_pops.X, color_for_pops.Y, color_for_pops.Z, 0.92f);

            /*
            style.Colors[(int)ImGuiCol.ComboBg] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 1.00f);
            style.Colors[(int)ImGuiCol.Column] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.32f);
            style.Colors[(int)ImGuiCol.ColumnHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.78f);
            style.Colors[(int)ImGuiCol.ColumnActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.Res.ZeGrip] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.15f);
            style.Colors[(int)ImGuiCol.Res.ZeGripHovered] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 0.78f);
            style.Colors[(int)ImGuiCol.Res.ZeGripActive] = new Vector4(color_for_head.X, color_for_head.Y, color_for_head.Z, 1.00f);
            style.Colors[(int)ImGuiCol.CloseButton] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.16f);
            style.Colors[(int)ImGuiCol.CloseButtonHovered] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 0.39f);
            style.Colors[(int)ImGuiCol.CloseButtonActive] = new Vector4(color_for_text.X, color_for_text.Y, color_for_text.Z, 1.00f);
            style.Colors[(int)ImGuiCol.ModalWindowDarkening] = new Vector4(color_for_area.X, color_for_area.Y, color_for_area.Z, 0.73f);*/
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

                if (messageString.Contains("linked"))
                {
                    return;
                }

                throw new Exception(messageString);
            }
        }
    }

}



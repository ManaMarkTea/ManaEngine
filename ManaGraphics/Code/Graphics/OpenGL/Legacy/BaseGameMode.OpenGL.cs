using Mana.Code.Graphics;
using ManaCore.Graphics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mana.Code
{
    public abstract partial class BaseGameMode
    {
        public GL GL = null!;
        public GameModeGraphicsLevelDebugger _GlDebugger = null!;
        private static readonly GraphicsAPI RequestedGraphicsApi = GraphicsAPI.Default;
        public virtual void LoadForOpenGlBackend(IWindow window, GL gl, IInputContext input)
        {
            _Window = window;
            GL = gl;

            LogLoadDiagnostics(input);

            var world = new ManaWorld();
            Context = new ManaContext(_Window, GL, input, world);
            Context.GameMode = this;
            Context.DeferGpuUploads = DeferGpuUploadsOnStartup;

            world.Initialize(Context);

            GL.Enable(EnableCap.DepthTest);

            _GlDebugger = new GameModeGraphicsLevelDebugger(GL);
            GraphicsDiagnosticsProvider = new ManaOpenGlDiagnosticsProvider(Context);
            GraphicsDiagnosticsProvider.CaptureInitializationSnapshot();
            // Initialize the physics engine
            Context.Physics = new ManaPhysics(Context);

            // Same opt-in as the WebGPU path. Without this the tree is never built on OpenGL, so the UI
            // recorded a draw list every frame that nothing could ever submit.
            if (EnableManaUi)
            {
                ManaUi = new Mana.Code.UI.ManaUi.ManaUiHost(window, input);
                BuildManaUi(ManaUi);
                WrapManaUiWithLoadingScreen();
            }

            Load();
        }

        private void LogLoadDiagnostics(IInputContext input)
        {
            var majorVersion = GL.GetInteger(GLEnum.MajorVersion);
            var minorVersion = GL.GetInteger(GLEnum.MinorVersion);
            var requestedApi = DescribeGraphicsApi(RequestedGraphicsApi);
            var windowApi = DescribeGraphicsApi(_Window.API);
            var debugCallbacks = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "disabled on macOS before OpenGL 4.3"
                : "enabled after startup diagnostics";

            WriteStartupLog("Mana: startup diagnostics");
            WriteStartupLog($"Mana: Requested API: {requestedApi}");
            WriteStartupLog($"Mana: Window API: {windowApi}");
            WriteStartupLog(
                $"Mana: OpenGL {majorVersion}.{minorVersion} | Version='{GetGlString(StringName.Version)}' | GLSL='{GetGlString(StringName.ShadingLanguageVersion)}'");
            WriteStartupLog($"Mana: GPU: {GetGlString(StringName.Renderer)}");
            WriteStartupLog($"Mana: Vendor: {GetGlString(StringName.Vendor)}");
            WriteStartupLog(
                $"Mana: Window: size={_Window.Size.X}x{_Window.Size.Y}, framebuffer={_Window.FramebufferSize.X}x{_Window.FramebufferSize.Y}, " +
                $"fps={_Window.FramesPerSecond:0.##}, updateHz={_Window.UpdatesPerSecond:0.##}, vsync={_Window.VSync}, " +
                $"depth={_Window.PreferredDepthBufferBits?.ToString() ?? "default"}, stencil={_Window.PreferredStencilBufferBits?.ToString() ?? "default"}");
            WriteStartupLog(
                $"Mana: Input: keyboards={input.Keyboards.Count}, mice={input.Mice.Count}, gamepads={input.Gamepads.Count}, joysticks={input.Joysticks.Count}, other={input.OtherDevices.Count}");
            WriteStartupLog(
                $"Mana: GL limits: maxTexture={GL.GetInteger(GLEnum.MaxTextureSize)}, maxCubeMap={GL.GetInteger(GLEnum.MaxCubeMapTextureSize)}, " +
                $"maxTextureUnits={GL.GetInteger(GLEnum.MaxCombinedTextureImageUnits)}, maxVertexAttribs={GL.GetInteger(GLEnum.MaxVertexAttribs)}, " +
                $"maxDrawBuffers={GL.GetInteger(GLEnum.MaxDrawBuffers)}, maxColorAttachments={GL.GetInteger(GLEnum.MaxColorAttachments)}, " +
                $"maxSamples={GL.GetInteger(GLEnum.MaxSamples)}, maxRenderbuffer={GL.GetInteger(GLEnum.MaxRenderbufferSize)}");
            WriteStartupLog($"Mana: Runtime: {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.ProcessArchitecture}");
            WriteStartupLog($"Mana: GL debug callback: {debugCallbacks}");
            WriteStartupLog("Mana: GL 3.3 note: Silk.NET GraphicsAPI.Default requests OpenGL 3.3 Core/ForwardCompatible, matching the project's #version 330 shaders.");
        }

        private string GetGlString(StringName name)
        {
            try
            {
                return GL.GetStringS(name);
            }
            catch (Exception ex)
            {
                return $"unavailable ({ex.GetType().Name})";
            }
        }

        private static string DescribeGraphicsApi(GraphicsAPI api)
        {
            return $"{api.API} {api.Version.MajorVersion}.{api.Version.MinorVersion} {api.Profile}/{api.Flags}";
        }

        private static void WriteStartupLog(string message)
        {
            Console.WriteLine(message);
            Trace.WriteLine(message);
        }

        public virtual void Run()
        {
            new GameSilkOpenGl(this, "ManaGL - SceneViewerMode", RequestedGraphicsApi).Run();
        }

    }
}

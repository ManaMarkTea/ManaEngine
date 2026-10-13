#if !MANA_BROWSER
using Silk.NET.Input;
using Silk.NET.WebGPU;
using Silk.NET.Windowing;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// A platform that brings its own window, input and wgpu-native (iOS, Android): GLFW and Silk.NET's desktop
    /// library loading do not exist there. Set <see cref="ManaWebGpuNativeHost.Current"/> before running a game mode
    /// and <see cref="GameSilkWebGpu"/> runs on it instead of opening a desktop window.
    /// </summary>
    public unsafe interface IManaWebGpuNativeHost
    {
        /// <summary>The app's view as a Silk.NET window. Its Run fires Load and returns; the platform drives frames.</summary>
        IWindow Window { get; }

        IInputContext Input { get; }

        /// <summary>The WebGPU API over the platform's own copy of wgpu-native.</summary>
        WebGPU CreateApi();

        /// <summary>A surface on the platform's view (a CAMetalLayer on iOS, an ANativeWindow on Android).</summary>
        Surface* CreateSurface(WebGPU api, Instance* instance);
    }

    public static class ManaWebGpuNativeHost
    {
        public static IManaWebGpuNativeHost? Current { get; set; }
    }
}
#endif

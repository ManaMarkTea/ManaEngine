// Mana Mark Tea - 2023
global using Mana.Code;
global using Mana.Code.ManaShaders;
global using Mana.Code.Materials;
global using Mana.Code.Renderers;
global using Mana.Code.Shapes;
global using Mana.Code.GameModes;
global using ManaCore.Graphics;
global using ManaGraphics.Code.Graphics;
global using ManaGraphics.Code.Diagnostics;
global using ManaVoxel.Code.Loaders;
global using System.Numerics;
global using Silk.NET.Input;
global using Silk.NET.Windowing;

namespace Mana
{
    public class Program
    {
        static void Main(string[] args)
        {
            try
            {
                ManaUiDensity.RestorePreset();

                var launchOptions = ManaLaunchOptions.Parse(args);
                var backendKind = launchOptions.GraphicsBackend;

#if !MANA_BROWSER
                if (launchOptions.PrecacheThumbnails)
                {
                    ManaVoxel.Code.Assets.MeshThumbnailCache.PrecacheLibrary(Console.WriteLine);
                    return;
                }
#endif

                // --play skips the editor entirely: same executable, but the game rather than the tool.
                if (launchOptions.PlayLevelPath != null || launchOptions.PlayMenu)
                {
                    var play = new VoxelPlayGameMode(launchOptions.PlayLevelPath, backendKind)
                    {
                        CommandFilePath = launchOptions.CommandFilePath,
                        OffersLevelSelect = launchOptions.PlayMenu
                    };
#if !MANA_BROWSER
                    if (backendKind == ManaGraphicsBackendKind.WebGpu)
                    {
                        play.RunWebGpu(launchOptions.WebGpuBackend);
                        return;
                    }

                    play.Run();
                    return;
#else
                    play.RunWebGpu(launchOptions.WebGpuBackend);
                    return;
#endif
                }

                var game = new VoxelEditorGameMode(backendKind)
                {
                    StartupLevelPath = launchOptions.EditLevelPath,
                    CommandFilePath = launchOptions.CommandFilePath
                };
#if !MANA_BROWSER
                if (backendKind == ManaGraphicsBackendKind.WebGpu)
                {
                    game.RunWebGpu(launchOptions.WebGpuBackend);
                    return;
                }

                game.Run();
#else
                // PurgeStaleDockLayout is ImGui docking-layout housekeeping (BaseGameMode.ImGuiDocking.cs),
                // excluded on browser; Run() is GLFW's WindowExtensions.Run(IView), desktop-only -- browser
                // is WebGPU-only and browser-wasm's Main() can't block synchronously the way this whole
                // Program.cs assumes (see ManaVoxel.Web/Program.cs's own bring-up sequence, which awaits
                // ManaBrowserInterop.InitializeAsync() before touching a game mode at all). A real browser
                // launcher for the editor is a separate entry-point project, not this file -- same relationship
                // ManaVoxel.Web already has to VoxelPlayGameMode: ManaVoxel.csproj compiling for browser-wasm
                // (this build) is what makes that project possible, not a replacement for it.
                throw new PlatformNotSupportedException(
                    "ManaVoxel.exe's own Program.cs is not the browser entry point for the editor. " +
                    "See ManaVoxel.Web/Program.cs for the pattern a browser-hosted editor launcher should follow.");
#endif
            }
            finally
            {
                ManaConsole.Shutdown();
            }
        }
    }
}

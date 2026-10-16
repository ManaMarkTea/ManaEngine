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
                        OffersLevelSelect = launchOptions.PlayMenu,
                        Muted = launchOptions.Mute
                    };
                    play.RunWebGpu(launchOptions.WebGpuBackend);
                    return;
                }

                var game = new VoxelEditorGameMode(backendKind)
                {
                    StartupLevelPath = launchOptions.EditLevelPath,
                    CommandFilePath = launchOptions.CommandFilePath,
                    Muted = launchOptions.Mute
                };
                game.RunWebGpu(launchOptions.WebGpuBackend);
            }
            finally
            {
                ManaConsole.Shutdown();
            }
        }
    }
}

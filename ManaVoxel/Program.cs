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

                // --play skips the editor entirely: same executable, but the game rather than the tool.
                if (launchOptions.PlayLevelPath is { } playLevelPath)
                {
                    var play = new VoxelPlayGameMode(playLevelPath, backendKind);
                    if (backendKind == ManaGraphicsBackendKind.WebGpu)
                    {
                        play.RunWebGpu(launchOptions.WebGpuBackend);
                        return;
                    }

                    play.Run();
                    return;
                }

                var game = new VoxelEditorGameMode(backendKind)
                {
                    StartupLevelPath = launchOptions.EditLevelPath
                };
                game.PurgeStaleDockLayout();

                if (backendKind == ManaGraphicsBackendKind.WebGpu)
                {
                    game.RunWebGpu(launchOptions.WebGpuBackend);
                    return;
                }

                game.Run();
            }
            finally
            {
                ManaConsole.Shutdown();
            }
        }
    }
}

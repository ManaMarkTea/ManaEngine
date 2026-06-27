// Mana Mark Tea - 2023
global using Mana.Code;
global using Mana.Code.ManaShaders;
global using Mana.Code.Materials;
global using Mana.Code.Renderers;
global using Mana.Code.Shapes;
global using Mana.Code.GameModes;
global using Mana.Code.Graphics;
global using Mana.Code.Diagnostics;
global using System.Numerics;

namespace Mana
{
    public class Program
    {
        static void Main(string[] args)
        {
            var launchOptions = ManaLaunchOptions.Parse(args);
            if (launchOptions.GraphicsBackend == ManaGraphicsBackend.WebGpu)
            {
                ManaConsole.AttachForDiagnostics("ManaVoxel WebGPU Diagnostics");
                try
                {
                    ManaConsole.WriteDiagnostic("Starting Mana WebGPU - VoxelEditor");
                    ManaConsole.WriteDiagnostic($"Requested WGPU backend: {launchOptions.WebGpuBackend ?? "auto"}");
                    new GameSilkWebGpu("Mana WebGPU - VoxelEditor", launchOptions.WebGpuBackend).Run();
                }
                catch (Exception ex)
                {
                    ManaConsole.WriteDiagnostic("[Mana WebGPU] Fatal startup error:");
                    ManaConsole.WriteDiagnostic(ex);
                    throw;
                }

                return;
            }

            VoxelEditorGameMode game = new VoxelEditorGameMode();
            game.Run();
        }
    }

}

// Mana Mark Tea - 2023
global using Mana.Code;
global using Mana.Code.ManaShaders;
global using Mana.Code.Materials;
global using Mana.Code.Renderers;
global using Mana.Code.Shapes;
global using Mana.Code.GameModes;
global using System.Numerics;

namespace Mana
{
    public class Program
    {
        static void Main(string[] args)
        {
            VoxelEditorGameMode game = new VoxelEditorGameMode();
            game.Run();
        }
    }

}
// Mana Mark Tea - 2023
global using Mana.Code;
global using Mana.Code.ManaShaders;
global using Mana.Code.Materials;
global using Mana.Code.Renderers;
global using Mana.Code.Shapes;
global using Mana.Code.Nodes;

namespace Mana
{
    public class Program
    {
        static void Main(string[] args)
        {
            ManaUiDensity.RestorePreset();

            SceneViewerMode game = new SceneViewerMode();
            game.PurgeStaleDockLayout();
            game.Run();
        }
    }

}

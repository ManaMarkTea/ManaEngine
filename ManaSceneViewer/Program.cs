// Mana Mark Tea - 2023
using Mana;
using Mana.Code;

namespace ManaSceneViewer
{
    public class Program
    {
        static void Main(string[] args)
        {
            ManaUiDensity.RestorePreset();

            SceneViewerMode game = new SceneViewerMode();
            game.Run();
        }
    }
}

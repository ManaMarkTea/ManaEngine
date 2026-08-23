// Mana Mark Tea - 2023

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

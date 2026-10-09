using ManaVoxel.Code.GridMaps;
using TrainYardWars;
using TrainYardWars.View;

namespace ManaVoxel.Code.Levels
{
    /// <summary>
    /// The games that live in projects of their own (<c>Games/</c>): the editor lists their level game modes and lends
    /// them what only the app has, such as mesh import.
    /// </summary>
    internal static class ManaGames
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Register()
        {
            TywModel.Loader = (asset, world, into) => ManaGridMapNode.LoadAsset(ManaGridMapNode.AssetOf(asset), world, into);
            ManaLevelGameModes.Register(new ManaLevelGameModes.Definition
            {
                Type = TrainYardWarsMode.ModeType,
                DisplayName = TrainYardWarsMode.DisplayName,
                Description = TrainYardWarsMode.Description,
                ModeType = typeof(TrainYardWarsMode),
                Create = () => new TrainYardWarsMode()
            });
        }
    }
}

using System.Runtime.CompilerServices;
using Mana.Code.Models;

namespace ManaEditor.Code.Assets
{
    /// <summary>Gives the engine's model library (<see cref="ManaModel.Loader"/>) the editor's mesh importer, so a game mode can place models by path.</summary>
    internal static class ManaModelLoaderRegistration
    {
        [ModuleInitializer]
        internal static void Register()
        {
            // A game's own app may set its own afterwards.
            ManaModel.Loader ??= (asset, context, scene) => ManaMeshAssetLoader.For(context.World).Load(asset, scene);
        }
    }
}

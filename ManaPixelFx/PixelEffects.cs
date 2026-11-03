using ManaPixelFx.Core;
using ManaPixelFx.Effects.Fire;

namespace ManaPixelFx
{
    /// <summary>Every effect ported so far, by its pixelfx id ("fire/torch"); the rest still play from imported sheets.</summary>
    public static class PixelEffects
    {
        private static readonly Dictionary<string, PixelEffect> ById = new PixelEffect[]
        {
            new Torch(),
            new Campfire(),
            new Candle(),
            new Brazier(),
        }.ToDictionary(effect => effect.Meta.Id, StringComparer.OrdinalIgnoreCase);

        public static IEnumerable<PixelEffect> All => ById.Values;

        public static PixelEffect? Find(string id)
        {
            ById.TryGetValue(id, out var effect);
            return effect;
        }
    }
}

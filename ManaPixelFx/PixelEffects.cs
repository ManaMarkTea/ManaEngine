using ManaPixelFx.Core;
using ManaPixelFx.Effects.Fire;

namespace ManaPixelFx
{
    /// <summary>Every effect ported so far, by its pixelfx id ("fire/torch").</summary>
    public static class PixelEffects
    {
        private static readonly object Sync = new();

        private static readonly Dictionary<string, PixelEffect> ById = new PixelEffect[]
        {
            new Torch(),
            new Campfire(),
            new Candle(),
            new Brazier(),
        }.ToDictionary(effect => effect.Meta.Id, StringComparer.OrdinalIgnoreCase);

        /// <summary>The effects in id order (category, then name).</summary>
        public static IReadOnlyList<PixelEffect> All
        {
            get
            {
                lock (Sync)
                {
                    return ById.Values.OrderBy(effect => effect.Meta.Id, StringComparer.Ordinal).ToList();
                }
            }
        }

        public static PixelEffect? Find(string id)
        {
            lock (Sync)
            {
                ById.TryGetValue(id, out var effect);
                return effect;
            }
        }

        /// <summary>Adds an effect from outside the library -- a game's own, or a test's.</summary>
        public static void Register(PixelEffect effect)
        {
            lock (Sync)
            {
                ById[effect.Meta.Id] = effect;
            }
        }

        /// <summary>The pixelfx file an effect was ported from, inside a pixelfx checkout.</summary>
        public static string OriginalFile(PixelEffect effect) => Path.Combine("src", "fx", effect.Meta.Id + ".js");
    }
}

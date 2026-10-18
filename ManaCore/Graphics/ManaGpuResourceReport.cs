using System.Text;

namespace ManaCore.Graphics
{
    /// <summary>
    /// Live GPU object counts per kind ("buffers", "textures", ...), asked of the backend through <see cref="Provider"/>.
    /// A kind's own key counts what the app still holds; "kind+internal" counts what the app released but the driver
    /// keeps alive (in-flight work). Only backends that can enumerate their objects (wgpu-native) set a provider.
    /// </summary>
    public static class ManaGpuResourceReport
    {
        /// <summary>Set by the renderer while it is alive; null where nothing can be counted (the browser).</summary>
        public static Func<IReadOnlyDictionary<string, long>?>? Provider { get; set; }

        public static IReadOnlyDictionary<string, long>? Capture() => Provider?.Invoke();

        /// <summary>One line of the counts, kinds that have none left out.</summary>
        public static string Format(IReadOnlyDictionary<string, long> counts)
        {
            var text = new StringBuilder();
            foreach (var pair in counts.Where(pair => pair.Value != 0).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                text.Append(text.Length == 0 ? "" : ", ").Append(pair.Key).Append('=').Append(pair.Value);
            }

            return text.Length == 0 ? "nothing live" : text.ToString();
        }

        /// <summary>Kinds whose count moved since <paramref name="before"/>, as signed changes, in key order.</summary>
        public static IReadOnlyList<KeyValuePair<string, long>> Changes(
            IReadOnlyDictionary<string, long> before, IReadOnlyDictionary<string, long> after)
        {
            var changes = new List<KeyValuePair<string, long>>();
            foreach (var key in before.Keys.Union(after.Keys).OrderBy(key => key, StringComparer.Ordinal))
            {
                before.TryGetValue(key, out var was);
                after.TryGetValue(key, out var now);
                if (now != was)
                {
                    changes.Add(new KeyValuePair<string, long>(key, now - was));
                }
            }

            return changes;
        }
    }
}

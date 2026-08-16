using System.Linq;
using Mana;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The ManaUI counterpart to ManaWorld's ImGui "World Perf" window: live per-system frame timings,
    /// whatever streaming status a subsystem chose to push (see <see cref="ManaWorld.RecordFrameStatText"/>),
    /// and the one-time load-time history.
    /// <para>
    /// The two live dictionaries are keyed by system name and overwritten every frame rather than replaced,
    /// so the panel does not rebuild every frame just because a duration changed -- only the row list is
    /// rebuilt, and only when the actual set of names changes (a search or a planet stat starting or
    /// stopping to report). Each row polls its own value independently.
    /// </para>
    /// </summary>
    public static class WorldPerfManaView
    {
        public static UiElement Build(UiTheme theme, ManaWorld world)
        {
            var meshCount = new TextBlock().Foreground(theme.Text);
            meshCount.BindTextPolled(() => $"Meshes rendered: {world.LastMeshCount}");

            return new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                new Expander().Header("Live (this frame)").Section(theme.Spacing,
                    meshCount,
                    DictionaryRows(theme, () => world.LiveFrameStats,
                        (name, duration) => $"{duration.TotalMilliseconds,7:F2}ms - {name}")),

                new Expander().Header("Streaming").Section(theme.Spacing,
                    DictionaryRows(theme, () => world.LiveFrameStatTexts,
                        (name, value) => $"{name}: {value}")),

                new Expander().Header("Load times").Section(theme.Spacing,
                    LoadTimeRows(theme, world)));
        }

        private static UiElement LoadTimeRows(UiTheme theme, ManaWorld world)
        {
            // A one-time history from startup -- built once rather than polled, since nothing appends to
            // it once the load screen is gone.
            var stack = new StackPanel().Vertical(theme.Spacing * 0.5f);
            foreach (var perf in world.PerformanceLogs)
            {
                stack.Children.Add(new TextBlock()
                    .Text($"{perf.Duration.Milliseconds}ms - {perf.Name}")
                    .Foreground(theme.TextMuted));
            }

            return stack;
        }

        /// <summary>
        /// One row per key currently in <paramref name="source"/>, rebuilt only when the key set itself
        /// changes -- not on every poll, which would throw a live row's binding away every frame just to
        /// build the same row again.
        /// </summary>
        private static UiElement DictionaryRows<TValue>(
            UiTheme theme,
            Func<IReadOnlyDictionary<string, TValue>> source,
            Func<string, TValue, string> format)
        {
            var keys = new StableKeySet();

            return new DynamicContent()
                .Showing<string[]>(
                    () => keys.CurrentKeys(source()),
                    snapshot =>
                    {
                        var stack = new StackPanel().Vertical(theme.Spacing * 0.5f);
                        foreach (var key in snapshot)
                        {
                            var row = new TextBlock().Foreground(theme.TextMuted);
                            row.BindTextPolled(() => source().TryGetValue(key, out var value)
                                ? format(key, value)
                                : string.Empty);
                            stack.Children.Add(row);
                        }

                        return stack;
                    })
                .Empty(new TextBlock().Text("Nothing yet.").Foreground(theme.TextMuted));
        }

        /// <summary>
        /// Sorted so row order is predictable regardless of dictionary enumeration order, and reused as
        /// long as the actual set of names is unchanged -- <see cref="DynamicContent"/> rebuilds on
        /// reference identity, so a fresh array every poll would rebuild every frame for nothing.
        /// </summary>
        private sealed class StableKeySet
        {
            private string[] _current = Array.Empty<string>();

            public string[] CurrentKeys<TValue>(IReadOnlyDictionary<string, TValue> source)
            {
                if (source.Count == _current.Length)
                {
                    var unchanged = true;
                    foreach (var key in _current)
                    {
                        if (!source.ContainsKey(key))
                        {
                            unchanged = false;
                            break;
                        }
                    }

                    if (unchanged)
                    {
                        return _current;
                    }
                }

                var candidate = source.Keys.ToArray();
                Array.Sort(candidate, StringComparer.Ordinal);
                _current = candidate;
                return _current;
            }
        }
    }
}

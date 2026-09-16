using System.Linq;
using Mana;
using ManaCore.Diagnostics;
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

            var hitchSummary = new TextBlock().FontFamily(theme.FontFamilyMono).Foreground(theme.Text);
            hitchSummary.BindTextPolled(() =>
                $"{ManaFrameProfiler.HitchCount} hitches over {ManaFrameProfiler.HitchThresholdMs:0}ms "
                + $"in {ManaFrameProfiler.FrameCount} frames, worst {ManaFrameProfiler.WorstFrameMs:0.0}ms");

            return new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                // Peak first, and open by default: this is the section that actually answers "why did it
                // stutter", since a hitch is over by the time anyone reads a this-frame number.
                new Expander().Header("Frame timings (peak over last 2s)").Expanded().Section(theme.Spacing,
                    hitchSummary,
                    ProfilerRows(theme)),

                new Expander().Header("Live (this frame)").Section(theme.Spacing,
                    meshCount,
                    DictionaryRows(theme, () => world.LiveFrameStats,
                        key => LiveStatRow(theme, key, () => world.LiveFrameStats))),

                new Expander().Header("Streaming").Section(theme.Spacing,
                    DictionaryRows(theme, () => world.LiveFrameStatTexts,
                        key => StreamingStatusRow(theme, key, () => world.LiveFrameStatTexts))),

                new Expander().Header("Load times").Section(theme.Spacing,
                    LoadTimeRows(theme, world)));
        }

        // Columns every numeric field in this file lines up on. A combined string padded with
        // "{value,width:format}" assumed the font drawing it was genuinely monospace, which held only as
        // long as it resolved to one -- theme.FontFamilyMono ("RobotoMono-Regular") isn't a font this app
        // ships, so it silently fell back to plain (proportional) Roboto, and every digit having a
        // different advance width made these rows visibly jitter as the numbers changed. A fixed-width
        // TextBlock per field can't do that regardless of what font ends up drawing the digits -- the same
        // fix ManaUiPerformanceStrip's status-bar readout needed for exactly the same reason.
        private const float MsColumnWidth = 56.0f;
        private const float KbColumnWidth = 64.0f;

        private static TextBlock NumericField(UiTheme theme, float width) => new TextBlock()
            .FontFamily(theme.FontFamilyMono)
            .FontSize(theme.FontSizeSmall)
            .Foreground(theme.TextMuted)
            .Align(TextAlignment.Right)
            .Width(width);

        /// <summary>
        /// One row per profiler scope, ordered by name rather than by cost. Cost order would be the more
        /// useful reading, but it reshuffles every frame -- and <see cref="DynamicContent"/> rebuilds on key
        /// identity, so a list that reorders is a list that rebuilds every frame. The peak column carries
        /// the ranking instead.
        /// </summary>
        private static UiElement ProfilerRows(UiTheme theme)
        {
            return new DynamicContent()
                .Showing<IReadOnlyList<string>>(
                    () => ManaFrameProfiler.Names,
                    names =>
                    {
                        var stack = new StackPanel().Vertical(theme.Spacing * 0.5f);
                        stack.Children.Add(ProfilerHeaderRow(theme));
                        foreach (var name in names)
                        {
                            stack.Children.Add(ProfilerRow(theme, name));
                        }

                        return stack;
                    })
                .Empty(new TextBlock().Text("Nothing measured yet.").Foreground(theme.TextMuted));
        }

        private static UiElement ProfilerHeaderRow(UiTheme theme)
        {
            TextBlock Label(string text, float width) => new TextBlock()
                .Text(text)
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextDisabled)
                .Align(TextAlignment.Right)
                .Width(width);

            return new Grid().Columns($"{MsColumnWidth}, {MsColumnWidth}, {MsColumnWidth}, {KbColumnWidth}, *", 6.0f)
                .Rows("Auto")
                .Children(
                    Label("Peak", MsColumnWidth).GridColumn(0),
                    Label("Now", MsColumnWidth).GridColumn(1),
                    Label("Avg", MsColumnWidth).GridColumn(2),
                    Label("KB", KbColumnWidth).GridColumn(3),
                    new TextBlock().Text("Scope").FontSize(theme.FontSizeSmall).Foreground(theme.TextDisabled).GridColumn(4));
        }

        // Peak leads because it is the number that catches a hitch. One binding (Peak) drives the sample
        // lookup and caches the other columns' text; they just read what it last found, the same
        // one-driver-many-readers split ManaUiPerformanceStrip uses for its own per-frame sampling.
        private static UiElement ProfilerRow(UiTheme theme, string name)
        {
            var peak = NumericField(theme, MsColumnWidth);
            var now = NumericField(theme, MsColumnWidth);
            var avg = NumericField(theme, MsColumnWidth);
            var kb = NumericField(theme, KbColumnWidth);
            var label = new TextBlock()
                .FontFamily(theme.FontFamilyMono)
                .FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextMuted)
                .Ellipsis();

            var nowText = string.Empty;
            var avgText = string.Empty;
            var kbText = string.Empty;
            var labelText = name;

            peak.BindTextPolled(() =>
            {
                if (!ManaFrameProfiler.TryGetSample(name, out var sample))
                {
                    nowText = avgText = kbText = string.Empty;
                    labelText = name;
                    return string.Empty;
                }

                nowText = $"{sample.LastMs:F2}";
                avgText = $"{sample.AverageMs:F2}";
                kbText = $"{sample.PeakKb:F0}";
                labelText = sample.LastCalls > 1 ? $"{name} x{sample.LastCalls}" : name;
                return $"{sample.PeakMs:F2}";
            });

            now.BindTextPolled(() => nowText);
            avg.BindTextPolled(() => avgText);
            kb.BindTextPolled(() => kbText);
            label.BindTextPolled(() => labelText);

            return new Grid().Columns($"{MsColumnWidth}, {MsColumnWidth}, {MsColumnWidth}, {KbColumnWidth}, *", 6.0f)
                .Rows("Auto")
                .Children(
                    peak.GridColumn(0),
                    now.GridColumn(1),
                    avg.GridColumn(2),
                    kb.GridColumn(3),
                    label.GridColumn(4));
        }

        private static UiElement LoadTimeRows(UiTheme theme, ManaWorld world)
        {
            // A one-time history from startup -- built once rather than polled, since nothing appends to
            // it once the load screen is gone, so a fixed-width column here is about alignment rather than
            // jitter (the numbers never change once drawn).
            var stack = new StackPanel().Vertical(theme.Spacing * 0.5f);
            foreach (var perf in world.PerformanceLogs)
            {
                stack.Children.Add(new Grid().Columns($"{MsColumnWidth}, *", 6.0f).Rows("Auto").Children(
                    new TextBlock()
                        .Text($"{perf.Duration.Milliseconds}")
                        .FontFamily(theme.FontFamilyMono)
                        .Foreground(theme.TextMuted)
                        .Align(TextAlignment.Right)
                        .Width(MsColumnWidth)
                        .GridColumn(0),
                    new TextBlock().Text(perf.Name).Foreground(theme.TextMuted).GridColumn(1)));
            }

            return stack;
        }

        /// <summary>Duration, right-aligned in a fixed column, then the system name.</summary>
        private static UiElement LiveStatRow(
            UiTheme theme, string key, Func<IReadOnlyDictionary<string, TimeSpan>> source)
        {
            var duration = NumericField(theme, MsColumnWidth);
            var label = new TextBlock().Foreground(theme.TextMuted).Ellipsis();

            duration.BindTextPolled(() =>
            {
                if (!source().TryGetValue(key, out var value))
                {
                    label.Text = string.Empty;
                    return string.Empty;
                }

                label.Text = key;
                return $"{value.TotalMilliseconds:F2}ms";
            });

            return new Grid().Columns($"{MsColumnWidth}, *", 6.0f).Rows("Auto").Children(
                duration.GridColumn(0),
                label.GridColumn(1));
        }

        /// <summary>System name in a fixed column, then whatever status text it last pushed.</summary>
        private static UiElement StreamingStatusRow(
            UiTheme theme, string key, Func<IReadOnlyDictionary<string, string>> source)
        {
            const float NameColumnWidth = 90.0f;

            var name = new TextBlock().Text(key).Foreground(theme.TextMuted).Ellipsis().Width(NameColumnWidth);
            var value = new TextBlock().Foreground(theme.TextMuted);
            value.BindTextPolled(() => source().TryGetValue(key, out var text) ? text : string.Empty);

            return new Grid().Columns($"{NameColumnWidth}, *", 6.0f).Rows("Auto").Children(
                name.GridColumn(0),
                value.GridColumn(1));
        }

        /// <summary>
        /// One row per key currently in <paramref name="source"/>, rebuilt only when the key set itself
        /// changes -- not on every poll, which would throw a live row's binding away every frame just to
        /// build the same row again.
        /// </summary>
        private static UiElement DictionaryRows<TValue>(
            UiTheme theme,
            Func<IReadOnlyDictionary<string, TValue>> source,
            Func<string, UiElement> buildRow)
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
                            stack.Children.Add(buildRow(key));
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

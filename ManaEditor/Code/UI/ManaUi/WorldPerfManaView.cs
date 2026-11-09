using System.Linq;
using Mana;
using ManaCore.Diagnostics;
using ManaUI;

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>
    /// The World Perf panel: how fast the frame is (fps, frame time, hitches) and where the time goes -- the profiler's
    /// scopes grouped by system, one line each, with a bar for their share of a 120 Hz frame and colour for what is
    /// heavy -- then the ManaUI panels' own cost, live per-system stats, streaming status and load times.
    /// <para>
    /// Rows are rebuilt only when the set of scope names changes; each row polls its own numbers. Nested scopes
    /// overlap (a pass inside "Frame: Render" counts in both), so rows are not meant to add up.
    /// </para>
    /// </summary>
    public static class WorldPerfManaView
    {
        // A 120 Hz frame: what the bars measure against, and where colour turns.
        private const double BudgetMs = 1000.0 / 120.0;
        private const string PanelPrefix = "ManaUI panel: ";

        private const float BarWidth = 70.0f;
        private const float MsWidth = 46.0f;
        private const float KbWidth = 54.0f;

        public static UiElement Build(UiTheme theme, ManaWorld world)
        {
            var meshCount = new TextBlock().Foreground(theme.TextMuted).FontSize(theme.FontSizeSmall);
            meshCount.BindTextPolled(() => $"Meshes rendered: {world.LastMeshCount}");

            return new StackPanel().Vertical(theme.Spacing).Padding(8.0f).Children(
                Summary(theme),
                meshCount,
                new Expander().Header("Frame timings · avg over 120 frames").Expanded().Section(theme.Spacing,
                    FrameRows(theme)),
                new Expander().Header("ManaUI panels · CPU").Section(theme.Spacing,
                    new TextBlock().Text("Average ms per phase; allocation at its peak frame.")
                        .FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted),
                    PanelRows(theme)),
                new Expander().Header("Live (this frame)").Section(theme.Spacing,
                    DictionaryRows(theme, () => world.LiveFrameStats,
                        key => LiveStatRow(theme, key, () => world.LiveFrameStats))),
                new Expander().Header("Streaming").Section(theme.Spacing,
                    DictionaryRows(theme, () => world.LiveFrameStatTexts,
                        key => StreamingStatusRow(theme, key, () => world.LiveFrameStatTexts))),
                new Expander().Header("Load times").Section(theme.Spacing,
                    LoadTimeRows(theme, world)));
        }

        // ----- summary ----------------------------------------------------------------------------------

        private static UiElement Summary(UiTheme theme)
        {
            var reset = new ManaUI.Button().Label("Reset").Variant(ButtonVariant.Subtle).OnClick(ManaFrameProfiler.Reset);
            reset.WithTooltip("Clear the worst frame, hitch count and every scope's history -- after loading, say.");

            return new Grid().Columns("*, *, *, *, Auto", 6.0f).Rows("Auto").Children(
                Stat(theme, "fps", () =>
                {
                    var ms = ManaFrameProfiler.AverageFrameMs;
                    if (ms <= 0.0)
                    {
                        return ("-", theme.TextMuted);
                    }
                    return ($"{1000.0 / ms:0}", FrameColor(theme, ms));
                }).GridColumn(0),
                Stat(theme, "frame ms", () => ($"{ManaFrameProfiler.AverageFrameMs:0.0}", FrameColor(theme, ManaFrameProfiler.AverageFrameMs))).GridColumn(1),
                Stat(theme, "worst ms", () => ($"{ManaFrameProfiler.WorstFrameMs:0}", FrameColor(theme, ManaFrameProfiler.WorstFrameMs))).GridColumn(2),
                Stat(theme, $"hitches > {ManaFrameProfiler.HitchThresholdMs:0} ms", () =>
                {
                    var color = theme.Success;
                    if (ManaFrameProfiler.HitchCount > 0)
                    {
                        color = theme.Warning;
                    }
                    return ($"{ManaFrameProfiler.HitchCount}", color);
                }).GridColumn(3),
                reset.CenterV().GridColumn(4));
        }

        private static UiElement Stat(UiTheme theme, string caption, Func<(string Text, Vector4 Color)> read)
        {
            var color = theme.Text;
            var value = new TextBlock().FontFamily(theme.FontFamilyMedium).FontSize(theme.FontSize * 1.6f);
            value.BindTextPolled(() =>
            {
                var (text, now) = read();
                color = now;
                return text;
            });
            value.BindPolled(t => t.Foreground, () => (Vector4?)color);
            return new Border()
                .Fill(theme.SurfaceRaised)
                .Rounded(6.0f)
                .PaddingOf(8.0f, 4.0f)
                .Content(new StackPanel().Vertical(0.0f).Children(
                    value,
                    new TextBlock().Text(caption).FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted)));
        }

        // Green inside a 120 Hz frame, amber inside 60 Hz, red past it.
        private static Vector4 FrameColor(UiTheme theme, double ms)
        {
            if (ms <= BudgetMs * 1.05)
            {
                return theme.Success;
            }
            if (ms <= BudgetMs * 2.1)
            {
                return theme.Warning;
            }
            return theme.Danger;
        }

        // A scope's colour by how much of the frame it takes: quiet below a millisecond, amber to a quarter of the
        // budget, red beyond.
        private static Vector4 CostColor(UiTheme theme, double ms)
        {
            if (ms < 0.5)
            {
                return theme.TextDisabled;
            }
            if (ms < 1.0)
            {
                return theme.TextMuted;
            }
            if (ms < BudgetMs * 0.25)
            {
                return theme.Text;
            }
            if (ms < BudgetMs * 0.5)
            {
                return theme.Warning;
            }
            return theme.Danger;
        }

        // ----- frame timings -----------------------------------------------------------------------------

        /// <summary>The system a scope belongs to: the part before its colon ("GPU: Draw Meshes"), or its first word.</summary>
        private static string GroupOf(string name)
        {
            var colon = name.IndexOf(':');
            if (colon > 0)
            {
                return name[..colon];
            }
            var space = name.IndexOf(' ');
            if (space > 0)
            {
                return name[..space];
            }
            return name;
        }

        private static string ShortName(string name)
        {
            var colon = name.IndexOf(':');
            if (colon > 0)
            {
                return name[(colon + 1)..].Trim();
            }
            return name;
        }

        private static UiElement FrameRows(UiTheme theme)
        {
            return new DynamicContent()
                .Showing<IReadOnlyList<string>>(
                    () => ManaFrameProfiler.Names,
                    names =>
                    {
                        var stack = new StackPanel().Vertical(2.0f);
                        stack.Children.Add(HeaderRow(theme, "", "share of frame", "avg", "peak", "peak KB"));
                        var groups = names
                            .Where(name => !name.StartsWith(PanelPrefix, StringComparison.Ordinal))
                            .GroupBy(GroupOf)
                            .OrderBy(group => GroupOrder(group.Key))
                            .ThenBy(group => group.Key, StringComparer.Ordinal);
                        foreach (var group in groups)
                        {
                            stack.Children.Add(new TextBlock().Text(group.Key)
                                .FontFamily(theme.FontFamilyMedium).Foreground(theme.Accent)
                                .Margin(0.0f, 6.0f, 0.0f, 2.0f));
                            var alternate = false;
                            foreach (var name in group)
                            {
                                stack.Children.Add(ScopeRow(theme, name, alternate));
                                alternate = !alternate;
                            }
                        }
                        return stack;
                    })
                .Empty(new TextBlock().Text("Nothing measured yet.").Foreground(theme.TextMuted));
        }

        // The frame's own scopes first, then the GPU's, then the rest by name.
        private static int GroupOrder(string group) => group switch
        {
            "Frame" => 0,
            "GPU" => 1,
            "Scene" => 2,
            "World" => 3,
            _ => 4
        };

        private static UiElement HeaderRow(UiTheme theme, string first, string bar, string avg, string peak, string kb)
        {
            TextBlock Label(string text) => new TextBlock().Text(text).FontSize(theme.FontSizeSmall)
                .Foreground(theme.TextDisabled).Align(TextAlignment.Right);
            return new Grid().Columns($"*, {BarWidth}, {MsWidth}, {MsWidth}, {KbWidth}", 6.0f).Rows("Auto").Children(
                new TextBlock().Text(first).FontSize(theme.FontSizeSmall).Foreground(theme.TextDisabled).GridColumn(0),
                new TextBlock().Text(bar).FontSize(theme.FontSizeSmall).Foreground(theme.TextDisabled).GridColumn(1),
                Label(avg).GridColumn(2),
                Label(peak).GridColumn(3),
                Label(kb).GridColumn(4));
        }

        private static TextBlock Number(UiTheme theme, float width) => new TextBlock()
            .FontFamily(theme.FontFamilyMono)
            .FontSize(theme.FontSizeSmall)
            .Align(TextAlignment.Right)
            .Width(width);

        // One line: name (with its call count), a bar of its average against the frame budget, avg, peak and
        // allocation. One binding reads the sample; the rest show what it found.
        private static UiElement ScopeRow(UiTheme theme, string name, bool alternate)
        {
            var label = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.Text).Ellipsis();
            var avg = Number(theme, MsWidth);
            var peak = Number(theme, MsWidth);
            var kb = Number(theme, KbWidth);
            var shortName = ShortName(name);
            var labelText = shortName;
            var peakText = string.Empty;
            var kbText = string.Empty;
            var avgColor = theme.TextMuted;
            var peakColor = theme.TextMuted;
            var kbColor = theme.TextMuted;
            var share = 0.0f;
            var barColor = theme.Accent;

            avg.BindTextPolled(() =>
            {
                if (!ManaFrameProfiler.TryGetSample(name, out var sample))
                {
                    peakText = kbText = string.Empty;
                    share = 0.0f;
                    return string.Empty;
                }

                labelText = shortName;
                if (sample.LastCalls > 1)
                {
                    labelText = $"{shortName}  ×{sample.LastCalls}";
                }
                peakText = $"{sample.PeakMs:F2}";
                kbText = $"{sample.PeakKb:F0}";
                avgColor = CostColor(theme, sample.AverageMs);
                peakColor = CostColor(theme, sample.PeakMs * 0.5);
                kbColor = theme.TextDisabled;
                if (sample.PeakKb >= 1024)
                {
                    kbColor = theme.Warning;
                }
                share = (float)Math.Clamp(sample.AverageMs / BudgetMs, 0.0, 1.0);
                // Accent while it is a small share; the warning colours once it is a real part of the frame.
                barColor = theme.Accent;
                if (sample.AverageMs >= BudgetMs * 0.25)
                {
                    barColor = CostColor(theme, sample.AverageMs);
                }
                return $"{sample.AverageMs:F2}";
            });
            avg.BindPolled(t => t.Foreground, () => (Vector4?)avgColor);
            label.BindTextPolled(() => labelText);
            peak.BindTextPolled(() => peakText);
            peak.BindPolled(t => t.Foreground, () => (Vector4?)peakColor);
            kb.BindTextPolled(() => kbText);
            kb.BindPolled(t => t.Foreground, () => (Vector4?)kbColor);

            var row = new Grid().Columns($"*, {BarWidth}, {MsWidth}, {MsWidth}, {KbWidth}", 6.0f).Rows("Auto").Children(
                label.CenterV().GridColumn(0),
                new ShareBar(() => share, () => barColor, theme.Divider).CenterV().GridColumn(1),
                avg.GridColumn(2),
                peak.GridColumn(3),
                kb.GridColumn(4));

            var background = UiColor.Transparent;
            if (alternate)
            {
                background = theme.RowAlternate;
            }
            return new Border().Fill(background).Rounded(3.0f).PaddingOf(4.0f, 1.0f).Content(row);
        }

        /// <summary>A thin bar: a track, filled to a share in a colour, both read each frame.</summary>
        private sealed class ShareBar(Func<float> share, Func<Vector4> color, Vector4 track) : UiElement
        {
            protected override Vector2 MeasureOverride(Vector2 availableSize) => new(BarWidth, 8.0f);

            protected override void OnRender(ManaUiDrawList drawList)
            {
                var bounds = Bounds;
                drawList.FillRect(bounds, track, new CornerRadius(3.0f));
                var fraction = Math.Clamp(share(), 0.0f, 1.0f);
                if (fraction > 0.0f)
                {
                    var width = MathF.Max(2.0f, bounds.Width * fraction);
                    drawList.FillRect(new UiRect(bounds.X, bounds.Y, width, bounds.Height), color(), new CornerRadius(3.0f));
                }
            }
        }

        // ----- ManaUI panels ----------------------------------------------------------------------------

        private static readonly string[] Phases = ["Bindings", "Measure", "Arrange", "Record"];

        private static UiElement PanelRows(UiTheme theme)
        {
            return new DynamicContent()
                .Showing<IReadOnlyList<string>>(
                    () => ManaFrameProfiler.Names,
                    names =>
                    {
                        var panels = names
                            .Where(name => name.StartsWith(PanelPrefix, StringComparison.Ordinal))
                            .Select(name => name[PanelPrefix.Length..])
                            .Select(rest => rest.Split(" / ")[0])
                            .Distinct()
                            .OrderBy(panel => panel, StringComparer.Ordinal)
                            .ToList();
                        var stack = new StackPanel().Vertical(2.0f);
                        var header = new Grid().Columns($"*, {MsWidth}, {MsWidth}, {MsWidth}, {MsWidth}, {KbWidth}", 6.0f).Rows("Auto");
                        header.Children.Add(new TextBlock().Text("panel").FontSize(theme.FontSizeSmall).Foreground(theme.TextDisabled).GridColumn(0));
                        for (var i = 0; i < Phases.Length; i++)
                        {
                            header.Children.Add(new TextBlock().Text(Phases[i].ToLowerInvariant()).FontSize(theme.FontSizeSmall)
                                .Foreground(theme.TextDisabled).Align(TextAlignment.Right).GridColumn(i + 1));
                        }
                        header.Children.Add(new TextBlock().Text("peak KB").FontSize(theme.FontSizeSmall)
                            .Foreground(theme.TextDisabled).Align(TextAlignment.Right).GridColumn(5));
                        stack.Children.Add(header);
                        var alternate = false;
                        foreach (var panel in panels)
                        {
                            stack.Children.Add(PanelRow(theme, panel, alternate));
                            alternate = !alternate;
                        }
                        return stack;
                    })
                .Empty(new TextBlock().Text("Nothing measured yet.").Foreground(theme.TextMuted));
        }

        private static UiElement PanelRow(UiTheme theme, string panel, bool alternate)
        {
            var row = new Grid().Columns($"*, {MsWidth}, {MsWidth}, {MsWidth}, {MsWidth}, {KbWidth}", 6.0f).Rows("Auto");
            row.Children.Add(new TextBlock().Text(panel).FontSize(theme.FontSizeSmall).Foreground(theme.Text).Ellipsis().CenterV().GridColumn(0));
            var peakKb = 0.0;
            for (var i = 0; i < Phases.Length; i++)
            {
                var scope = PanelPrefix + panel + " / " + Phases[i];
                var first = i == 0;
                var cell = Number(theme, MsWidth);
                var color = theme.TextMuted;
                cell.BindTextPolled(() =>
                {
                    if (first)
                    {
                        peakKb = 0.0;
                    }
                    if (!ManaFrameProfiler.TryGetSample(scope, out var sample))
                    {
                        color = theme.TextDisabled;
                        return "-";
                    }
                    peakKb = Math.Max(peakKb, sample.PeakKb);
                    color = CostColor(theme, sample.AverageMs * 4.0);
                    return $"{sample.AverageMs:F2}";
                });
                cell.BindPolled(t => t.Foreground, () => (Vector4?)color);
                row.Children.Add(cell.GridColumn(i + 1));
            }
            var kb = Number(theme, KbWidth);
            kb.BindTextPolled(() => $"{peakKb:F0}");
            kb.BindPolled(t => t.Foreground, () =>
            {
                if (peakKb >= 1024)
                {
                    return (Vector4?)theme.Warning;
                }
                return (Vector4?)theme.TextDisabled;
            });
            row.Children.Add(kb.GridColumn(5));

            var background = UiColor.Transparent;
            if (alternate)
            {
                background = theme.RowAlternate;
            }
            return new Border().Fill(background).Rounded(3.0f).PaddingOf(4.0f, 1.0f).Content(row);
        }

        // ----- live, streaming, load times ----------------------------------------------------------------

        private static UiElement LoadTimeRows(UiTheme theme, ManaWorld world)
        {
            // A one-time history from startup: built once, since nothing appends to it after loading.
            var stack = new StackPanel().Vertical(theme.Spacing * 0.5f);
            foreach (var perf in world.PerformanceLogs)
            {
                stack.Children.Add(new Grid().Columns($"{MsWidth + 16.0f}, *", 6.0f).Rows("Auto").Children(
                    Number(theme, MsWidth + 16.0f).Text($"{perf.Duration.TotalMilliseconds:F2}").Foreground(theme.TextMuted).GridColumn(0),
                    new TextBlock().Text(perf.Name).FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).GridColumn(1)));
            }

            return stack;
        }

        /// <summary>Duration, right-aligned in a fixed column, then the system name.</summary>
        private static UiElement LiveStatRow(
            UiTheme theme, string key, Func<IReadOnlyDictionary<string, TimeSpan>> source)
        {
            var duration = Number(theme, MsWidth + 16.0f).Foreground(theme.TextMuted);
            var label = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Ellipsis();

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

            return new Grid().Columns($"{MsWidth + 16.0f}, *", 6.0f).Rows("Auto").Children(
                duration.GridColumn(0),
                label.GridColumn(1));
        }

        /// <summary>System name in a fixed column, then whatever status text it last pushed.</summary>
        private static UiElement StreamingStatusRow(
            UiTheme theme, string key, Func<IReadOnlyDictionary<string, string>> source)
        {
            const float NameColumnWidth = 90.0f;

            var name = new TextBlock().Text(key).FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted).Ellipsis().Width(NameColumnWidth);
            var value = new TextBlock().FontSize(theme.FontSizeSmall).Foreground(theme.TextMuted);
            value.BindTextPolled(() =>
            {
                if (source().TryGetValue(key, out var text))
                {
                    return text;
                }
                return string.Empty;
            });

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

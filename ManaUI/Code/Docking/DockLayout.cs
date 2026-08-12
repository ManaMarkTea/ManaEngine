using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManaUI
{
    /// <summary>
    /// Serialisable snapshot of a dock tree. Panels are referenced by <see cref="DockItem.Id"/> rather than
    /// captured, because content is application-owned and cannot be reconstructed from a file -- the app
    /// supplies a factory to turn an id back into a panel on load.
    /// </summary>
    public sealed class DockLayoutNode
    {
        /// <summary>
        /// The application's layout version, written at the root node only.
        /// <para>
        /// Owned by the application, not this class: it means "the panel set or default arrangement this
        /// file was written against", so the app can tell an out-of-date layout from a current one and
        /// decide what to do about it. Without it, a panel added after a layout was saved can never appear
        /// for anyone who has run the app before.
        /// </para>
        /// </summary>
        [JsonPropertyName("version")]
        public int LayoutVersion { get; set; }

        /// <summary>"split" or "tabs".</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; } = "tabs";

        [JsonPropertyName("orientation")]
        public Orientation Orientation { get; set; }

        [JsonPropertyName("weights")]
        public List<float>? Weights { get; set; }

        [JsonPropertyName("children")]
        public List<DockLayoutNode>? Children { get; set; }

        /// <summary>Ids of the panels in a tab group, in tab order.</summary>
        [JsonPropertyName("items")]
        public List<string>? Items { get; set; }

        [JsonPropertyName("selected")]
        public int SelectedIndex { get; set; }
    }

    /// <summary>Saves and restores dock layouts.</summary>
    public static class DockLayout
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Captures the current layout, or null when the host is empty.</summary>
        public static DockLayoutNode? Capture(DockHost host, int layoutVersion = 0)
        {
            ArgumentNullException.ThrowIfNull(host);

            var captured = CaptureNode(host.RootNode);
            if (captured != null)
            {
                captured.LayoutVersion = layoutVersion;
            }

            return captured;
        }

        private static DockLayoutNode? CaptureNode(DockNode? node)
        {
            switch (node)
            {
                case DockTabGroup group:
                {
                    var items = new List<string>();
                    var selected = 0;

                    for (var i = 0; i < group.Items.Count; i++)
                    {
                        var item = group.Items[i];

                        // An item with no id cannot be restored, so it is not recorded either -- writing it
                        // would produce a layout that silently loses a panel on load.
                        if (string.IsNullOrEmpty(item.Id))
                        {
                            continue;
                        }

                        // Recorded as an index into what is being written, not into the live group. Skipping
                        // an id-less tab shifts everything after it, and clamping the live index instead
                        // would restore the wrong panel.
                        if (i == group.SelectedIndex)
                        {
                            selected = items.Count;
                        }

                        items.Add(item.Id);
                    }

                    return items.Count == 0
                        ? null
                        : new DockLayoutNode
                        {
                            Type = "tabs",
                            Items = items,
                            SelectedIndex = selected
                        };
                }
                case DockSplit split:
                {
                    var children = new List<DockLayoutNode>();
                    var weights = new List<float>();

                    for (var i = 0; i < split.Children.Count; i++)
                    {
                        var captured = CaptureNode(split.Children[i] as DockNode);
                        if (captured == null)
                        {
                            continue;
                        }

                        children.Add(captured);
                        weights.Add(i < split.Weights.Count ? split.Weights[i] : 1.0f);
                    }

                    return children.Count switch
                    {
                        0 => null,

                        // A split that lost all but one child is not worth recording as a split.
                        1 => children[0],
                        _ => new DockLayoutNode
                        {
                            Type = "split",
                            Orientation = split.Orientation,
                            Children = children,
                            Weights = weights
                        }
                    };
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Rebuilds a layout, resolving each recorded id through <paramref name="resolve"/>.
        /// <para>
        /// Ids that no longer resolve are skipped rather than treated as an error: a saved layout outlives
        /// the code that produced it, and a renamed or removed panel must not stop the rest of the layout
        /// from loading.
        /// </para>
        /// </summary>
        /// <returns>False when nothing could be restored, leaving the host untouched.</returns>
        public static bool Apply(DockHost host, DockLayoutNode? layout, Func<string, DockItem?> resolve)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(resolve);

            var rebuilt = layout == null ? null : Build(layout, resolve);
            if (rebuilt == null)
            {
                return false;
            }

            host.RootNode = rebuilt;
            host.Prune();
            return host.RootNode != null;
        }

        private static DockNode? Build(DockLayoutNode layout, Func<string, DockItem?> resolve)
        {
            if (layout.Type == "split" && layout.Children is { Count: > 0 })
            {
                var split = new DockSplit(layout.Orientation);
                var restoredWeights = new List<float>();

                for (var i = 0; i < layout.Children.Count; i++)
                {
                    var child = Build(layout.Children[i], resolve);
                    if (child != null)
                    {
                        split.Add(child);
                        restoredWeights.Add(layout.Weights != null && i < layout.Weights.Count
                            ? layout.Weights[i]
                            : 1.0f);
                    }
                }

                // Applied in one go once every pane exists. Passing weights to Add would renormalise after
                // each insertion and drift away from the saved proportions.
                split.SetWeights(restoredWeights);

                return split.Children.Count switch
                {
                    0 => null,
                    1 => Detach(split, split.Children[0] as DockNode),
                    _ => split
                };
            }

            if (layout.Items is not { Count: > 0 })
            {
                return null;
            }

            var group = new DockTabGroup();
            foreach (var id in layout.Items)
            {
                var item = resolve(id);
                if (item != null)
                {
                    group.Add(item);
                }
            }

            if (group.Items.Count == 0)
            {
                return null;
            }

            group.SelectedIndex = Math.Clamp(layout.SelectedIndex, 0, group.Items.Count - 1);
            return group;
        }

        /// <summary>Lifts a lone child out of a split that no longer needs to exist.</summary>
        private static DockNode? Detach(DockSplit split, DockNode? child)
        {
            if (child != null)
            {
                split.Remove(child);
            }

            return child;
        }

        public static string ToJson(DockLayoutNode? layout)
        {
            return JsonSerializer.Serialize(layout, SerializerOptions);
        }

        public static DockLayoutNode? FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<DockLayoutNode>(json, SerializerOptions);
            }
            catch (JsonException)
            {
                // A corrupt layout file must fall back to the default layout, not crash startup.
                return null;
            }
        }

        public static void Save(DockHost host, string path, int layoutVersion = 0)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, ToJson(Capture(host, layoutVersion)));
        }

        /// <summary>Loads a layout file. Returns false when it is missing, corrupt or unusable.</summary>
        public static bool Load(DockHost host, string path, Func<string, DockItem?> resolve)
        {
            return Load(host, path, resolve, out _);
        }

        /// <summary>
        /// Loads a layout file, reporting the version it was written against so the caller can tell a
        /// current layout from one that predates the panels it now has.
        /// </summary>
        public static bool Load(DockHost host, string path, Func<string, DockItem?> resolve, out int layoutVersion)
        {
            layoutVersion = 0;

            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var layout = FromJson(File.ReadAllText(path));
                layoutVersion = layout?.LayoutVersion ?? 0;
                return Apply(host, layout, resolve);
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}

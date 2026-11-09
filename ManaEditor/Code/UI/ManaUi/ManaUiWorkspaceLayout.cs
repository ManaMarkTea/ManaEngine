using System.Text.Json;
using System.Text.Json.Serialization;
using ManaUI;
#if MANA_BROWSER
using ManaCore.Browser;
#endif

namespace ManaEditor.Code.UI.ManaUi
{
    /// <summary>Starting arrangements offered in the Layout menu, named after the tools they mimic. Not modes -- everything done afterward saves over the top.</summary>
    public enum WorkspacePreset
    {
        /// <summary>Everything in one column on the right, closest to how the panels started out.</summary>
        RightColumn,

        /// <summary>Tools left; outliner above inspector on the right, the way Unity stacks them.</summary>
        Unity,

        /// <summary>Tools left; outliner top-right with the details panel filling the space beneath it.</summary>
        Unreal,

        /// <summary>Outliner on the far left, inspector on the far right, viewport clear between them.</summary>
        Split
    }

    /// <summary>One floating panel: which it is, and the frame it was left in.</summary>
    public sealed class FloatingPanelLayout
    {
        public bool Desktop { get; set; }
        public int HomeHostIndex { get; set; }
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// What a native window holds when it holds more than the one panel <see cref="Id"/> names. Null for a
        /// single panel, which keeps older files -- and files this build writes for the common case -- readable.
        /// </summary>
        public DockLayoutNode? Layout { get; set; }

        /// <summary>Home host index per panel id, for a <see cref="Layout"/> whose panels came from different columns.</summary>
        public Dictionary<string, int>? ItemHomes { get; set; }

        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }
    }

    /// <summary>The persisted workspace: the columns, their widths, and which preset they came from.</summary>
    public sealed class ManaUiWorkspaceLayout
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Matches the panel set, so an older file has new panels merged into it.</summary>
        public int Version { get; set; }

        public WorkspacePreset Preset { get; set; } = WorkspacePreset.RightColumn;

        /// <summary>Dark or light. Kept with the rest of the workspace rather than a separate settings file.</summary>
        public UiThemeKind Theme { get; set; } = UiThemeKind.Dark;

        /// <summary>UI font family, or null for the theme's default.</summary>
        public string? FontFamily { get; set; }

        /// <summary>Backdrop blur radius, so a workspace opens looking the way it was left.</summary>
        public float BackdropBlur { get; set; }

        public float LeftWidth { get; set; }

        public float RightWidth { get; set; }

        public float BottomHeight { get; set; }

        public DockLayoutNode? Left { get; set; }

        public DockLayoutNode? Right { get; set; }

        /// <summary>The strip under the menu, dedicated to the editor toolbar.</summary>
        public DockLayoutNode? Top { get; set; }

        /// <summary>The strip along the bottom, dedicated to Import -- spans the left column and the viewport, not the right column.</summary>
        public DockLayoutNode? Bottom { get; set; }

        /// <summary>Panels torn out of the columns and left floating, held separately since they belong to neither.</summary>
        public List<FloatingPanelLayout> Floating { get; set; } = new();

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, SerializerOptions);
        }

        public static ManaUiWorkspaceLayout? FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<ManaUiWorkspaceLayout>(json, SerializerOptions);
            }
            catch (JsonException)
            {
                // A corrupt workspace falls back to a preset rather than taking startup down with it.
                return null;
            }
        }

        public static ManaUiWorkspaceLayout? Load(string path)
        {
            try
            {
#if MANA_BROWSER
                // The wasm virtual filesystem is wiped on every page reload, so a layout written there
                // would never actually be seen again -- localStorage is the one thing on this target that
                // survives a reload. Keyed by filename, not the full path: the path is a desktop
                // filesystem location (SharedSettingsFilePath/IniFilePath under LocalApplicationData) that
                // means nothing here, but the filename alone ("manaui-dock.json") is still a stable,
                // unique-enough key across the one workspace file this project ever saves.
                var json = ManaBrowserSettingsBridge.TryGet(Path.GetFileName(path));
                return string.IsNullOrEmpty(json) ? null : FromJson(json);
#else
                return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
#endif
            }
            catch (IOException)
            {
                return null;
            }
        }

        public void Save(string path)
        {
            try
            {
#if MANA_BROWSER
                ManaBrowserSettingsBridge.Set(Path.GetFileName(path), ToJson());
#else
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, ToJson());
#endif
            }
            catch (IOException)
            {
                // A layout that cannot be written is not worth interrupting the editor for.
            }
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using ManaUI;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The editor arrangements offered in the Layout menu, named after the tools they mimic.
    /// <para>
    /// These are starting points, not modes. Picking one rebuilds the columns; everything afterwards --
    /// dragging a tab out, splitting a pane, resizing a column -- is saved over the top of it, and the
    /// preset is only reapplied when another one is chosen.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// The persisted workspace: both columns, their widths, and which preset they came from.
    /// <para>
    /// A wrapper around two <see cref="DockLayoutNode"/> trees rather than an extension of them, because
    /// column widths and the preset belong to the workspace rather than to any one dock host.
    /// </para>
    /// </summary>
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

        public float LeftWidth { get; set; }

        public float RightWidth { get; set; }

        public DockLayoutNode? Left { get; set; }

        public DockLayoutNode? Right { get; set; }

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
                return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null;
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
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, ToJson());
            }
            catch (IOException)
            {
                // A layout that cannot be written is not worth interrupting the editor for.
            }
        }
    }
}

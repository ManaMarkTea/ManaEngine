using Mana.Code.Content;

namespace ManaEditor.Code.Assets
{
    /// <summary>
    /// One kind of user-made asset: what it is called, its file extension, the Content folder new ones go in, the
    /// short badge its tile shows without a picture, whether a picture can be drawn from it, and -- for a kind that
    /// can be made from nothing -- a blank one.
    /// </summary>
    public sealed record ManaContentKind(string Name, string Extension, string Folder, string Badge, bool HasThumbnail, Func<string, object>? Blank = null)
    {
        public bool CanCreate => Blank != null;
    }

    /// <summary>
    /// Every kind of asset the Project panel knows. Adding a kind is one line here -- the panel lists it, filters by
    /// it and offers New for it; its editor and its placing are wired by its name.
    /// </summary>
    public static class ManaContentKinds
    {
        public static readonly ManaContentKind Prefab = new("Prefab", ManaPrefab.Extension, "Prefabs", "PREFAB", true);
        public static readonly ManaContentKind GridMapPreset = new("Grid Map Preset", ".managridpreset", "GridMapPresets", "GRID", false);
        public static readonly ManaContentKind Generator = new("Generator", ".manawfc", "Generators", "WFC", false);
        public static readonly ManaContentKind SpriteSheet = new("Sprite Sheet", ManaSpriteSheet.Extension, "Sprites", "SHEET", true,
            name => new ManaSpriteSheet { Name = name, Animations = [new ManaSpriteAnimation { Name = "idle", Frames = [0] }] });
        public static readonly ManaContentKind Slice = new("9-Slice", ManaSpriteSlice.Extension, "Sprites", "SLICE", true,
            name => new ManaSpriteSlice { Name = name });

        public static readonly ManaContentKind UiLayout = new("UI Layout", ManaUI.UiMarkup.Extension, "UI", "UI", false,
            name => ManaEditor.Code.UI.ManaUi.UiDesignerDocument.NewDocument(name));

        public static IReadOnlyList<ManaContentKind> All { get; } = [Prefab, SpriteSheet, Slice, GridMapPreset, Generator, UiLayout];

        public static ManaContentKind? ForPath(string path) =>
            All.FirstOrDefault(kind => string.Equals(Path.GetExtension(path), kind.Extension, StringComparison.OrdinalIgnoreCase));

        public static ManaContentKind? Named(string name) => All.FirstOrDefault(kind => kind.Name == name);
    }
}

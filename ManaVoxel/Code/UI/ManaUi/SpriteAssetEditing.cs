using System.Text.Json;
using Mana;
using Mana.Code.Content;

namespace ManaVoxel.Code.UI.ManaUi
{
    /// <summary>
    /// The sprite sheet or 9-slice open in the Sprite Asset window. Each change is saved as it is made, so every
    /// sprite drawn from the asset updates in the scene while it is being set up; Revert puts back the file as it was
    /// when the window opened it.
    /// </summary>
    public static class SpriteAssetEditing
    {
        public static string? Path { get; private set; }
        public static ManaSpriteSheet? Sheet { get; private set; }
        public static ManaSpriteSlice? Slice { get; private set; }
        public static bool WindowOpen { get; private set; }
        public static string Status { get; private set; } = string.Empty;

        /// <summary>Replaced whenever a different asset is opened (or reverted), so the window rebuilds for it.</summary>
        public static object Token { get; private set; } = new();

        /// <summary>A picture of the whole asset -- the slice laid out as a block, the sheet's frames -- worked out after each change.</summary>
        public static string? PreviewFile { get; private set; }

        private static string? _opened;

        public static void Open(string path)
        {
            try
            {
                var text = File.ReadAllText(path);
                Sheet = System.IO.Path.GetExtension(path) == ManaSpriteSheet.Extension ? JsonSerializer.Deserialize<ManaSpriteSheet>(text, ManaContentAssets.Json) : null;
                Slice = System.IO.Path.GetExtension(path) == ManaSpriteSlice.Extension ? JsonSerializer.Deserialize<ManaSpriteSlice>(text, ManaContentAssets.Json) : null;
                if (Sheet == null && Slice == null)
                {
                    throw new InvalidDataException("Not a sprite sheet or a 9-slice.");
                }
                Path = path;
                _opened = text;
                WindowOpen = true;
                Status = string.Empty;
                Token = new object();
                UpdatePreview();
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {
                Status = exception.Message;
            }
        }

        /// <summary>Saves the change straight away; the scene redraws from the file.</summary>
        public static void Changed()
        {
            if (Path == null)
            {
                return;
            }
            try
            {
                if (Sheet != null)
                {
                    ManaContentAssets.Save(Sheet, Path);
                }
                else if (Slice != null)
                {
                    ManaContentAssets.Save(Slice, Path);
                }
                Status = "Saved";
                UpdatePreview();
                ManaUiDockDemo.MeshThumbnails?.ForceRefresh(Path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Status = exception.Message;
            }
        }

        public static void Revert()
        {
            if (Path == null || _opened == null)
            {
                return;
            }
            var path = Path;
            File.WriteAllText(path, _opened);
            Open(path);
            ManaUiDockDemo.MeshThumbnails?.ForceRefresh(path);
            Status = "Reverted to how it was when opened";
        }

        public static void Close()
        {
            WindowOpen = false;
            Path = null;
            Sheet = null;
            Slice = null;
            PreviewFile = null;
            Token = new object();
        }

        private static void UpdatePreview()
        {
            PreviewFile = Slice != null ? ManaSpritePictures.JoinGrid(Slice.Layout(5, 3))
                : Sheet != null && Sheet.Frames.Count > 0 ? ManaSpritePictures.Join(Sheet.Frames)
                : Sheet != null ? ManaSpritePictures.Resolve(Sheet.Texture) : null;
        }
    }
}

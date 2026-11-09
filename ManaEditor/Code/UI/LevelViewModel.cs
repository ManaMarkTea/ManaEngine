using ManaEditor.Code.Levels;
using Mana;

namespace ManaEditor.Code.UI
{
    /// <summary>
    /// What the level and play UI needs from the game mode. The panel and the menu only ever see this, so
    /// both can be built and driven headlessly against a stub.
    /// </summary>
    public interface IManaLevelHost
    {
        bool IsPlaying { get; }

        /// <summary>False while the world is still loading -- playing a half-built level is not a thing.</summary>
        bool CanPlay { get; }

        bool HasPlayerStart { get; }

        void NewLevel();
        void OpenLevel(string path);
        void SaveLevel(string path);

        /// <summary>Saves the open level to a new file and carries on in that one, renamed to match.</summary>
        void SaveLevelAs(string path);
        void SaveScene(ManaScene scene);
        bool SceneHasChanges(ManaScene scene);

        /// <summary>Drops a Player Start where the camera is looking, or moves the existing one there.</summary>
        void PlacePlayerStart();

        void TogglePlay();

        /// <summary>Play, but starting the character in front of the camera instead of at the Player Start.</summary>
        void PlayFromCamera();

        /// <summary>Saves the level to a scratch file and launches a second process on it.</summary>
        void PlayInNewWindow();
    }

    /// <summary>
    /// Level and play state for the UI: which file is open, what is on disk, and what the last thing that
    /// happened had to say about it.
    /// <para>
    /// Split out of <see cref="VoxelEditorViewModel"/> rather than added to it because none of this is
    /// voxel editing -- and because the host it drives is the game mode, which the rest of that view model
    /// deliberately does not know about.
    /// </para>
    /// </summary>
    public sealed class LevelViewModel
    {
        private IReadOnlyList<string> _levels = Array.Empty<string>();
        private bool _scanned;

        public IManaLevelHost? Host { get; set; }

        /// <summary>The file the level was last saved to or opened from, or null for a level never saved.</summary>
        public string? CurrentPath { get; private set; }

        /// <summary>What Save writes to. Edited in the panel, so it doubles as "Save As".</summary>
        public string LevelName { get; set; } = "Untitled";

        /// <summary>The last thing that happened, shown under the buttons. Errors and successes both.</summary>
        public string Status { get; private set; } = string.Empty;

        public bool IsPlaying => Host?.IsPlaying ?? false;

        public bool CanPlay => Host?.CanPlay ?? false;

        public bool HasPlayerStart => Host?.HasPlayerStart ?? false;

        public string PlayButtonText => IsPlaying ? "Stop (Esc)" : "Play (F5)";

        public string CurrentLevelLabel =>
            CurrentPath == null ? $"{LevelName} (unsaved)" : ManaLevelLibrary.DisplayName(CurrentPath);

        /// <summary>The level files on disk. Rescanned lazily so the panel can just read it every frame.</summary>
        public IReadOnlyList<string> Levels
        {
            get
            {
                if (!_scanned)
                {
                    Refresh();
                }

                return _levels;
            }
        }

        public void Refresh()
        {
            _scanned = true;
            _levels = ManaLevelLibrary.List();
        }

        public void SetStatus(string status) => Status = status;

        public void MarkSaved(string path)
        {
            CurrentPath = path;
            LevelName = ManaLevelLibrary.DisplayName(path);
            Refresh();
        }

        public void MarkOpened(string path)
        {
            CurrentPath = path;
            LevelName = ManaLevelLibrary.DisplayName(path);
        }

        public void MarkNew()
        {
            CurrentPath = null;
            LevelName = "Untitled";
        }

        // ---- Commands, so the panel and the menu invoke the same thing ---------------------------------

        public void New() => Host?.NewLevel();

        /// <summary>Saves to <see cref="LevelName"/>, which is also how Save As works -- rename, then save.</summary>
        public void Save() => Host?.SaveLevel(CurrentPath != null && LevelName == ManaLevelLibrary.DisplayName(CurrentPath)
            ? CurrentPath : ManaLevelLibrary.ResolvePath(LevelName));

        /// <summary>Saves the open level to <paramref name="path"/> (a new file) and carries on editing that copy.</summary>
        public void SaveAs(string path) => Host?.SaveLevelAs(path);

        /// <summary>Shows the Save As box; set by the toolbar, which owns the button it drops down from.</summary>
        public Action? ShowSaveAs { get; set; }

        /// <summary>Save As from anywhere -- the File menu, Ctrl+Shift+S -- drops the toolbar's box down.</summary>
        public void RequestSaveAs()
        {
            if (ShowSaveAs == null)
            {
                SetStatus("Save As needs the toolbar on screen.");
                return;
            }
            ShowSaveAs();
        }

        /// <summary>A name for a copy that is not taken yet: "Name copy", then "Name copy 2" and on.</summary>
        public string SuggestCopyName(string folder)
        {
            var name = $"{LevelName} copy";
            var number = 2;
            while (File.Exists(Path.Combine(folder, name + ManaLevel.FileExtension)))
            {
                name = $"{LevelName} copy {number}";
                number++;
            }
            return name;
        }

        /// <summary>
        /// Where Save As can put a level: the open level's own folder first (a test level stays among the tests), then
        /// the main levels folder and each game's own -- labelled as paths from the project root.
        /// </summary>
        public static IReadOnlyList<(string Label, string Folder)> SaveFolders(string? currentPath)
        {
            var main = ManaLevelLibrary.ResolveFolder();
            var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(main))) ?? main;
            var folders = new List<string>();
            if (currentPath != null && Path.GetDirectoryName(Path.GetFullPath(currentPath)) is { } own)
            {
                folders.Add(own);
            }
            foreach (var (_, folder) in ManaLevelLibrary.Folders())
            {
                if (!folders.Any(known => string.Equals(Path.GetFullPath(known), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase)))
                {
                    folders.Add(folder);
                }
            }
            return folders.Select(folder => (Path.GetRelativePath(root, folder).Replace('\\', '/'), folder)).ToList();
        }

        /// <summary>The file a Save As name lands at in a folder, or null when the name has nothing usable in it.</summary>
        public static string? SaveAsPath(string name, string folder)
        {
            var safe = string.Concat((name ?? string.Empty).Split(Path.GetInvalidFileNameChars())).Trim();
            if (safe.Length == 0)
            {
                return null;
            }
            return Path.Combine(folder, safe + ManaLevel.FileExtension);
        }

        public void SaveScene(ManaScene scene) => Host?.SaveScene(scene);

        public bool SceneHasChanges(ManaScene scene) => Host?.SceneHasChanges(scene) ?? false;

        public void Open(string path) => Host?.OpenLevel(path);

        /// <summary>Reopens the file on disk, throwing away everything since the last save.</summary>
        public void Reload()
        {
            if (CurrentPath != null)
            {
                Host?.OpenLevel(CurrentPath);
            }
        }

        public void PlacePlayerStart() => Host?.PlacePlayerStart();

        public void TogglePlay() => Host?.TogglePlay();

        public void PlayFromCamera() => Host?.PlayFromCamera();

        public void PlayInNewWindow() => Host?.PlayInNewWindow();
    }
}

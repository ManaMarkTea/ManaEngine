using ManaVoxel.Code.Levels;
using Mana;

namespace ManaVoxel.Code.UI
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
        public void Save() => Host?.SaveLevel(ManaLevelLibrary.ResolvePath(LevelName));

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

using ManaCore.Diagnostics;

namespace Mana.Code.GameModes
{
    // Level game modes' editor previews: what play would build, shown while the level is open and not playing.
    public partial class VoxelEditorGameMode
    {
        private readonly HashSet<ManaLevelGameMode> _previewed = new();

        private void SyncGameModePreviews()
        {
            var showing = !IsMatchRunning && !IsLoading && !_openingLevel;
            List<ManaLevelGameMode> modes;
            lock (Context.World.SceneLock)
            {
                modes = Context.World.Scenes.SelectMany(scene => scene.GameModes).ToList();
            }

            // A mode taken off its level (or the level closed) takes its preview with it.
            foreach (var gone in _previewed.Where(mode => !modes.Contains(mode)).ToList())
            {
                Preview(gone, false);
                _previewed.Remove(gone);
            }

            foreach (var mode in modes)
            {
                Preview(mode, showing);
                _previewed.Add(mode);
            }
        }

        // Down before play starts, so the match never shares the board with its own preview.
        private void HideGameModePreviews()
        {
            foreach (var mode in _previewed)
            {
                Preview(mode, false);
            }
        }

        private void Preview(ManaLevelGameMode mode, bool visible)
        {
            try
            {
                mode.EditorPreview(Context, visible);
            }
            catch (Exception exception)
            {
                ManaLog.WriteDiagnostic($"Game mode {mode.Type} preview failed: {exception.Message}");
            }
        }
    }
}

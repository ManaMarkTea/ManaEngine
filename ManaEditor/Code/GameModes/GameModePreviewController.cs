using ManaCore.Diagnostics;

namespace Mana.Code.GameModes
{
    // Level game modes' editor previews: what play would build, shown while the level is open and not playing.
    internal sealed class GameModePreviewController
    {
        private readonly HashSet<ManaLevelGameMode> _previewed = new();

        public void Sync(ManaContext context, bool showing)
        {
            List<ManaLevelGameMode> modes;
            lock (context.World.SceneLock)
            {
                modes = context.World.Scenes.SelectMany(scene => scene.GameModes).ToList();
            }

            // A mode taken off its level (or the level closed) takes its preview with it.
            foreach (var gone in _previewed.Where(mode => !modes.Contains(mode)).ToList())
            {
                Preview(context, gone, false);
                _previewed.Remove(gone);
            }

            foreach (var mode in modes)
            {
                Preview(context, mode, showing);
                _previewed.Add(mode);
            }
        }

        // Down before play starts, so the match never shares the board with its own preview.
        public void Hide(ManaContext context)
        {
            foreach (var mode in _previewed)
            {
                Preview(context, mode, false);
            }
        }

        private static void Preview(ManaContext context, ManaLevelGameMode mode, bool visible)
        {
            try
            {
                mode.EditorPreview(context, visible);
            }
            catch (Exception exception)
            {
                ManaLog.WriteDiagnostic($"Game mode {mode.Type} preview failed: {exception.Message}");
            }
        }
    }
}

namespace Mana.Code
{
    // Lightweight, self-throttled working-set history for a live "like the FPS graph" debug plot (see
    // VoxelEditorPanelsView's Debug tab) -- mirrors ManaTime's own ring-buffer/CopyOrderedHistory pattern.
    // Call Sample() every frame; it only actually records a new point once every SampleIntervalMs, so a
    // modest fixed-size buffer covers a multi-minute window without needing per-frame resolution (memory
    // doesn't move nearly as fast as frame time does).
    public static class ManaMemoryStats
    {
        public const int HistoryCapacity = 240;
        private const long SampleIntervalMs = 250;

        private static readonly float[] History = new float[HistoryCapacity];

        // 0, not long.MinValue -- Environment.TickCount64 is always non-negative, so comparing against a
        // MinValue sentinel via subtraction overflows unchecked long arithmetic and silently blocks the
        // very first sample forever (bit this exact team once already in VoxelEditorGameMode).
        private static long _lastSampleMs;
        private static int _historyIndex = -1;
        private static int _historyCount;

        public static void Sample()
        {
            var now = Environment.TickCount64;
            if (now - _lastSampleMs < SampleIntervalMs)
            {
                return;
            }

            _lastSampleMs = now;
            var workingSetMB = (float)(Environment.WorkingSet / (1024.0 * 1024.0));

            _historyIndex = (_historyIndex + 1) % History.Length;
            History[_historyIndex] = workingSetMB;
            _historyCount = Math.Min(_historyCount + 1, History.Length);
        }

        public static float Latest => _historyCount > 0 ? History[_historyIndex] : 0f;

        public static int CopyOrderedHistory(float[] buffer)
        {
            var count = Math.Min(_historyCount, buffer.Length);
            if (count == 0)
            {
                return 0;
            }

            var oldestIndex = (_historyIndex - _historyCount + 1 + History.Length) % History.Length;
            var skippedSamples = _historyCount - count;
            var firstIndex = (oldestIndex + skippedSamples) % History.Length;
            for (var i = 0; i < count; i++)
            {
                buffer[i] = History[(firstIndex + i) % History.Length];
            }

            return count;
        }
    }
}

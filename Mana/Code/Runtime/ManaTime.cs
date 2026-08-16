namespace Mana.Code
{
    public class ManaTime
    {
        public static ManaTime GlobalTime = new ManaTime();

        public bool TimePaused = false;

        public DateTime StartTime = DateTime.Now;

        public TimeSpan DeltaTime = TimeSpan.Zero;
        public TimeSpan GameTime = TimeSpan.Zero;

        public double FramesPerSecond => DeltaTime.TotalSeconds > 0.0
            ? 1.0 / DeltaTime.TotalSeconds
            : 0.0;

        public TimeSpan DeltaTimeAvg => HistoryCount > 0
            ? TimeSpan.FromSeconds(_historySecondsTotal / HistoryCount)
            : TimeSpan.Zero;
        public double FramesPerSecondAvg => DeltaTimeAvg.TotalSeconds > 0.0
            ? 1.0 / DeltaTimeAvg.TotalSeconds
            : 0.0;

        public DateTime LastUpdate = DateTime.Now;
        public Vector4 EncodedDateTime
        {
            get
            {
                var now = DateTime.Now;
                return new Vector4(now.Year, now.Month - 1, now.Day - 1, now.Hour * 3600 + now.Minute * 60 + now.Second);
            }
        }

        public TimeSpan[] History = new TimeSpan[300];

        /// <summary>
        /// Millisecond mirror of <see cref="History"/>, for a live frame-time graph (see ManaUI's
        /// DebugManaView / Sparkline) to bind straight to without copying it out every frame the way
        /// CopyOrderedHistory does -- Sparkline reads a ring buffer in place via its own WriteCursor.
        /// </summary>
        public float[] HistoryMilliseconds = new float[300];

        public int HistoryIndex = -1;
        public int HistoryCount { get; private set; }
        private double _historySecondsTotal;

        public void UpdateTime(double deltaTime)
        {
            DateTime now = DateTime.Now;

            DeltaTime = TimeSpan.FromSeconds(deltaTime);

            if (!TimePaused)
            {
                GameTime += DeltaTime;
            }

            HistoryIndex = (HistoryIndex + 1) % History.Length;
            if (HistoryCount == History.Length)
            {
                _historySecondsTotal -= History[HistoryIndex].TotalSeconds;
            }
            else
            {
                HistoryCount++;
            }

            History[HistoryIndex] = DeltaTime;
            HistoryMilliseconds[HistoryIndex] = (float)DeltaTime.TotalMilliseconds;
            _historySecondsTotal += DeltaTime.TotalSeconds;

            LastUpdate = now;
        }

        public TimeSpan[] OrderedHistory()
        {
            var history = new TimeSpan[HistoryCount];
            CopyOrderedHistory(history);
            return history;
        }

        public TimeSpan[] OrderedHistory(ref TimeSpan[] buffer)
        {
            if (buffer.Length != HistoryCount)
            {
                buffer = new TimeSpan[HistoryCount];
            }

            CopyOrderedHistory(buffer);
            return buffer;
        }

        public int CopyOrderedHistory(TimeSpan[] buffer)
        {
            var count = Math.Min(HistoryCount, buffer.Length);
            if (count == 0)
            {
                return 0;
            }

            var skippedSamples = HistoryCount - count;
            var oldestIndex = (HistoryIndex - HistoryCount + 1 + History.Length) % History.Length;
            var firstIndex = (oldestIndex + skippedSamples) % History.Length;
            for (var i = 0; i < count; i++)
            {
                buffer[i] = History[(firstIndex + i) % History.Length];
            }

            return count;
        }
    }
}

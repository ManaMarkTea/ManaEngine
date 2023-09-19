namespace Mana.Code
{
    public class ManaTime
    {
        public static ManaTime GlobalTime = new ManaTime();

        public bool TimePaused = false;

        public DateTime StartTime = DateTime.Now;

        public TimeSpan DeltaTime = TimeSpan.Zero;
        public TimeSpan GameTime = TimeSpan.Zero;

        public double FramesPerSecond { get { return 1.0 / DeltaTime.TotalSeconds; } }

        public TimeSpan DeltaTimeAvg { get { return History.Average(); } }
        public double FramesPerSecondAvg { get { return History.Average(x => 1.0 / (x.TotalSeconds != 0 ? x.TotalSeconds : 1.0)); } }

        public DateTime LastUpdate = DateTime.Now;

        public TimeSpan[] History = new TimeSpan[300];
        public int HistoryIndex = 0;

        public void UpdateTime(double deltaTime)
        {
            DateTime now = DateTime.Now;

            DeltaTime = TimeSpan.FromSeconds(deltaTime);

            if (!TimePaused)
            {
                GameTime += DeltaTime;
            }

            HistoryIndex++;
            HistoryIndex %= History.Length;
            History[HistoryIndex] = DeltaTime;

            LastUpdate = now;
        }
    }
}

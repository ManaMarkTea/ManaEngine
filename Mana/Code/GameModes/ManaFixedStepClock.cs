namespace Mana.Code.GameModes
{
    /// <summary>
    /// Runs a simulation in fixed ticks against real time -- at a chosen speed, unless paused -- so its rules see the
    /// same step whatever the frame rate. <see cref="Alpha"/> says how far into the next tick the frame is, for drawing
    /// things smoothly between ticks. A long frame (a hitch, a debugger stop) is capped rather than caught up.
    /// </summary>
    public sealed class ManaFixedStepClock
    {
        private readonly int[] _speeds;
        private float _accumulator;

        /// <param name="tickSeconds">One step of the simulation.</param>
        /// <param name="speeds">The speeds <see cref="CycleSpeed"/> goes through, starting at the first.</param>
        public ManaFixedStepClock(float tickSeconds, params int[] speeds)
        {
            TickSeconds = tickSeconds;
            _speeds = speeds;
            if (_speeds.Length == 0)
            {
                _speeds = [1, 2, 4];
            }
            Speed = _speeds[0];
        }

        public float TickSeconds { get; }

        /// <summary>The most ticks one frame runs: past it a slow machine falls behind rather than freezing.</summary>
        public int MaxTicksPerFrame { get; init; } = 40;

        public bool Paused { get; set; }

        public int Speed { get; private set; }

        /// <summary>How far into the next tick the clock is, 0..1, for smooth drawing.</summary>
        public float Alpha => _accumulator / TickSeconds;

        public void CycleSpeed()
        {
            var next = (Array.IndexOf(_speeds, Speed) + 1) % _speeds.Length;
            Speed = _speeds[next];
        }

        /// <summary>Runs <paramref name="tick"/> once per tick due in <paramref name="dt"/> seconds of play; returns how many ran.</summary>
        public int Advance(float dt, Action tick)
        {
            if (Paused || !float.IsFinite(dt) || dt <= 0)
            {
                return 0;
            }
            _accumulator += Math.Min(dt, 0.25f) * Speed;
            var ticks = 0;
            // The epsilon keeps float drift from dropping a tick from a whole number of them.
            while (_accumulator >= TickSeconds - 1e-5f && ticks < MaxTicksPerFrame)
            {
                tick();
                ticks++;
                _accumulator -= TickSeconds;
            }
            _accumulator = Math.Clamp(_accumulator, 0, TickSeconds);
            return ticks;
        }

        public void Reset()
        {
            _accumulator = 0;
            Paused = false;
        }
    }
}

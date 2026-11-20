namespace ManaCore.Runtime
{
    /// <summary>
    /// The frame's time, one value for everything in a frame: stepped once at the frame's start (by Mana's ManaTime) by the
    /// frame's length, so the sky, the wind, flickering lights, shader animation and live aircraft all move by the same
    /// amount within a frame, rather than by whenever each happened to read a clock of its own.
    /// </summary>
    public static class ManaFrameClock
    {
        /// <summary>Seconds since the first frame, stepped by each frame's length; 0 where no frame loop runs (tests, thumbnails).</summary>
        public static double Seconds { get; private set; }

        /// <summary>The real-world time of this frame in Unix seconds, NaN before the first frame.</summary>
        public static double UnixSeconds { get; private set; } = double.NaN;

        /// <param name="fixedFrames">A recording steps by whole frames: the Unix time follows them, not the wall clock.</param>
        public static void Advance(double deltaSeconds, bool fixedFrames)
        {
            var step = Math.Max(deltaSeconds, 0.0);
            Seconds += step;

            // Stepped by the frame too, and eased back toward the wall clock so it never drifts.
            var wall = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            if (double.IsNaN(UnixSeconds) || (!fixedFrames && Math.Abs(wall - UnixSeconds) > 1.0))
            {
                UnixSeconds = wall;
                return;
            }
            UnixSeconds += step;
            if (!fixedFrames)
            {
                UnixSeconds += (wall - UnixSeconds) * 0.02;
            }
        }
    }
}

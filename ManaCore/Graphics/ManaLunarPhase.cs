namespace ManaCore.Graphics
{
    /// <summary>Mean lunar phase and illumination, independent of sky rendering and clock controls.</summary>
    public static class ManaLunarPhase
    {
        private const double SynodicMonthDays = 29.530588853;
        private const int PhaseCount = 8;
        private const int PrincipalPhaseCount = 4;
        private static readonly DateTimeOffset NewMoonEpoch = new(2000, 1, 6, 18, 14, 0, TimeSpan.Zero);
        private static readonly string[] PhaseNames =
        [
            "New moon", "Waxing crescent", "First quarter", "Waxing gibbous",
            "Full moon", "Waning gibbous", "Last quarter", "Waning crescent"
        ];
        private static readonly string[] PhaseEmojis = ["🌑", "🌒", "🌓", "🌔", "🌕", "🌖", "🌗", "🌘"];
        private static readonly string[] PrincipalPhaseEmojis = ["🌑", "🌓", "🌕", "🌗"];

        /// <summary>The fraction of the lunar cycle since new moon, from 0 to 1.</summary>
        public static float PhaseAt(DateTimeOffset time)
        {
            var cycles = (time.ToUniversalTime() - NewMoonEpoch).TotalDays / SynodicMonthDays;
            return (float)(cycles - Math.Floor(cycles));
        }

        /// <summary>The illuminated fraction for a phase from 0 (new) to 1 (next new moon).</summary>
        public static float Illumination(float phase)
        {
            var wrappedPhase = phase - MathF.Floor(phase);
            return (1.0f - MathF.Cos(MathF.Tau * wrappedPhase)) * 0.5f;
        }

        /// <summary>The nearest named moon phase for a date and time.</summary>
        public static string NameAt(DateTimeOffset time) => PhaseNames[PhaseIndex(PhaseAt(time))];

        /// <summary>The emoji for the nearest named moon phase for a date and time.</summary>
        public static string EmojiAt(DateTimeOffset time) => PhaseEmojis[PhaseIndex(PhaseAt(time))];

        /// <summary>An emoji only within half a day of new, first-quarter, full, or last-quarter moon.</summary>
        public static string? PrincipalPhaseEmojiAt(DateTimeOffset time)
        {
            var phase = PhaseAt(time);
            var quarter = (int)MathF.Round(phase * PrincipalPhaseCount);
            var distance = MathF.Abs(phase - quarter / (float)PrincipalPhaseCount);
            distance = MathF.Min(distance, 1.0f - distance);
            return distance * (float)SynodicMonthDays <= 0.55f
                ? PrincipalPhaseEmojis[quarter & (PrincipalPhaseCount - 1)]
                : null;
        }

        private static int PhaseIndex(float phase)
        {
            var wrappedPhase = phase - MathF.Floor(phase);
            return (int)MathF.Floor(wrappedPhase * PhaseCount + 0.5f) & (PhaseCount - 1);
        }
    }
}
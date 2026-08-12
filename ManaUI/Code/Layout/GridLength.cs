namespace ManaUI
{
    public enum GridUnitType
    {
        /// <summary>Size to the largest child in the track.</summary>
        Auto,

        /// <summary>A fixed number of logical pixels.</summary>
        Pixel,

        /// <summary>A weighted share of whatever is left after Auto and Pixel tracks are satisfied.</summary>
        Star
    }

    public readonly struct GridLength : IEquatable<GridLength>
    {
        public static readonly GridLength Auto = new GridLength(0.0f, GridUnitType.Auto);

        /// <summary>One share of the remaining space -- the "*" of XAML.</summary>
        public static readonly GridLength Star = new GridLength(1.0f, GridUnitType.Star);

        public readonly float Value;
        public readonly GridUnitType UnitType;

        public GridLength(float value, GridUnitType unitType)
        {
            Value = MathF.Max(0.0f, value);
            UnitType = unitType;
        }

        public bool IsAuto => UnitType == GridUnitType.Auto;
        public bool IsPixel => UnitType == GridUnitType.Pixel;
        public bool IsStar => UnitType == GridUnitType.Star;

        public static GridLength Pixels(float value)
        {
            return new GridLength(value, GridUnitType.Pixel);
        }

        public static GridLength Stars(float weight)
        {
            return new GridLength(weight, GridUnitType.Star);
        }

        /// <summary>A bare number is a pixel length, matching XAML's shorthand.</summary>
        public static implicit operator GridLength(float pixels)
        {
            return Pixels(pixels);
        }

        /// <summary>Parses "Auto", "*", "2*" and "120".</summary>
        public static GridLength Parse(string text)
        {
            var trimmed = text.Trim();

            if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            {
                return Auto;
            }

            if (trimmed.EndsWith('*'))
            {
                var weightText = trimmed[..^1];
                var weight = weightText.Length == 0 ? 1.0f : float.Parse(weightText);
                return Stars(weight);
            }

            return Pixels(float.Parse(trimmed));
        }

        public bool Equals(GridLength other)
        {
            return Value == other.Value && UnitType == other.UnitType;
        }

        public override bool Equals(object? obj)
        {
            return obj is GridLength other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Value, UnitType);
        }

        public override string ToString()
        {
            return UnitType switch
            {
                GridUnitType.Auto => "Auto",
                GridUnitType.Star => Value == 1.0f ? "*" : $"{Value}*",
                _ => Value.ToString()
            };
        }
    }

    /// <summary>One row or column track, with optional independent min/max clamps.</summary>
    public sealed class GridTrack
    {
        public GridTrack()
        {
        }

        public GridTrack(GridLength length)
        {
            Length = length;
        }

        public GridLength Length { get; set; } = GridLength.Star;
        public float MinLength { get; set; }
        public float MaxLength { get; set; } = float.PositiveInfinity;

        /// <summary>Size this track resolved to in the last layout pass. Read-only to callers; useful for
        /// splitters and for asserting layout in tests.</summary>
        public float Resolved { get; internal set; }

        /// <summary>Offset from the grid's content origin, filled in during arrange.</summary>
        public float Offset { get; internal set; }

        internal float Clamp(float value)
        {
            return Math.Clamp(value, MinLength, MathF.Max(MinLength, MaxLength));
        }
    }
}

namespace ManaUI
{
    /// <summary>
    /// Per-corner rounding, ordered clockwise from the top-left. Reaches the GPU as a vec4, where the
    /// SDF shader clamps each radius against the box half-extents.
    /// </summary>
    public readonly struct CornerRadius : IEquatable<CornerRadius>
    {
        public static readonly CornerRadius Zero = default;

        public readonly float TopLeft;
        public readonly float TopRight;
        public readonly float BottomRight;
        public readonly float BottomLeft;

        public CornerRadius(float uniform)
        {
            TopLeft = uniform;
            TopRight = uniform;
            BottomRight = uniform;
            BottomLeft = uniform;
        }

        public CornerRadius(float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            TopLeft = topLeft;
            TopRight = topRight;
            BottomRight = bottomRight;
            BottomLeft = bottomLeft;
        }

        public bool IsZero => TopLeft == 0.0f && TopRight == 0.0f && BottomRight == 0.0f && BottomLeft == 0.0f;
        public float Max => MathF.Max(MathF.Max(TopLeft, TopRight), MathF.Max(BottomRight, BottomLeft));

        public Vector4 ToVector4()
        {
            return new Vector4(TopLeft, TopRight, BottomRight, BottomLeft);
        }

        public static CornerRadius Top(float radius)
        {
            return new CornerRadius(radius, radius, 0.0f, 0.0f);
        }

        public static CornerRadius Bottom(float radius)
        {
            return new CornerRadius(0.0f, 0.0f, radius, radius);
        }

        public static implicit operator CornerRadius(float uniform)
        {
            return new CornerRadius(uniform);
        }

        public bool Equals(CornerRadius other)
        {
            return TopLeft == other.TopLeft
                && TopRight == other.TopRight
                && BottomRight == other.BottomRight
                && BottomLeft == other.BottomLeft;
        }

        public override bool Equals(object? obj)
        {
            return obj is CornerRadius other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);
        }

        public static bool operator ==(CornerRadius a, CornerRadius b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(CornerRadius a, CornerRadius b)
        {
            return !a.Equals(b);
        }
    }
}

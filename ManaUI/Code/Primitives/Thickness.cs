namespace ManaUI
{
    /// <summary>
    /// Per-edge spacing, used for both Margin (outside) and Padding (inside).
    /// </summary>
    public readonly struct Thickness : IEquatable<Thickness>
    {
        public static readonly Thickness Zero = default;

        public readonly float Left;
        public readonly float Top;
        public readonly float Right;
        public readonly float Bottom;

        public Thickness(float uniform)
        {
            Left = uniform;
            Top = uniform;
            Right = uniform;
            Bottom = uniform;
        }

        public Thickness(float horizontal, float vertical)
        {
            Left = horizontal;
            Right = horizontal;
            Top = vertical;
            Bottom = vertical;
        }

        public Thickness(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public float Horizontal => Left + Right;
        public float Vertical => Top + Bottom;
        public Vector2 Size => new Vector2(Horizontal, Vertical);
        public Vector2 TopLeft => new Vector2(Left, Top);
        public bool IsZero => Left == 0.0f && Top == 0.0f && Right == 0.0f && Bottom == 0.0f;

        public static implicit operator Thickness(float uniform)
        {
            return new Thickness(uniform);
        }

        public static Thickness operator +(Thickness a, Thickness b)
        {
            return new Thickness(a.Left + b.Left, a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom);
        }

        public static Thickness operator *(Thickness a, float scale)
        {
            return new Thickness(a.Left * scale, a.Top * scale, a.Right * scale, a.Bottom * scale);
        }

        public bool Equals(Thickness other)
        {
            return Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
        }

        public override bool Equals(object? obj)
        {
            return obj is Thickness other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Left, Top, Right, Bottom);
        }

        public static bool operator ==(Thickness a, Thickness b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Thickness a, Thickness b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return $"({Left}, {Top}, {Right}, {Bottom})";
        }
    }
}

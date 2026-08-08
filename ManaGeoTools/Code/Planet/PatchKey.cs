using System.Collections.Immutable;

namespace ManaGeoTools.Code.Planet
{
    /// <summary>
    /// Identifies one leaf patch of the planet's quadtree: the original grid coordinates it started
    /// from, plus the sequence of quadrant choices (0-3) that reached it if it's the result of one or
    /// more splits (see PlanetNode.SplitPatch) -- empty for an original, never-split grid patch.
    /// Quadrant numbering: bit0 = U half (0=low,1=high), bit1 = V half (0=low,1=high).
    /// </summary>
    public readonly struct PatchKey : IEquatable<PatchKey>
    {
        public int Face { get; }
        public int Row { get; }
        public int Col { get; }
        public ImmutableArray<int> SplitPath { get; }

        private PatchKey(int face, int row, int col, ImmutableArray<int> splitPath)
        {
            Face = face;
            Row = row;
            Col = col;
            SplitPath = splitPath;
        }

        public static PatchKey Root(int face, int row, int col)
        {
            return new PatchKey(face, row, col, ImmutableArray<int>.Empty);
        }

        public PatchKey Child(int quadrant)
        {
            return new PatchKey(Face, Row, Col, SplitPath.Add(quadrant));
        }

        /// <summary>True for an original, never-split grid patch -- has no coarser patch to merge into.</summary>
        public bool IsRoot => SplitPath.IsEmpty;

        /// <summary>The quadrant (0-3) this key was reached by from its immediate parent. Caller must check !IsRoot first.</summary>
        public int Quadrant => SplitPath[^1];

        /// <summary>Inverse of Child(quadrant): the key one level coarser than this one. Caller must check !IsRoot first.</summary>
        public PatchKey Parent()
        {
            return new PatchKey(Face, Row, Col, SplitPath.RemoveAt(SplitPath.Length - 1));
        }

        // ImmutableArray<T>'s own Equals/GetHashCode compare the backing array by REFERENCE, not
        // content -- using it as a Dictionary key field would silently break lookups unless Equals/
        // GetHashCode are hand-written to walk the contents instead, as done here.
        public bool Equals(PatchKey other)
        {
            return Face == other.Face && Row == other.Row && Col == other.Col
                && SplitPath.AsSpan().SequenceEqual(other.SplitPath.AsSpan());
        }

        public override bool Equals(object? obj)
        {
            return obj is PatchKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(Face);
            hashCode.Add(Row);
            hashCode.Add(Col);
            foreach (var quadrant in SplitPath)
            {
                hashCode.Add(quadrant);
            }
            return hashCode.ToHashCode();
        }
    }
}

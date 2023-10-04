using Mana;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace System.Numerics
{

    /// <summary>
    /// Represents a ray primitive. Used as a basis for intersection calculations.
    /// </summary>
    public struct Ray
    {
        public readonly Vector3 Origin;
        public readonly Vector3 Direction;
        public readonly float Distance;
        public Ray(Vector3 start, Vector3 direction, float distance)
        {
            this.Origin = start;
            this.Direction = direction.Normalized();
            this.Distance = distance;
        }
        public Ray(Vector3 start, Vector3 direction) : this(start, direction, float.PositiveInfinity) { }
    }
}

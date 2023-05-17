using Silk.NET.Assimp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code
{
    public class ManaGeometryEdge
    {
        public ManaDynamicGeometryContainer Geom;

        public ManaGeometryVertex A;
        public ManaGeometryVertex B;

        public List<ManaGeometryFace> Faces = new List<ManaGeometryFace>();

        public Vector3 Normal;

    }
}

using Mana.Code.ManaShaders;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code
{
    public class ManaLightSourceComponent
    {
        private ManaContext Context;

        public double Intensity { get; set; } = 1.0;

        public Vector3 Direction { get; set; } = Vector3.UnitY * -1;

        public ManaLightSourceComponent(ManaContext context)
        {
            this.Context = context;
        }


    }
}

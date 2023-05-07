using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code
{
    public class ManaObject
    {
        public ManaContext Context { get; set; }

        public ManaObject(ManaContext context)
        {
            this.Context = context;
        }
    }
}

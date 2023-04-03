using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana
{
    public class ManaComponent
    {
        public ManaSceneNode Owner { get; set; }
        public ManaComponent(ManaSceneNode owner) 
        {
            Owner = owner;
        }

        public virtual void UpdateComponent() { }
        public virtual void DrawComponent() { }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Shapes.Modifiers
{
    public class ModifierInputs
    {
        public ManaMesh? InputMesh;
    }

    public class ModifierOutputs
    {
        public ManaMesh? OuputMesh;
    }

    public abstract class ManaMeshModifier
    {
        public abstract ModifierOutputs ApplyModifier(ModifierInputs inputs);
    }
}

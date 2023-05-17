using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mana.Code.Shapes.Modifiers
{
    public class ManaMeshModifierSin : ManaMeshModifier
    {
        public override ModifierOutputs ApplyModifier(ModifierInputs inputs)
        {
            var output = new ModifierOutputs();

            var container = inputs.InputMesh?.DynamicGeometryContainer;
            if (container == null)
            {
                return output;
            }

            Parallel.For(0, container.Verts.Count, (i, loopState) =>
            {                
                var pos = container.Verts[i].Position;
                var normal = container.Verts[i].Normal;
                pos += normal * MathF.Sin((float)i) * 0.01f;
                container.Verts[i].Position = pos;
            });
            
            return output;
        }
    }
}

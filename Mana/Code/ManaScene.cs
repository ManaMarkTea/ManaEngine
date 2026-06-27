using System.Diagnostics;
using Mana.Code.Nodes;

namespace Mana
{
    public class ManaScene : ManaObject
    {
        public List<ManaSceneNode> Children { get; set; }


        public ManaScene(ManaContext context) : base(context)
        {
            Children = new List<ManaSceneNode>();
        }

        public void Update()
        {
            foreach (var node in Children)
            {
                node.UpdateTransform();
            }
        }

        public void Draw()
        {
            foreach (var node in Children)
            {
                node.Draw();
            }
        }

        public void CollectRenderMeshInstances(List<ManaCore.Graphics.ManaRenderMeshInstance> output)
        {
            foreach (var node in Children)
            {
                node.CollectRenderMeshInstances(output);
            }
        }

    }
}

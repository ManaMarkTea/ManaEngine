using System.Diagnostics;
using Mana.Code.Nodes;
using ManaCore.Graphics;

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
            for (int i = 0, count = Children.Count; i < count; i++)
            {
                Children[i].UpdateTransform();
            }
        }

        public void Draw()
        {
            for (int i = 0, count = Children.Count; i < count; i++)
            {
                Children[i].Draw();
            }
        }

        public void CollectRenderMeshInstances(List<ManaRenderMeshInstance> output)
        {
            for (int i = 0, count = Children.Count; i < count; i++)
            {
                Children[i].CollectRenderMeshInstances(output);
            }
        }

    }
}

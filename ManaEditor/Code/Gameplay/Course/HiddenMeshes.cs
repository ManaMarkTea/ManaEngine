using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;

namespace ManaEditor.Code.Gameplay.Course
{
    /// <summary>
    /// Hides a node's meshes for the rest of a run and brings them back afterwards. The node itself stays
    /// in the scene: removing it mid-tick would mutate the very list the scene walk is iterating, and a node
    /// that is still there is what lets stopping play put it back.
    /// </summary>
    internal sealed class HiddenMeshes
    {
        private readonly List<MeshRendererComponent> _hidden = new();

        public void Hide(ManaSceneNode node)
        {
            foreach (var renderer in node.Components.OfType<MeshRendererComponent>())
            {
                if (renderer.IsVisible)
                {
                    renderer.IsVisible = false;
                    _hidden.Add(renderer);
                }
            }

            foreach (var child in node.Children)
            {
                Hide(child);
            }
        }

        public void Restore()
        {
            foreach (var renderer in _hidden)
            {
                renderer.IsVisible = true;
            }

            _hidden.Clear();
        }
    }
}

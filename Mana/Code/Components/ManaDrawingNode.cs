using Mana.Code.Nodes;

namespace Mana.Code.Components
{
    /// <summary>
    /// An editor-transient child a component draws through (a sprite's quad, a pixel effect's two): its renderers are
    /// the component's own, so they are not listed on the node, saved, or removed on their own.
    /// </summary>
    public sealed class ManaDrawingNode
    {
        private readonly ManaSceneNode _owner;
        private ManaSceneNode? _node;

        public ManaDrawingNode(ManaSceneNode owner, string name)
        {
            _owner = owner;
            Name = name;
        }

        public string Name { get; }

        /// <summary>Draws <paramref name="mesh"/> on the child, made if need be, and returns the renderer drawing it.</summary>
        public MeshRendererComponent Add(ManaMesh mesh)
        {
            lock (_owner.Context.World.SceneLock)
            {
                if (_node == null || _node.Parent != _owner)
                {
                    // Placed with its owner at once, so its first frame is not drawn at the world origin.
                    _node = new ManaSceneNode(_owner.Context, Matrix4x4.Identity) { Name = Name, IsEditorTransient = true, DrawsParent = true };
                    _owner.AddChild(_node);
                    _node.WorldTransform = _owner.WorldTransform;
                }
                var renderer = new MeshRendererComponent(_node, mesh);
                _node.Components.Add(renderer);
                return renderer;
            }
        }

        public void Remove(MeshRendererComponent? renderer)
        {
            if (renderer == null || _node == null)
            {
                return;
            }
            lock (_owner.Context.World.SceneLock)
            {
                _node.Components.Remove(renderer);
            }
        }

        /// <summary>The renderers <paramref name="node"/>'s components draw through their hidden children.</summary>
        public static IEnumerable<MeshRendererComponent> RenderersOf(ManaSceneNode node) =>
            node.Children.Where(child => child.DrawsParent).SelectMany(child => child.Components.OfType<MeshRendererComponent>());

        /// <summary>Takes the child, and everything drawn through it, off the owner.</summary>
        public void Clear()
        {
            if (_node == null)
            {
                return;
            }
            lock (_owner.Context.World.SceneLock)
            {
                _owner.RemoveChild(_node);
            }
            _node = null;
        }
    }
}

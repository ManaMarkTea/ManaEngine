using Mana;
using Mana.Code.Components;
using Mana.Code.Nodes;
using Mana.Code.Shapes;

namespace ManaVoxel.Code.Gameplay.Course
{
    /// <summary>
    /// A wireframe box an invisible course piece shows in the editor, so it can be seen and placed. A child
    /// node with no LevelSource, which is why a level never saves it; hidden while playing.
    /// </summary>
    internal sealed class EditorVolumeMarker
    {
        private const float Thickness = 0.03f;

        private readonly ManaSceneNode _owner;
        private readonly string _name;
        private ManaSceneNode? _node;
        private MeshRendererComponent? _renderer;
        private Vector3 _size;

        public EditorVolumeMarker(ManaSceneNode owner, string name = "Trigger Volume")
        {
            _owner = owner;
            _name = name;
        }

        public ManaSceneNode? Node => _node;

        /// <param name="ownerLocalOffset">Where the box's centre sits, in the owner's own axes.</param>
        public void Update(bool visible, Vector3 fullSize, Vector3 ownerLocalOffset, Vector4 color)
        {
            if (_node == null)
            {
                if (!visible)
                {
                    return;
                }

                _node = new ManaSceneNode(_owner.Context, Matrix4x4.Identity)
                {
                    Name = _name,

                    // Editor furniture, like the gizmos: kept out of the outliner (where it read as one of the
                    // piece's own children) and never treated as something to select or edit.
                    IsEditorTransient = true,
                    IncludeInEnvironmentCapture = false
                };
                _renderer = new MeshRendererComponent(_node, Build(fullSize, color));
                _node.Components.Add(_renderer);
                _size = fullSize;
                _owner.AddChild(_node);
            }

            if (_renderer == null)
            {
                return;
            }

            _renderer.IsVisible = visible;
            if (!visible)
            {
                return;
            }

            if (_size != fullSize)
            {
                _size = fullSize;
                _renderer.Mesh = Build(fullSize, color);
            }

            // The marker hangs off the node, so the node's scale would stretch it. The volumes ignore
            // scale, so the marker undoes it.
            var scale = _owner.Scale;
            var inverse = new Vector3(
                scale.X == 0.0f ? 1.0f : 1.0f / scale.X,
                scale.Y == 0.0f ? 1.0f : 1.0f / scale.Y,
                scale.Z == 0.0f ? 1.0f : 1.0f / scale.Z);
            _node.Scale = inverse;
            _node.Position = ownerLocalOffset * inverse;
        }

        private ManaMesh Build(Vector3 fullSize, Vector4 color)
        {
            return new ManaWireframeCage(_owner.Context, color, fullSize * 0.5f, Thickness);
        }
    }
}

using Mana;
using Mana.Code.Nodes;

namespace ManaVoxel.Code.UI
{
    public sealed class VoxelLayerViewModel
    {
        public string Name { get; set; }
        public string Source { get; }
        public ManaVoxelShape Shape { get; }
        public ManaVoxelsNode Node { get; }

        public VoxelLayerViewModel(string name, string source, ManaVoxelShape shape, ManaVoxelsNode node)
        {
            Name = name;
            Source = source;
            Shape = shape;
            Node = node;
        }
    }
}

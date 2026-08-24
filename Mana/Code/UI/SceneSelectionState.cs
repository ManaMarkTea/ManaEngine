using Mana.Code.Nodes;

namespace Mana.Code.UI
{
    public sealed class SceneSelectionState
    {
        public global::Mana.ManaScene? SelectedScene { get; set; }

        public ManaSceneNode? SelectedNode { get; set; }

        // Finer-grained than SelectedNode -- lets one node host several independently-selectable
        // MeshRendererComponents (see PlanetNode's shared-root-node patches). Whenever this is set
        // non-null, SelectedNode should also be set to SelectedComponent.Owner in the same place, so
        // node-level consumers keep working unchanged; component-aware code checks this directly.
        public ManaComponent? SelectedComponent { get; set; }
    }
}

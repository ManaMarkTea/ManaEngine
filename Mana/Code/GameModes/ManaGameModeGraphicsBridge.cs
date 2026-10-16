using Mana.Code.Components;
using Mana.Code.Materials;
using Mana.Code.Nodes;

namespace Mana.Code.Graphics
{
    public static class ManaGameModeGraphicsBridge
    {
        public static void FinalizeGpuUploads(ManaContext context)
        {
            context.DeferGpuUploads = false;
            foreach (var scene in context.World.Scenes)
                foreach (var node in scene.Children)
                    FinalizeNode(node);
            foreach (var material in context.World.Materials)
                FinalizeMaterial(material);
        }

        private static void FinalizeNode(ManaSceneNode node)
        {
            foreach (var component in node.Components)
            {
                if (component is not MeshRendererComponent renderer) continue;
                if (renderer.Mesh != null)
                    foreach (var material in renderer.Mesh.Materials)
                        FinalizeMaterial(material);
                if (renderer.Material != null) FinalizeMaterial(renderer.Material);
            }
            foreach (var child in node.Children) FinalizeNode(child);
        }

        private static void FinalizeMaterial(ManaMaterial material)
        {
            foreach (var texture in new ManaTexture?[] { material.Albedo, material.Normal,
                material.Emission, material.Metallic, material.Roughness, material.Occlusion, material.Opacity })
                if (texture is { IsLoaded: false }) texture.Load();
            foreach (var channel in material.Channels.Values)
                if (channel.Texture is { IsLoaded: false } texture) texture.Load();
        }
    }
}

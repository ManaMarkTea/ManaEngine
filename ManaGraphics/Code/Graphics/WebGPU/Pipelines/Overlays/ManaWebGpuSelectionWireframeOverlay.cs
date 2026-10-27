using ManaCore.Graphics;
using Silk.NET.WebGPU;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Pipelines.Overlays
{
    /// <summary>The editor selection's edges, faint, so its shape reads through the outline without hiding the surface.</summary>
    internal sealed unsafe class ManaWebGpuSelectionWireframeOverlay : ManaWebGpuOverlayPass
    {
        public ManaWebGpuSelectionWireframeOverlay(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, TextureFormat colorFormat)
            : base(gpu, bindings, colorFormat)
        {
        }

        public override string ShaderFileName => "selection_wireframe.wgsl";

        public override string Name => "Selection Wireframe";

        public override bool Blended => true;

        // The full wireframe already draws every edge.
        public override bool IsEnabled(ManaIrradianceSettings settings) => !settings.WireframeEnabled;

        public override bool Includes(ManaWebGpuSceneMesh sceneMesh) => sceneMesh.Instance.SelectionWireframe;

        public override void DrawMesh(RenderPassEncoder* renderPass, ManaWebGpuSceneMesh sceneMesh)
        {
            if (!sceneMesh.TryGetOrCreateWireframeIndexBuffer(out var buffer, out var indexCount))
            {
                return;
            }

            sceneMesh.Mesh.DrawWithIndexBuffer(renderPass, buffer, indexCount);
        }
    }
}

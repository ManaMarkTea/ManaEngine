using ManaCore.Graphics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Overlays
{
    /// <summary>
    /// Draws each mesh's edges, reusing the mesh's own vertex buffer against a separate line index
    /// buffer (see ManaWebGpuSceneMesh.TryGetOrCreateWireframeIndexBuffer).
    /// </summary>
    internal sealed unsafe class ManaWebGpuWireframeOverlay : ManaWebGpuOverlayPass
    {
        public override string ShaderFileName => "wireframe.wgsl";

        public override string Name => "Wireframe";

        public override bool IsEnabled(ManaIrradianceSettings settings) => settings.WireframeEnabled;

        public override void DrawMesh(
            WebGPU api,
            Device* device,
            Queue* queue,
            RenderPassEncoder* renderPass,
            ManaWebGpuContext.ManaWebGpuSceneMesh sceneMesh)
        {
            if (!sceneMesh.TryGetOrCreateWireframeIndexBuffer(api, device, queue, out var buffer, out var indexCount))
            {
                return;
            }

            sceneMesh.Mesh.DrawWithIndexBuffer(renderPass, buffer, indexCount);
        }
    }
}

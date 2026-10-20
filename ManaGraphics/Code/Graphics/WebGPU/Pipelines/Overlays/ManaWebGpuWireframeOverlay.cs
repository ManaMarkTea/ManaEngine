using ManaCore.Graphics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Overlays
{
    /// <summary>Each mesh's edges: its own vertices through a line index buffer.</summary>
    internal sealed unsafe class ManaWebGpuWireframeOverlay : ManaWebGpuOverlayPass
    {
        public ManaWebGpuWireframeOverlay(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, TextureFormat colorFormat)
            : base(gpu, bindings, colorFormat)
        {
        }

        public override string ShaderFileName => "wireframe.wgsl";

        public override string Name => "Wireframe";

        public override bool IsEnabled(ManaIrradianceSettings settings) => settings.WireframeEnabled;

        public override void DrawMesh(
            WebGPU api,
            Device* device,
            Queue* queue,
            RenderPassEncoder* renderPass,
            ManaWebGpuSceneMesh sceneMesh)
        {
            if (!sceneMesh.TryGetOrCreateWireframeIndexBuffer(api, device, queue, out var buffer, out var indexCount))
            {
                return;
            }

            sceneMesh.Mesh.DrawWithIndexBuffer(renderPass, buffer, indexCount);
        }
    }
}

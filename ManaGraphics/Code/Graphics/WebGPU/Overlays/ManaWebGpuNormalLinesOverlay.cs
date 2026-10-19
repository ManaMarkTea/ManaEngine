using ManaCore.Graphics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Overlays
{
    /// <summary>
    /// Draws a short line along each vertex normal. Needs its own vertex buffer rather than an index
    /// buffer over the mesh's, because the line tips aren't existing vertices.
    /// </summary>
    internal sealed unsafe class ManaWebGpuNormalLinesOverlay : ManaWebGpuOverlayPass
    {
        public ManaWebGpuNormalLinesOverlay(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, TextureFormat colorFormat)
            : base(gpu, bindings, colorFormat)
        {
        }

        public override string ShaderFileName => "normal_lines.wgsl";

        public override string Name => "NormalLines";

        public override bool IsEnabled(ManaIrradianceSettings settings) => settings.VertexNormalsEnabled;

        public override void DrawMesh(
            WebGPU api,
            Device* device,
            Queue* queue,
            RenderPassEncoder* renderPass,
            ManaWebGpuContext.ManaWebGpuSceneMesh sceneMesh)
        {
            sceneMesh.GetOrCreateNormalLinesMesh(api, device, queue)?.Draw(renderPass);
        }
    }
}

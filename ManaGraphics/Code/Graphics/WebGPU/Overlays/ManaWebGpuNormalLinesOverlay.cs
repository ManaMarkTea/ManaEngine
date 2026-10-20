using ManaCore.Graphics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Overlays
{
    /// <summary>A short line along each vertex normal, from a vertex buffer of its own (the tips are not mesh vertices).</summary>
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
            ManaWebGpuSceneMesh sceneMesh)
        {
            sceneMesh.GetOrCreateNormalLinesMesh(api, device, queue)?.Draw(renderPass);
        }
    }
}

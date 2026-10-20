using ManaCore.Graphics;
using Silk.NET.WebGPU;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Pipelines.Overlays
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

        public override void DrawMesh(RenderPassEncoder* renderPass, ManaWebGpuSceneMesh sceneMesh)
        {
            sceneMesh.GetOrCreateNormalLinesMesh()?.Draw(renderPass);
        }
    }
}

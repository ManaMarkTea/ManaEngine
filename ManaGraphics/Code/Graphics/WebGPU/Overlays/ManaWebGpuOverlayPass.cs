using ManaCore.Graphics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Overlays
{
    /// <summary>
    /// A debug overlay drawn over the scene after the main mesh pass -- wireframe, vertex normals, and
    /// anything similar added later. Subclasses supply a WGSL file, an enable flag and per-mesh draw
    /// behaviour; ManaWebGpuContext owns pipeline creation and the shared mesh loop, so adding an
    /// overlay means adding one file here rather than another pair of methods on the context.
    /// </summary>
    internal abstract unsafe class ManaWebGpuOverlayPass
    {
        /// <summary>WGSL file name under Assets/Shaders/WebGpu.</summary>
        public abstract string ShaderFileName { get; }

        /// <summary>Pipeline cache key and diagnostic label.</summary>
        public abstract string Name { get; }

        public virtual PrimitiveTopology Topology => PrimitiveTopology.LineList;

        public abstract bool IsEnabled(ManaIrradianceSettings settings);

        /// <summary>
        /// Issues draw calls for one scene mesh. The pipeline, the frame bind group and this mesh's
        /// object bind group are already bound.
        /// </summary>
        public abstract void DrawMesh(
            WebGPU api,
            Device* device,
            Queue* queue,
            RenderPassEncoder* renderPass,
            ManaWebGpuContext.ManaWebGpuSceneMesh sceneMesh);
    }
}

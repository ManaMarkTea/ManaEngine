using ManaCore.Graphics;
using Silk.NET.WebGPU;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>
    /// One piece of the frame, owning its pipelines and GPU state. Registered in frame order; overrides only the hooks
    /// it draws in. Disposed in reverse order, before the shared bindings and the device.
    /// </summary>
    internal abstract unsafe class ManaWebGpuRenderPass : ManaWebGpuDeviceObject
    {
        protected ManaWebGpuRenderPass(ManaWebGpuDevice gpu)
            : base(gpu)
        {
        }

        /// <summary>The frame profiler's label for this pass's encoding time.</summary>
        public abstract string ProfileName { get; }

        /// <summary>For the diagnostics panel's resource counts.</summary>
        public virtual int PipelineCount => 0;
        public virtual int ShaderModuleCount => 0;

        /// <summary>Before the scene pass opens, on the frame's encoder: passes rendering into their own targets.</summary>
        public virtual void Prepare(ManaWebGpuFrame frame)
        {
        }

        /// <summary>Inside the main scene pass, after the opaque and transparent meshes.</summary>
        public virtual void DrawScene(ManaWebGpuFrame frame, RenderPassEncoder* pass)
        {
        }

        /// <summary>After the scene and handle passes close, before HDR post-processing reads the targets.</summary>
        public virtual void AfterScene(ManaWebGpuFrame frame)
        {
        }

        public override void Dispose()
        {
        }
    }

    /// <summary>What a pass may read while it encodes one frame. Filled by the context; valid only during that frame.</summary>
    internal sealed unsafe class ManaWebGpuFrame
    {
        /// <summary>Counts frames: what lets per-mesh work done by the first pass to need it be skipped by the rest.</summary>
        public long Number;
        public CommandEncoder* Encoder;
        public ManaRenderScene Scene = null!;
        public IReadOnlyList<ManaWebGpuSceneMesh> Meshes = [];
        public ManaWebGpuSceneBindings Bindings = null!;
        public TextureFormat SceneColorFormat;
        /// <summary>What the scene pass draws into: the HDR target, or the surface when there is no post.</summary>
        public TextureView* SceneTarget;
        /// <summary>A loading frame: the scene was neither refreshed nor drawn, so passes drawing over it skip.</summary>
        public bool SceneSuspended;
        public TextureView* Depth;
        public TextureView* HandleDepth;
        /// <summary>The HDR post-processor's outline mask; null without HDR post.</summary>
        public TextureView* OutlineMask;
        /// <summary>The environment cube the sky and reflections sample; null until it has loaded.</summary>
        public TextureView* EnvironmentMap;
        /// <summary>The scene camera's projection corrected to the framebuffer's aspect.</summary>
        public System.Numerics.Matrix4x4 Projection;
        /// <summary>Seconds since the context started: shader animation time.</summary>
        public float Time;
        /// <summary>Samples a pixel in the scene pass: 4 under MSAA, else 1. Pipelines drawn there must match it.</summary>
        public uint SampleCount = 1;
        /// <summary>The scene target's size in pixels.</summary>
        public uint Width;
        public uint Height;
    }
}

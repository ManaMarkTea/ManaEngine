using ManaCore.Graphics;
using ManaGraphics.Code.Diagnostics;
using Silk.NET.WebGPU;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Pipelines.Overlays
{
    /// <summary>
    /// A debug overlay drawn over the meshes. Subclasses give a shader, an enable flag and the per-mesh draw; the
    /// pipeline is built the first frame the overlay is on.
    /// </summary>
    internal abstract unsafe class ManaWebGpuOverlayPass : ManaWebGpuRenderPass
    {
        private readonly ManaWebGpuSceneBindings _bindings;
        private readonly TextureFormat _colorFormat;
        private ShaderModule* _shaderModule;
        private RenderPipeline* _pipeline;
        private string? _profileName;

        protected ManaWebGpuOverlayPass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, TextureFormat colorFormat)
            : base(gpu)
        {
            _bindings = bindings;
            _colorFormat = colorFormat;
        }

        /// <summary>WGSL file name under Assets/Shaders/WebGpu.</summary>
        public abstract string ShaderFileName { get; }

        /// <summary>Diagnostic label.</summary>
        public abstract string Name { get; }

        public virtual PrimitiveTopology Topology => PrimitiveTopology.LineList;

        /// <summary>Alpha-blended over the scene rather than written over it.</summary>
        public virtual bool Blended => false;

        /// <summary>Whether this mesh gets the overlay; every mesh by default.</summary>
        public virtual bool Includes(ManaWebGpuSceneMesh sceneMesh) => true;

        public override string ProfileName => _profileName ??= $"GPU: {Name} Overlay";
        public override int PipelineCount => _pipeline != null ? 1 : 0;
        public override int ShaderModuleCount => _shaderModule != null ? 1 : 0;

        public abstract bool IsEnabled(ManaIrradianceSettings settings);

        /// <summary>Draws one mesh; pipeline, frame group and the mesh's object group are already bound.</summary>
        public abstract void DrawMesh(RenderPassEncoder* renderPass, ManaWebGpuSceneMesh sceneMesh);

        // In the scene pass, so the lines depth-test against the depth the meshes just wrote.
        public override void DrawScene(ManaWebGpuFrame frame, RenderPassEncoder* pass)
        {
            if (frame.Meshes.Count == 0 || !IsEnabled(frame.Scene.IrradianceSettings))
            {
                return;
            }

            if (_pipeline == null)
            {
                _shaderModule = Gpu.CreateShaderModule(ShaderFileName);
                _pipeline = ManaWebGpuMeshPipeline.Create(
                    Gpu,
                    _shaderModule,
                    _colorFormat,
                    _bindings.MeshLayout,
                    Name,
                    isTransparent: Blended,
                    disableDepthWrite: true,
                    depthCompare: CompareFunction.LessEqual,
                    primitiveTopology: Topology);
                ManaConsole.WriteDiagnostic($"WebGPU {Name} overlay pipeline created successfully.");
            }

            Api.RenderPassEncoderSetPipeline(pass, _pipeline);
            Api.RenderPassEncoderSetBindGroup(pass, 0, _bindings.FrameGroup, 0, null);

            foreach (var sceneMesh in frame.Meshes)
            {
                if (!Includes(sceneMesh))
                {
                    continue;
                }

                // An instanced mesh's own uniform is otherwise left where it was when it last drew alone.
                sceneMesh.PrepareObjectUniform(frame.Number);
                if (sceneMesh.ObjectBindGroup != null)
                {
                    Api.RenderPassEncoderSetBindGroup(pass, 1, sceneMesh.ObjectBindGroup, 0, null);
                }

                DrawMesh(pass, sceneMesh);
            }
        }

        public override void Dispose()
        {
            if (_pipeline != null)
            {
                Api.RenderPipelineRelease(_pipeline);
                _pipeline = null;
            }

            if (_shaderModule != null)
            {
                Api.ShaderModuleRelease(_shaderModule);
                _shaderModule = null;
            }
        }
    }
}

using Silk.NET.WebGPU;
using ManaGraphics.Code.Graphics.Renderers;
using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>Draws the opaque meshes depth-only into each shadow cascade, before the scene pass samples them.</summary>
    internal sealed unsafe class ManaWebGpuShadowPass : ManaWebGpuRenderPass
    {
        private readonly ManaWebGpuShadowMap _map;
        private readonly ManaWebGpuDepthOnlyDrawer _drawer;

        public ManaWebGpuShadowPass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings, ManaWebGpuShadowMap map)
            : base(gpu)
        {
            _map = map;
            _drawer = new ManaWebGpuDepthOnlyDrawer(gpu, bindings, ManaWebGpuShadowMap.CascadeCount, TextureFormat.Depth32float, bias: true, "shadow");
        }

        public override string ProfileName => "GPU: Shadow Cascades";
        public override int PipelineCount => _drawer.PipelineCount;
        public override int ShaderModuleCount => _drawer.PipelineCount;

        // One depth-only pass per cascade, into that cascade's own layer of the array. The editor room and helpers cast
        // nothing: the room's floor would only shadow itself.
        public override void Prepare(ManaWebGpuFrame frame)
        {
            if (!ManaWebGpuShadowMap.IsCasting(frame.Scene))
            {
                return;
            }
            _drawer.Draw(frame, _map.CascadeViewProjections, cascade => (nint)_map.CascadeView(cascade), ManaWebGpuShadowMap.IsCaster);
        }

        public override void Dispose()
        {
            _drawer.Dispose();
        }
    }
}

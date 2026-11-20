using System.Numerics;
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

        private readonly Matrix4x4[] _cull = new Matrix4x4[ManaWebGpuShadowMap.CascadeCount];

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
            // The cascades take positions relative to the camera; culling wants them in world space.
            var origin = _map.Origin;
            var toOrigin = Matrix4x4.CreateTranslation(-origin);
            for (var i = 0; i < _cull.Length; i++)
            {
                _cull[i] = toOrigin * _map.CascadeViewProjections[i];
            }
            _drawer.Draw(frame, _cull, cascade => (nint)_map.CascadeView(cascade), ManaWebGpuShadowMap.IsCaster, relative: _map.CascadeViewProjections);
        }

        public override void Dispose()
        {
            _drawer.Dispose();
        }
    }
}

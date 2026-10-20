using ManaCore.Diagnostics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>Draws GPU particle emitters after the meshes; the renderer is built when the first emitter appears.</summary>
    internal sealed unsafe class ManaWebGpuParticlePass : ManaWebGpuRenderPass
    {
        private readonly TextureFormat _colorFormat;
        private ManaWebGpuParticles? _particles;

        public ManaWebGpuParticlePass(ManaWebGpuDevice gpu, TextureFormat colorFormat)
            : base(gpu)
        {
            _colorFormat = colorFormat;
        }

        public override string ProfileName => "GPU: Particle Draw Encode";
        public override int PipelineCount => _particles != null ? 1 : 0;

        public long Drawn { get; private set; }
        public long Requested { get; private set; }

        public override void DrawScene(ManaWebGpuFrame frame, RenderPassEncoder* pass)
        {
            Drawn = 0;
            Requested = 0;
            var emitters = frame.Scene.GpuParticles;
            if (emitters.Count == 0)
            {
                return;
            }

            foreach (var emitter in emitters)
            {
                Requested += Math.Max(0, emitter.Count);
            }

            var budget = -1;
#if MANA_BROWSER
            budget = Browser.ManaBrowserInterop.GetGpuParticleBudget();
#endif
            var density = budget < 0 || Requested <= budget ? 1.0 : (double)budget / Requested;
            using (ManaFrameProfiler.Measure(ProfileName))
            {
                _particles ??= new ManaWebGpuParticles(Api, Gpu.Device, Gpu.Queue, _colorFormat, Gpu.DepthFormat,
                    ManaWebGpuDevice.ResolveAssetPath(ManaWebGpuDevice.ShaderPath("particles.wgsl")));
                Drawn = _particles.Draw(pass, emitters, frame.Scene.Camera, frame.Projection, frame.Scene.Wind, density);
            }
        }

        public override void Dispose()
        {
            _particles?.Dispose();
            _particles = null;
        }
    }
}

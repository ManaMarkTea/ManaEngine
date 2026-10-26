using ManaGraphics.Code.Graphics.Resources;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>
    /// Keeps a level's light probes baked (ManaWebGpuProbeVolume) ahead of the scene pass, after the environment
    /// capture so the sky they see is this frame's; binds new harmonics into the frame group when they are remade.
    /// </summary>
    internal sealed unsafe class ManaWebGpuProbeVolumePass : ManaWebGpuRenderPass
    {
        private readonly ManaWebGpuSceneBindings _bindings;

        public ManaWebGpuProbeVolumePass(ManaWebGpuDevice gpu, ManaWebGpuSceneBindings bindings)
            : base(gpu)
        {
            _bindings = bindings;
            Volume = new ManaWebGpuProbeVolume(gpu);
        }

        public ManaWebGpuProbeVolume Volume { get; }

        public override string ProfileName => "GPU: Probe Volume";
        public override int PipelineCount => Volume.PipelineCount;
        public override int ShaderModuleCount => Volume.PipelineCount;

        public override void Prepare(ManaWebGpuFrame frame)
        {
            if (Volume.Update(frame.Encoder, frame.Scene, frame.EnvironmentMap))
            {
                _bindings.SetProbeVolume(Volume.Red, Volume.Green, Volume.Blue);
            }
        }

        public override void Dispose()
        {
            Volume.Dispose();
        }
    }
}

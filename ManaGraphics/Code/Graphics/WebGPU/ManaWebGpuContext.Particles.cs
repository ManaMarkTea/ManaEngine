using ManaCore.Diagnostics;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics;

public unsafe sealed partial class ManaWebGpuContext
{
    private ManaWebGpuParticles? _particles;
    public long LastGpuParticleCount { get; private set; }
    public long RequestedGpuParticleCount { get; private set; }

    private static bool UseMobileRendering
    {
        get
        {
#if MANA_BROWSER
            return Browser.ManaBrowserInterop.IsMobileRendering();
#else
            return false;
#endif
        }
    }

    /// <summary>The sky shader: 0 simple gradients, 1 those plus the real star catalogue, 2 the full atmosphere.</summary>
    private static int SkyMode
    {
        get
        {
#if MANA_BROWSER
            return Browser.ManaBrowserInterop.GetSkyMode();
#else
            return 2;
#endif
        }
    }

    private void DrawGpuParticles(RenderPassEncoder* pass)
    {
        LastGpuParticleCount = 0;
        RequestedGpuParticleCount = 0;
        if (_activeRenderScene == null || _activeRenderScene.GpuParticles.Count == 0) return;
        foreach (var emitter in _activeRenderScene.GpuParticles) RequestedGpuParticleCount += Math.Max(0, emitter.Count);
        var budget = -1;
#if MANA_BROWSER
        budget = Browser.ManaBrowserInterop.GetGpuParticleBudget();
#endif
        var density = budget < 0 || RequestedGpuParticleCount <= budget ? 1.0 : (double)budget / RequestedGpuParticleCount;
        using (ManaFrameProfiler.Measure("GPU: Particle Draw Encode"))
        {
            _particles ??= new ManaWebGpuParticles(_api, _device, _queue, GetSceneColorFormat(), _depthFormat,
                ResolveAssetPath(Path.Combine("Assets", "Shaders", "WebGpu", "particles.wgsl")));
            LastGpuParticleCount = _particles.Draw(pass, _activeRenderScene.GpuParticles, _activeRenderScene.Camera,
                GetAspectCorrectedProjection(_activeRenderScene.Camera), _activeRenderScene.Wind, density);
        }
    }
}

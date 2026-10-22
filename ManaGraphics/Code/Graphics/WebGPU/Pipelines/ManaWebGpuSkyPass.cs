using System.Numerics;
using ManaCore.Graphics;
using Silk.NET.WebGPU;
using ManaGraphics.Code.Graphics.Renderers;

namespace ManaGraphics.Code.Graphics.Pipelines
{
    /// <summary>
    /// The sky, drawn by the material pass behind the opaque meshes (<see cref="DrawSky"/>) so covered pixels never run its
    /// shader; skipped until the environment map exists.
    /// </summary>
    internal sealed unsafe class ManaWebGpuSkyPass : ManaWebGpuRenderPass
    {
        private ManaWebGpuSkybox? _skybox;

        public ManaWebGpuSkyPass(ManaWebGpuDevice gpu)
            : base(gpu)
        {
        }

        public override string ProfileName => "GPU: Sky";
        public override int PipelineCount => _skybox != null ? 1 : 0;

        /// <summary>The sky shader: 0 simple gradients, 1 those plus the real star catalogue, 2 the full atmosphere.</summary>
        public static int Mode
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

        public static string ShaderPath => ManaWebGpuDevice.ResolveAssetPath(
            ManaWebGpuDevice.ShaderPath(Mode == 2 ? "skybox.wgsl" : "skybox_mobile.wgsl"));

        public static string? MoonTexturePath => ManaWebGpuDevice.TryResolveAssetPath(Path.Combine("Assets", "Textures", "Sky", "moon_lroc_1k.jpg"));

        // A still sky's sun sits where the scene's own sun is, if it has one (its direction is the way the light
        // travels); a day-night cycle moves its own.
        public static Vector3 TowardSun(ManaRenderScene scene)
        {
            return scene.HasSun && scene.Sky.Mode != ManaSkyMode.DayNightCycle ? -scene.SunDirection : scene.Sky.TowardSun;
        }

        public void DrawSky(ManaWebGpuFrame frame, RenderPassEncoder* pass)
        {
            if (frame.EnvironmentMap == null)
            {
                return;
            }

            _skybox ??= new ManaWebGpuSkybox(
                Gpu, Gpu.DepthFormat,
                ShaderPath,
                moonPath: MoonTexturePath,
                starCatalogue: Mode >= 1);

            var scene = frame.Scene;
            _skybox.Draw(
                pass,
                frame.SceneColorFormat,
                frame.Bindings.ClampSampler,
                frame.EnvironmentMap,
                scene.Camera,
                frame.Projection,
                TowardSun(scene),
                scene.Sky,
                ManaSky.Seconds);
        }

        public override void Dispose()
        {
            _skybox?.Dispose();
            _skybox = null;
        }
    }
}

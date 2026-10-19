using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.Maths;
using ManaGraphics.Code.Graphics.Overlays;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        private void CreateDemoMeshPipeline()
        {
            ManaConsole.WriteDiagnostic("Creating WebGPU mesh pipeline.");

            var renderScene = _renderSceneProvider?.Invoke() ?? ManaWebGpuDemoScene.Create();

            _activeRenderScene = renderScene;
            // The frame group binds the shadow map, so the map comes first.
            _shadowMap = new ManaWebGpuShadowMap(_gpu);
            _bindings = new ManaWebGpuSceneBindings(_gpu, _shadowMap.ArrayView, _shadowMap.Sampler);
            AddPass(new ManaWebGpuShadowPass(_gpu, _bindings, _shadowMap));
            CreateHdrPostProcessor();
            var sceneColor = GetSceneColorFormat();
            AddPass(new ManaWebGpuMaterialPass(_gpu, _bindings, sceneColor));
            _particlePass = AddPass(new ManaWebGpuParticlePass(_gpu, sceneColor));
            AddPass(new ManaWebGpuWireframeOverlay(_gpu, _bindings, sceneColor));
            AddPass(new ManaWebGpuNormalLinesOverlay(_gpu, _bindings, sceneColor));
            AddPass(new ManaWebGpuOutlinePass(_gpu, _bindings));

            ManaConsole.WriteDiagnostic("WebGPU mesh pipeline ready.");
            if (renderScene.Meshes.Count > 0)
            {
                UploadSceneMeshes(renderScene);
            }
        }

        private void CreateHdrPostProcessor()
        {
            if (_hdrPostProcessor != null)
            {
                return;
            }

            var shaderPath = ManaWebGpuDevice.ResolveAssetPath(ManaWebGpuDevice.ShaderPath("hdr_tonemap.wgsl"));
            _hdrPostProcessor = new ManaWebGpuHdrPostProcessor(_api, _device, _surfaceFormat, shaderPath);
            _hdrPostProcessor.Resize(_framebufferSize);
        }

        private TextureFormat GetSceneColorFormat()
        {
            return _hdrPostProcessor?.SceneFormat ?? _surfaceFormat;
        }

    }
}

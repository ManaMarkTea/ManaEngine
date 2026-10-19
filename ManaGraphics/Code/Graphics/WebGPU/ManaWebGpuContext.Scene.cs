using System.Runtime.InteropServices;
using System.Numerics;
using System.Text;
using ManaGraphics.Code.Diagnostics;
using ManaCore.Graphics;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
#if !MANA_BROWSER
using Silk.NET.WebGPU.Extensions.WGPU;
#endif
using Silk.NET.Windowing;
using Silk.NET.Input;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;
using ManaCore.Diagnostics;

namespace ManaGraphics.Code.Graphics
{
    public unsafe sealed partial class ManaWebGpuContext : IDisposable
    {
        /// <summary>
        /// Activates a loaded environment map at the frame boundary. Meshes bound to the old one are released and
        /// come back against the new textures: the provider's scene on its next refresh, or a provider-less demo
        /// scene uploaded again here -- without that, its meshes vanished the moment the first map landed.
        /// </summary>
        public void CheckPendingEnvironmentMap()
        {
            if (_disposed || _environment == null || !_environment.ActivatePending(_activeRenderScene))
            {
                return;
            }

            _meshes?.ReleaseAll();
            if (_renderSceneProvider == null && _activeRenderScene != null && _bindings != null)
            {
                _meshes!.Upload(_activeRenderScene, _bindings);
            }
        }

        // Also while scene work is suspended: the loading screen waits on the environment map, so it must start
        // without the mesh upload RefreshRenderScene would otherwise do first.
        private void EnsureEnvironmentInitializationStarted()
        {
            _activeRenderScene ??= _renderSceneProvider?.Invoke();
            if (_activeRenderScene != null)
            {
                _environment?.EnsureMap(_activeRenderScene);
            }
        }

        private void RefreshRenderScene()
        {
            var renderScene = _renderSceneProvider?.Invoke();
            // Without scene pipelines there is nothing to draw meshes with, or to build their bindings from.
            if (renderScene == null || _bindings == null)
            {
                return;
            }

            IReadOnlyList<ManaRenderMeshInstance> renderableMeshes;
            using (ManaFrameProfiler.Measure("GPU: Gather Renderables"))
            {
                renderableMeshes = ManaWebGpuSceneMeshes.GetRenderableMeshes(renderScene);
            }

            _activeRenderScene = renderScene;
            using (ManaFrameProfiler.Measure("GPU: Reconcile Scene Meshes"))
            {
                _meshes!.Reconcile(renderableMeshes, renderScene, _bindings!);
            }
        }
    }
}

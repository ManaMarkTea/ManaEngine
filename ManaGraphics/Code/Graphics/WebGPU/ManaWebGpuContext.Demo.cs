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
            CreateSharedPipelineLayouts();
            CreateShadowPassResources();
            CreateHdrPostProcessor();
            
            // Pre-compile default pipeline
            GetOrCreatePipeline("ManaPbrShader");
            
            ManaConsole.WriteDiagnostic("WebGPU mesh pipeline ready.");
            if (renderScene.Meshes.Count > 0)
            {
                UploadSceneMeshes(renderScene);
            }
        }

        private static string ResolveAssetPath(string relativePath)
        {
            return TryResolveAssetPath(relativePath)
                ?? throw new FileNotFoundException($"Could not locate asset file '{relativePath}'.");
        }

        /// <summary>The file, or null -- for one that may legitimately be absent, like the web build's environment photo.</summary>
        private static string? TryResolveAssetPath(string relativePath)
        {
            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, relativePath),
                Path.Combine(Environment.CurrentDirectory, "Mana", relativePath),
                Path.Combine(Environment.CurrentDirectory, "..", "Mana", relativePath),
                Path.Combine(AppContext.BaseDirectory, relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Mana", relativePath)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return null;
        }

        private void DrawDemoMesh(RenderPassEncoder* renderPass)
        {
            if (_sceneMeshes.Count == 0)
            {
                return;
            }

            // Group 0 (frame globals) is identical for every mesh this frame -- bind it once here
            // rather than once per mesh inside DrawSceneMesh.
            if (_frameBindGroup != null)
            {
                _api.RenderPassEncoderSetBindGroup(renderPass, 0, _frameBindGroup, 0, null);
            }

            foreach (var sceneMesh in _sceneMeshes.Where(mesh => !mesh.Instance.IsTransparent && !mesh.Instance.AlwaysOnTop))
            {
                DrawSceneMesh(renderPass, sceneMesh);
            }

            var cameraPosition = _activeRenderScene?.Camera.Position ?? Vector3.Zero;
            foreach (var sceneMesh in _sceneMeshes
                .Where(mesh => mesh.Instance.IsTransparent && !mesh.Instance.AlwaysOnTop)
                .OrderByDescending(mesh => Vector3.DistanceSquared(
                    mesh.Instance.WorldTransform.Translation,
                    cameraPosition)))
            {
                DrawSceneMesh(renderPass, sceneMesh);
            }

            // Always-on-top handles are drawn in a separate pass with their own depth buffer.
        }

        private void DrawSceneMesh(RenderPassEncoder* renderPass, ManaWebGpuSceneMesh sceneMesh)
        {
            // Only actually hits the GPU when the mesh's transform/material changed since last frame
            // -- see ManaWebGpuSceneMesh.TryUpdateObjectUniform.
            sceneMesh.TryUpdateObjectUniform(_api, _queue, BuildObjectUniform(sceneMesh.Instance));

            if (sceneMesh.CustomUniformBuffer != null)
            {
                BuildCustomMaterialUniformData(sceneMesh.Instance, sceneMesh.CustomUniformLayout, sceneMesh.CustomUniformData);
                sceneMesh.TryUpdateCustomUniform(_api, _queue, sceneMesh.CustomUniformData);
            }

            var pipeline = GetOrCreatePipeline(
                sceneMesh.Instance.ShaderName,
                sceneMesh.Instance.IsTransparent,
                sceneMesh.Instance.DisableDepthWrite,
                // Only real alpha-blended materials (glTF glass etc) self-cull -- not the intrinsically
                // transparent shaders (water/liquid/lightning), which stay OpacityMode.Auto/Opaque at the
                // material level and want both faces for their own volumetric/surface effects.
                cullBackFaces: sceneMesh.Instance.OpacityMode is ManaOpacityMode.Blend or ManaOpacityMode.Additive,
                alwaysOnTop: sceneMesh.Instance.AlwaysOnTop,
                additiveBlend: sceneMesh.Instance.OpacityMode == ManaOpacityMode.Additive);
            if (pipeline != null)
            {
                _api.RenderPassEncoderSetPipeline(renderPass, pipeline);
            }

            // Group 0 must be (re)bound AFTER SetPipeline, not once up front in DrawDemoMesh:
            // setPipeline with a layout incompatible with the currently-bound one unsets every bind
            // group from the first incompatible index onward. The skybox draws immediately before
            // this and its group 0 is {uniform, sampler, texture_cube} vs. this pipeline's
            // {uniform} -- incompatible at index 0, so switching to this pipeline drops group 0
            // (and 1 and 2) on the floor. Binding it here matches what DrawOverlays already does
            // correctly (SetPipeline first, then group 0).
            if (_frameBindGroup != null)
            {
                _api.RenderPassEncoderSetBindGroup(renderPass, 0, _frameBindGroup, 0, null);
            }

            if (sceneMesh.ObjectBindGroup != null)
            {
                _api.RenderPassEncoderSetBindGroup(renderPass, 1, sceneMesh.ObjectBindGroup, 0, null);
            }

            if (sceneMesh.CustomBindGroup != null)
            {
                _api.RenderPassEncoderSetBindGroup(renderPass, 2, sceneMesh.CustomBindGroup, 0, null);
            }

            sceneMesh.Mesh.Draw(renderPass);
        }

        // View-option overlay (see ManaIrradianceSettings.WireframeEnabled / IrradianceSettingsPanelView):
        // draws every resident mesh's edges as thin lines on top of the just-rendered opaque geometry,
        // in the SAME render pass as DrawDemoMesh (called right after it, before that pass ends) so the
        // depth buffer the opaque pass just wrote is what these lines depth-test against -- correctly
        // occluded by anything in front, without needing a separate render target the way the outline
        // mask pass does.
        // One loop for every debug overlay (see Overlays/ManaWebGpuOverlayPass). Adding one is a new
        // subclass plus an entry in _overlayPasses, not more methods here.
        private void DrawOverlays(RenderPassEncoder* renderPass)
        {
            var settings = _activeRenderScene?.IrradianceSettings;
            if (settings == null || _sceneMeshes.Count == 0)
            {
                return;
            }

            foreach (var overlay in _overlayPasses)
            {
                if (!overlay.IsEnabled(settings))
                {
                    continue;
                }

                var pipeline = GetOrCreateOverlayPipeline(overlay);
                if (pipeline == null)
                {
                    continue;
                }

                _api.RenderPassEncoderSetPipeline(renderPass, pipeline);
                if (_frameBindGroup != null)
                {
                    _api.RenderPassEncoderSetBindGroup(renderPass, 0, _frameBindGroup, 0, null);
                }

                foreach (var sceneMesh in _sceneMeshes)
                {
                    if (sceneMesh.ObjectBindGroup != null)
                    {
                        _api.RenderPassEncoderSetBindGroup(renderPass, 1, sceneMesh.ObjectBindGroup, 0, null);
                    }

                    overlay.DrawMesh(_api, _device, _queue, renderPass, sceneMesh);
                }
            }
        }

        private void DrawOutlineMask(CommandEncoder* commandEncoder)
        {
            // Explicit ternary rather than `?.` -- see the matching comment in ManaWebGpuContext.ClearFrame:
            // `?.` on a pointer-typed member is CS8978 ("cannot be made nullable") and only some Roslyn
            // versions actually diagnose it.
            var outlineMaskView = _hdrPostProcessor != null ? _hdrPostProcessor.OutlineMaskView : null;
            if (commandEncoder == null || outlineMaskView == null || _depthTextureView == null)
            {
                return;
            }

            var colorAttachment = new RenderPassColorAttachment
            {
                View = outlineMaskView,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color { R = 0.0, G = 0.0, B = 0.0, A = 0.0 }
            };

            var depthAttachment = new RenderPassDepthStencilAttachment
            {
                View = _depthTextureView,
                DepthLoadOp = LoadOp.Undefined,
                DepthStoreOp = StoreOp.Undefined,
                DepthClearValue = 0.0f,
                DepthReadOnly = true,
                StencilLoadOp = LoadOp.Undefined,
                StencilStoreOp = StoreOp.Undefined,
                StencilReadOnly = true
            };

            var descriptor = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
                DepthStencilAttachment = &depthAttachment
            };

            var renderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &descriptor);
            try
            {
                // Ordinary meshes use scene depth; handles use their separate depth attachment below.
                _api.RenderPassEncoderSetPipeline(renderPass, GetOrCreateOutlineMaskPipeline());

                // Reuses the frame (group 0) and object (group 1) bind groups already made current by
                // DrawDemoMesh's main pass this frame -- no uniform rewrite needed here at all, since the
                // main pass already dirty-checked/uploaded whatever changed.
                if (_frameBindGroup != null)
                {
                    _api.RenderPassEncoderSetBindGroup(renderPass, 0, _frameBindGroup, 0, null);
                }

                foreach (var sceneMesh in _sceneMeshes)
                {
                    if (!sceneMesh.Instance.OutlineEnabled || sceneMesh.Instance.AlwaysOnTop)
                    {
                        continue;
                    }

                    _api.RenderPassEncoderSetBindGroup(renderPass, 1, sceneMesh.ObjectBindGroup, 0, null);
                    sceneMesh.Mesh.Draw(renderPass);
                }

                if (_sceneMeshes.Any(mesh => mesh.Instance.OutlineEnabled && mesh.Instance.AlwaysOnTop))
                {
                    _api.RenderPassEncoderEnd(renderPass);
                    _api.RenderPassEncoderRelease(renderPass);
                    colorAttachment.LoadOp = LoadOp.Load;
                    depthAttachment.View = _handleDepthTextureView;
                    renderPass = _api.CommandEncoderBeginRenderPass(commandEncoder, &descriptor);
                    _api.RenderPassEncoderSetPipeline(renderPass, GetOrCreateOutlineMaskPipeline());
                    if (_frameBindGroup != null)
                    {
                        _api.RenderPassEncoderSetBindGroup(renderPass, 0, _frameBindGroup, 0, null);
                    }

                    foreach (var sceneMesh in _sceneMeshes)
                    {
                        if (!sceneMesh.Instance.OutlineEnabled || !sceneMesh.Instance.AlwaysOnTop)
                        {
                            continue;
                        }

                        _api.RenderPassEncoderSetBindGroup(renderPass, 1, sceneMesh.ObjectBindGroup, 0, null);
                        sceneMesh.Mesh.Draw(renderPass);
                    }
                }

                _api.RenderPassEncoderEnd(renderPass);
            }
            finally
            {
                _api.RenderPassEncoderRelease(renderPass);
            }
        }

        private void CreateHdrPostProcessor()
        {
            if (_hdrPostProcessor != null)
            {
                return;
            }

            var shaderPath = ResolveAssetPath(Path.Combine("Assets", "Shaders", "WebGpu", "hdr_tonemap.wgsl"));
            _hdrPostProcessor = new ManaWebGpuHdrPostProcessor(_api, _device, _surfaceFormat, shaderPath);
            _hdrPostProcessor.Resize(_framebufferSize);
        }

        private TextureFormat GetSceneColorFormat()
        {
            return _hdrPostProcessor?.SceneFormat ?? _surfaceFormat;
        }

    }
}

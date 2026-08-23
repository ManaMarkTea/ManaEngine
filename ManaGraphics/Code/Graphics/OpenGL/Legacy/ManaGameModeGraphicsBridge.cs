using System.Numerics;
using Mana.Code.Components;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using ManaCore.Graphics;
#if !MANA_BROWSER
using Silk.NET.OpenGL;
#endif

namespace Mana.Code.Graphics
{
    public static class ManaGameModeGraphicsBridge
    {
        public static void ClearLoadingFrame(ManaContext context, ManaGraphicsBackendKind backend)
        {
#if !MANA_BROWSER
            if (backend != ManaGraphicsBackendKind.OpenGL)
            {
                return;
            }

            var gl = context.RequireOpenGl();
            gl.ClearColor(0.12f, 0.14f, 0.18f, 1.0f);
            gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
#endif
        }

        public static void DrawWorld(ManaContext context, ManaGraphicsBackendKind backend)
        {
#if !MANA_BROWSER
            if (backend == ManaGraphicsBackendKind.OpenGL)
            {
                // Services any SampleHeightsAsync requests queued from background threads (see
                // ManaOpenGlGraphicsDevice/ManaOpenGlHeightSampleCompute) -- must run on this thread since
                // it owns the GL context. No-op until the compute shader has actually been used once.
                (context.GraphicsDevice as ManaOpenGlGraphicsDevice)?.ProcessPendingHeightSampleRequests();
                context.World.Draw();
            }
#endif
        }

#if !MANA_BROWSER
        private static ManaOpenGlUiRenderer? _openGlUiRenderer;
        private static bool _openGlUiRendererFailed;
#endif

        /// <summary>
        /// Draws the ManaUI tree on the OpenGL backend. The WebGPU path submits inside its own frame, so
        /// this is a no-op there.
        /// <para>
        /// Created lazily, and dropped permanently if it cannot be built, so a project that never uses
        /// ManaUI pays nothing and a shader failure reports once instead of every frame.
        /// </para>
        /// </summary>
        public static void DrawManaUi(
            ManaContext context,
            ManaGraphicsBackendKind backend,
            ManaUI.UiRoot? root)
        {
#if !MANA_BROWSER
            if (backend != ManaGraphicsBackendKind.OpenGL || root == null)
            {
                return;
            }

            // Taken from the context rather than the caller: BaseGameMode._Window is only assigned on the
            // OpenGL load path, so reading it at the call site threw on WebGPU before this backend check
            // could reject the call. ManaContext.Window is set by both paths.
            var window = context.Window;
            if (window == null)
            {
                return;
            }

            var framebufferSize = new Vector2(window.FramebufferSize.X, window.FramebufferSize.Y);

            if (_openGlUiRendererFailed)
            {
                return;
            }

            if (_openGlUiRenderer == null)
            {
                try
                {
                    _openGlUiRenderer = new ManaOpenGlUiRenderer(context.gl);
                }
                catch (Exception ex)
                {
                    // Latched, not just logged: without the flag this retries every frame and floods the
                    // log with the same failure at frame rate.
                    _openGlUiRendererFailed = true;
                    ManaCore.Diagnostics.ManaLog.WriteDiagnostic(
                        $"ManaUI OpenGL renderer failed to initialise, disabling it: {ex.Message}");
                    return;
                }
            }

            _openGlUiRenderer.Render(root.DrawList, root.Atlas, root.Size, framebufferSize);
#endif
        }

        public static void DisposeManaUi()
        {
#if !MANA_BROWSER
            _openGlUiRenderer?.Dispose();
            _openGlUiRenderer = null;
            _openGlUiRendererFailed = false;
#endif
        }

        public static void InitializeRendererForLoading(ManaContext context, ManaGraphicsBackendKind backend)
        {
#if !MANA_BROWSER
            if (backend != ManaGraphicsBackendKind.OpenGL)
            {
                return;
            }

            context.DeferGpuUploads = false;
            context.World.Renderer.Initialize();
            context.DeferGpuUploads = true;
#endif
        }

        public static void FinalizeGpuUploads(ManaContext context, ManaGraphicsBackendKind backend)
        {
            context.DeferGpuUploads = false;
            foreach (var scene in context.World.Scenes)
            {
                foreach (var rootNode in scene.Children)
                {
                    FinalizeSceneNode(context, backend, rootNode);
                }
            }

            foreach (var material in context.World.Materials)
            {
                FinalizeMaterial(context, backend, material);
            }
        }

        public static void Dispose(ManaContext context, IManaImGuiController? imGui)
        {
            imGui?.Dispose();
            context.Input?.Dispose();
#if !MANA_BROWSER
            if (context.GraphicsBackend == ManaGraphicsBackendKind.OpenGL)
            {
                context.RequireOpenGl().Dispose();
            }
#endif
        }

        private static void FinalizeSceneNode(
            ManaContext context,
            ManaGraphicsBackendKind backend,
            ManaSceneNode node)
        {
            foreach (var component in node.Components)
            {
                if (component is not MeshRendererComponent meshRenderer)
                {
                    continue;
                }

                var mesh = meshRenderer.Mesh;
                if (mesh != null)
                {
#if !MANA_BROWSER
                    if (mesh.VertexBufferObject == null &&
                        mesh.CpuData != null &&
                        backend == ManaGraphicsBackendKind.OpenGL)
                    {
                        var gpuMesh = context.RequireOpenGlDevice().CreateMesh(context, mesh.CpuData);
                        if (gpuMesh is ManaOpenGlMesh openGlMesh)
                        {
                            mesh.VertexBufferObject = openGlMesh.VertexBufferObject;
                        }
                    }
#endif

                    foreach (var material in mesh.Materials)
                    {
                        FinalizeMaterial(context, backend, material);
                    }
                }

                if (meshRenderer.Material != null)
                {
                    FinalizeMaterial(context, backend, meshRenderer.Material);
                }
            }

            foreach (var child in node.Children)
            {
                FinalizeSceneNode(context, backend, child);
            }
        }

        private static void FinalizeMaterial(
            ManaContext context,
            ManaGraphicsBackendKind backend,
            ManaMaterial material)
        {
#if !MANA_BROWSER
            if (backend == ManaGraphicsBackendKind.OpenGL && material.Shader == null)
            {
                material.Shader = new ManaPbrShader(context);
            }
#endif

            FinalizeTexture(material.Albedo);
            FinalizeTexture(material.Normal);
            FinalizeTexture(material.Emission);
            FinalizeTexture(material.Metallic);
            FinalizeTexture(material.Roughness);
            FinalizeTexture(material.Occlusion);

            foreach (var channel in material.Channels.Values)
            {
                FinalizeTexture(channel.Texture);
            }
        }

        private static void FinalizeTexture(ManaTexture? texture)
        {
            if (texture is { IsLoaded: false })
            {
                texture.Load();
            }
        }
    }
}

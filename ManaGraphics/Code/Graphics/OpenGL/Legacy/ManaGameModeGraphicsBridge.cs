using Mana.Code.Components;
using Mana.Code.Materials;
using Mana.Code.Nodes;
using ManaCore.Graphics;
using Silk.NET.OpenGL;

namespace Mana.Code.Graphics
{
    public static class ManaGameModeGraphicsBridge
    {
        public static void ClearLoadingFrame(ManaContext context, ManaGraphicsBackendKind backend)
        {
            if (backend != ManaGraphicsBackendKind.OpenGL)
            {
                return;
            }

            var gl = context.RequireOpenGl();
            gl.ClearColor(0.12f, 0.14f, 0.18f, 1.0f);
            gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));
        }

        public static void DrawWorld(ManaContext context, ManaGraphicsBackendKind backend)
        {
            if (backend == ManaGraphicsBackendKind.OpenGL)
            {
                // Services any SampleHeightsAsync requests queued from background threads (see
                // ManaOpenGlGraphicsDevice/ManaOpenGlHeightSampleCompute) -- must run on this thread since
                // it owns the GL context. No-op until the compute shader has actually been used once.
                (context.GraphicsDevice as ManaOpenGlGraphicsDevice)?.ProcessPendingHeightSampleRequests();
                context.World.Draw();
            }
        }

        public static void InitializeRendererForLoading(ManaContext context, ManaGraphicsBackendKind backend)
        {
            if (backend != ManaGraphicsBackendKind.OpenGL)
            {
                return;
            }

            context.DeferGpuUploads = false;
            context.World.Renderer.Initialize();
            context.DeferGpuUploads = true;
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
            if (context.GraphicsBackend == ManaGraphicsBackendKind.OpenGL)
            {
                context.RequireOpenGl().Dispose();
            }
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
            if (backend == ManaGraphicsBackendKind.OpenGL && material.Shader == null)
            {
                material.Shader = new ManaPbrShader(context);
            }

            FinalizeTexture(material.Albedo);
            FinalizeTexture(material.Normal);
            FinalizeTexture(material.Emission);
            FinalizeTexture(material.Metallic);
            FinalizeTexture(material.Roughness);
            FinalizeTexture(material.Ambient);

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

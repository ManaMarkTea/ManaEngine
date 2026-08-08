using System.Numerics;
using ManaCore.Graphics;
using Silk.NET.OpenGL;

namespace Mana.Code.Graphics
{
    public sealed class ManaOpenGlGraphicsDevice : IOpenGlGraphicsDevice, IHeightSamplingComputeDevice, IDisposable
    {
        // Lazily created on first use (GL is already available at construction time, unlike WebGPU's
        // context, so there's no chicken-and-egg wiring needed here -- see ManaWebGpuGraphicsDevice for
        // the contrast).
        private ManaOpenGlHeightSampleCompute? _heightSampleCompute;

        public ManaOpenGlGraphicsDevice(GL gl)
        {
            Gl = gl;
        }

        public ManaGraphicsBackendKind Backend => ManaGraphicsBackendKind.OpenGL;
        public string DisplayName => "OpenGL";
        public GL Gl { get; }

        public IManaImmediateGpuMesh CreateMesh(ManaContext context, ManaMeshCpuData meshData, BufferUsageARB usageHint = BufferUsageARB.StaticDraw)
        {
            var vertexBufferObject = new ManaVertexBufferObject(
                context,
                meshData.Indexes,
                meshData.Positions,
                meshData.Normals,
                meshData.Colors,
                meshData.TextureCoordinates0,
                meshData.TextureCoordinates1,
                usageHint);
            vertexBufferObject.ConstructVBO();
            return new ManaOpenGlMesh(vertexBufferObject);
        }

        public Task<float[]> SampleHeightsAsync(
            ReadOnlyMemory<Vector2> lonLatPointsDegrees,
            IReadOnlyList<HeightSampleTileDescriptor> tilesNewestFirst)
        {
            _heightSampleCompute ??= new ManaOpenGlHeightSampleCompute(
                Gl,
                ResolveHeightSampleShaderPath());
            return _heightSampleCompute.SampleHeightsAsync(lonLatPointsDegrees, tilesNewestFirst);
        }

        // Called once per frame from the GL-context/main thread -- see
        // ManaGameModeGraphicsBridge.DrawWorld's OpenGL branch. No-op until SampleHeightsAsync has been
        // called at least once (compute shader compiled lazily).
        public void ProcessPendingHeightSampleRequests()
        {
            _heightSampleCompute?.ProcessPendingRequests();
        }

        // Mirrors ManaWebGpuContext.Demo.cs's ResolveAssetPath -- ManaGraphics' own Assets folder is
        // copied alongside the build output, so the simple candidates are normally what actually hits,
        // with the rest as fallback for less common launch working directories.
        private static string ResolveHeightSampleShaderPath()
        {
            const string relativePath = "Assets/Shaders/OpenGL/height_sample_compute.glsl";
            string[] candidates =
            {
                Path.Combine(Environment.CurrentDirectory, relativePath),
                Path.Combine(AppContext.BaseDirectory, relativePath),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ManaGraphics", relativePath)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            throw new FileNotFoundException($"Could not locate asset file '{relativePath}'.");
        }

        public void Dispose()
        {
            _heightSampleCompute?.Dispose();
            Gl.Dispose();
        }
    }
}

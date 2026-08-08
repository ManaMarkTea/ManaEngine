using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using ManaCore.Graphics;
using Silk.NET.OpenGL;

namespace Mana.Code.Graphics
{
    // OpenGL counterpart to ManaWebGpuHeightSampleCompute -- structurally simpler, since
    // glGetBufferSubData on a Shader Storage Buffer Object is defined to block the calling thread until
    // the GPU has produced correct data, so no async native callback machinery (unlike WebGPU's
    // BufferMapAsync) is needed at all. The only real constraint is that GL context calls must run on
    // whichever thread owns the context (the main thread) -- SampleHeightsAsync (callable from any
    // thread, matching IHeightSamplingComputeDevice's contract) just enqueues a request;
    // ProcessPendingRequests (called once per frame from the GL-context/main thread, see
    // ManaGameModeGraphicsBridge.DrawWorld's OpenGL branch) drains and actually dispatches.
    internal unsafe sealed class ManaOpenGlHeightSampleCompute : IDisposable
    {
        // Bounds are pre-projected into normalized Web Mercator space (not raw lon/lat degrees) -- see
        // this struct's field-level comment inline in height_sample_compute.glsl for why: computing the
        // lon/lat->Mercator projection (asinh+tan) once per tile here, instead of once per (point, tile)
        // pair inside the shader's hot loop, is what actually fixes the GPU hang this was built to avoid.
        [StructLayout(LayoutKind.Sequential)]
        private struct GpuTileDescriptor
        {
            public float MercXMin;
            public float MercXMax;
            public float MercYMin;
            public float MercYMax;
            public uint PixelOffset;
            public uint Width;
            public uint Height;
            public uint Padding0;
        }

        // Const MaxLatitudeDegrees is intentionally duplicated (not shared with e.g. SlippyMapMath) --
        // Mana doesn't reference ManaGeoTools (wrong dependency direction), and this needs to match
        // height_sample_compute.glsl's own MAX_LATITUDE_DEGREES constant exactly regardless.
        private const float MaxLatitudeDegrees = 85.0511287798f;

        // Mirrors height_sample_compute.glsl's own mercatorXy function exactly -- called once per tile
        // here (CPU), matching the shader's once-per-point call, instead of the shader ALSO doing it once
        // per tile inside its hot loop (the original, much slower design).
        private static Vector2 MercatorXy(float lonDegrees, float latDegrees)
        {
            var latClamped = Math.Clamp(latDegrees, -MaxLatitudeDegrees, MaxLatitudeDegrees);
            var latRad = latClamped * MathF.PI / 180f;
            var x = (lonDegrees + 180f) / 360f;
            var y = (1f - MathF.Asinh(MathF.Tan(latRad)) / MathF.PI) / 2f;
            return new Vector2(x, y);
        }

        private sealed class PendingRequest
        {
            public required ReadOnlyMemory<Vector2> Points;
            public required IReadOnlyList<HeightSampleTileDescriptor> Tiles;
            public required TaskCompletionSource<float[]> Tcs;
        }

        // Caps how many requests drain in one frame so a burst of simultaneous patch requests doesn't
        // stall one frame -- mirrors this session's own "don't do unbounded per-frame GPU work" lesson
        // from the auto-LOD throttling work.
        private const int MaxRequestsPerFrame = 2;

        // Matches `layout(location = 0) uniform uint tileCount;` in height_sample_compute.glsl.
        private const int TileCountUniformLocation = 0;

        private readonly GL _gl;
        private readonly ManaOpenGlComputeShader _computeShader;
        private readonly ConcurrentQueue<PendingRequest> _pendingRequests = new();

        public ManaOpenGlHeightSampleCompute(GL gl, string shaderPath)
        {
            _gl = gl;
            var source = File.ReadAllText(shaderPath);
            _computeShader = new ManaOpenGlComputeShader(gl, "Height Sample", source);
        }

        // Callable from any thread -- just enqueues. See ProcessPendingRequests for where the real work
        // happens.
        public Task<float[]> SampleHeightsAsync(
            ReadOnlyMemory<Vector2> lonLatPointsDegrees,
            IReadOnlyList<HeightSampleTileDescriptor> tilesNewestFirst)
        {
            var tcs = new TaskCompletionSource<float[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests.Enqueue(new PendingRequest
            {
                Points = lonLatPointsDegrees,
                Tiles = tilesNewestFirst,
                Tcs = tcs
            });
            return tcs.Task;
        }

        // Must be called once per frame from the thread that owns the GL context.
        public void ProcessPendingRequests()
        {
            var drained = 0;
            while (drained < MaxRequestsPerFrame && _pendingRequests.TryDequeue(out var request))
            {
                drained++;
                try
                {
                    request.Tcs.TrySetResult(DispatchAndReadbackSync(request.Points.Span, request.Tiles));
                }
                catch (Exception ex)
                {
                    request.Tcs.TrySetException(ex);
                }
            }
        }

        public void Dispose()
        {
            while (_pendingRequests.TryDequeue(out var request))
            {
                request.Tcs.TrySetCanceled();
            }

            _computeShader.Dispose();
        }

        private float[] DispatchAndReadbackSync(ReadOnlySpan<Vector2> points, IReadOnlyList<HeightSampleTileDescriptor> tilesNewestFirst)
        {
            if (points.Length == 0)
            {
                return Array.Empty<float>();
            }

            // TEMP verification instrumentation -- remove once the "20 seconds for one dispatch" report
            // is root-caused.
            var totalSw = System.Diagnostics.Stopwatch.StartNew();

            var tileDescriptors = new GpuTileDescriptor[Math.Max(1, tilesNewestFirst.Count)];
            var heightDataFloatCount = 0;
            for (var i = 0; i < tilesNewestFirst.Count; i++)
            {
                var tile = tilesNewestFirst[i];
                // lat_max (north) maps to the SMALLER Mercator Y (top of a north-up image), lat_min
                // (south) to the larger one -- matches the shader's own documented convention.
                var mercMin = MercatorXy(tile.LonMin, tile.LatMax);
                var mercMax = MercatorXy(tile.LonMax, tile.LatMin);
                tileDescriptors[i] = new GpuTileDescriptor
                {
                    MercXMin = mercMin.X,
                    MercXMax = mercMax.X,
                    MercYMin = mercMin.Y,
                    MercYMax = mercMax.Y,
                    PixelOffset = (uint)heightDataFloatCount,
                    Width = (uint)tile.Width,
                    Height = (uint)tile.Height,
                    Padding0 = 0,
                };
                heightDataFloatCount += tile.Width * tile.Height;
            }

            var heightData = new float[Math.Max(1, heightDataFloatCount)];
            var writeOffset = 0;
            foreach (var tile in tilesNewestFirst)
            {
                tile.HeightData.Span.CopyTo(heightData.AsSpan(writeOffset));
                writeOffset += tile.HeightData.Length;
            }

            var prepMs = totalSw.ElapsedMilliseconds;

            var pointsBuffer = CreateStorageBuffer<Vector2>(points);
            var tilesBuffer = CreateStorageBuffer<GpuTileDescriptor>(tileDescriptors);
            var heightDataBuffer = CreateStorageBuffer<float>(heightData);
            var outputBuffer = CreateEmptyBuffer(points.Length * sizeof(float));

            _gl.UseProgram(_computeShader.Program);
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, pointsBuffer);
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, tilesBuffer);
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 2, heightDataBuffer);
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 3, outputBuffer);
            _gl.Uniform1(TileCountUniformLocation, (uint)tilesNewestFirst.Count);

            var workgroups = (uint)((points.Length + 63) / 64);
            _computeShader.Dispatch(new ManaComputeDispatchSize(workgroups, 1, 1));
            var dispatchMs = totalSw.ElapsedMilliseconds - prepMs;

            var heights = new float[points.Length];
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, outputBuffer);
            _gl.GetBufferSubData(BufferTargetARB.ShaderStorageBuffer, IntPtr.Zero, new Span<float>(heights));
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
            var readbackMs = totalSw.ElapsedMilliseconds - prepMs - dispatchMs;

            _gl.DeleteBuffer(pointsBuffer);
            _gl.DeleteBuffer(tilesBuffer);
            _gl.DeleteBuffer(heightDataBuffer);
            _gl.DeleteBuffer(outputBuffer);

            Console.WriteLine($"[HeightSampleTiming][OpenGL] points={points.Length} tiles={tilesNewestFirst.Count} prepMs={prepMs} dispatchMs={dispatchMs} readbackMs={readbackMs} totalMs={totalSw.ElapsedMilliseconds}");

            return heights;
        }

        private uint CreateStorageBuffer<T>(ReadOnlySpan<T> data) where T : unmanaged
        {
            _gl.GenBuffers(1, out uint buffer);
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, buffer);
            _gl.BufferData(BufferTargetARB.ShaderStorageBuffer, data, BufferUsageARB.StaticDraw);
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
            return buffer;
        }

        private uint CreateEmptyBuffer(int byteSize)
        {
            _gl.GenBuffers(1, out uint buffer);
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, buffer);
            _gl.BufferData(BufferTargetARB.ShaderStorageBuffer, (nuint)byteSize, null, BufferUsageARB.StaticDraw);
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, 0);
            return buffer;
        }
    }
}

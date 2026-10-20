using System.Collections.Concurrent;
using ManaGraphics.Code.Diagnostics;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Work handed in from any thread to run on the render thread at the next frame boundary: a background submit
    /// overlapping SurfacePresent deadlocks wgpu-native. Items check <see cref="IsClosed"/> so shutdown cancels them.
    /// </summary>
    internal sealed class ManaWebGpuRenderThreadQueue
    {
        private readonly ConcurrentQueue<Action> _work = new();
        private volatile bool _closed;

        public bool IsClosed => _closed;

        public void Enqueue(Action work)
        {
            _work.Enqueue(work);

            // The closing drain may already have run; run inline so a racing item still cancels.
            if (_closed)
            {
                Drain();
            }
        }

        /// <summary>Render thread only.</summary>
        public void Drain()
        {
            while (_work.TryDequeue(out var work))
            {
                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    ManaConsole.WriteDiagnostic($"[WebGPU] Deferred render-thread work failed: {ex}");
                }
            }
        }

        /// <summary>Shutdown: everything queued, now or later, runs once more and sees the queue closed.</summary>
        public void Close()
        {
            _closed = true;
            Drain();
        }
    }
}

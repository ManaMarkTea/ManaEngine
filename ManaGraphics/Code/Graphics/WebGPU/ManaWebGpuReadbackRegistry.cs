using System.Collections.Concurrent;

namespace ManaGraphics.Code.Graphics
{
    // Tracks in-flight ManaWebGpuBufferReadback requests. NOT part of the completion mechanism itself --
    // wgpu-native tracks pending buffer maps internally and invokes the native callback directly (see
    // ManaWebGpuBufferReadback.OnBufferMapped). This registry exists purely so ManaWebGpuContext.Dispose()
    // can find and cleanly fault any survivors before the device/queue go away (once polling stops, no
    // native callback will ever fire again, so an un-faulted awaiter would hang forever).
    internal sealed unsafe class ManaWebGpuReadbackRegistry
    {
        private readonly ConcurrentDictionary<ManaWebGpuBufferReadbackState, byte> _pending = new();

        public int PendingCount => _pending.Count;

        internal void Register(ManaWebGpuBufferReadbackState state)
        {
            _pending[state] = 0;
        }

        internal void Unregister(ManaWebGpuBufferReadbackState state)
        {
            _pending.TryRemove(state, out _);
        }

        // Called once, from ManaWebGpuContext.Dispose(), before _queue/_device are released.
        public void FaultAllPending()
        {
            foreach (var state in _pending.Keys.ToArray())
            {
                _pending.TryRemove(state, out _);
                if (!state.TryComplete())
                {
                    // The real native callback already won the race and is handling (or already handled)
                    // completion -- nothing left for teardown to do for this one.
                    continue;
                }

                state.Handle.Free();
                // Deliberately NOT calling BufferRelease here: _device is about to be (or already is)
                // released by the caller, and touching a buffer belonging to an already-torn-down device
                // is unsafe. wgpu-native cascades resource release when the owning Device is released, so
                // skipping the explicit release here is not a real leak.
                state.Tcs.TrySetCanceled();
            }
        }
    }
}

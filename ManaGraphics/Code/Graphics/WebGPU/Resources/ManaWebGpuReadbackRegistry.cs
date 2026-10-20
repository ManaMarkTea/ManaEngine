using System.Collections.Concurrent;
using Silk.NET.WebGPU;

namespace ManaGraphics.Code.Graphics.Resources
{
    // Native userdata is a never-reused ID, not a pointer to managed storage that shutdown can free.
    internal sealed class ManaWebGpuReadbackRegistry
    {
        private sealed record Request(ManaWebGpuReadbackRegistry Owner,
            Action<BufferMapAsyncStatus> Complete, Action<Exception?> Abort);

        private static long _nextId;
        private static readonly ConcurrentDictionary<nint, Request> Requests = new();
        private readonly object _gate = new();
        private readonly HashSet<nint> _pending = new();
        private bool _closed;

        public int PendingCount
        {
            get
            {
                lock (_gate)
                {
                    return _pending.Count;
                }
            }
        }

        // Admission, dispatch, callback cleanup and shutdown share a gate: none can outlive device teardown.
        internal void Register(Action<nint> dispatch, Action<BufferMapAsyncStatus> complete, Action<Exception?> abort)
        {
            lock (_gate)
            {
                if (_closed)
                {
                    abort(null);
                    return;
                }

                var serial = Interlocked.Increment(ref _nextId);
                if (serial <= 0 || serial > int.MaxValue)
                {
                    abort(new InvalidOperationException("WebGPU readback request IDs exhausted."));
                    return;
                }
                var id = (nint)serial;
                Requests[id] = new Request(this, complete, abort);
                _pending.Add(id);
                try
                {
                    dispatch(id);
                }
                catch (Exception ex)
                {
                    if (Remove(id))
                    {
                        abort(ex);
                    }
                }
            }
        }

        internal static void Complete(nint id, BufferMapAsyncStatus status)
        {
            if (!Requests.TryGetValue(id, out var request))
            {
                return;
            }
            lock (request.Owner._gate)
            {
                if (request.Owner.Remove(id))
                {
                    request.Complete(status);
                }
            }
        }

        private bool Remove(nint id)
        {
            _pending.Remove(id);
            return Requests.TryRemove(id, out _);
        }

        // Render thread, before releasing the device. Remove routing before abort can trigger a callback.
        public void FaultAllPending()
        {
            lock (_gate)
            {
                _closed = true;
                foreach (var id in _pending.ToArray())
                {
                    if (Requests.TryRemove(id, out var request))
                    {
                        _pending.Remove(id);
                        request.Abort(null);
                    }
                }
            }
        }
    }
}

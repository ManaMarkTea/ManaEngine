using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    // Per-request state kept alive via GCHandle while a native BufferMapAsync callback is in flight --
    // routing from the native callback back to the right request happens via userdata (a single shared
    // callback thunk services every in-flight request, see ManaWebGpuBufferReadback.OnBufferMappedThunk).
    internal sealed unsafe class ManaWebGpuBufferReadbackState
    {
        public required WebGPU Api { get; init; }
        public required WebGpuBuffer* StagingBuffer { get; init; }
        public required nuint ByteSize { get; init; }
        public required TaskCompletionSource<byte[]> Tcs { get; init; }
        public required ManaWebGpuReadbackRegistry Registry { get; init; }
        public GCHandle Handle;

        private int _completed;

        // CAS guard making the real native callback and ManaWebGpuReadbackRegistry.FaultAllPending's
        // teardown path mutually exclusive -- whichever reaches this first is the only one that frees
        // the GCHandle, touches the buffer, or completes the TCS. Prevents a double-free/double-complete
        // race if Dispose() runs while a map is still in flight.
        public bool TryComplete()
        {
            return Interlocked.CompareExchange(ref _completed, 1, 0) == 0;
        }
    }

    // Stateless helper for reading a GPU storage buffer back to the CPU asynchronously -- first use of
    // BufferUsage.Storage and GPU->CPU readback in this codebase, so see this file's (and the plan's) own
    // reasoning for why each piece is shaped the way it is, not just established local convention.
    internal static unsafe class ManaWebGpuBufferReadback
    {
        // A single, process-lifetime-rooted delegate services every in-flight request (no per-request
        // delegate allocation/pinning needed) -- routing to the correct request happens entirely through
        // the userdata pointer, mirroring how RequestAdapterCallback/RequestDeviceCallback are already
        // rooted as fields in ManaWebGpuContext.Device.cs, just static here since this callback needs no
        // instance state of its own.
        private static readonly BufferMapCallback OnBufferMappedThunk = OnBufferMapped;

        // Creates a MapRead|CopyDst staging buffer sized byteSize and encodes a copy from sourceBuffer
        // into it, into the CALLER's own (already-open) command encoder -- call this right after encoding
        // the compute dispatch that fills sourceBuffer, before CommandEncoderFinish, so the copy rides
        // along in the same QueueSubmit instead of forcing a second, wasted submit round-trip.
        public static WebGpuBuffer* EncodeCopyToStaging(
            WebGPU api,
            Device* device,
            CommandEncoder* commandEncoder,
            WebGpuBuffer* sourceBuffer,
            ulong byteSize)
        {
            var stagingBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
                Size = byteSize
            };
            var stagingBuffer = api.DeviceCreateBuffer(device, &stagingBufferDescriptor);
            if (stagingBuffer == null)
            {
                throw new InvalidOperationException("Failed to create WebGPU readback staging buffer.");
            }

            api.CommandEncoderCopyBufferToBuffer(commandEncoder, sourceBuffer, 0, stagingBuffer, 0, byteSize);
            return stagingBuffer;
        }

        // Callable from any thread. Must be called AFTER the command buffer containing the
        // EncodeCopyToStaging copy has actually been submitted (QueueSubmit). The returned Task's
        // continuations run on the ThreadPool, never inline on the main thread -- see the
        // RunContinuationsAsynchronously comment below.
        public static Task<byte[]> MapAsync(
            WebGPU api,
            WebGpuBuffer* stagingBuffer,
            ulong byteSize,
            ManaWebGpuReadbackRegistry registry)
        {
            // Without this flag, SetResult runs every awaiter's continuation synchronously, inline, on
            // whatever thread calls it -- which, per ManaWebGpuContext's DevicePoll-based pump, is the
            // MAIN THREAD, from inside ClearFrame. A background-thread awaiter's continuation would then
            // run reentrantly in the middle of the frame loop. With this flag, SetResult only *schedules*
            // the continuation onto the ThreadPool -- DevicePoll/ClearFrame keep going immediately.
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var state = new ManaWebGpuBufferReadbackState
            {
                Api = api,
                StagingBuffer = stagingBuffer,
                ByteSize = (nuint)byteSize,
                Tcs = tcs,
                Registry = registry
            };
            state.Handle = GCHandle.Alloc(state);
            registry.Register(state);

            api.BufferMapAsync(stagingBuffer, MapMode.Read, 0, (nuint)byteSize, OnBufferMappedThunk, (void*)GCHandle.ToIntPtr(state.Handle));
            return tcs.Task;
        }

        // Fires from inside WGPU.DevicePoll/InstanceProcessEvents -- i.e. on the main thread, reentrantly,
        // during ManaWebGpuContext.ClearFrame. Does the ENTIRE copy-out synchronously, inline, right here:
        // the caller never sees a raw mapped pointer at all, only an independent, already-copied array --
        // this sidesteps any "who owns the mapped range / when is unmap safe" ambiguity entirely. Payload
        // sizes here are a patch's height samples (tens of KB at most), so the synchronous copy costs well
        // under a microsecond on the main thread.
        private static void OnBufferMapped(BufferMapAsyncStatus status, void* userdata)
        {
            var handle = GCHandle.FromIntPtr((nint)userdata);
            // Teardown (FaultAllPending) frees the handle first; releasing the device then fires this callback late.
            if (handle.Target is not ManaWebGpuBufferReadbackState state)
            {
                return;
            }

            state.Registry.Unregister(state);

            if (!state.TryComplete())
            {
                // ManaWebGpuReadbackRegistry.FaultAllPending's teardown path already won this race.
                return;
            }

            handle.Free();

            if (status != BufferMapAsyncStatus.Success)
            {
                state.Api.BufferRelease(state.StagingBuffer);
                state.Tcs.TrySetException(new InvalidOperationException($"WebGPU buffer map failed: {status}"));
                return;
            }

            var mappedRange = state.Api.BufferGetMappedRange(state.StagingBuffer, 0, state.ByteSize);
            var bytes = new byte[state.ByteSize];
            new Span<byte>(mappedRange, (int)state.ByteSize).CopyTo(bytes);
            state.Api.BufferUnmap(state.StagingBuffer);
            state.Api.BufferRelease(state.StagingBuffer);
            state.Tcs.TrySetResult(bytes);
        }
    }
}

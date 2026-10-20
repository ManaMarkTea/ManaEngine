using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Resources
{
    // Staging ownership transfers here; callbacks and shutdown release it before completing the task.
    internal static unsafe class ManaWebGpuBufferReadback
    {
        // Browser delegates stay rooted; native/iOS use an ahead-of-time-compatible static entry point.
#if MANA_BROWSER
        private static readonly BufferMapCallback OnBufferMappedThunk = OnBufferMapped;
#else
        // A static native entry point rather than a delegate: iOS's ahead-of-time code cannot call back into one.
        private static readonly PfnBufferMapCallback OnBufferMappedThunk = new(&OnBufferMappedNative);

        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        private static void OnBufferMappedNative(BufferMapAsyncStatus status, void* userdata) => OnBufferMapped(status, userdata);
#endif

        // Encodes the copy into the caller's open encoder, so it rides the same submit as the work that fills the buffer.
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

        // Call after QueueSubmit. Retirement runs on the callback pump or shutdown thread, before device release.
        public static Task<byte[]> MapAsync(
            WebGPU api,
            WebGpuBuffer* stagingBuffer,
            ulong byteSize,
            ManaWebGpuReadbackRegistry registry,
            Action? releaseResources = null)
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var buffer = (nint)stagingBuffer;
            registry.Register(
                id => api.BufferMapAsync((WebGpuBuffer*)buffer, MapMode.Read, 0, (nuint)byteSize,
                    OnBufferMappedThunk, (void*)id),
                status =>
                {
                    try
                    {
                        byte[] bytes;
                        try
                        {
                            if (status != BufferMapAsyncStatus.Success)
                            {
                                throw new InvalidOperationException($"WebGPU buffer map failed: {status}");
                            }
                            var mapped = api.BufferGetMappedRange((WebGpuBuffer*)buffer, 0, (nuint)byteSize);
                            if (mapped == null)
                            {
                                throw new InvalidOperationException("WebGPU returned a null mapped range.");
                            }
                            bytes = new byte[checked((int)byteSize)];
                            new ReadOnlySpan<byte>(mapped, bytes.Length).CopyTo(bytes);
                        }
                        finally
                        {
                            Release(status == BufferMapAsyncStatus.Success);
                        }
                        tcs.TrySetResult(bytes);
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                },
                failure =>
                {
                    try
                    {
                        Release(unmap: true);
                        if (failure == null)
                        {
                            tcs.TrySetCanceled();
                        }
                        else
                        {
                            tcs.TrySetException(failure);
                        }
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                });
            return tcs.Task;

            void Release(bool unmap)
            {
                try
                {
                    try
                    {
                        if (unmap)
                        {
                            api.BufferUnmap((WebGpuBuffer*)buffer);
                        }
                    }
                    finally
                    {
                        api.BufferRelease((WebGpuBuffer*)buffer);
                    }
                }
                finally
                {
                    releaseResources?.Invoke();
                }
            }
        }

        private static void OnBufferMapped(BufferMapAsyncStatus status, void* userdata) =>
            ManaWebGpuReadbackRegistry.Complete((nint)userdata, status);
    }
}

using Silk.NET.Maths;
using Silk.NET.WebGPU;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace ManaGraphics.Code.Graphics
{
    // Captures the frame ClearFrame is about to present -- a copy of the swapchain texture that already
    // exists, not a new off-screen render like RenderThumbnailAsync's. Backs ManaVoxel's --command-file
    // automation (ManaCommandRunner), so a scripted run can save what actually got drawn to a real PNG
    // file instead of depending on OS-level screen capture, which has no access to this process's window
    // in a sandboxed/headless environment.
    public unsafe sealed partial class ManaWebGpuContext
    {
        private const uint ScreenshotBytesPerPixel = 4;

        // nint rather than WebGpuBuffer* -- see ThumbnailDrawResources's own nint fields in
        // ManaWebGpuContext.Thumbnail.cs for why: a pointer type can't be a generic type argument, and
        // both of these travel through a Nullable<T>.
        private readonly record struct PendingScreenshotRequest(string Path, TaskCompletionSource Completion, bool WithoutUi = false);

        private readonly record struct EncodedScreenshotCopy(
            nint OutputBuffer, ulong ByteCount, uint PaddedRowBytes, uint Width, uint Height, PendingScreenshotRequest Request);

        private PendingScreenshotRequest? _pendingScreenshot;

        /// <summary>
        /// Saves the next frame this context renders to <paramref name="path"/> as a PNG. Render-thread
        /// only (ManaCommandRunner's own caller already is), and only one capture in flight at a time.
        /// </summary>
        /// <param name="withoutUi">Leave ManaUI out of that frame: the scene alone, across the whole window.</param>
        public Task CaptureFrameAsync(string path, bool withoutUi = false)
        {
            if (_pendingScreenshot != null)
            {
                return Task.FromException(new InvalidOperationException("A screenshot is already in flight."));
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingScreenshot = new PendingScreenshotRequest(path, completion, withoutUi);
            return completion.Task;
        }

        // Called from ClearFrame right before CommandEncoderFinish, so the copy rides the same submit as
        // the rest of the frame -- the surface texture already carries TextureUsage.CopySrc (see
        // ConfigureSurface in ManaWebGpuContext.Resources.cs), so no extra reconfiguration is needed.
        private EncodedScreenshotCopy? EncodePendingScreenshotIfRequested(
            CommandEncoder* commandEncoder, Texture* frameTexture, Vector2D<int> framebufferSize)
        {
            if (_pendingScreenshot is not { } request)
            {
                return null;
            }

            _pendingScreenshot = null;

            var width = (uint)Math.Max(0, framebufferSize.X);
            var height = (uint)Math.Max(0, framebufferSize.Y);
            if (width == 0 || height == 0)
            {
                request.Completion.TrySetException(new InvalidOperationException("Cannot screenshot a zero-sized framebuffer."));
                return null;
            }

            var rowBytes = checked(width * ScreenshotBytesPerPixel);
            var paddedRowBytes = AlignTo(rowBytes, WebGpuCopyBytesPerRowAlignment);
            var byteCount = checked((ulong)paddedRowBytes * height);

            var outputBufferDescriptor = new BufferDescriptor
            {
                Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
                Size = byteCount
            };
            var outputBuffer = _api.DeviceCreateBuffer(_device, &outputBufferDescriptor);

            var copySource = new ImageCopyTexture
            {
                Texture = frameTexture,
                MipLevel = 0,
                Origin = new Origin3D(),
                Aspect = TextureAspect.All
            };
            var copyDestination = new ImageCopyBuffer
            {
                Buffer = outputBuffer,
                Layout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = paddedRowBytes,
                    RowsPerImage = height
                }
            };
            var copySize = new Extent3D { Width = width, Height = height, DepthOrArrayLayers = 1 };
            _api.CommandEncoderCopyTextureToBuffer(commandEncoder, &copySource, &copyDestination, &copySize);

            return new EncodedScreenshotCopy((nint)outputBuffer, byteCount, paddedRowBytes, width, height, request);
        }

        // Call only after QueueSubmit for the frame that carried EncodePendingScreenshotIfRequested's copy
        // has actually run -- MapAsync's own doc comment requires the copy to have been submitted first.
        private void FinishPendingScreenshotReadback(EncodedScreenshotCopy pending)
        {
            var outputBuffer = (WebGpuBuffer*)pending.OutputBuffer;
            var readbackTask = ManaWebGpuBufferReadback.MapAsync(_api, outputBuffer, pending.ByteCount, _readbackRegistry);
            var request = pending.Request;

            // No WebGPU API calls in this continuation (unlike Thumbnail's own draw-resource disposal), so
            // unlike RenderThumbnailAsync there's no need to hop back onto the render thread here -- pure
            // CPU-side row unpadding and PNG encoding is safe wherever MapAsync's ThreadPool continuation
            // happens to run.
            readbackTask.ContinueWith(
                readback =>
                {
                    try
                    {
                        if (readback.IsCanceled)
                        {
                            request.Completion.TrySetCanceled();
                            return;
                        }

                        if (readback.Exception != null)
                        {
                            request.Completion.TrySetException(readback.Exception.InnerExceptions);
                            return;
                        }

                        SaveScreenshotPng(readback.Result, pending.Width, pending.Height, pending.PaddedRowBytes, request.Path);
                        request.Completion.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        request.Completion.TrySetException(ex);
                    }
                },
                TaskContinuationOptions.ExecuteSynchronously);
        }

        private static void SaveScreenshotPng(byte[] readback, uint width, uint height, uint paddedRowBytes, string path)
        {
            var rowBytes = checked(width * ScreenshotBytesPerPixel);
            var pixels = new byte[rowBytes * height];
            for (var y = 0; y < height; y++)
            {
                new Span<byte>(readback, (int)(y * paddedRowBytes), (int)rowBytes)
                    .CopyTo(new Span<byte>(pixels, (int)(y * rowBytes), (int)rowBytes));
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Bgra32, matching the surface's own Bgra8UnormSrgb format -- no channel swizzle needed.
            using var image = ImageSharpImage.LoadPixelData<Bgra32>(pixels, (int)width, (int)height);
            image.SaveAsPng(path);
        }
    }
}

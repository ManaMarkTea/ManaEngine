using ManaCore.Imaging;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics
{
    /// <summary>
    /// Frame copies for automation: the presented frame as a PNG and the reflection cube's faces. A request rides the
    /// next frame's submit; then the readback owns its buffer. Render thread only, one screenshot at a time.
    /// </summary>
    internal sealed unsafe class ManaWebGpuFrameCapture : ManaWebGpuResource
    {
        private const uint BytesPerPixel = 4;

        private readonly ManaWebGpuReadbackRegistry _readbacks;
        private Screenshot? _pendingScreenshot;
        private string? _pendingDump;
        private bool _closed;

        public readonly record struct Screenshot(string Path, TaskCompletionSource Completion, bool WithoutUi);

        // Staging buffers travel as nint: a pointer cannot be a generic type argument, and these go through Nullable<T>.
        public readonly record struct EncodedCopies(
            nint ScreenshotBuffer, ulong ScreenshotBytes, uint ScreenshotRowBytes, uint Width, uint Height, Screenshot? Request,
            nint DumpBuffer, uint DumpSize, uint DumpRowBytes, string? DumpPrefix);

        public ManaWebGpuFrameCapture(ManaWebGpuDevice gpu, ManaWebGpuReadbackRegistry readbacks)
            : base(gpu)
        {
            _readbacks = readbacks;
        }

        /// <summary>Whether the next frame should leave ManaUI out: the scene across the whole window.</summary>
        public bool WithoutUi => _pendingScreenshot is { WithoutUi: true };

        public Task CaptureFrameAsync(string path, bool withoutUi)
        {
            if (_closed)
            {
                return Task.FromCanceled(new CancellationToken(true));
            }
            if (_pendingScreenshot != null)
            {
                return Task.FromException(new InvalidOperationException("A screenshot is already in flight."));
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingScreenshot = new Screenshot(path, completion, withoutUi);
            return completion.Task;
        }

        /// <summary>Writes the reflection cube's faces to <c>{prefix}.face{0-5}.png</c> after the next frame.</summary>
        public void DumpEnvironmentFaces(string prefix)
        {
            if (!_closed)
            {
                _pendingDump = prefix;
            }
        }

        /// <summary>
        /// Encodes the copies just before the frame finishes. Hand the result to <see cref="Finish"/> after submit, or to
        /// <see cref="Abandon"/> if the frame fails first.
        /// </summary>
        public EncodedCopies? Encode(CommandEncoder* encoder, Texture* frame, Vector2D<int> framebufferSize, ManaWebGpuEnvironmentReflectionMap? environment)
        {
            if (_pendingScreenshot == null && (_pendingDump == null || environment == null))
            {
                return null;
            }

            var copies = new EncodedCopies();
            if (_pendingScreenshot is { } request)
            {
                _pendingScreenshot = null;
                copies = EncodeScreenshot(encoder, frame, framebufferSize, request, copies);
            }

            if (_pendingDump is { } prefix && environment != null)
            {
                _pendingDump = null;
                copies = EncodeDump(encoder, environment, prefix, copies);
            }

            return copies;
        }

        private EncodedCopies EncodeScreenshot(CommandEncoder* encoder, Texture* frame, Vector2D<int> framebufferSize, Screenshot request, EncodedCopies copies)
        {
            WebGpuBuffer* outputBuffer = null;
            try
            {
                var width = (uint)Math.Max(0, framebufferSize.X);
                var height = (uint)Math.Max(0, framebufferSize.Y);
                if (width == 0 || height == 0)
                {
                    request.Completion.TrySetException(new InvalidOperationException("Cannot screenshot a zero-sized framebuffer."));
                    return copies;
                }

                var paddedRowBytes = ManaWebGpuDevice.AlignCopyRow(checked(width * BytesPerPixel));
                var byteCount = checked((ulong)paddedRowBytes * height);
                var outputBufferDescriptor = new BufferDescriptor
                {
                    Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
                    Size = byteCount
                };
                outputBuffer = Gpu.Api.DeviceCreateBuffer(Gpu.Device, &outputBufferDescriptor);
                if (outputBuffer == null)
                {
                    throw new InvalidOperationException("Failed to create WebGPU screenshot staging buffer.");
                }

                var copySource = new ImageCopyTexture
                {
                    Texture = frame,
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
                Gpu.Api.CommandEncoderCopyTextureToBuffer(encoder, &copySource, &copyDestination, &copySize);

                var encoded = copies with
                {
                    ScreenshotBuffer = (nint)outputBuffer, ScreenshotBytes = byteCount, ScreenshotRowBytes = paddedRowBytes,
                    Width = width, Height = height, Request = request
                };
                outputBuffer = null;
                return encoded;
            }
            catch (Exception ex)
            {
                request.Completion.TrySetException(ex);
                return copies;
            }
            finally
            {
                if (outputBuffer != null)
                {
                    Gpu.Api.BufferRelease(outputBuffer);
                }
            }
        }

        private EncodedCopies EncodeDump(CommandEncoder* encoder, ManaWebGpuEnvironmentReflectionMap map, string prefix, EncodedCopies copies)
        {
            var resource = map.EnvironmentMapResource;
            var size = resource.Width;
            var paddedRow = ManaWebGpuDevice.AlignCopyRow(size * 4);
            var descriptor = new BufferDescriptor
            {
                Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
                Size = (ulong)paddedRow * size * 6
            };
            var buffer = Gpu.Api.DeviceCreateBuffer(Gpu.Device, &descriptor);

            var source = new ImageCopyTexture { Texture = resource.Texture, MipLevel = 0, Origin = new Origin3D(), Aspect = TextureAspect.All };
            var destination = new ImageCopyBuffer
            {
                Buffer = buffer,
                Layout = new TextureDataLayout { Offset = 0, BytesPerRow = paddedRow, RowsPerImage = size }
            };
            var extent = new Extent3D { Width = size, Height = size, DepthOrArrayLayers = 6 };
            Gpu.Api.CommandEncoderCopyTextureToBuffer(encoder, &source, &destination, &extent);
            return copies with { DumpBuffer = (nint)buffer, DumpSize = size, DumpRowBytes = paddedRow, DumpPrefix = prefix };
        }

        /// <summary>After the frame's submit: each staging buffer now belongs to its readback.</summary>
        public void Finish(EncodedCopies copies)
        {
            if (copies.ScreenshotBuffer != 0 && copies.Request is { } request)
            {
                FinishScreenshot(copies, request);
            }

            if (copies.DumpBuffer != 0 && copies.DumpPrefix is { } prefix)
            {
                FinishDump(copies, prefix);
            }
        }

        /// <summary>A frame that failed before submission: release its copies and fail the screenshot.</summary>
        public void Abandon(EncodedCopies copies)
        {
            if (copies.ScreenshotBuffer != 0)
            {
                Gpu.Api.BufferRelease((WebGpuBuffer*)copies.ScreenshotBuffer);
                copies.Request?.Completion.TrySetException(
                    new InvalidOperationException("The frame carrying the screenshot failed before submission."));
            }

            if (copies.DumpBuffer != 0)
            {
                Gpu.Api.BufferRelease((WebGpuBuffer*)copies.DumpBuffer);
            }
        }

        public override void Dispose() => Close();

        /// <summary>Shutdown: cancels an unencoded screenshot and drops a pending dump; later requests are refused.</summary>
        public void Close()
        {
            _closed = true;
            _pendingScreenshot?.Completion.TrySetCanceled();
            _pendingScreenshot = null;
            _pendingDump = null;
        }

        private void FinishScreenshot(EncodedCopies copies, Screenshot request)
        {
            var readbackTask = ManaWebGpuBufferReadback.MapAsync(Gpu.Api, (WebGpuBuffer*)copies.ScreenshotBuffer, copies.ScreenshotBytes, _readbacks);

            // Only CPU work follows, so it can run wherever the readback's continuation lands.
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

                        SaveScreenshotPng(readback.Result, copies.Width, copies.Height, copies.ScreenshotRowBytes, request.Path);
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
            var rowBytes = checked(width * BytesPerPixel);
            var pixels = new byte[rowBytes * height];
            for (var y = 0; y < height; y++)
            {
                new Span<byte>(readback, (int)(y * paddedRowBytes), (int)rowBytes)
                    .CopyTo(new Span<byte>(pixels, (int)(y * rowBytes), (int)rowBytes));
            }

            // The surface is Bgra8UnormSrgb; the codec wants RGBA.
            for (var i = 0; i < pixels.Length; i += 4)
            {
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
            }
            ManaImageCodec.SavePng(new ManaImage((int)width, (int)height, pixels), path);
        }

        private void FinishDump(EncodedCopies copies, string prefix)
        {
            var byteCount = (ulong)copies.DumpRowBytes * copies.DumpSize * 6;
            ManaWebGpuBufferReadback.MapAsync(Gpu.Api, (WebGpuBuffer*)copies.DumpBuffer, byteCount, _readbacks).ContinueWith(readback =>
            {
                if (readback.Exception != null || readback.IsCanceled)
                {
                    return;
                }

                var data = readback.Result;
                var size = (int)copies.DumpSize;
                var row = (int)copies.DumpRowBytes;
                for (var face = 0; face < 6; face++)
                {
                    var image = new ManaImage(size, size);
                    for (var y = 0; y < size; y++)
                    {
                        var start = face * row * size + y * row;
                        for (var x = 0; x < size; x++)
                        {
                            var o = start + x * 4;
                            var p = (y * size + x) * 4;
                            image.Pixels[p] = data[o];
                            image.Pixels[p + 1] = data[o + 1];
                            image.Pixels[p + 2] = data[o + 2];
                            image.Pixels[p + 3] = 255;
                        }
                    }

                    ManaImageCodec.SavePng(image, $"{prefix}.face{face}.png");
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}

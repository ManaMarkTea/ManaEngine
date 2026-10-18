using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;
using ManaCore.Imaging;

namespace ManaGraphics.Code.Graphics
{
    // Saves the six faces of the reflection cube as PNGs, for checking what reflections are actually made of --
    // the sky drawn into them is otherwise only ever seen second-hand, in a mirror. Backs the dump_environment
    // automation command.
    public unsafe sealed partial class ManaWebGpuContext
    {
        private string? _pendingEnvironmentDump;

        /// <summary>Writes the reflection cube's faces to <c>{prefix}.face{0-5}.png</c> after the next frame.</summary>
        public void DumpEnvironmentFaces(string prefix)
        {
            if (!_disposed)
            {
                _pendingEnvironmentDump = prefix;
            }
        }

        private (nint Buffer, uint Size, uint PaddedRow, string Prefix)? EncodeEnvironmentDump(CommandEncoder* commandEncoder)
        {
            if (_pendingEnvironmentDump is not { } prefix || _environmentReflectionMap is not { } map)
            {
                return null;
            }

            _pendingEnvironmentDump = null;
            var resource = map.EnvironmentMapResource;
            var size = resource.Width;
            var paddedRow = AlignTo(size * 4, WebGpuCopyBytesPerRowAlignment);
            var descriptor = new BufferDescriptor
            {
                Usage = BufferUsage.MapRead | BufferUsage.CopyDst,
                Size = (ulong)paddedRow * size * 6
            };
            var buffer = _api.DeviceCreateBuffer(_device, &descriptor);

            var source = new ImageCopyTexture { Texture = resource.Texture, MipLevel = 0, Origin = new Origin3D(), Aspect = TextureAspect.All };
            var destination = new ImageCopyBuffer
            {
                Buffer = buffer,
                Layout = new TextureDataLayout { Offset = 0, BytesPerRow = paddedRow, RowsPerImage = size }
            };
            var extent = new Extent3D { Width = size, Height = size, DepthOrArrayLayers = 6 };
            _api.CommandEncoderCopyTextureToBuffer(commandEncoder, &source, &destination, &extent);
            return ((nint)buffer, size, paddedRow, prefix);
        }

        private void FinishEnvironmentDump((nint Buffer, uint Size, uint PaddedRow, string Prefix) pending)
        {
            var byteCount = (ulong)pending.PaddedRow * pending.Size * 6;
            ManaWebGpuBufferReadback.MapAsync(_api, (WebGpuBuffer*)pending.Buffer, byteCount, _readbackRegistry).ContinueWith(readback =>
            {
                if (readback.Exception != null || readback.IsCanceled)
                {
                    return;
                }

                var data = readback.Result;
                var size = (int)pending.Size;
                for (var face = 0; face < 6; face++)
                {
                    var image = new ManaImage(size, size);
                    for (var y = 0; y < size; y++)
                    {
                        var row = face * (int)pending.PaddedRow * size + y * (int)pending.PaddedRow;
                        for (var x = 0; x < size; x++)
                        {
                            var o = row + x * 4;
                            var p = (y * size + x) * 4;
                            image.Pixels[p] = data[o];
                            image.Pixels[p + 1] = data[o + 1];
                            image.Pixels[p + 2] = data[o + 2];
                            image.Pixels[p + 3] = 255;
                        }
                    }

                    ManaImageCodec.SavePng(image, $"{pending.Prefix}.face{face}.png");
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}

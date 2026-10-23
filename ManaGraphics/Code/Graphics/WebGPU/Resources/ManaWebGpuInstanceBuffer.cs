using Silk.NET.WebGPU;
using WebGpuBuffer = Silk.NET.WebGPU.Buffer;

namespace ManaGraphics.Code.Graphics.Resources
{
    /// <summary>
    /// A frame's instance data for instanced draws: runs of per-instance records gathered on the CPU, each starting on a
    /// dynamic-offset boundary, uploaded in one write and read by the shader through a storage binding of
    /// <see cref="BindingSize"/> bytes at the run's offset. Grows as needed; <see cref="Buffer"/> changes when it does.
    /// </summary>
    internal sealed unsafe class ManaWebGpuInstanceBuffer : ManaWebGpuDeviceObject
    {
        // WebGPU's default minStorageBufferOffsetAlignment.
        private const int OffsetAlignment = 256;

        private readonly string _name;
        private byte[] _staging = new byte[64 * 1024];
        private int _used;
        private ulong _capacity;

        public ManaWebGpuInstanceBuffer(ManaWebGpuDevice gpu, int bindingSize, string name)
            : base(gpu)
        {
            BindingSize = bindingSize;
            _name = name;
        }

        /// <summary>What one draw's binding covers from its offset; the buffer always reaches that far past every run.</summary>
        public int BindingSize { get; }

        public WebGpuBuffer* Buffer { get; private set; }

        /// <summary>Starts a frame's gathering.</summary>
        public void Begin() => _used = 0;

        /// <summary>Copies one run of records and returns the dynamic offset to draw it with.</summary>
        public uint Append<T>(ReadOnlySpan<T> records) where T : unmanaged
        {
            var offset = (_used + OffsetAlignment - 1) / OffsetAlignment * OffsetAlignment;
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(records);
            var needed = offset + Math.Max(bytes.Length, BindingSize);
            if (needed > _staging.Length)
            {
                Array.Resize(ref _staging, Math.Max(needed, _staging.Length * 2));
            }
            bytes.CopyTo(_staging.AsSpan(offset));
            _used = offset + bytes.Length;
            return (uint)offset;
        }

        /// <summary>Makes sure the GPU buffer holds what was gathered (remaking it larger if not) and writes it.</summary>
        public void Upload()
        {
            var needed = (ulong)(((_used + OffsetAlignment - 1) / OffsetAlignment * OffsetAlignment) + BindingSize);
            if (Buffer == null || needed > _capacity)
            {
                if (Buffer != null)
                {
                    Api.BufferRelease(Buffer);
                }
                _capacity = Math.Max(needed, Math.Max(_capacity * 2, 256 * 1024));
                var descriptor = new BufferDescriptor { Usage = BufferUsage.Storage | BufferUsage.CopyDst, Size = _capacity };
                Buffer = Api.DeviceCreateBuffer(Device, &descriptor);
                if (Buffer == null)
                {
                    throw new InvalidOperationException($"Failed to create the {_name} instance buffer.");
                }
            }

            if (_used > 0)
            {
                fixed (byte* data = _staging)
                {
                    // Whole multiples of four bytes, as a buffer write must be.
                    Api.QueueWriteBuffer(Queue, Buffer, 0, data, (nuint)((_used + 3) & ~3));
                }
            }
        }

        public override void Dispose()
        {
            if (Buffer != null)
            {
                Api.BufferRelease(Buffer);
                Buffer = null;
            }
        }
    }
}
